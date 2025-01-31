// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
namespace Microsoft.VisualStudio.TestPlatform.Client.UnitTests.DesignMode;

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestPlatform.Client.DesignMode;
using TestRunAttachmentsProcessing;
using RequestHelper;
using Common.Interfaces;
using CommunicationUtilities;
using CommunicationUtilities.Interfaces;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.ObjectModel;
using CrossPlatEngine;
using ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client.Payloads;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client.Interfaces;
using ObjectModel.Logging;
using PlatformAbstractions.Interfaces;
using TestTools.UnitTesting;

using Moq;

using Newtonsoft.Json.Linq;

[TestClass]
public class DesignModeClientTests
{
    private const int Timeout = 15 * 1000;

    private const int PortNumber = 123;

    private readonly Mock<ITestRequestManager> _mockTestRequestManager;

    private readonly Mock<ICommunicationManager> _mockCommunicationManager;

    private readonly DesignModeClient _designModeClient;

    private readonly int _protocolVersion = 5;

    private readonly AutoResetEvent _completeEvent;

    private readonly Mock<IEnvironment> _mockPlatformEnvrironment;

    public DesignModeClientTests()
    {
        _mockTestRequestManager = new Mock<ITestRequestManager>();
        _mockCommunicationManager = new Mock<ICommunicationManager>();
        _mockPlatformEnvrironment = new Mock<IEnvironment>();
        _designModeClient = new DesignModeClient(_mockCommunicationManager.Object, JsonDataSerializer.Instance, _mockPlatformEnvrironment.Object);
        _completeEvent = new AutoResetEvent(false);
    }

    [TestMethod]
    public void DesignModeClientBeforeConnectInstanceShouldReturnNull()
    {
        Assert.IsNull(DesignModeClient.Instance);
    }

    [TestMethod]
    public void DesignModeClientInitializeShouldInstantiateClassAndCreateClient()
    {
        DesignModeClient.Initialize();
        Assert.IsNotNull(DesignModeClient.Instance);
    }

    [TestMethod]
    public void TestRunMessageHandlerShouldCallCommmunicationManagerIfMessageisError()
    {
        _mockCommunicationManager.Setup(cm => cm.SendMessage(It.IsAny<string>()));

        _designModeClient.TestRunMessageHandler(new object(), new TestRunMessageEventArgs(TestMessageLevel.Error, "message"));

        _mockCommunicationManager.Verify(cm => cm.SendMessage(It.IsAny<string>(), It.IsAny<TestMessagePayload>()), Times.Once());
    }

    [TestMethod]
    public void TestRunMessageHandlerShouldCallCommmunicationManagerIfMessageisWarning()
    {
        _mockCommunicationManager.Setup(cm => cm.SendMessage(It.IsAny<string>()));

        _designModeClient.TestRunMessageHandler(new object(), new TestRunMessageEventArgs(TestMessageLevel.Warning, "message"));

        _mockCommunicationManager.Verify(cm => cm.SendMessage(It.IsAny<string>(), It.IsAny<TestMessagePayload>()), Times.Once());
    }

    [TestMethod]
    public void TestRunMessageHandlerShouldNotCallCommmunicationManagerIfMessageisInformational()
    {
        _mockCommunicationManager.Setup(cm => cm.SendMessage(It.IsAny<string>()));

        _designModeClient.TestRunMessageHandler(new object(), new TestRunMessageEventArgs(TestMessageLevel.Informational, "message"));

        _mockCommunicationManager.Verify(cm => cm.SendMessage(It.IsAny<string>(), It.IsAny<TestMessagePayload>()), Times.Never());
    }

    [TestMethod]
    public void DesignModeClientConnectShouldSetupChannel()
    {
        var verCheck = new Message { MessageType = MessageType.VersionCheck, Payload = _protocolVersion };
        var sessionEnd = new Message { MessageType = MessageType.SessionEnd };
        _mockCommunicationManager.Setup(cm => cm.WaitForServerConnection(It.IsAny<int>())).Returns(true);
        _mockCommunicationManager.SetupSequence(cm => cm.ReceiveMessage()).Returns(verCheck).Returns(sessionEnd);

        _designModeClient.ConnectToClientAndProcessRequests(PortNumber, _mockTestRequestManager.Object);

        _mockCommunicationManager.Verify(cm => cm.SetupClientAsync(new IPEndPoint(IPAddress.Loopback, PortNumber)), Times.Once);
        _mockCommunicationManager.Verify(cm => cm.WaitForServerConnection(It.IsAny<int>()), Times.Once);
        _mockCommunicationManager.Verify(cm => cm.SendMessage(MessageType.SessionConnected), Times.Once());
        _mockCommunicationManager.Verify(cm => cm.SendMessage(MessageType.VersionCheck, _protocolVersion), Times.Once());
    }

    [TestMethod]
    public void DesignModeClientConnectShouldNotSendConnectedIfServerConnectionTimesOut()
    {
        var verCheck = new Message { MessageType = MessageType.VersionCheck, Payload = _protocolVersion };
        var sessionEnd = new Message { MessageType = MessageType.SessionEnd };
        _mockCommunicationManager.Setup(cm => cm.WaitForServerConnection(It.IsAny<int>())).Returns(false);
        _mockCommunicationManager.SetupSequence(cm => cm.ReceiveMessage()).Returns(verCheck).Returns(sessionEnd);

        Assert.ThrowsException<TimeoutException>(() => _designModeClient.ConnectToClientAndProcessRequests(PortNumber, _mockTestRequestManager.Object));

        _mockCommunicationManager.Verify(cm => cm.SetupClientAsync(new IPEndPoint(IPAddress.Loopback, PortNumber)), Times.Once);
        _mockCommunicationManager.Verify(cm => cm.WaitForServerConnection(It.IsAny<int>()), Times.Once);
        _mockCommunicationManager.Verify(cm => cm.SendMessage(MessageType.SessionConnected), Times.Never);
        _mockCommunicationManager.Verify(cm => cm.SendMessage(MessageType.VersionCheck, It.IsAny<int>()), Times.Never);
    }

    [TestMethod]
    public void DesignModeClientDuringConnectShouldHighestCommonVersionWhenReceivedVersionIsGreaterThanSupportedVersion()
    {
        var verCheck = new Message { MessageType = MessageType.VersionCheck, Payload = 5 };
        var sessionEnd = new Message { MessageType = MessageType.SessionEnd };
        _mockCommunicationManager.Setup(cm => cm.WaitForServerConnection(It.IsAny<int>())).Returns(true);
        _mockCommunicationManager.SetupSequence(cm => cm.ReceiveMessage()).Returns(verCheck).Returns(sessionEnd);

        _designModeClient.ConnectToClientAndProcessRequests(PortNumber, _mockTestRequestManager.Object);

        _mockCommunicationManager.Verify(cm => cm.SendMessage(MessageType.VersionCheck, _protocolVersion), Times.Once());
    }

    [TestMethod]
    public void DesignModeClientDuringConnectShouldHighestCommonVersionWhenReceivedVersionIsSmallerThanSupportedVersion()
    {
        var verCheck = new Message { MessageType = MessageType.VersionCheck, Payload = 1 };
        var sessionEnd = new Message { MessageType = MessageType.SessionEnd };
        _mockCommunicationManager.Setup(cm => cm.WaitForServerConnection(It.IsAny<int>())).Returns(true);
        _mockCommunicationManager.SetupSequence(cm => cm.ReceiveMessage()).Returns(verCheck).Returns(sessionEnd);

        _designModeClient.ConnectToClientAndProcessRequests(PortNumber, _mockTestRequestManager.Object);

        _mockCommunicationManager.Verify(cm => cm.SendMessage(MessageType.VersionCheck, 1), Times.Once());
    }

    [TestMethod]
    public void DesignModeClientWithGetTestRunnerProcessStartInfoShouldDeserializeTestsWithTraitsCorrectly()
    {
        // Arrange.
        var testCase = new TestCase("A.C.M", new Uri("d:\\executor"), "A.dll");
        testCase.Traits.Add(new Trait("foo", "bar"));

        var testList = new System.Collections.Generic.List<TestCase> { testCase };
        var testRunPayload = new TestRunRequestPayload { RunSettings = null, TestCases = testList };

        var getProcessStartInfoMessage = new Message
        {
            MessageType = MessageType.GetTestRunnerProcessStartInfoForRunSelected,
            Payload = JToken.FromObject("random")
        };

        var sessionEnd = new Message { MessageType = MessageType.SessionEnd };
        TestRunRequestPayload receivedTestRunPayload = null;
        var allTasksComplete = new ManualResetEvent(false);

        // Setup mocks.
        _mockCommunicationManager.Setup(cm => cm.WaitForServerConnection(It.IsAny<int>())).Returns(true);
        _mockCommunicationManager.Setup(cm => cm.DeserializePayload<TestRunRequestPayload>(getProcessStartInfoMessage))
            .Returns(testRunPayload);

        _mockTestRequestManager.Setup(
                trm =>
                    trm.RunTests(
                        It.IsAny<TestRunRequestPayload>(),
                        It.IsAny<ITestHostLauncher>(),
                        It.IsAny<ITestRunEventsRegistrar>(),
                        It.IsAny<ProtocolConfig>()))
            .Callback(
                (TestRunRequestPayload trp,
                    ITestHostLauncher testHostManager,
                    ITestRunEventsRegistrar testRunEventsRegistrar,
                    ProtocolConfig config) =>
                {
                    receivedTestRunPayload = trp;
                    allTasksComplete.Set();
                });

        _mockCommunicationManager.SetupSequence(cm => cm.ReceiveMessage())
            .Returns(getProcessStartInfoMessage)
            .Returns(sessionEnd);

        // Act.
        _designModeClient.ConnectToClientAndProcessRequests(0, _mockTestRequestManager.Object);

        // wait for the internal spawned of tasks to complete.
        Assert.IsTrue(allTasksComplete.WaitOne(1000), "Timed out waiting for mock request manager.");

        // Assert.
        Assert.IsNotNull(receivedTestRunPayload);
        Assert.IsNotNull(receivedTestRunPayload.TestCases);
        Assert.AreEqual(1, receivedTestRunPayload.TestCases.Count);

        // Validate traits
        var traits = receivedTestRunPayload.TestCases.ToArray()[0].Traits;
        Assert.AreEqual("foo", traits.ToArray()[0].Name);
        Assert.AreEqual("bar", traits.ToArray()[0].Value);
    }

    [TestMethod]
    public void DesignModeClientWithRunSelectedTestCasesShouldDeserializeTestsWithTraitsCorrectly()
    {
        // Arrange.
        var testCase = new TestCase("A.C.M", new Uri("d:\\executor"), "A.dll");
        testCase.Traits.Add(new Trait("foo", "bar"));

        var testList = new System.Collections.Generic.List<TestCase> { testCase };
        var testRunPayload = new TestRunRequestPayload { RunSettings = null, TestCases = testList };

        var getProcessStartInfoMessage = new Message
        {
            MessageType = MessageType.TestRunSelectedTestCasesDefaultHost,
            Payload = JToken.FromObject("random")
        };

        var sessionEnd = new Message { MessageType = MessageType.SessionEnd };
        TestRunRequestPayload receivedTestRunPayload = null;
        var allTasksComplete = new ManualResetEvent(false);

        // Setup mocks.
        _mockCommunicationManager.Setup(cm => cm.WaitForServerConnection(It.IsAny<int>())).Returns(true);
        _mockCommunicationManager.Setup(cm => cm.DeserializePayload<TestRunRequestPayload>(getProcessStartInfoMessage))
            .Returns(testRunPayload);
        _mockTestRequestManager.Setup(
                trm =>
                    trm.RunTests(
                        It.IsAny<TestRunRequestPayload>(),
                        It.IsAny<ITestHostLauncher>(),
                        It.IsAny<ITestRunEventsRegistrar>(),
                        It.IsAny<ProtocolConfig>()))
            .Callback(
                (TestRunRequestPayload trp,
                    ITestHostLauncher testHostManager,
                    ITestRunEventsRegistrar testRunEventsRegistrar,
                    ProtocolConfig config) =>
                {
                    receivedTestRunPayload = trp;
                    allTasksComplete.Set();
                });
        _mockCommunicationManager.SetupSequence(cm => cm.ReceiveMessage())
            .Returns(getProcessStartInfoMessage)
            .Returns(sessionEnd);

        // Act.
        _designModeClient.ConnectToClientAndProcessRequests(0, _mockTestRequestManager.Object);

        // wait for the internal spawned of tasks to complete.
        allTasksComplete.WaitOne(1000);

        // Assert.
        Assert.IsNotNull(receivedTestRunPayload);
        Assert.IsNotNull(receivedTestRunPayload.TestCases);
        Assert.AreEqual(1, receivedTestRunPayload.TestCases.Count);

        // Validate traits
        var traits = receivedTestRunPayload.TestCases.ToArray()[0].Traits;
        Assert.AreEqual("foo", traits.ToArray()[0].Name);
        Assert.AreEqual("bar", traits.ToArray()[0].Value);
    }

    [TestMethod]
    public void DesignModeClientOnBadConnectionShouldStopServerAndThrowTimeoutException()
    {
        _mockCommunicationManager.Setup(cm => cm.WaitForServerConnection(It.IsAny<int>())).Returns(false);

        var ex = Assert.ThrowsException<TimeoutException>(() => _designModeClient.ConnectToClientAndProcessRequests(PortNumber, _mockTestRequestManager.Object));
        Assert.AreEqual("vstest.console process failed to connect to translation layer process after 90 seconds. This may occur due to machine slowness, please set environment variable VSTEST_CONNECTION_TIMEOUT to increase timeout.", ex.Message);

        _mockCommunicationManager.Verify(cm => cm.SetupClientAsync(new IPEndPoint(IPAddress.Loopback, PortNumber)), Times.Once);
        _mockCommunicationManager.Verify(cm => cm.WaitForServerConnection(It.IsAny<int>()), Times.Once);
        _mockCommunicationManager.Verify(cm => cm.StopClient(), Times.Once);
    }

    [TestMethod]
    public void DesignModeClientShouldStopCommunicationOnParentProcessExit()
    {
        _mockPlatformEnvrironment.Setup(pe => pe.Exit(It.IsAny<int>()));
        _designModeClient.HandleParentProcessExit();

        _mockCommunicationManager.Verify(cm => cm.StopClient(), Times.Once);
    }

    [TestMethod]
    public void DesignModeClientLaunchCustomHostMustReturnIfAckComes()
    {
        var testableDesignModeClient = new TestableDesignModeClient(_mockCommunicationManager.Object, JsonDataSerializer.Instance, _mockPlatformEnvrironment.Object);

        _mockCommunicationManager.Setup(cm => cm.WaitForServerConnection(It.IsAny<int>())).Returns(true);

        var expectedProcessId = 1234;
        Action sendMessageAction = () => testableDesignModeClient.InvokeCustomHostLaunchAckCallback(expectedProcessId, null);

        _mockCommunicationManager.Setup(cm => cm.SendMessage(MessageType.CustomTestHostLaunch, It.IsAny<object>())).
            Callback(() => Task.Run(sendMessageAction));

        var info = new TestProcessStartInfo();
        var processId = testableDesignModeClient.LaunchCustomHost(info, CancellationToken.None);

        Assert.AreEqual(expectedProcessId, processId);
    }

    [TestMethod]
    [ExpectedException(typeof(TestPlatformException))]
    public void DesignModeClientLaunchCustomHostMustThrowIfInvalidAckComes()
    {
        var testableDesignModeClient = new TestableDesignModeClient(_mockCommunicationManager.Object, JsonDataSerializer.Instance, _mockPlatformEnvrironment.Object);

        _mockCommunicationManager.Setup(cm => cm.WaitForServerConnection(It.IsAny<int>())).Returns(true);

        var expectedProcessId = -1;
        Action sendMessageAction = () => testableDesignModeClient.InvokeCustomHostLaunchAckCallback(expectedProcessId, "Dummy");

        _mockCommunicationManager
            .Setup(cm => cm.SendMessage(MessageType.CustomTestHostLaunch, It.IsAny<object>()))
            .Callback(() => Task.Run(sendMessageAction));

        var info = new TestProcessStartInfo();
        testableDesignModeClient.LaunchCustomHost(info, CancellationToken.None);
    }

    [TestMethod]
    [ExpectedException(typeof(TestPlatformException))]
    public void DesignModeClientLaunchCustomHostMustThrowIfCancellationOccursBeforeHostLaunch()
    {
        var testableDesignModeClient = new TestableDesignModeClient(_mockCommunicationManager.Object, JsonDataSerializer.Instance, _mockPlatformEnvrironment.Object);

        var info = new TestProcessStartInfo();
        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        testableDesignModeClient.LaunchCustomHost(info, cancellationTokenSource.Token);
    }

    [TestMethod]
    public void DesignModeClientConnectShouldSendTestMessageAndDiscoverCompleteOnExceptionInDiscovery()
    {
        var payload = new DiscoveryRequestPayload();
        var startDiscovery = new Message { MessageType = MessageType.StartDiscovery, Payload = JToken.FromObject(payload) };
        _mockCommunicationManager.Setup(cm => cm.WaitForServerConnection(It.IsAny<int>())).Returns(true);
        _mockCommunicationManager.SetupSequence(cm => cm.ReceiveMessage()).Returns(startDiscovery);
        _mockCommunicationManager
            .Setup(cm => cm.SendMessage(MessageType.DiscoveryComplete, It.IsAny<DiscoveryCompletePayload>()))
            .Callback(() => _completeEvent.Set());
        _mockTestRequestManager.Setup(
                rm => rm.DiscoverTests(
                    It.IsAny<DiscoveryRequestPayload>(),
                    It.IsAny<ITestDiscoveryEventsRegistrar>(),
                    It.IsAny<ProtocolConfig>()))
            .Throws(new Exception());

        _designModeClient.ConnectToClientAndProcessRequests(PortNumber, _mockTestRequestManager.Object);

        Assert.IsTrue(_completeEvent.WaitOne(Timeout), "Discovery not completed.");
        _mockCommunicationManager.Verify(cm => cm.SendMessage(MessageType.TestMessage, It.IsAny<TestMessagePayload>()), Times.Once());
        _mockCommunicationManager.Verify(cm => cm.SendMessage(MessageType.DiscoveryComplete, It.IsAny<DiscoveryCompletePayload>()), Times.Once());
    }

    [TestMethod]
    public void DesignModeClientConnectShouldSendTestMessageAndDiscoverCompleteOnTestPlatformExceptionInDiscovery()
    {
        var payload = new DiscoveryRequestPayload();
        var startDiscovery = new Message { MessageType = MessageType.StartDiscovery, Payload = JToken.FromObject(payload) };
        _mockCommunicationManager.Setup(cm => cm.WaitForServerConnection(It.IsAny<int>())).Returns(true);
        _mockCommunicationManager.SetupSequence(cm => cm.ReceiveMessage()).Returns(startDiscovery);
        _mockCommunicationManager
            .Setup(cm => cm.SendMessage(MessageType.DiscoveryComplete, It.IsAny<DiscoveryCompletePayload>()))
            .Callback(() => _completeEvent.Set());
        _mockTestRequestManager.Setup(
                rm => rm.DiscoverTests(
                    It.IsAny<DiscoveryRequestPayload>(),
                    It.IsAny<ITestDiscoveryEventsRegistrar>(),
                    It.IsAny<ProtocolConfig>()))
            .Throws(new TestPlatformException("Hello world"));

        _designModeClient.ConnectToClientAndProcessRequests(PortNumber, _mockTestRequestManager.Object);

        Assert.IsTrue(_completeEvent.WaitOne(Timeout), "Discovery not completed.");
        _mockCommunicationManager.Verify(cm => cm.SendMessage(MessageType.TestMessage, It.IsAny<TestMessagePayload>()), Times.Once());
        _mockCommunicationManager.Verify(cm => cm.SendMessage(MessageType.DiscoveryComplete, It.IsAny<DiscoveryCompletePayload>()), Times.Once());
    }

    [TestMethod]
    public void DesignModeClientConnectShouldSendTestMessageAndAttachmentsProcessingCompleteOnExceptionInAttachmentsProcessing()
    {
        var payload = new TestRunAttachmentsProcessingPayload();
        var startAttachmentsProcessing = new Message { MessageType = MessageType.TestRunAttachmentsProcessingStart, Payload = JToken.FromObject(payload) };
        _mockCommunicationManager.Setup(cm => cm.WaitForServerConnection(It.IsAny<int>())).Returns(true);
        _mockCommunicationManager.SetupSequence(cm => cm.ReceiveMessage()).Returns(startAttachmentsProcessing);
        _mockCommunicationManager
            .Setup(cm => cm.SendMessage(MessageType.TestRunAttachmentsProcessingComplete, It.IsAny<TestRunAttachmentsProcessingCompletePayload>()))
            .Callback(() => _completeEvent.Set());
        _mockTestRequestManager.Setup(
                rm => rm.ProcessTestRunAttachments(
                    It.IsAny<TestRunAttachmentsProcessingPayload>(),
                    It.IsAny<ITestRunAttachmentsProcessingEventsHandler>(),
                    It.IsAny<ProtocolConfig>()))
            .Throws(new Exception());

        _designModeClient.ConnectToClientAndProcessRequests(PortNumber, _mockTestRequestManager.Object);

        Assert.IsTrue(_completeEvent.WaitOne(Timeout), "AttachmentsProcessing not completed.");
        _mockCommunicationManager.Verify(cm => cm.SendMessage(MessageType.TestMessage, It.IsAny<TestMessagePayload>()), Times.Once());
        _mockCommunicationManager.Verify(cm => cm.SendMessage(MessageType.TestRunAttachmentsProcessingComplete, It.Is<TestRunAttachmentsProcessingCompletePayload>(p => p.Attachments == null)), Times.Once());
    }

    [TestMethod]
    public void DesignModeClientConnectShouldSendTestMessageAndDiscoverCompleteOnTestPlatformExceptionInAttachmentsProcessing()
    {
        var payload = new TestRunAttachmentsProcessingPayload();
        var startAttachmentsProcessing = new Message { MessageType = MessageType.TestRunAttachmentsProcessingStart, Payload = JToken.FromObject(payload) };
        _mockCommunicationManager.Setup(cm => cm.WaitForServerConnection(It.IsAny<int>())).Returns(true);
        _mockCommunicationManager.SetupSequence(cm => cm.ReceiveMessage()).Returns(startAttachmentsProcessing);
        _mockCommunicationManager
            .Setup(cm => cm.SendMessage(MessageType.TestRunAttachmentsProcessingComplete, It.IsAny<TestRunAttachmentsProcessingCompletePayload>()))
            .Callback(() => _completeEvent.Set());
        _mockTestRequestManager.Setup(
                rm => rm.ProcessTestRunAttachments(
                    It.IsAny<TestRunAttachmentsProcessingPayload>(),
                    It.IsAny<ITestRunAttachmentsProcessingEventsHandler>(),
                    It.IsAny<ProtocolConfig>()))
            .Throws(new TestPlatformException("Hello world"));

        _designModeClient.ConnectToClientAndProcessRequests(PortNumber, _mockTestRequestManager.Object);

        Assert.IsTrue(_completeEvent.WaitOne(Timeout), "AttachmentsProcessing not completed.");
        _mockCommunicationManager.Verify(cm => cm.SendMessage(MessageType.TestMessage, It.IsAny<TestMessagePayload>()), Times.Once());
        _mockCommunicationManager.Verify(cm => cm.SendMessage(MessageType.TestRunAttachmentsProcessingComplete, It.Is<TestRunAttachmentsProcessingCompletePayload>(p => p.Attachments == null)), Times.Once());
    }

    [TestMethod]
    public void DesignModeClientConnectShouldCallRequestManagerForAttachmentsProcessingStart()
    {
        var payload = new TestRunAttachmentsProcessingPayload();
        var startAttachmentsProcessing = new Message { MessageType = MessageType.TestRunAttachmentsProcessingStart, Payload = JToken.FromObject(payload) };
        _mockCommunicationManager.Setup(cm => cm.WaitForServerConnection(It.IsAny<int>())).Returns(true);
        _mockCommunicationManager.SetupSequence(cm => cm.ReceiveMessage()).Returns(startAttachmentsProcessing);

        _mockTestRequestManager
            .Setup(
                rm => rm.ProcessTestRunAttachments(
                    It.IsAny<TestRunAttachmentsProcessingPayload>(),
                    It.IsAny<ITestRunAttachmentsProcessingEventsHandler>(),
                    It.IsAny<ProtocolConfig>()))
            .Callback(() => _completeEvent.Set());

        _designModeClient.ConnectToClientAndProcessRequests(PortNumber, _mockTestRequestManager.Object);

        Assert.IsTrue(_completeEvent.WaitOne(Timeout), "AttachmentsProcessing not completed.");
        _mockCommunicationManager.Verify(cm => cm.SendMessage(MessageType.TestMessage, It.IsAny<TestMessagePayload>()), Times.Never);
        _mockCommunicationManager.Verify(cm => cm.SendMessage(MessageType.TestRunAttachmentsProcessingComplete, It.IsAny<TestRunAttachmentsProcessingCompletePayload>()), Times.Never);
        _mockTestRequestManager.Verify(rm => rm.ProcessTestRunAttachments(It.IsAny<TestRunAttachmentsProcessingPayload>(), It.IsAny<TestRunAttachmentsProcessingEventsHandler>(), It.IsAny<ProtocolConfig>()));
    }

    [TestMethod]
    public void DesignModeClientConnectShouldCallRequestManagerForAttachmentsProcessingCancel()
    {
        var cancelAttachmentsProcessing = new Message { MessageType = MessageType.TestRunAttachmentsProcessingCancel };
        _mockCommunicationManager.Setup(cm => cm.WaitForServerConnection(It.IsAny<int>())).Returns(true);
        _mockCommunicationManager.SetupSequence(cm => cm.ReceiveMessage()).Returns(cancelAttachmentsProcessing);

        _designModeClient.ConnectToClientAndProcessRequests(PortNumber, _mockTestRequestManager.Object);

        _mockCommunicationManager.Verify(cm => cm.SendMessage(MessageType.TestMessage, It.IsAny<TestMessagePayload>()), Times.Never);
        _mockCommunicationManager.Verify(cm => cm.SendMessage(MessageType.TestRunAttachmentsProcessingComplete, It.IsAny<TestRunAttachmentsProcessingCompletePayload>()), Times.Never);
        _mockTestRequestManager.Verify(rm => rm.CancelTestRunAttachmentsProcessing());
    }

    [TestMethod]
    public void DesignModeClientConnectShouldSendTestMessageAndExecutionCompleteOnExceptionInTestRun()
    {
        var payload = new TestRunRequestPayload();
        var testRunAll = new Message { MessageType = MessageType.TestRunAllSourcesWithDefaultHost, Payload = JToken.FromObject(payload) };
        _mockCommunicationManager.Setup(cm => cm.WaitForServerConnection(It.IsAny<int>())).Returns(true);
        _mockCommunicationManager.SetupSequence(cm => cm.ReceiveMessage()).Returns(testRunAll);
        _mockCommunicationManager
            .Setup(cm => cm.SendMessage(MessageType.ExecutionComplete, It.IsAny<TestRunCompletePayload>()))
            .Callback(() => _completeEvent.Set());
        _mockTestRequestManager.Setup(
            rm => rm.RunTests(
                It.IsAny<TestRunRequestPayload>(),
                null,
                It.IsAny<DesignModeTestEventsRegistrar>(),
                It.IsAny<ProtocolConfig>())).Throws(new Exception());

        _designModeClient.ConnectToClientAndProcessRequests(PortNumber, _mockTestRequestManager.Object);

        Assert.IsTrue(_completeEvent.WaitOne(Timeout), "Execution not completed.");
        _mockCommunicationManager.Verify(cm => cm.SendMessage(MessageType.TestMessage, It.IsAny<TestMessagePayload>()), Times.Once());
        _mockCommunicationManager.Verify(cm => cm.SendMessage(MessageType.ExecutionComplete, It.IsAny<TestRunCompletePayload>()), Times.Once());
    }

    [TestMethod]
    public void DesignModeClientConnectShouldSendTestMessageAndExecutionCompleteOnTestPlatformExceptionInTestRun()
    {
        var payload = new TestRunRequestPayload();
        var testRunAll = new Message { MessageType = MessageType.TestRunAllSourcesWithDefaultHost, Payload = JToken.FromObject(payload) };
        _mockCommunicationManager.Setup(cm => cm.WaitForServerConnection(It.IsAny<int>())).Returns(true);
        _mockCommunicationManager.SetupSequence(cm => cm.ReceiveMessage()).Returns(testRunAll);
        _mockCommunicationManager
            .Setup(cm => cm.SendMessage(MessageType.ExecutionComplete, It.IsAny<TestRunCompletePayload>()))
            .Callback(() => _completeEvent.Set());
        _mockTestRequestManager.Setup(
            rm => rm.RunTests(
                It.IsAny<TestRunRequestPayload>(),
                null,
                It.IsAny<DesignModeTestEventsRegistrar>(),
                It.IsAny<ProtocolConfig>())).Throws(new TestPlatformException("Hello world"));

        _designModeClient.ConnectToClientAndProcessRequests(PortNumber, _mockTestRequestManager.Object);

        Assert.IsTrue(_completeEvent.WaitOne(Timeout), "Execution not completed.");
        _mockCommunicationManager.Verify(cm => cm.SendMessage(MessageType.TestMessage, It.IsAny<TestMessagePayload>()), Times.Once());
        _mockCommunicationManager.Verify(cm => cm.SendMessage(MessageType.ExecutionComplete, It.IsAny<TestRunCompletePayload>()), Times.Once());
    }

    [TestMethod]
    public void DesignModeClientConnectShouldReturnNullSessionWhenStartTestSessionThrows()
    {
        var payload = new StartTestSessionPayload();
        var startTestSessionMessage = new Message()
        {
            MessageType = MessageType.StartTestSession,
            Payload = JToken.FromObject(payload)
        };

        _mockCommunicationManager.Setup(cm => cm.WaitForServerConnection(It.IsAny<int>())).Returns(true);
        _mockCommunicationManager.SetupSequence(cm => cm.ReceiveMessage()).Returns(startTestSessionMessage);
        _mockCommunicationManager.Setup(cm => cm.SendMessage(
                MessageType.StartTestSessionCallback,
                It.IsAny<StartTestSessionAckPayload>()))
            .Callback((string _, object actualPayload) =>
            {
                _completeEvent.Set();
                Assert.IsNull(((StartTestSessionAckPayload)actualPayload).TestSessionInfo);
            });
        _mockCommunicationManager.Setup(
                cm => cm.DeserializePayload<StartTestSessionPayload>(
                    startTestSessionMessage))
            .Returns(payload);

        _mockTestRequestManager.Setup(
            rm => rm.StartTestSession(
                It.IsAny<StartTestSessionPayload>(),
                It.IsAny<ITestHostLauncher>(),
                It.IsAny<ITestSessionEventsHandler>(),
                It.IsAny<ProtocolConfig>())).Throws(new SettingsException("DummyException"));

        _designModeClient.ConnectToClientAndProcessRequests(PortNumber, _mockTestRequestManager.Object);

        Assert.IsTrue(_completeEvent.WaitOne(Timeout), "Start test session not completed.");
        _mockCommunicationManager.Verify(
            cm => cm.SendMessage(
                MessageType.StartTestSessionCallback,
                It.IsAny<StartTestSessionAckPayload>()),
            Times.Once());
    }

    [TestMethod]
    public void DesignModeClientConnectShouldReturnFalseWhenStopTestSessionThrows()
    {
        var mockTestPool = new Mock<TestSessionPool>();
        TestSessionPool.Instance = mockTestPool.Object;

        var testSessionInfo = new TestSessionInfo();
        var stopTestSessionMessage = new Message()
        {
            MessageType = MessageType.StopTestSession,
            Payload = JToken.FromObject(testSessionInfo)
        };

        _mockCommunicationManager.Setup(cm => cm.WaitForServerConnection(It.IsAny<int>())).Returns(true);
        _mockCommunicationManager.SetupSequence(cm => cm.ReceiveMessage()).Returns(stopTestSessionMessage);
        _mockCommunicationManager.Setup(cm => cm.SendMessage(
                MessageType.StopTestSessionCallback,
                It.IsAny<StopTestSessionAckPayload>()))
            .Callback((string _, object actualPayload) =>
            {
                _completeEvent.Set();

                Assert.AreEqual(((StopTestSessionAckPayload)actualPayload).TestSessionInfo, testSessionInfo);
                Assert.IsFalse(((StopTestSessionAckPayload)actualPayload).IsStopped);
            });

        _mockCommunicationManager.Setup(
                cm => cm.DeserializePayload<TestSessionInfo>(
                    stopTestSessionMessage))
            .Returns(testSessionInfo);

        mockTestPool.Setup(tp => tp.KillSession(testSessionInfo)).Throws(new Exception("DummyException"));

        _designModeClient.ConnectToClientAndProcessRequests(PortNumber, _mockTestRequestManager.Object);

        Assert.IsTrue(_completeEvent.WaitOne(Timeout), "Start test session not completed.");
        _mockCommunicationManager.Verify(
            cm => cm.SendMessage(
                MessageType.StopTestSessionCallback,
                It.IsAny<StopTestSessionAckPayload>()),
            Times.Once());
    }

    [TestMethod]
    public void DesignModeClientConnectShouldReturnFalseWhenStopTestSessionReturnsFalse()
    {
        var mockTestPool = new Mock<TestSessionPool>();
        TestSessionPool.Instance = mockTestPool.Object;

        var testSessionInfo = new TestSessionInfo();
        var stopTestSessionMessage = new Message()
        {
            MessageType = MessageType.StopTestSession,
            Payload = JToken.FromObject(testSessionInfo)
        };

        _mockCommunicationManager.Setup(cm => cm.WaitForServerConnection(It.IsAny<int>())).Returns(true);
        _mockCommunicationManager.SetupSequence(cm => cm.ReceiveMessage()).Returns(stopTestSessionMessage);
        _mockCommunicationManager.Setup(cm => cm.SendMessage(
                MessageType.StopTestSessionCallback,
                It.IsAny<StopTestSessionAckPayload>()))
            .Callback((string _, object actualPayload) =>
            {
                _completeEvent.Set();

                Assert.AreEqual(((StopTestSessionAckPayload)actualPayload).TestSessionInfo, testSessionInfo);
                Assert.IsFalse(((StopTestSessionAckPayload)actualPayload).IsStopped);
            });

        _mockCommunicationManager.Setup(
                cm => cm.DeserializePayload<TestSessionInfo>(
                    stopTestSessionMessage))
            .Returns(testSessionInfo);

        mockTestPool.Setup(tp => tp.KillSession(testSessionInfo)).Returns(false);

        _designModeClient.ConnectToClientAndProcessRequests(PortNumber, _mockTestRequestManager.Object);

        Assert.IsTrue(_completeEvent.WaitOne(Timeout), "Start test session not completed.");
        _mockCommunicationManager.Verify(
            cm => cm.SendMessage(
                MessageType.StopTestSessionCallback,
                It.IsAny<StopTestSessionAckPayload>()),
            Times.Once());
    }

    [TestMethod]
    public void DesignModeClientConnectShouldReturnTruenWhenStopTestSessionReturnsTrue()
    {
        var mockTestPool = new Mock<TestSessionPool>();
        TestSessionPool.Instance = mockTestPool.Object;

        var testSessionInfo = new TestSessionInfo();
        var stopTestSessionMessage = new Message()
        {
            MessageType = MessageType.StopTestSession,
            Payload = JToken.FromObject(testSessionInfo)
        };

        _mockCommunicationManager.Setup(cm => cm.WaitForServerConnection(It.IsAny<int>())).Returns(true);
        _mockCommunicationManager.SetupSequence(cm => cm.ReceiveMessage()).Returns(stopTestSessionMessage);
        _mockCommunicationManager.Setup(cm => cm.SendMessage(
                MessageType.StopTestSessionCallback,
                It.IsAny<StopTestSessionAckPayload>()))
            .Callback((string _, object actualPayload) =>
            {
                _completeEvent.Set();

                Assert.AreEqual(((StopTestSessionAckPayload)actualPayload).TestSessionInfo, testSessionInfo);
                Assert.IsTrue(((StopTestSessionAckPayload)actualPayload).IsStopped);
            });

        _mockCommunicationManager.Setup(
                cm => cm.DeserializePayload<TestSessionInfo>(
                    stopTestSessionMessage))
            .Returns(testSessionInfo);

        mockTestPool.Setup(tp => tp.KillSession(testSessionInfo)).Returns(true);

        _designModeClient.ConnectToClientAndProcessRequests(PortNumber, _mockTestRequestManager.Object);

        Assert.IsTrue(_completeEvent.WaitOne(Timeout), "Start test session not completed.");
        _mockCommunicationManager.Verify(
            cm => cm.SendMessage(
                MessageType.StopTestSessionCallback,
                It.IsAny<StopTestSessionAckPayload>()),
            Times.Once());
    }

    [TestMethod]
    public void DesignModeClientSendTestMessageShouldSendTestMessage()
    {
        var testPayload = new TestMessagePayload { MessageLevel = TestMessageLevel.Error, Message = "DummyMessage" };

        _designModeClient.SendTestMessage(testPayload.MessageLevel, testPayload.Message);

        _mockCommunicationManager.Verify(cm => cm.SendMessage(MessageType.TestMessage, It.IsAny<TestMessagePayload>()), Times.Once());
    }

    private class TestableDesignModeClient : DesignModeClient
    {
        internal TestableDesignModeClient(
            ICommunicationManager communicationManager,
            IDataSerializer dataSerializer,
            IEnvironment platformEnvironment)
            : base(communicationManager, dataSerializer, platformEnvironment)
        {
        }

        public void InvokeCustomHostLaunchAckCallback(int processId, string errorMessage)
        {
            var payload = new CustomHostLaunchAckPayload()
            {
                HostProcessId = processId,
                ErrorMessage = errorMessage
            };
            onCustomTestHostLaunchAckReceived?.Invoke(
                new Message() { MessageType = MessageType.CustomTestHostLaunchCallback, Payload = JToken.FromObject(payload) });
        }
    }
}