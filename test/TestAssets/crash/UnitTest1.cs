// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;

#pragma warning disable IDE1006 // Naming Styles
namespace timeout
#pragma warning restore IDE1006 // Naming Styles
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void TestMethod1()
        {
            // stack overflow
            Span<byte> s = stackalloc byte[int.MaxValue];
        }
    }
}
