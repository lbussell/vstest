// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.TestPlatform.Common.ExtensionFramework;

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;

using Utilities;
using Logging;
using ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using CommonResources = Resources.Resources;
using Microsoft.VisualStudio.TestPlatform.Common.Utilities;

/// <summary>
/// Discovers test extensions in a directory.
/// </summary>
internal class TestPluginDiscoverer
{
    private static readonly HashSet<string> UnloadableFiles = new();
    private readonly MetadataReaderExtensionsHelper _extensionHelper = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TestPluginDiscoverer"/> class.
    /// </summary>
    public TestPluginDiscoverer()
    {
    }

    #region Fields

#if WINDOWS_UAP
        private static HashSet<string> platformAssemblies = new HashSet<string>(new string[] {
            "MICROSOFT.VISUALSTUDIO.TESTPLATFORM.UNITTESTFRAMEWORK.DLL",
            "MICROSOFT.VISUALSTUDIO.TESTPLATFORM.TESTEXECUTOR.CORE.DLL",
            "MICROSOFT.VISUALSTUDIO.TESTPLATFORM.OBJECTMODEL.DLL",
            "VSTEST_EXECUTIONENGINE_PLATFORMBRIDGE.DLL",
            "VSTEST_EXECUTIONENGINE_PLATFORMBRIDGE.WINMD",
            "VSTEST.EXECUTIONENGINE.WINDOWSPHONE.DLL",
            "MICROSOFT.CSHARP.DLL",
            "MICROSOFT.VISUALBASIC.DLL",
            "CLRCOMPRESSION.DLL",
        });

        private const string SYSTEM_ASSEMBLY_PREFIX = "system.";
#endif

    #endregion

    #region Public Methods

    /// <summary>
    /// Gets information about each of the test extensions available.
    /// </summary>
    /// <param name="extensionPaths">
    ///     The path to the extensions.
    /// </param>
    /// <returns>
    /// A dictionary of assembly qualified name and test plugin information.
    /// </returns>
    public Dictionary<string, TPluginInfo> GetTestExtensionsInformation<TPluginInfo, TExtension>(IEnumerable<string> extensionPaths) where TPluginInfo : TestPluginInformation
    {
        Debug.Assert(extensionPaths != null);

        var pluginInfos = new Dictionary<string, TPluginInfo>();

        // C++ UWP adapters do not follow TestAdapater naming convention, so making this exception
        if (!extensionPaths.Any())
        {
            AddKnownExtensions(ref extensionPaths);
        }

        GetTestExtensionsFromFiles<TPluginInfo, TExtension>(extensionPaths.ToArray(), pluginInfos);

        return pluginInfos;
    }

    #endregion

    #region Private Methods

    private void AddKnownExtensions(ref IEnumerable<string> extensionPaths)
    {
        // For C++ UWP adapter, & OLD C# UWP(MSTest V1) adapter
        // In UWP .Net Native Compilation mode managed dll's are packaged differently, & File.Exists() fails.
        // Include these two dll's if so far no adapters(extensions) were found, & let Assembly.Load() fail if they are not present.
        extensionPaths = extensionPaths.Concat(new[] { "Microsoft.VisualStudio.TestTools.CppUnitTestFramework.CppUnitTestExtension.dll", "Microsoft.VisualStudio.TestPlatform.Extensions.MSAppContainerAdapter.dll" });
    }

    /// <summary>
    /// Gets test extension information from the given collection of files.
    /// </summary>
    /// <typeparam name="TPluginInfo">
    /// Type of Test Plugin Information.
    /// </typeparam>
    /// <typeparam name="TExtension">
    /// Type of extension.
    /// </typeparam>
    /// <param name="files">
    /// List of dll's to check for test extension availability
    /// </param>
    /// <param name="pluginInfos">
    /// Test plugins collection to add to.
    /// </param>
    private void GetTestExtensionsFromFiles<TPluginInfo, TExtension>(
        string[] files,
        Dictionary<string, TPluginInfo> pluginInfos) where TPluginInfo : TestPluginInformation
    {
        Debug.Assert(files != null, "null files");
        Debug.Assert(pluginInfos != null, "null pluginInfos");

        // Scan each of the files for data extensions.
        foreach (var file in files)
        {
            if (UnloadableFiles.Contains(file))
            {
                continue;
            }
            try
            {
                Assembly assembly = null;
                var assemblyName = Path.GetFileNameWithoutExtension(file);
                assembly = Assembly.Load(new AssemblyName(assemblyName));
                if (assembly != null)
                {
                    GetTestExtensionsFromAssembly<TPluginInfo, TExtension>(assembly, pluginInfos, file);
                }
            }
            catch (FileLoadException e)
            {
                EqtTrace.Warning("TestPluginDiscoverer-FileLoadException: Failed to load extensions from file '{0}'.  Skipping test extension scan for this file.  Error: {1}", file, e);
                string fileLoadErrorMessage = string.Format(CultureInfo.CurrentUICulture, CommonResources.FailedToLoadAdapaterFile, file);
                TestSessionMessageLogger.Instance.SendMessage(TestMessageLevel.Warning, fileLoadErrorMessage);
                UnloadableFiles.Add(file);
            }
            catch (Exception e)
            {
                EqtTrace.Warning("TestPluginDiscoverer: Failed to load extensions from file '{0}'.  Skipping test extension scan for this file.  Error: {1}", file, e);
            }
        }
    }

    /// <summary>
    /// Gets test extensions from a given assembly.
    /// </summary>
    /// <param name="assembly">Assembly to check for test extension availability</param>
    /// <param name="pluginInfos">Test extensions collection to add to.</param>
    /// <typeparam name="TPluginInfo">
    /// Type of Test Plugin Information.
    /// </typeparam>
    /// <typeparam name="TExtension">
    /// Type of Extensions.
    /// </typeparam>
    private void GetTestExtensionsFromAssembly<TPluginInfo, TExtension>(Assembly assembly, Dictionary<string, TPluginInfo> pluginInfos, string filePath) where TPluginInfo : TestPluginInformation
    {
        Debug.Assert(assembly != null, "null assembly");
        Debug.Assert(pluginInfos != null, "null pluginInfos");

        List<Type> types = new();
        Type extension = typeof(TExtension);

        try
        {
            var discoveredExtensions = _extensionHelper.DiscoverTestExtensionTypesV2Attribute(assembly, filePath);
            if (discoveredExtensions?.Length > 0)
            {
                types.AddRange(discoveredExtensions);
            }
        }
        catch (Exception e)
        {
            EqtTrace.Warning("TestPluginDiscoverer: Failed to get types searching for 'TestPlatformExtensionVersionAttribute' from assembly '{0}'. Error: {1}", assembly.FullName, e.ToString());
        }

        try
        {
            var typesToLoad = TypesToLoadUtilities.GetTypesToLoad(assembly);
            if (typesToLoad?.Any() == true)
            {
                types.AddRange(typesToLoad);
            }

            if (!types.Any())
            {
                types.AddRange(assembly.GetTypes().Where(type => type.GetTypeInfo().IsClass && !type.GetTypeInfo().IsAbstract));
            }
        }
        catch (ReflectionTypeLoadException e)
        {
            EqtTrace.Warning("TestPluginDiscoverer: Failed to get types from assembly '{0}'. Error: {1}", assembly.FullName, e.ToString());

            if (e.Types?.Length > 0)
            {
                types.AddRange(e.Types.Where(type => type.GetTypeInfo().IsClass && !type.GetTypeInfo().IsAbstract));
            }

            if (e.LoaderExceptions != null)
            {
                foreach (var ex in e.LoaderExceptions)
                {
                    EqtTrace.Warning("LoaderExceptions: {0}", ex);
                }
            }
        }

        if (types != null && types.Any())
        {
            foreach (var type in types)
            {
                GetTestExtensionFromType(type, extension, pluginInfos, filePath);
            }
        }
    }

    /// <summary>
    /// Attempts to find a test extension from given type.
    /// </summary>
    /// <typeparam name="TPluginInfo">
    /// Type of the test plugin information
    /// </typeparam>
    /// <param name="type">
    /// Type to inspect for being test extension
    /// </param>
    /// <param name="extensionType">
    /// Test extension type to look for.
    /// </param>
    /// <param name="extensionCollection">
    /// Test extensions collection to add to.
    /// </param>
    private void GetTestExtensionFromType<TPluginInfo>(
        Type type,
        Type extensionType,
        Dictionary<string, TPluginInfo> extensionCollection,
        string filePath)
        where TPluginInfo : TestPluginInformation
    {
        if (extensionType.GetTypeInfo().IsAssignableFrom(type.GetTypeInfo()))
        {
            var rawPluginInfo = Activator.CreateInstance(typeof(TPluginInfo), type);
            var pluginInfo = (TPluginInfo)rawPluginInfo;
            pluginInfo.FilePath = filePath;

            if (pluginInfo == null || pluginInfo.IdentifierData == null)
            {
                if (EqtTrace.IsErrorEnabled)
                {
                    EqtTrace.Error(
                        "GetTestExtensionFromType: Either PluginInformation is null or PluginInformation doesn't contain IdentifierData for type {0}.", type.FullName);
                }
                return;
            }

            if (extensionCollection.ContainsKey(pluginInfo.IdentifierData))
            {
                EqtTrace.Warning(
                    "GetTestExtensionFromType: Discovered multiple test extensions with identifier data '{0}' and type '{1}' inside file '{2}'; keeping the first one '{3}'.",
                    pluginInfo.IdentifierData, pluginInfo.AssemblyQualifiedName, filePath, extensionCollection[pluginInfo.IdentifierData].AssemblyQualifiedName);
            }
            else
            {
                extensionCollection.Add(pluginInfo.IdentifierData, pluginInfo);
                EqtTrace.Info("GetTestExtensionFromType: Register extension with identifier data '{0}' and type '{1}' inside file '{2}'",
                    pluginInfo.IdentifierData, pluginInfo.AssemblyQualifiedName, filePath);
            }
        }
    }

    #endregion
}