﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public class ReindexJobTask : IReindexJobTask
    {
        private readonly Func<IScoped<IFhirOperationDataStore>> _fhirOperationDataStoreFactory;
        private readonly ReindexJobConfiguration _reindexJobConfiguration;
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly ISupportedSearchParameterDefinitionManager _supportedSearchParameterDefinitionManager;
        private readonly IReindexUtilities _reindexUtilities;
        private readonly ILogger _logger;

        private ReindexJobRecord _reindexJobRecord;
        private WeakETag _weakETag;

        public ReindexJobTask(
            Func<IScoped<IFhirOperationDataStore>> fhirOperationDataStoreFactory,
            IOptions<ReindexJobConfiguration> reindexJobConfiguration,
            Func<IScoped<ISearchService>> searchServiceFactory,
            ISupportedSearchParameterDefinitionManager supportedSearchParameterDefinitionManager,
            Func<IScoped<IFhirDataStore>> fhirDataStoreFactory,
            IReindexUtilities reindexUtilities,
            ILogger<ReindexJobTask> logger)
        {
            EnsureArg.IsNotNull(fhirOperationDataStoreFactory, nameof(fhirOperationDataStoreFactory));
            EnsureArg.IsNotNull(reindexJobConfiguration?.Value, nameof(reindexJobConfiguration));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(supportedSearchParameterDefinitionManager, nameof(supportedSearchParameterDefinitionManager));
            EnsureArg.IsNotNull(fhirDataStoreFactory, nameof(fhirDataStoreFactory));
            EnsureArg.IsNotNull(reindexUtilities, nameof(reindexUtilities));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirOperationDataStoreFactory = fhirOperationDataStoreFactory;
            _reindexJobConfiguration = reindexJobConfiguration.Value;
            _searchServiceFactory = searchServiceFactory;
            _supportedSearchParameterDefinitionManager = supportedSearchParameterDefinitionManager;
            _reindexUtilities = reindexUtilities;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task ExecuteAsync(ReindexJobRecord reindexJobRecord, WeakETag weakETag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(reindexJobRecord, nameof(reindexJobRecord));
            EnsureArg.IsNotNull(weakETag, nameof(weakETag));

            _reindexJobRecord = reindexJobRecord;
            _weakETag = weakETag;

            try
            {
                if (_reindexJobRecord.Status != OperationStatus.Running)
                {
                    // update job record to running
                    _reindexJobRecord.Status = OperationStatus.Running;
                    _reindexJobRecord.StartTime = Clock.UtcNow;
                    await UpdateJobAsync(cancellationToken);
                }

                // If we are resuming a job, we can detect that by checking the progress info from the job record.
                // If no queries have been added to the progress then this is a new job
                if (_reindexJobRecord.QueryList?.Count == 0)
                {
                    // Build query based on new search params
                    // Find supported, but not yet searchable params
                    var notYetIndexedParams = _supportedSearchParameterDefinitionManager.GetSupportedButNotSearchableParams();

                    // if there are not any parameters which are supported but not yet indexed, then we have nothing to do
                    if (!notYetIndexedParams.Any())
                    {
                        _reindexJobRecord.Error.Add(new OperationOutcomeIssue(
                            OperationOutcomeConstants.IssueSeverity.Information,
                            OperationOutcomeConstants.IssueType.Informational,
                            Resources.NoSearchParametersNeededToBeIndexed));
                        _reindexJobRecord.CanceledTime = DateTimeOffset.UtcNow;
                        await CompleteJobAsync(OperationStatus.Canceled, cancellationToken);
                        return;
                    }

                    // From the param list, get the list of necessary resources which should be
                    // included in our query
                    var resourceList = new HashSet<string>();
                    foreach (var param in notYetIndexedParams)
                    {
                        resourceList.UnionWith(param.TargetResourceTypes);

                        // TODO: Expand the BaseResourceTypes to all child resources
                        resourceList.UnionWith(param.BaseResourceTypes);
                    }

                    _reindexJobRecord.Resources.AddRange(resourceList);
                    _reindexJobRecord.SearchParams.AddRange(notYetIndexedParams.Select(p => p.Name));

                    // generate and run first query
                    var queryStatus = new ReindexJobQueryStatus(null);
                    queryStatus.LastModified = DateTimeOffset.UtcNow;
                    queryStatus.Status = OperationStatus.Queued;

                    _reindexJobRecord.QueryList.Add(queryStatus);

                    // update the complete total
                    var countOnlyResults = await ExecuteReindexQueryAsync(queryStatus, countOnly: true, cancellationToken);
                    _reindexJobRecord.Count = countOnlyResults.TotalCount.Value;

                    // Query first batch of resources
                    await ProcessQueryAsync(queryStatus, cancellationToken);
                }
                else
                {
                    // check to see if queries are queued
                    // TODO: this while loop is temporary until we multithread this task so multiple threads can
                    // processes queries
                    while (_reindexJobRecord.QueryList.Where(q => q.Status == OperationStatus.Queued).Any())
                    {
                        // grab the next query from the list which is labeled as queued and run it
                        var query = _reindexJobRecord.QueryList.Where(q => q.Status == OperationStatus.Queued).OrderBy(q => q.LastModified).FirstOrDefault();

                        await ProcessQueryAsync(query, cancellationToken);
                    }
                }
            }
            catch (JobConflictException)
            {
                // The reindex job was updated externally.
                _logger.LogTrace("The job was updated by another process.");
            }
            catch (Exception ex)
            {
                _reindexJobRecord.Error.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    ex.Message));

                _reindexJobRecord.FailureCount++;

                _logger.LogError(ex, $"Encountered an unhandled exception. The job failure count increased to {_reindexJobRecord.FailureCount}.");

                await UpdateJobAsync(cancellationToken);

                if (_reindexJobRecord.FailureCount >= _reindexJobConfiguration.ConsecutiveFailuresThreshold)
                {
                    await CompleteJobAsync(OperationStatus.Failed, cancellationToken);
                }
            }
        }

        private async Task ProcessQueryAsync(ReindexJobQueryStatus query, CancellationToken cancellationToken)
        {
            try
            {
                query.Status = OperationStatus.Running;
                query.LastModified = DateTimeOffset.UtcNow;

                // Query first batch of resources
                var results = await ExecuteReindexQueryAsync(query, false, cancellationToken);

                // if continuation token then update next query
                if (!string.IsNullOrEmpty(results.ContinuationToken))
                {
                    var nextQuery = new ReindexJobQueryStatus(results.ContinuationToken);
                    nextQuery.LastModified = DateTimeOffset.UtcNow;
                    nextQuery.Status = OperationStatus.Queued;
                    _reindexJobRecord.QueryList.Add(nextQuery);
                }

                await UpdateJobAsync(cancellationToken);

                // TODO: Release lock on job document so another thread may pick up the next query.

                await _reindexUtilities.ProcessSearchResultsAsync(results, _reindexJobRecord.Hash, cancellationToken);

                // TODO: reaquire document lock and update _etag
                query.Status = OperationStatus.Completed;
                await UpdateJobAsync(cancellationToken);

                await CheckJobCompletionStatus(cancellationToken);
            }
            catch (Exception ex)
            {
                query.Error = ex.Message;

                query.FailureCount++;

                _logger.LogError(ex, $"Encountered an unhandled exception. The query failure count increased to {_reindexJobRecord.FailureCount}.");

                if (query.FailureCount >= _reindexJobConfiguration.ConsecutiveFailuresThreshold)
                {
                    query.Status = OperationStatus.Failed;
                }
                else
                {
                    query.Status = OperationStatus.Queued;
                }

                await UpdateJobAsync(cancellationToken);
            }
        }

        private async Task CheckJobCompletionStatus(CancellationToken cancellationToken)
        {
            // If any query still in progress then we are not done
            if (_reindexJobRecord.QueryList.Where(q =>
                q.Status == OperationStatus.Queued ||
                q.Status == OperationStatus.Running).Any())
            {
                return;
            }
            else
            {
                // all queries marked as complete, reindex job is done, check success or failure
                if (_reindexJobRecord.QueryList.All(q => q.Status == OperationStatus.Completed))
                {
                    await CompleteJobAsync(OperationStatus.Completed, cancellationToken);
                    _logger.LogTrace("Successfully completed the job.");
                }
                else
                {
                    await CompleteJobAsync(OperationStatus.Failed, cancellationToken);
                    _logger.LogTrace("Reindex job did not complete successfully.");
                }
            }
        }

        private async Task<SearchResult> ExecuteReindexQueryAsync(ReindexJobQueryStatus queryStatus, bool countOnly, CancellationToken cancellationToken)
        {
            var queryParametersList = new List<Tuple<string, string>>()
            {
                Tuple.Create(KnownQueryParameterNames.Count, _reindexJobConfiguration.MaximumNumberOfResourcesPerQuery.ToString(CultureInfo.InvariantCulture)),
                Tuple.Create(KnownQueryParameterNames.Type, _reindexJobRecord.ResourceList),
            };

            if (queryStatus.ContinuationToken != null)
            {
                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, queryStatus.ContinuationToken));
            }

            using (IScoped<ISearchService> searchService = _searchServiceFactory())
            {
                try
                {
                    return await searchService.Value.SearchForReindexAsync(queryParametersList, _reindexJobRecord.Hash, countOnly, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error running reindex query.");
                    queryStatus.FailureCount++;
                    queryStatus.Error = ex.Message;

                    throw;
                }
            }
        }

        private async Task CompleteJobAsync(OperationStatus completionStatus, CancellationToken cancellationToken)
        {
            _reindexJobRecord.Status = completionStatus;
            _reindexJobRecord.EndTime = Clock.UtcNow;

            await UpdateJobAsync(cancellationToken);
        }

        private async Task UpdateJobAsync(CancellationToken cancellationToken)
        {
            _reindexJobRecord.LastModified = Clock.UtcNow;
            using (IScoped<IFhirOperationDataStore> store = _fhirOperationDataStoreFactory())
            {
                var wrapper = await store.Value.UpdateReindexJobAsync(_reindexJobRecord, _weakETag, cancellationToken);
                _weakETag = wrapper.ETag;
            }
        }
    }
}
