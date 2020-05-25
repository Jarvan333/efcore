// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.Cosmos.TestUtilities;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore.Cosmos
{
    public class OverzealousInitializationCosmosTest
        : OverzealousInitializationTestBase<OverzealousInitializationCosmosTest.OverzealousInitializationCosmosFixture>
    {
        public OverzealousInitializationCosmosTest(OverzealousInitializationCosmosFixture fixture)
            : base(fixture)
        {
        }

        public class OverzealousInitializationCosmosFixture : OverzealousInitializationFixtureBase
        {
            protected override ITestStoreFactory TestStoreFactory => CosmosTestStoreFactory.Instance;
        }
    }
}
