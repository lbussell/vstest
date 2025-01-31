﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.TestPlatform.TestUtilities;

using Microsoft.VisualStudio.TestPlatform.Common.ExtensionFramework;
using Microsoft.VisualStudio.TestPlatform.Common.SettingsProvider;

using System;
using System.Collections.Generic;

public class TestableTestPluginCache : TestPluginCache
{
    public Action Action;
    public TestableTestPluginCache(List<string> extensionsPath)
    {
        TestDiscoveryExtensionManager.Destroy();
        TestExecutorExtensionManager.Destroy();
        SettingsProviderExtensionManager.Destroy();
        UpdateExtensions(extensionsPath, skipExtensionFilters: false);
    }

    public TestableTestPluginCache() : this(new List<string>())
    {
    }

    protected override IEnumerable<string> GetFilteredExtensions(List<string> extensions, string searchPattern)
    {
        Action?.Invoke();
        return extensions;
    }

    new public void SetupAssemblyResolver(string extensionAssembly)
    {
        base.SetupAssemblyResolver(extensionAssembly);
    }
}