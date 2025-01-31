// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.TestPlatform.CommunicationUtilities.UnitTests.ObjectModel;

using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.EventHandlers;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.Interfaces;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using VisualStudio.TestTools.UnitTesting;

using Moq;

[TestClass]
public class TestRunEventsHandlerTests
{
    private Mock<ITestRequestHandler> _mockClient;
    private TestRunEventsHandler _testRunEventHandler;

    [TestInitialize]
    public void InitializeTests()
    {
        _mockClient = new Mock<ITestRequestHandler>();
        _testRunEventHandler = new TestRunEventsHandler(_mockClient.Object);
    }

    [TestMethod]
    public void HandleTestRunStatsChangeShouldSendTestRunStatisticsToClient()
    {
        _testRunEventHandler.HandleTestRunStatsChange(null);
        _mockClient.Verify(th => th.SendTestRunStatistics(null), Times.Once);
    }

    [TestMethod]
    public void HandleTestRunCompleteShouldInformClient()
    {
        _testRunEventHandler.HandleTestRunComplete(null, null, null, null);
        _mockClient.Verify(th => th.SendExecutionComplete(null, null, null, null), Times.Once);
    }

    [TestMethod]
    public void HandleTestRunMessageShouldSendMessageToClient()
    {
        _testRunEventHandler.HandleLogMessage(TestMessageLevel.Informational, string.Empty);

        _mockClient.Verify(th => th.SendLog(TestMessageLevel.Informational, string.Empty), Times.AtLeast(1));
    }
}