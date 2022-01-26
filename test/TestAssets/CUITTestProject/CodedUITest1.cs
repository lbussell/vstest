// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace CodedUITestProject
{
    using Microsoft.VisualStudio.TestTools.UITesting;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [CodedUITest]
    public class CodedUITestProject
    {
        [TestMethod]
        public void CodedUITestMethod1()
        {
            UITestControl.Desktop.DrawHighlight();
        }
    }
}
