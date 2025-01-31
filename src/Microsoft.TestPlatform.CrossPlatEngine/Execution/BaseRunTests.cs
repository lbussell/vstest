// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Execution;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using CommunicationUtilities.Interfaces;

using Common.ExtensionFramework;
using Microsoft.VisualStudio.TestPlatform.Common.ExtensionFramework.Utilities;
using Common.Interfaces;
using Common.Telemetry;
using Microsoft.VisualStudio.TestPlatform.Common.Utilities;
using CommunicationUtilities;
using CoreUtilities.Tracing.Interfaces;
using Adapter;
using DataCollection.Interfaces;
using ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using ObjectModel.Engine;
using ObjectModel.Engine.ClientProtocol;
using ObjectModel.Logging;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Utilities;
using PlatformAbstractions;
using PlatformAbstractions.Interfaces;

using CrossPlatEngineResources = Resources.Resources;

/// <summary>
/// The base run tests.
/// </summary>
internal abstract class BaseRunTests
{
    #region private fields

    private readonly ITestEventsPublisher _testEventsPublisher;
    private protected string _package;
    private readonly IRequestData _requestData;

    /// <summary>
    /// Specifies that the test run cancellation is requested
    /// </summary>
    private volatile bool _isCancellationRequested;

    /// <summary>
    /// Active executor which is executing the tests currently
    /// </summary>
    private ITestExecutor _activeExecutor;
    private readonly ITestCaseEventsHandler _testCaseEventsHandler;
    private readonly ITestPlatformEventSource _testPlatformEventSource;

    /// <summary>
    /// To create thread in given apartment state.
    /// </summary>
    private readonly IThread _platformThread;

    /// <summary>
    /// The Run configuration. To determine framework and execution thread apartment state.
    /// </summary>
    private RunConfiguration _runConfiguration;

    /// <summary>
    /// The Serializer to clone testcase object in case of user input test source is package. E.g UWP scenario(appx/build.appxrecipe).
    /// </summary>
    private readonly IDataSerializer _dataSerializer;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseRunTests"/> class.
    /// </summary>
    /// <param name="requestData">The request data for providing common execution services and data</param>
    /// <param name="package">The user input test source(package) if it differs from actual test source otherwise null.</param>
    /// <param name="runSettings">The run settings.</param>
    /// <param name="testExecutionContext">The test execution context.</param>
    /// <param name="testCaseEventsHandler">The test case events handler.</param>
    /// <param name="testRunEventsHandler">The test run events handler.</param>
    /// <param name="testPlatformEventSource">Test platform event source.</param>
    protected BaseRunTests(
        IRequestData requestData,
        string package,
        string runSettings,
        TestExecutionContext testExecutionContext,
        ITestCaseEventsHandler testCaseEventsHandler,
        ITestRunEventsHandler testRunEventsHandler,
        ITestPlatformEventSource testPlatformEventSource)
        : this(
            requestData,
            package,
            runSettings,
            testExecutionContext,
            testCaseEventsHandler,
            testRunEventsHandler,
            testPlatformEventSource,
            testCaseEventsHandler as ITestEventsPublisher,
            new PlatformThread(),
            JsonDataSerializer.Instance)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseRunTests"/> class.
    /// </summary>
    /// <param name="requestData">Provides services and data for execution</param>
    /// <param name="package">The user input test source(package) list if it differs from actual test source otherwise null.</param>
    /// <param name="runSettings">The run settings.</param>
    /// <param name="testExecutionContext">The test execution context.</param>
    /// <param name="testCaseEventsHandler">The test case events handler.</param>
    /// <param name="testRunEventsHandler">The test run events handler.</param>
    /// <param name="testPlatformEventSource">Test platform event source.</param>
    /// <param name="testEventsPublisher">Publisher for test events.</param>
    /// <param name="platformThread">Platform Thread.</param>
    /// <param name="dataSerializer">Data Serializer for cloning TestCase and test results object.</param>
    protected BaseRunTests(
        IRequestData requestData,
        string package,
        string runSettings,
        TestExecutionContext testExecutionContext,
        ITestCaseEventsHandler testCaseEventsHandler,
        ITestRunEventsHandler testRunEventsHandler,
        ITestPlatformEventSource testPlatformEventSource,
        ITestEventsPublisher testEventsPublisher,
        IThread platformThread,
        IDataSerializer dataSerializer)
    {
        _package = package;
        RunSettings = runSettings;
        TestExecutionContext = testExecutionContext;
        _testCaseEventsHandler = testCaseEventsHandler;
        TestRunEventsHandler = testRunEventsHandler;
        _requestData = requestData;

        _isCancellationRequested = false;
        _testPlatformEventSource = testPlatformEventSource;
        _testEventsPublisher = testEventsPublisher;
        _platformThread = platformThread;
        _dataSerializer = dataSerializer;
        SetContext();
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the run settings.
    /// </summary>
    protected string RunSettings { get; private set; }

    /// <summary>
    /// Gets the test execution context.
    /// </summary>
    protected TestExecutionContext TestExecutionContext { get; private set; }

    /// <summary>
    /// Gets the test run events handler.
    /// </summary>
    protected ITestRunEventsHandler TestRunEventsHandler { get; private set; }

    /// <summary>
    /// Gets the test run cache.
    /// </summary>
    protected ITestRunCache TestRunCache { get; private set; }

    protected bool IsCancellationRequested => _isCancellationRequested;

    protected RunContext RunContext { get; private set; }

    protected FrameworkHandle FrameworkHandle { get; private set; }

    protected ICollection<string> ExecutorUrisThatRanTests { get; private set; }

    #endregion

    #region Public methods

    public void RunTests()
    {
        using (TestRunCache)
        {
            TimeSpan elapsedTime = TimeSpan.Zero;

            Exception exception = null;
            bool isAborted = false;
            bool shutdownAfterRun = false;

            try
            {
                // Call Session-Start event on in-proc datacollectors
                SendSessionStart();

                elapsedTime = RunTestsInternal();

                // Check the adapter setting for shutting down this process after run
                shutdownAfterRun = FrameworkHandle.EnableShutdownAfterTestRun;
            }
            catch (Exception ex)
            {
                if (EqtTrace.IsErrorEnabled)
                {
                    EqtTrace.Error("BaseRunTests.RunTests: Failed to run the tests. Reason: {0}.", ex);
                }

                exception = new Exception(ex.Message, ex.InnerException);

                isAborted = true;
            }
            finally
            {
                // Trigger Session End on in-proc datacollectors
                SendSessionEnd();

                try
                {
                    // Send the test run complete event.
                    RaiseTestRunComplete(exception, _isCancellationRequested, isAborted, shutdownAfterRun, elapsedTime);
                }
                catch (Exception ex2)
                {
                    if (EqtTrace.IsErrorEnabled)
                    {
                        EqtTrace.Error("BaseRunTests.RunTests: Failed to raise runCompletion error. Reason: {0}.", ex2);
                    }

                    // TODO : this does not crash the process currently because of the job queue.
                    // Let the process crash
                    throw;
                }
            }
        }

        EqtTrace.Verbose("BaseRunTests.RunTests: Run is complete.");
    }

    internal void Abort()
    {
        EqtTrace.Verbose("BaseRunTests.Abort: Calling RaiseTestRunComplete");
        RaiseTestRunComplete(exception: null, canceled: _isCancellationRequested, aborted: true, adapterHintToShutdownAfterRun: false, elapsedTime: TimeSpan.Zero);
    }

    /// <summary>
    /// Cancel the current run by setting cancellation token for active executor
    /// </summary>
    internal void Cancel()
    {
        // Note: Test host delegates the cancellation to active executor and doesn't call HandleTestRunComplete in cancel request.
        // Its expected from active executor to respect the cancel request and thus return from RunTests quickly (canceling the tests).
        _isCancellationRequested = true;

        if (_activeExecutor == null)
        {
            return;
        }

        if (NotRequiredStaThread() || !TryToRunInStaThread(() => CancelTestRunInternal(_activeExecutor), false))
        {
            Task.Run(() => CancelTestRunInternal(_activeExecutor));
        }
    }

    #region Abstract methods

    protected abstract void BeforeRaisingTestRunComplete(bool exceptionsHitDuringRunTests);

    protected abstract IEnumerable<Tuple<Uri, string>> GetExecutorUriExtensionMap(
        IFrameworkHandle testExecutorFrameworkHandle,
        RunContext runContext);

    protected abstract void InvokeExecutor(
        LazyExtension<ITestExecutor, ITestExecutorCapabilities> executor,
        Tuple<Uri, string> executorUriExtensionTuple,
        RunContext runContext,
        IFrameworkHandle frameworkHandle);

    /// <summary>
    /// Asks the adapter about attaching the debugger to the default test host.
    /// </summary>
    /// <param name="executor">The executor used to run the tests.</param>
    /// <param name="executorUriExtensionTuple">The executor URI.</param>
    /// <param name="runContext">The run context.</param>
    /// <returns>
    /// <see cref="true"/> if must attach the debugger to the default test host,
    /// <see cref="false"/> otherwise.
    /// </returns>
    protected abstract bool ShouldAttachDebuggerToTestHost(
        LazyExtension<ITestExecutor, ITestExecutorCapabilities> executor,
        Tuple<Uri, string> executorUriExtensionTuple,
        RunContext runContext);

    protected abstract void SendSessionStart();

    protected abstract void SendSessionEnd();

    #endregion

    private void CancelTestRunInternal(ITestExecutor executor)
    {
        try
        {
            executor.Cancel();
        }
        catch (Exception e)
        {
            EqtTrace.Info("{0}.Cancel threw an exception: {1} ", executor.GetType().FullName, e);
        }
    }
    #endregion

    #region Private methods

    private void SetContext()
    {
        TestRunCache = new TestRunCache(TestExecutionContext.FrequencyOfRunStatsChangeEvent, TestExecutionContext.RunStatsChangeEventTimeout, OnCacheHit);

        RunContext = new RunContext();
        RunContext.RunSettings = RunSettingsUtilities.CreateAndInitializeRunSettings(RunSettings);
        RunContext.KeepAlive = TestExecutionContext.KeepAlive;
        RunContext.InIsolation = TestExecutionContext.InIsolation;
        RunContext.IsDataCollectionEnabled = TestExecutionContext.IsDataCollectionEnabled;
        RunContext.IsBeingDebugged = TestExecutionContext.IsDebug;

        var runConfig = XmlRunSettingsUtilities.GetRunConfigurationNode(RunSettings);
        RunContext.TestRunDirectory = RunSettingsUtilities.GetTestResultsDirectory(runConfig);
        RunContext.SolutionDirectory = RunSettingsUtilities.GetSolutionDirectory(runConfig);
        _runConfiguration = runConfig;

        FrameworkHandle = new FrameworkHandle(
            _testCaseEventsHandler,
            TestRunCache,
            TestExecutionContext,
            TestRunEventsHandler);
        FrameworkHandle.TestRunMessage += OnTestRunMessage;

        ExecutorUrisThatRanTests = new List<string>();
    }

    private void OnTestRunMessage(object sender, TestRunMessageEventArgs e)
    {
        TestRunEventsHandler.HandleLogMessage(e.Level, e.Message);
    }

    private TimeSpan RunTestsInternal()
    {
        long totalTests = 0;

        var executorUriExtensionMap = GetExecutorUriExtensionMap(FrameworkHandle, RunContext);

        // Set on the logger the TreatAdapterErrorAsWarning setting from runsettings.
        SetAdapterLoggingSettings();

        var stopwatch = new Stopwatch();
        stopwatch.Start();
        _testPlatformEventSource.ExecutionStart();
        var exceptionsHitDuringRunTests = RunTestInternalWithExecutors(
            executorUriExtensionMap,
            totalTests);

        stopwatch.Stop();
        _testPlatformEventSource.ExecutionStop(TestRunCache.TotalExecutedTests);
        BeforeRaisingTestRunComplete(exceptionsHitDuringRunTests);
        return stopwatch.Elapsed;
    }

    private bool RunTestInternalWithExecutors(IEnumerable<Tuple<Uri, string>> executorUriExtensionMap, long totalTests)
    {
        // Collecting Total Number of Adapters Discovered in Machine.
        _requestData.MetricsCollection.Add(TelemetryDataConstants.NumberOfAdapterDiscoveredDuringExecution, executorUriExtensionMap.Count());

        var attachedToTestHost = false;
        var executorCache = new Dictionary<string, LazyExtension<ITestExecutor, ITestExecutorCapabilities>>();
        foreach (var executorUriExtensionTuple in executorUriExtensionMap)
        {
            // Avoid processing the same executor twice.
            if (executorCache.ContainsKey(executorUriExtensionTuple.Item1.AbsoluteUri))
            {
                continue;
            }

            // Get the extension manager.
            var extensionManager = GetExecutorExtensionManager(executorUriExtensionTuple.Item2);

            // Look up the executor.
            var executor = extensionManager.TryGetTestExtension(executorUriExtensionTuple.Item1);
            if (executor == null)
            {
                // Commenting this out because of a compatibility issue with Microsoft.Dotnet.ProjectModel released on nuGet.org.
                // this.activeExecutor = null;
                // var runtimeVersion = string.Concat(PlatformServices.Default.Runtime.RuntimeType, " ",	
                // PlatformServices.Default.Runtime.RuntimeVersion);
                var runtimeVersion = " ";
                TestRunEventsHandler?.HandleLogMessage(
                    TestMessageLevel.Warning,
                    string.Format(
                        CultureInfo.CurrentUICulture,
                        CrossPlatEngineResources.NoMatchingExecutor,
                        executorUriExtensionTuple.Item1.AbsoluteUri,
                        runtimeVersion));

                continue;
            }

            // Cache the executor.
            executorCache.Add(executorUriExtensionTuple.Item1.AbsoluteUri, executor);

            // Check if we actually have to attach to the default test host.
            if (!RunContext.IsBeingDebugged || attachedToTestHost)
            {
                // We already know we should attach to the default test host, simply continue.
                continue;
            }

            // If there's at least one adapter in the filtered adapters list that doesn't
            // implement the new test executor interface, we should attach to the default test
            // host by default.
            // Same goes if all adapters implement the new test executor interface but at
            // least one of them needs the test platform to attach to the default test host.
            if (!(executor.Value is ITestExecutor2)
                || ShouldAttachDebuggerToTestHost(executor, executorUriExtensionTuple, RunContext))
            {
                EqtTrace.Verbose("Attaching to default test host.");

                attachedToTestHost = true;
                var pid = Process.GetCurrentProcess().Id;
                if (!FrameworkHandle.AttachDebuggerToProcess(pid))
                {
                    EqtTrace.Warning(
                        string.Format(
                            CultureInfo.CurrentUICulture,
                            CrossPlatEngineResources.AttachDebuggerToDefaultTestHostFailure,
                            pid));
                }
            }
        }


        // Call the executor for each group of tests.
        var exceptionsHitDuringRunTests = false;
        var executorsFromDeprecatedLocations = false;
        double totalTimeTakenByAdapters = 0;

        foreach (var executorUriExtensionTuple in executorUriExtensionMap)
        {
            var executorUri = executorUriExtensionTuple.Item1.AbsoluteUri;
            // Get the executor from the cache.
            if (!executorCache.TryGetValue(executorUri, out var executor))
            {
                continue;
            }

            try
            {
                if (EqtTrace.IsVerboseEnabled)
                {
                    EqtTrace.Verbose(
                        "BaseRunTests.RunTestInternalWithExecutors: Running tests for {0}",
                        executor.Metadata.ExtensionUri);
                }

                // set the active executor
                _activeExecutor = executor.Value;

                // If test run cancellation is requested, skip the next executor
                if (_isCancellationRequested)
                {
                    break;
                }

                var timeStartNow = DateTime.UtcNow;

                var currentTotalTests = TestRunCache.TotalExecutedTests;
                _testPlatformEventSource.AdapterExecutionStart(executorUri);

                // Run the tests.
                if (NotRequiredStaThread() || !TryToRunInStaThread(() => InvokeExecutor(executor, executorUriExtensionTuple, RunContext, FrameworkHandle), true))
                {
                    InvokeExecutor(executor, executorUriExtensionTuple, RunContext, FrameworkHandle);
                }

                _testPlatformEventSource.AdapterExecutionStop(TestRunCache.TotalExecutedTests - currentTotalTests);

                var totalTimeTaken = DateTime.UtcNow - timeStartNow;

                // Identify whether the executor did run any tests at all
                if (TestRunCache.TotalExecutedTests > totalTests)
                {
                    ExecutorUrisThatRanTests.Add(executorUri);

                    // Collecting Total Tests Ran by each Adapter
                    var totalTestRun = TestRunCache.TotalExecutedTests - totalTests;
                    _requestData.MetricsCollection.Add(string.Format("{0}.{1}", TelemetryDataConstants.TotalTestsRanByAdapter, executorUri), totalTestRun);

                    // Only enable this for MSTestV1 telemetry for now, this might become more generic later.
                    if (MsTestV1TelemetryHelper.IsMsTestV1Adapter(executorUri))
                    {
                        foreach (var adapterMetrics in TestRunCache.AdapterTelemetry.Keys.Where(k => k.StartsWith(executorUri)))
                        {
                            var value = TestRunCache.AdapterTelemetry[adapterMetrics];

                            _requestData.MetricsCollection.Add(string.Format("{0}.{1}", TelemetryDataConstants.TotalTestsRunByMSTestv1, adapterMetrics), value);
                        }
                    }

                    if (!CrossPlatEngine.Constants.DefaultAdapters.Contains(executor.Metadata.ExtensionUri, StringComparer.OrdinalIgnoreCase))
                    {
                        var executorLocation = executor.Value.GetType().GetTypeInfo().Assembly.GetAssemblyLocation();

                        executorsFromDeprecatedLocations |= Path.GetDirectoryName(executorLocation).Equals(CrossPlatEngine.Constants.DefaultAdapterLocation);
                    }

                    totalTests = TestRunCache.TotalExecutedTests;
                }

                if (EqtTrace.IsVerboseEnabled)
                {
                    EqtTrace.Verbose(
                        "BaseRunTests.RunTestInternalWithExecutors: Completed running tests for {0}",
                        executor.Metadata.ExtensionUri);
                }

                // Collecting Time Taken by each executor Uri
                _requestData.MetricsCollection.Add(string.Format("{0}.{1}", TelemetryDataConstants.TimeTakenToRunTestsByAnAdapter, executorUri), totalTimeTaken.TotalSeconds);
                totalTimeTakenByAdapters += totalTimeTaken.TotalSeconds;
            }
            catch (Exception e)
            {
                string exceptionMessage = (e is UnauthorizedAccessException)
                    ? string.Format(CultureInfo.CurrentCulture, CrossPlatEngineResources.AccessDenied, e.Message)
                    : ExceptionUtilities.GetExceptionMessage(e);

                exceptionsHitDuringRunTests = true;

                if (EqtTrace.IsErrorEnabled)
                {
                    EqtTrace.Error(
                        "BaseRunTests.RunTestInternalWithExecutors: An exception occurred while invoking executor {0}. {1}.",
                        executorUriExtensionTuple.Item1,
                        e);
                }

                TestRunEventsHandler?.HandleLogMessage(
                    TestMessageLevel.Error,
                    string.Format(
                        CultureInfo.CurrentCulture,
                        CrossPlatEngineResources.ExceptionFromRunTests,
                        executorUriExtensionTuple.Item1,
                        exceptionMessage));
            }
            finally
            {
                _activeExecutor = null;
            }
        }

        // Collecting Total Time Taken by Adapters
        _requestData.MetricsCollection.Add(TelemetryDataConstants.TimeTakenByAllAdaptersInSec, totalTimeTakenByAdapters);

        if (executorsFromDeprecatedLocations)
        {
            TestRunEventsHandler?.HandleLogMessage(TestMessageLevel.Warning, string.Format(CultureInfo.CurrentCulture, CrossPlatEngineResources.DeprecatedAdapterPath));
        }

        return exceptionsHitDuringRunTests;
    }

    private bool NotRequiredStaThread()
    {
        return _runConfiguration.ExecutionThreadApartmentState != PlatformApartmentState.STA;
    }

    private TestExecutorExtensionManager GetExecutorExtensionManager(string extensionAssembly)
    {
        try
        {
            if (string.IsNullOrEmpty(extensionAssembly)
                || string.Equals(extensionAssembly, Constants.UnspecifiedAdapterPath))
            {
                // full execution. Since the extension manager is cached this can be created multiple times without harming performance.
                return TestExecutorExtensionManager.Create();
            }
            else
            {
                return TestExecutorExtensionManager.GetExecutionExtensionManager(extensionAssembly);
            }
        }
        catch (Exception ex)
        {
            EqtTrace.Error(
                "BaseRunTests: GetExecutorExtensionManager: Exception occurred while loading extensions {0}",
                ex);

            return null;
        }
    }

    private void SetAdapterLoggingSettings()
    {
        // TODO: enable the below once runsettings is in.
        // var sessionMessageLogger = testExecutorFrameworkHandle as TestSessionMessageLogger;
        // if (sessionMessageLogger != null
        //        && testExecutionContext != null
        //        && testExecutionContext.TestRunConfiguration != null)
        // {
        //    sessionMessageLogger.TreatTestAdapterErrorsAsWarnings
        //        = testExecutionContext.TestRunConfiguration.TreatTestAdapterErrorsAsWarnings;
        // }
    }

    private void RaiseTestRunComplete(
        Exception exception,
        bool canceled,
        bool aborted,
        bool adapterHintToShutdownAfterRun,
        TimeSpan elapsedTime)
    {
        var runStats = TestRunCache?.TestRunStatistics ?? new TestRunStatistics(new Dictionary<TestOutcome, long>());
        var lastChunkTestResults = TestRunCache?.GetLastChunk() ?? new List<TestResult>();

        if (TestRunEventsHandler != null)
        {
            // Collecting Total Tests Run
            _requestData.MetricsCollection.Add(TelemetryDataConstants.TotalTestsRun, runStats.ExecutedTests);

            // Collecting Test Run State
            _requestData.MetricsCollection.Add(TelemetryDataConstants.RunState, canceled ? "Canceled" : (aborted ? "Aborted" : "Completed"));

            // Collecting Number of Adapters Used to run tests.
            _requestData.MetricsCollection.Add(TelemetryDataConstants.NumberOfAdapterUsedToRunTests, ExecutorUrisThatRanTests.Count);

            if (lastChunkTestResults.Any() && IsTestSourceIsPackage())
            {
                UpdateTestCaseSourceToPackage(lastChunkTestResults, null, out lastChunkTestResults, out _);
            }

            var testRunChangedEventArgs = new TestRunChangedEventArgs(runStats, lastChunkTestResults, Enumerable.Empty<TestCase>());

            // Adding Metrics along with Test Run Complete Event Args
            Collection<AttachmentSet> attachments = FrameworkHandle?.Attachments;
            var testRunCompleteEventArgs = new TestRunCompleteEventArgs(
                runStats,
                canceled,
                aborted,
                exception,
                attachments,
                // Today we don't offer an extension to run collectors for test adapters.
                new Collection<InvokedDataCollector>(),
                elapsedTime);
            testRunCompleteEventArgs.Metrics = _requestData.MetricsCollection.Metrics;

            TestRunEventsHandler.HandleTestRunComplete(
                testRunCompleteEventArgs,
                testRunChangedEventArgs,
                attachments,
                ExecutorUrisThatRanTests);
        }
        else
        {
            EqtTrace.Warning("Could not pass run completion as the callback is null. Aborted :{0}", aborted);
        }
    }

    private bool IsTestSourceIsPackage()
    {
        return !string.IsNullOrEmpty(_package);
    }

    private void OnCacheHit(TestRunStatistics testRunStats, ICollection<TestResult> results, ICollection<TestCase> inProgressTestCases)
    {
        if (TestRunEventsHandler != null)
        {
            if (IsTestSourceIsPackage())
            {
                UpdateTestCaseSourceToPackage(results, inProgressTestCases, out results, out inProgressTestCases);
            }

            var testRunChangedEventArgs = new TestRunChangedEventArgs(testRunStats, results, inProgressTestCases);
            TestRunEventsHandler.HandleTestRunStatsChange(testRunChangedEventArgs);
        }
        else
        {
            if (EqtTrace.IsErrorEnabled)
            {
                EqtTrace.Error("BaseRunTests.OnCacheHit: Unable to send TestRunStatsChange Event as TestRunEventsHandler is NULL");
            }
        }
    }

    private bool TryToRunInStaThread(Action action, bool waitForCompletion)
    {
        bool success = true;
        try
        {
            EqtTrace.Verbose("BaseRunTests.TryToRunInSTAThread: Using STA thread to call adapter API.");
            _platformThread.Run(action, PlatformApartmentState.STA, waitForCompletion);
        }
        catch (ThreadApartmentStateNotSupportedException ex)
        {
            success = false;
            EqtTrace.Warning("BaseRunTests.TryToRunInSTAThread: Failed to run in STA thread: {0}", ex);
            TestRunEventsHandler.HandleLogMessage(
                TestMessageLevel.Warning,
                string.Format(CultureInfo.CurrentUICulture, CrossPlatEngineResources.ExecutionThreadApartmentStateNotSupportedForFramework, _runConfiguration.TargetFramework.ToString()));
        }

        return success;
    }

    private void UpdateTestCaseSourceToPackage(
        ICollection<TestResult> testResults,
        ICollection<TestCase> inProgressTestCases,
        out ICollection<TestResult> updatedTestResults,
        out ICollection<TestCase> updatedInProgressTestCases)
    {
        EqtTrace.Verbose("BaseRunTests.UpdateTestCaseSourceToPackage: Update source details for testResults and testCases.");

        updatedTestResults = UpdateTestResults(testResults, _package);
        updatedInProgressTestCases = UpdateInProgressTests(inProgressTestCases, _package);
    }

    private ICollection<TestResult> UpdateTestResults(ICollection<TestResult> testResults, string package)
    {
        ICollection<TestResult> updatedTestResults = new List<TestResult>();

        foreach (var testResult in testResults)
        {
            var updatedTestResult = _dataSerializer.Clone(testResult);
            updatedTestResult.TestCase.Source = package;
            updatedTestResults.Add(updatedTestResult);
        }

        return updatedTestResults;
    }

    private ICollection<TestCase> UpdateInProgressTests(ICollection<TestCase> inProgressTestCases, string package)
    {
        if (inProgressTestCases == null)
        {
            return null;
        }

        ICollection<TestCase> updatedTestCases = new List<TestCase>();
        foreach (var inProgressTestCase in inProgressTestCases)
        {
            var updatedTestCase = _dataSerializer.Clone(inProgressTestCase);
            updatedTestCase.Source = package;
            updatedTestCases.Add(updatedTestCase);
        }

        return updatedTestCases;
    }

    #endregion
}