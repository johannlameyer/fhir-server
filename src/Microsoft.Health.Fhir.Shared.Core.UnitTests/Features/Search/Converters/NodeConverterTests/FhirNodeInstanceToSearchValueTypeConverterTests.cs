﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters.NodeConverterTests
{
    public abstract class FhirNodeInstanceToSearchValueTypeConverterTests<TElement>
        where TElement : Element, new()
    {
        protected FhirNodeInstanceToSearchValueTypeConverterTests(IFhirNodeToSearchValueTypeConverter converter)
        {
            TypeConverter = converter;
        }

        protected IFhirNodeToSearchValueTypeConverter TypeConverter { get; }

        protected TElement Element { get; } = new TElement();

        protected virtual ITypedElement TypedElement => Element.ToTypedElement();

        [Fact]
        public void GivenANullValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            IEnumerable<ISearchValue> values = TypeConverter.ConvertTo(null);

            Assert.NotNull(values);
            Assert.Empty(values);
        }

        protected void Test<TValue>(Action<TElement> setup, Action<TValue, ISearchValue> validator, params TValue[] expected)
        {
            setup(Element);

            IEnumerable<ISearchValue> values = TypeConverter.ConvertTo(TypedElement);

            Assert.NotNull(values);
            Assert.Collection(
                values,
                expected.Select(e => new Action<ISearchValue>(sv => validator(e, sv))).ToArray());
        }

        protected void Test(Action<TElement> setup)
        {
            setup(Element);

            IEnumerable<ISearchValue> values = TypeConverter.ConvertTo(TypedElement);

            Assert.NotNull(values);
            Assert.Empty(values);
        }
    }
}
