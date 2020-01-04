﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Models;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    public class ValidateControllerTests
    {
        [Fact]
        public async void GivenAValidateRequest_WhenTheServerDoesNotSupportValidate_ThenANotSupportedErrorIsReturned()
        {
            ValidateController disabledValidateController = GetController(false);
            Resource payload = new Observation();

            OperationNotImplementedException ex = await Assert.ThrowsAsync<OperationNotImplementedException>(() => disabledValidateController.Validate(payload, null, null, "Observation"));

            CheckOperationOutcomeIssue(
                ex,
                OperationOutcome.IssueSeverity.Error,
                OperationOutcome.IssueType.NotSupported,
                Resources.ValidationNotSupported);
        }

        [Theory]
        [MemberData(nameof(GetValidationFunctions))]
        public async void GivenAValidationRequest_WhenPassedAProfileQueryParameter_ThenANotSupportedErrorIsReturned(Func<Resource, string, string, Func<System.Threading.Tasks.Task>> validate)
        {
            Resource payload = new Observation();
            OperationNotImplementedException ex = await Assert.ThrowsAsync<OperationNotImplementedException>(validate(payload, "profile", null));

            CheckOperationOutcomeIssue(
                ex,
                OperationOutcome.IssueSeverity.Error,
                OperationOutcome.IssueType.NotSupported,
                Resources.ValidateWithProfileNotSupported);
        }

        [Theory]
        [MemberData(nameof(GetValidationFunctions))]
        public async void GivenAValidationRequest_WhenPassedAModeQueryParameter_ThenANotSupportedErrorIsReturned(Func<Resource, string, string, Func<System.Threading.Tasks.Task>> validate)
        {
            Resource payload = new Observation();
            OperationNotImplementedException ex = await Assert.ThrowsAsync<OperationNotImplementedException>(validate(payload, null, "mode"));

            CheckOperationOutcomeIssue(
                ex,
                OperationOutcome.IssueSeverity.Error,
                OperationOutcome.IssueType.NotSupported,
                Resources.ValidationModesNotSupported);
        }

        private void CheckOperationOutcomeIssue(
            OperationNotImplementedException exception,
            OperationOutcome.IssueSeverity expectedSeverity,
            OperationOutcome.IssueType expectedCode,
            string expectedMessage)
        {
            IEnumerator<OperationOutcomeIssue> enumerator = exception.Issues.GetEnumerator();
            enumerator.MoveNext();
            OperationOutcome.IssueComponent issue = enumerator.Current.ToPoco();

            // Check expected outcome
            Assert.Equal(expectedSeverity, issue.Severity);
            Assert.Equal(expectedCode, issue.Code);
            Assert.Equal(expectedMessage, issue.Diagnostics);
        }

        public static IEnumerable<object[]> GetValidationFunctions()
        {
            ValidateController validateController = GetController(true);
            yield return new object[] { new Func<Resource, string, string, Func<System.Threading.Tasks.Task>>((Resource payload, string profile, string mode) => new Func<System.Threading.Tasks.Task>(() => validateController.Validate(payload, profile, mode, payload.TypeName))) };
            yield return new object[] { new Func<Resource, string, string, Func<System.Threading.Tasks.Task>>((Resource payload, string profile, string mode) => new Func<System.Threading.Tasks.Task>(() => validateController.ValidateById(payload, profile, mode, payload.TypeName, payload.Id))) };
        }

        private static ValidateController GetController(bool enableValidate)
        {
            var featureConfiguration = new FeatureConfiguration
            {
                SupportsValidate = enableValidate,
            };
            IOptions<FeatureConfiguration> optionsFeatureConfiguration = Substitute.For<IOptions<FeatureConfiguration>>();
            optionsFeatureConfiguration.Value.Returns(featureConfiguration);

            IMediator mediator = Substitute.For<IMediator>();

            return new ValidateController(mediator, optionsFeatureConfiguration);
        }
    }
}
