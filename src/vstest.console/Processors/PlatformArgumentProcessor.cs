// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.TestPlatform.CommandLine.Processors;

using System;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Linq;

using Common;
using Common.Interfaces;
using Microsoft.VisualStudio.TestPlatform.Common.Utilities;
using ObjectModel;
using Microsoft.VisualStudio.TestPlatform.Utilities.Helpers;

using CommandLineResources = Resources.Resources;

/// <summary>
///  An argument processor that allows the user to specify the target platform architecture
///  for test run.
/// </summary>
internal class PlatformArgumentProcessor : IArgumentProcessor
{
    #region Constants

    /// <summary>
    /// The name of the command line argument that the OutputArgumentExecutor handles.
    /// </summary>
    public const string CommandName = "/Platform";

    #endregion

    private Lazy<IArgumentProcessorCapabilities> _metadata;

    private Lazy<IArgumentExecutor> _executor;

    /// <summary>
    /// Gets the metadata.
    /// </summary>
    public Lazy<IArgumentProcessorCapabilities> Metadata
    {
        get
        {
            if (_metadata == null)
            {
                _metadata = new Lazy<IArgumentProcessorCapabilities>(() => new PlatformArgumentProcessorCapabilities());
            }

            return _metadata;
        }
    }

    /// <summary>
    /// Gets or sets the executor.
    /// </summary>
    public Lazy<IArgumentExecutor> Executor
    {
        get
        {
            if (_executor == null)
            {
                _executor = new Lazy<IArgumentExecutor>(() => new PlatformArgumentExecutor(CommandLineOptions.Instance, RunSettingsManager.Instance));
            }

            return _executor;
        }

        set
        {
            _executor = value;
        }
    }
}

internal class PlatformArgumentProcessorCapabilities : BaseArgumentProcessorCapabilities
{
    public override string CommandName => PlatformArgumentProcessor.CommandName;
    public override bool AllowMultiple => false;

    public override bool IsAction => false;

    public override ArgumentProcessorPriority Priority => ArgumentProcessorPriority.AutoUpdateRunSettings;

    public override string HelpContentResourceName => CommandLineResources.PlatformArgumentHelp;

    public override HelpContentPriority HelpPriority => HelpContentPriority.PlatformArgumentProcessorHelpPriority;
}

/// <summary>
/// Argument Executor for the "/Platform" command line argument.
/// </summary>
internal class PlatformArgumentExecutor : IArgumentExecutor
{
    #region Fields

    /// <summary>
    /// Used for getting sources.
    /// </summary>
    private readonly CommandLineOptions _commandLineOptions;

    private readonly IRunSettingsProvider _runSettingsManager;

    public const string RunSettingsPath = "RunConfiguration.TargetPlatform";

    #endregion

    #region Constructor

    /// <summary>
    /// Default constructor.
    /// </summary>
    /// <param name="options"> The options. </param>
    /// <param name="runSettingsManager"> The runsettings manager. </param>
    public PlatformArgumentExecutor(CommandLineOptions options, IRunSettingsProvider runSettingsManager)
    {
        Contract.Requires(options != null);
        Contract.Requires(runSettingsManager != null);
        _commandLineOptions = options;
        _runSettingsManager = runSettingsManager;
    }

    #endregion

    #region IArgumentExecutor

    /// <summary>
    /// Initializes with the argument that was provided with the command.
    /// </summary>
    /// <param name="argument">Argument that was provided with the command.</param>
    public void Initialize(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            throw new CommandLineException(CommandLineResources.PlatformTypeRequired);
        }

        var validPlatforms = Enum.GetValues(typeof(Architecture)).Cast<Architecture>()
            .Where(e => e != Architecture.AnyCPU && e != Architecture.Default)
            .ToList();

        var validPlatform = Enum.TryParse(argument, true, out Architecture platform);
        if (validPlatform)
        {
            // Ensure that the case-insensitively parsed enum is in the list of valid platforms.
            // This filters out:
            //  - values that parse correctly but the enum does not define them (e.g. "1" parses as valid enum value 1)
            //  - the Default or AnyCpu that are not valid target to provide via settings
            validPlatform = validPlatforms.Contains(platform);
        }

        if (validPlatform)
        {
            RunSettingsHelper.Instance.IsDefaultTargetArchitecture = false;
            _commandLineOptions.TargetArchitecture = platform;
            _runSettingsManager.UpdateRunSettingsNode(RunSettingsPath, platform.ToString());
        }
        else
        {
            throw new CommandLineException(
                string.Format(CultureInfo.CurrentCulture, CommandLineResources.InvalidPlatformType, argument, string.Join(", ", validPlatforms)));
        }

        if (EqtTrace.IsInfoEnabled)
        {
            EqtTrace.Info("Using platform:{0}", _commandLineOptions.TargetArchitecture);
        }
    }

    /// <summary>
    /// The output path is already set, return success.
    /// </summary>
    /// <returns> The <see cref="ArgumentProcessorResult"/> Success </returns>
    public ArgumentProcessorResult Execute()
    {
        return ArgumentProcessorResult.Success;
    }

    #endregion
}