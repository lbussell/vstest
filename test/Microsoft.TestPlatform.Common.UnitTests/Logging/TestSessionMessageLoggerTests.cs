// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.TestPlatform.Common.UnitTests.Logging;

using Microsoft.VisualStudio.TestPlatform.Common.Logging;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

using VisualStudio.TestTools.UnitTesting;

[TestClass]
public class TestSessionMessageLoggerTests
{
    private TestSessionMessageLogger _testSessionMessageLogger;

    private TestRunMessageEventArgs _currentEventArgs;

    [TestInitialize]
    public void TestInit()
    {
        _testSessionMessageLogger = TestSessionMessageLogger.Instance;
    }

    [TestCleanup]
    public void TestCleanup()
    {
        TestSessionMessageLogger.Instance = null;
    }

    [TestMethod]
    public void InstanceShouldReturnALoggerInstance()
    {
        Assert.IsNotNull(_testSessionMessageLogger);
    }

    [TestMethod]
    public void SendMessageShouldLogErrorMessages()
    {
        _testSessionMessageLogger.TestRunMessage += OnMessage;

        var message = "Alert";
        _testSessionMessageLogger.SendMessage(TestMessageLevel.Error, message);

        Assert.AreEqual(TestMessageLevel.Error, _currentEventArgs.Level);
        Assert.AreEqual(message, _currentEventArgs.Message);
    }

    [TestMethod]
    public void SendMessageShouldLogErrorAsWarningIfSpecifiedSo()
    {
        _testSessionMessageLogger.TestRunMessage += OnMessage;
        _testSessionMessageLogger.TreatTestAdapterErrorsAsWarnings = true;

        var message = "Alert";
        _testSessionMessageLogger.SendMessage(TestMessageLevel.Error, message);

        Assert.AreEqual(TestMessageLevel.Warning, _currentEventArgs.Level);
        Assert.AreEqual(message, _currentEventArgs.Message);
    }

    private void OnMessage(object sender, TestRunMessageEventArgs e)
    {
        _currentEventArgs = e;
    }
}