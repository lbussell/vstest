// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.TestPlatform.Client.DesignMode;

using System.Threading;

using ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client.Interfaces;

/// <summary>
/// DesignMode TestHost Launcher for hosting of test process
/// </summary>
internal class DesignModeTestHostLauncher : ITestHostLauncher2
{
    private readonly IDesignModeClient _designModeClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="DesignModeTestHostLauncher"/> class.
    /// </summary>
    /// <param name="designModeClient">Design mode client instance.</param>
    public DesignModeTestHostLauncher(IDesignModeClient designModeClient)
    {
        _designModeClient = designModeClient;
    }

    /// <inheritdoc/>
    public virtual bool IsDebug => false;

    /// <inheritdoc/>
    public bool AttachDebuggerToProcess(int pid)
    {
        return _designModeClient.AttachDebuggerToProcess(pid, CancellationToken.None);
    }

    /// <inheritdoc/>
    public bool AttachDebuggerToProcess(int pid, CancellationToken cancellationToken)
    {
        return _designModeClient.AttachDebuggerToProcess(pid, cancellationToken);
    }

    /// <inheritdoc/>
    public int LaunchTestHost(TestProcessStartInfo defaultTestHostStartInfo)
    {
        return _designModeClient.LaunchCustomHost(defaultTestHostStartInfo, CancellationToken.None);
    }

    /// <inheritdoc/>
    public int LaunchTestHost(TestProcessStartInfo defaultTestHostStartInfo, CancellationToken cancellationToken)
    {
        return _designModeClient.LaunchCustomHost(defaultTestHostStartInfo, cancellationToken);
    }
}

/// <summary>
/// DesignMode Debug Launcher to use if debugging enabled
/// </summary>
internal class DesignModeDebugTestHostLauncher : DesignModeTestHostLauncher
{
    /// <inheritdoc/>
    public DesignModeDebugTestHostLauncher(IDesignModeClient designModeClient) : base(designModeClient)
    {
    }

    /// <inheritdoc/>
    public override bool IsDebug => true;
}