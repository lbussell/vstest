﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.TestPlatform.Client.TestRunAttachmentsProcessing;

using CommunicationUtilities.Interfaces;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.ObjectModel;
using ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using ObjectModel.Logging;

using System.Collections.Generic;

/// <summary>
/// The test run attachments processing events handler.
/// </summary>
/// 
public class TestRunAttachmentsProcessingEventsHandler : ITestRunAttachmentsProcessingEventsHandler
{
    private readonly ICommunicationManager _communicationManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestRunAttachmentsProcessingEventsHandler"/> class.
    /// </summary>
    /// <param name="communicationManager"> The communication manager. </param>
    public TestRunAttachmentsProcessingEventsHandler(ICommunicationManager communicationManager)
    {
        _communicationManager = communicationManager;
    }

    /// <inheritdoc/>
    public void HandleTestRunAttachmentsProcessingComplete(TestRunAttachmentsProcessingCompleteEventArgs attachmentsProcessingCompleteEventArgs, IEnumerable<AttachmentSet> lastChunk)
    {
        if (EqtTrace.IsInfoEnabled)
        {
            EqtTrace.Info("Test run attachments processing completed.");
        }

        var payload = new TestRunAttachmentsProcessingCompletePayload()
        {
            AttachmentsProcessingCompleteEventArgs = attachmentsProcessingCompleteEventArgs,
            Attachments = lastChunk
        };

        _communicationManager.SendMessage(MessageType.TestRunAttachmentsProcessingComplete, payload);
    }

    /// <inheritdoc/>
    public void HandleTestRunAttachmentsProcessingProgress(TestRunAttachmentsProcessingProgressEventArgs attachmentsProcessingProgressEventArgs)
    {
        var payload = new TestRunAttachmentsProcessingProgressPayload()
        {
            AttachmentsProcessingProgressEventArgs = attachmentsProcessingProgressEventArgs,
        };

        _communicationManager.SendMessage(MessageType.TestRunAttachmentsProcessingProgress, payload);
    }

    /// <inheritdoc/>
    public void HandleProcessedAttachmentsChunk(IEnumerable<AttachmentSet> attachments)
    {
        throw new System.NotImplementedException();
    }

    /// <inheritdoc/>
    public void HandleLogMessage(TestMessageLevel level, string message)
    {
        var testMessagePayload = new TestMessagePayload { MessageLevel = level, Message = message };
        _communicationManager.SendMessage(MessageType.TestMessage, testMessagePayload);
    }

    /// <inheritdoc/>
    public void HandleRawMessage(string rawMessage)
    {
        // No-Op
    }
}