﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    /// <summary>
    /// A SearchParameterDefinitionManager that only returns actively searchable parameters.
    /// </summary>
    public class SearchableSearchParameterDefinitionManager : ISearchParameterDefinitionManager
    {
        private readonly SearchParameterDefinitionManager _inner;
        private IFhirRequestContextAccessor _fhirReqeustContextAccessor;

        public SearchableSearchParameterDefinitionManager(SearchParameterDefinitionManager inner, IFhirRequestContextAccessor fhirRequestContextAccessor)
        {
            EnsureArg.IsNotNull(inner, nameof(inner));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));

            _inner = inner;
            _fhirReqeustContextAccessor = fhirRequestContextAccessor;
        }

        public IEnumerable<SearchParameterInfo> AllSearchParameters => GetAllSearchParameters();

        public string SearchParametersHash => _inner.SearchParametersHash;

        public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType)
        {
            if (_fhirReqeustContextAccessor.FhirRequestContext.IncludePartiallyIndexedSearchParams)
            {
                return _inner.GetSearchParameters(resourceType)
                .Where(x => x.IsSupported);
            }
            else
            {
                return _inner.GetSearchParameters(resourceType)
                    .Where(x => x.IsSearchable);
            }
        }

        public bool TryGetSearchParameter(string resourceType, string name, out SearchParameterInfo searchParameter)
        {
            searchParameter = null;

            if (_inner.TryGetSearchParameter(resourceType, name, out var parameter) &&
                (parameter.IsSearchable || (_fhirReqeustContextAccessor.FhirRequestContext.IncludePartiallyIndexedSearchParams && parameter.IsSupported)))
            {
                searchParameter = parameter;

                return true;
            }

            return false;
        }

        public SearchParameterInfo GetSearchParameter(string resourceType, string name)
        {
            SearchParameterInfo parameter = _inner.GetSearchParameter(resourceType, name);

            if (parameter.IsSearchable ||
                (_fhirReqeustContextAccessor.FhirRequestContext.IncludePartiallyIndexedSearchParams && parameter.IsSupported))
            {
                return parameter;
            }

            throw new SearchParameterNotSupportedException(resourceType, name);
        }

        public SearchParameterInfo GetSearchParameter(Uri definitionUri)
        {
            SearchParameterInfo parameter = _inner.GetSearchParameter(definitionUri);

            if (parameter.IsSearchable ||
                (_fhirReqeustContextAccessor.FhirRequestContext.IncludePartiallyIndexedSearchParams && parameter.IsSupported))
            {
                return parameter;
            }

            throw new SearchParameterNotSupportedException(definitionUri);
        }

        public SearchParamType GetSearchParameterType(SearchParameterInfo searchParameter, int? componentIndex)
        {
            return _inner.GetSearchParameterType(searchParameter, componentIndex);
        }

        private IEnumerable<SearchParameterInfo> GetAllSearchParameters()
        {
            if (_fhirReqeustContextAccessor.FhirRequestContext.IncludePartiallyIndexedSearchParams)
            {
                return _inner.AllSearchParameters.Where(x => x.IsSupported);
            }
            else
            {
                return _inner.AllSearchParameters.Where(x => x.IsSearchable);
            }
        }
    }
}
