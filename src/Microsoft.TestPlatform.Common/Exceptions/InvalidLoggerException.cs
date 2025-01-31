﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.TestPlatform.Common.Exceptions;

using ObjectModel;

using System;

public class InvalidLoggerException : TestPlatformException
{
    #region Constructors

    /// <summary>
    /// Initializes with the message.
    /// </summary>
    /// <param name="message">Message for the exception.</param>
    public InvalidLoggerException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes with message and inner exception.
    /// </summary>
    /// <param name="message">Message for the exception.</param>
    /// <param name="innerException">The inner exception.</param>
    public InvalidLoggerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    #endregion
}