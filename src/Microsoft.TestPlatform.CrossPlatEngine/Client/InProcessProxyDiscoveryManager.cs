﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Common.ExtensionFramework;
using ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using ObjectModel.Engine;
using ObjectModel.Engine.TesthostProtocol;
using ObjectModel.Host;
using ObjectModel.Logging;

internal class InProcessProxyDiscoveryManager : IProxyDiscoveryManager
{
    private readonly ITestHostManagerFactory _testHostManagerFactory;
    private readonly IDiscoveryManager _discoveryManager;
    private readonly ITestRuntimeProvider _testHostManager;

    public bool IsInitialized { get; private set; } = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="InProcessProxyDiscoveryManager"/> class.
    /// </summary>
    /// <param name="testHostManager">
    /// The test Host Manager.
    /// </param>
    /// <param name="testHostManagerFactory">
    /// Manager factory
    /// </param>
    public InProcessProxyDiscoveryManager(ITestRuntimeProvider testHostManager, ITestHostManagerFactory testHostManagerFactory)
    {
        _testHostManager = testHostManager;
        _testHostManagerFactory = testHostManagerFactory;
        _discoveryManager = _testHostManagerFactory.GetDiscoveryManager();
    }

    /// <summary>
    /// Initializes test discovery.
    /// <param name="skipDefaultAdapters">Skip default adapters flag.</param>
    /// </summary>
    public void Initialize(bool skipDefaultAdapters)
    {
    }

    /// <summary>
    /// Discovers tests
    /// </summary>
    /// <param name="discoveryCriteria">Settings, parameters for the discovery request</param>
    /// <param name="eventHandler">EventHandler for handling discovery events from Engine</param>
    public void DiscoverTests(DiscoveryCriteria discoveryCriteria, ITestDiscoveryEventsHandler2 eventHandler)
    {
        Task.Run(() =>
        {
            try
            {
                // Initialize extension before discovery
                InitializeExtensions(discoveryCriteria.Sources);
                discoveryCriteria.UpdateDiscoveryCriteria(_testHostManager);

                _discoveryManager.DiscoverTests(discoveryCriteria, eventHandler);
            }
            catch (Exception exception)
            {
                EqtTrace.Error("InProcessProxyDiscoveryManager.DiscoverTests: Failed to discover tests: {0}", exception);

                // Send a discovery complete to caller.
                eventHandler.HandleLogMessage(TestMessageLevel.Error, exception.ToString());

                var discoveryCompeleteEventsArg = new DiscoveryCompleteEventArgs(-1, true);

                eventHandler.HandleDiscoveryComplete(discoveryCompeleteEventsArg, Enumerable.Empty<TestCase>());
            }
        });
    }

    /// <summary>
    /// Closes the current test operation.
    /// This function is of no use in this context as we are not creating any testhost
    /// </summary>
    public void Close()
    {
    }

    /// <summary>
    /// Aborts the test operation.
    /// </summary>
    public void Abort()
    {
        Task.Run(() => _testHostManagerFactory.GetDiscoveryManager().Abort());
    }

    private void InitializeExtensions(IEnumerable<string> sources)
    {
        var extensionsFromSource = _testHostManager.GetTestPlatformExtensions(sources, Enumerable.Empty<string>());
        if (extensionsFromSource.Any())
        {
            TestPluginCache.Instance.UpdateExtensions(extensionsFromSource, false);
        }

        // We don't need to pass list of extension as we are running inside vstest.console and
        // it will use TestPluginCache of vstest.console
        _discoveryManager.Initialize(Enumerable.Empty<string>(), null);
    }
}