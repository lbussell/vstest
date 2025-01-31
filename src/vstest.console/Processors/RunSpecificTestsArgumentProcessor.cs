// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.TestPlatform.CommandLine.Processors;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Linq;

using Common;
using Common.Interfaces;
using Microsoft.VisualStudio.TestPlatform.Common.Utilities;
using Client.RequestHelper;
using Internal;
using TestPlatformHelpers;
using ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.Utilities;
using CommandLineResources = Resources.Resources;

internal class RunSpecificTestsArgumentProcessor : IArgumentProcessor
{
    public const string CommandName = "/Tests";

    private Lazy<IArgumentProcessorCapabilities> _metadata;

    private Lazy<IArgumentExecutor> _executor;

    public Lazy<IArgumentProcessorCapabilities> Metadata
    {
        get
        {
            if (_metadata == null)
            {
                _metadata = new Lazy<IArgumentProcessorCapabilities>(() => new RunSpecificTestsArgumentProcessorCapabilities());
            }

            return _metadata;
        }
    }

    public Lazy<IArgumentExecutor> Executor
    {
        get
        {
            if (_executor == null)
            {
                _executor = new Lazy<IArgumentExecutor>(() =>
                    new RunSpecificTestsArgumentExecutor(
                        CommandLineOptions.Instance,
                        RunSettingsManager.Instance,
                        TestRequestManager.Instance,
                        ConsoleOutput.Instance));
            }

            return _executor;
        }

        set
        {
            _executor = value;
        }
    }
}

internal class RunSpecificTestsArgumentProcessorCapabilities : BaseArgumentProcessorCapabilities
{
    public override string CommandName => RunSpecificTestsArgumentProcessor.CommandName;

    public override bool IsAction => true;

    public override bool AllowMultiple => false;

    public override string HelpContentResourceName => CommandLineResources.RunSpecificTestsHelp;

    public override HelpContentPriority HelpPriority => HelpContentPriority.RunSpecificTestsArgumentProcessorHelpPriority;

    public override ArgumentProcessorPriority Priority => ArgumentProcessorPriority.Normal;
}

internal class RunSpecificTestsArgumentExecutor : IArgumentExecutor
{
    public const char SplitDelimiter = ',';
    public const char EscapeDelimiter = '\\';

    #region Fields

    /// <summary>
    /// Used for getting sources.
    /// </summary>
    private readonly CommandLineOptions _commandLineOptions;

    /// <summary>
    /// The instance of testPlatforms
    /// </summary>
    private readonly ITestRequestManager _testRequestManager;

    /// <summary>
    /// Used for sending output.
    /// </summary>
    internal IOutput Output;

    /// <summary>
    /// RunSettingsManager to get currently active run settings.
    /// </summary>
    private readonly IRunSettingsProvider _runSettingsManager;

    /// <summary>
    /// Given Collection of strings for filtering test cases
    /// </summary>
    private Collection<string> _selectedTestNames;

    /// <summary>
    /// Used for tracking the total no. of tests discovered from the given sources.
    /// </summary>
    private long _discoveredTestCount = 0;

    /// <summary>
    /// Collection of test cases that match at least one of the given search strings
    /// </summary>
    private readonly Collection<TestCase> _selectedTestCases = new();

    /// <summary>
    /// Effective run settings applicable to test run after inferring the multi-targeting settings.
    /// </summary>
    private string _effectiveRunSettings = null;

    /// <summary>
    /// List of filters that have not yet been discovered
    /// </summary>
    HashSet<string> _undiscoveredFilters = new();

    /// <summary>
    /// Registers for discovery events during discovery
    /// </summary>
    private readonly ITestDiscoveryEventsRegistrar _discoveryEventsRegistrar;

    /// <summary>
    /// Registers and Unregisters for test run events before and after test run
    /// </summary>
    private readonly ITestRunEventsRegistrar _testRunEventsRegistrar;

    #endregion

    #region Constructor

    /// <summary>
    /// Default constructor.
    /// </summary>
    public RunSpecificTestsArgumentExecutor(
        CommandLineOptions options,
        IRunSettingsProvider runSettingsProvider,
        ITestRequestManager testRequestManager,
        IOutput output)
    {
        Contract.Requires(options != null);
        Contract.Requires(testRequestManager != null);

        _commandLineOptions = options;
        _testRequestManager = testRequestManager;

        _runSettingsManager = runSettingsProvider;
        Output = output;
        _discoveryEventsRegistrar = new DiscoveryEventsRegistrar(DiscoveryRequest_OnDiscoveredTests);
        _testRunEventsRegistrar = new TestRunRequestEventsRegistrar(Output, _commandLineOptions);
    }

    #endregion

    #region IArgumentProcessor

    /// <summary>
    /// Splits given the search strings and adds to selectTestNamesCollection.
    /// </summary>
    /// <param name="argument"></param>
    public void Initialize(string argument)
    {
        if (!string.IsNullOrWhiteSpace(argument))
        {
            _selectedTestNames = new Collection<string>(
                argument.Tokenize(SplitDelimiter, EscapeDelimiter)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(s => s.Trim()).ToList());
        }

        if (_selectedTestNames == null || _selectedTestNames.Count <= 0)
        {
            throw new CommandLineException(CommandLineResources.SpecificTestsRequired);
        }

        // by default all filters are not discovered on launch
        _undiscoveredFilters = new HashSet<string>(_selectedTestNames);
    }

    /// <summary>
    /// Execute specific tests that match any of the given strings.
    /// </summary>
    /// <returns></returns>
    public ArgumentProcessorResult Execute()
    {
        Contract.Assert(Output != null);
        Contract.Assert(_commandLineOptions != null);
        Contract.Assert(_testRequestManager != null);
        Contract.Assert(!string.IsNullOrWhiteSpace(_runSettingsManager.ActiveRunSettings.SettingsXml));

        if (!_commandLineOptions.Sources.Any())
        {
            throw new CommandLineException(string.Format(CultureInfo.CurrentUICulture, CommandLineResources.MissingTestSourceFile));
        }

        _effectiveRunSettings = _runSettingsManager.ActiveRunSettings.SettingsXml;

        // Discover tests from sources and filter on every discovery reported.
        DiscoverTestsAndSelectSpecified(_commandLineOptions.Sources);

        // Now that tests are discovered and filtered, we run only those selected tests.
        ExecuteSelectedTests();

        bool treatNoTestsAsError = RunSettingsUtilities.GetTreatNoTestsAsError(_effectiveRunSettings);

        return treatNoTestsAsError && _selectedTestCases.Count == 0 ? ArgumentProcessorResult.Fail : ArgumentProcessorResult.Success;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Discovers tests from the given sources and selects only specified tests.
    /// </summary>
    /// <param name="sources"> Test source assemblies paths. </param>
    private void DiscoverTestsAndSelectSpecified(IEnumerable<string> sources)
    {
        Output.WriteLine(CommandLineResources.StartingDiscovery, OutputLevel.Information);
        if (!string.IsNullOrEmpty(EqtTrace.LogFile))
        {
            Output.Information(false, CommandLineResources.VstestDiagLogOutputPath, EqtTrace.LogFile);
        }

        _testRequestManager.DiscoverTests(
            new DiscoveryRequestPayload() { Sources = sources, RunSettings = _effectiveRunSettings }, _discoveryEventsRegistrar, Constants.DefaultProtocolConfig);
    }

    /// <summary>
    ///  Executes the selected tests
    /// </summary>
    private void ExecuteSelectedTests()
    {
        if (_selectedTestCases.Count > 0)
        {
            if (_undiscoveredFilters.Count != 0)
            {
                string missingFilters = string.Join(", ", _undiscoveredFilters);
                string warningMessage = string.Format(CultureInfo.CurrentCulture, CommandLineResources.SomeTestsUnavailableAfterFiltering, _discoveredTestCount, missingFilters);
                Output.Warning(false, warningMessage);
            }

            // for command line keep alive is always false.
            bool keepAlive = false;

            EqtTrace.Verbose("RunSpecificTestsArgumentProcessor:Execute: Test run is queued.");
            var runRequestPayload = new TestRunRequestPayload() { TestCases = _selectedTestCases.ToList(), RunSettings = _effectiveRunSettings, KeepAlive = keepAlive, TestPlatformOptions = new TestPlatformOptions() { TestCaseFilter = _commandLineOptions.TestCaseFilterValue } };
            _testRequestManager.RunTests(runRequestPayload, null, _testRunEventsRegistrar, Constants.DefaultProtocolConfig);
        }
        else
        {
            string warningMessage;
            if (_discoveredTestCount > 0)
            {
                // No tests that matched any of the given strings.
                warningMessage = string.Format(CultureInfo.CurrentCulture, CommandLineResources.NoTestsAvailableAfterFiltering, _discoveredTestCount, string.Join(", ", _selectedTestNames));
            }
            else
            {
                // No tests were discovered from the given sources.
                warningMessage = string.Format(CultureInfo.CurrentUICulture, CommandLineResources.NoTestsAvailableInSources, string.Join(", ", _commandLineOptions.Sources));

                if (string.IsNullOrEmpty(_commandLineOptions.TestAdapterPath))
                {
                    warningMessage = string.Format(CultureInfo.CurrentCulture, CommandLineResources.StringFormatToJoinTwoStrings, warningMessage, CommandLineResources.SuggestTestAdapterPathIfNoTestsIsFound);
                }
            }

            Output.Warning(false, warningMessage);
        }
    }

    /// <summary>
    /// Filter discovered tests and find matching tests from given search strings.
    /// Any name of the test that can match multiple strings will be added only once.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    private void DiscoveryRequest_OnDiscoveredTests(object sender, DiscoveredTestsEventArgs args)
    {
        _discoveredTestCount += args.DiscoveredTestCases.Count();
        foreach (var testCase in args.DiscoveredTestCases)
        {
            foreach (var nameCriteria in _selectedTestNames)
            {
                if (testCase.FullyQualifiedName.IndexOf(nameCriteria, StringComparison.OrdinalIgnoreCase) != -1)
                {
                    _selectedTestCases.Add(testCase);

                    // If a testcase matched then a filter matched - so remove the filter from not found list
                    _undiscoveredFilters.Remove(nameCriteria);
                    break;
                }
            }
        }
    }

    #endregion

    private class DiscoveryEventsRegistrar : ITestDiscoveryEventsRegistrar
    {
        private readonly EventHandler<DiscoveredTestsEventArgs> _discoveredTestsHandler;

        public DiscoveryEventsRegistrar(EventHandler<DiscoveredTestsEventArgs> discoveredTestsHandler)
        {
            _discoveredTestsHandler = discoveredTestsHandler;
        }

        public void LogWarning(string message)
        {
            ConsoleLogger.RaiseTestRunWarning(message);
        }

        public void RegisterDiscoveryEvents(IDiscoveryRequest discoveryRequest)
        {
            discoveryRequest.OnDiscoveredTests += _discoveredTestsHandler;
        }

        public void UnregisterDiscoveryEvents(IDiscoveryRequest discoveryRequest)
        {
            discoveryRequest.OnDiscoveredTests -= _discoveredTestsHandler;
        }
    }

    private class TestRunRequestEventsRegistrar : ITestRunEventsRegistrar
    {
        private readonly IOutput _output;
        private readonly CommandLineOptions _commandLineOptions;

        public TestRunRequestEventsRegistrar(IOutput output, CommandLineOptions commandLineOptions)
        {
            _output = output;
            _commandLineOptions = commandLineOptions;
        }

        public void LogWarning(string message)
        {
            ConsoleLogger.RaiseTestRunWarning(message);
        }

        public void RegisterTestRunEvents(ITestRunRequest testRunRequest)
        {
            testRunRequest.OnRunCompletion += TestRunRequest_OnRunCompletion;
        }

        public void UnregisterTestRunEvents(ITestRunRequest testRunRequest)
        {
            testRunRequest.OnRunCompletion -= TestRunRequest_OnRunCompletion;
        }

        /// <summary>
        /// Handles the TestRunRequest complete event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e">RunCompletion args</param>
        private void TestRunRequest_OnRunCompletion(object sender, TestRunCompleteEventArgs e)
        {
            // If run is not aborted/canceled then check the count of executed tests.
            // we need to check if there are any tests executed - to try show some help info to user to check for installed vsix extensions
            if (!e.IsAborted && !e.IsCanceled)
            {
                var testsFoundInAnySource = e.TestRunStatistics != null && (e.TestRunStatistics.ExecutedTests > 0);

                // Indicate the user to use testadapterpath command if there are no tests found
                if (!testsFoundInAnySource && string.IsNullOrEmpty(CommandLineOptions.Instance.TestAdapterPath) && _commandLineOptions.TestCaseFilterValue == null)
                {
                    _output.Warning(false, CommandLineResources.SuggestTestAdapterPathIfNoTestsIsFound);
                }
            }
        }
    }
}