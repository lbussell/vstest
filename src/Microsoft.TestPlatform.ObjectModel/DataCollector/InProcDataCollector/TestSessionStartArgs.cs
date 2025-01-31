// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.TestPlatform.ObjectModel.DataCollector.InProcDataCollector;

using System;
using System.Collections.Generic;

/// <summary>
/// The test session start args.
/// </summary>
public class TestSessionStartArgs : InProcDataCollectionArgs
{
    private readonly IDictionary<string, object> _properties;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestSessionStartArgs"/> class.
    /// </summary>
    public TestSessionStartArgs()
    {
        Configuration = String.Empty;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestSessionStartArgs"/> class.
    /// </summary>
    /// <param name="properties">
    /// Properties.
    /// </param>
    public TestSessionStartArgs(IDictionary<string, object> properties)
    {
        Configuration = String.Empty;
        _properties = properties;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestSessionStartArgs"/> class.
    /// </summary>
    /// <param name="configuration">
    /// The configuration.
    /// </param>
    public TestSessionStartArgs(string configuration)
    {
        Configuration = configuration;
    }

    /// <summary>
    /// Gets or sets the configuration.
    /// </summary>
    public string Configuration { get; set; }

    /// <summary>
    /// Gets session start properties enumerator
    /// </summary>
    public IEnumerator<KeyValuePair<string, object>> GetProperties()
    {
        return _properties.GetEnumerator();
    }

    /// <summary>
    /// Gets property value
    /// </summary>
    /// <param name="property">
    /// Property name
    /// </param>
    public T GetPropertyValue<T>(string property)
    {
        ValidateArg.NotNullOrEmpty(property, nameof(property));

        return _properties.ContainsKey(property) ? (T)_properties[property] : default;
    }

    /// <summary>
    /// Gets property value
    /// </summary>
    /// <param name="property">
    /// Property name
    /// </param>
    public object GetPropertyValue(string property)
    {
        ValidateArg.NotNullOrEmpty(property, nameof(property));

        _properties.TryGetValue(property, out var propertyValue);

        return propertyValue;
    }
}