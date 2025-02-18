﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using AIDevGallery.Models;
using Microsoft.Diagnostics.Telemetry;
using Microsoft.Diagnostics.Telemetry.Internal;
using Microsoft.Extensions.AI;
using System;
using System.Diagnostics.Tracing;
using System.Linq;

namespace AIDevGallery.Telemetry.Events;

[EventData]
internal class SampleInteractionEvent : EventBase
{
    public override PartA_PrivTags PartA_PrivTags => PrivTags.ProductAndServiceUsage;

    public string SampleId { get; }
    public string Model1Id { get; }
    public string HardwareAccelerator1 { get; }
    public string Model2Id { get; }
    public string HardwareAccelerator2 { get; }
    public string CustomInfo { get; }

    private SampleInteractionEvent(string sampleId, string model1Id, HardwareAccelerator hardwareAccelerator1, string? model2Id, HardwareAccelerator? hardwareAccelerator2, string? customInfo)
    {
        SampleId = sampleId;
        Model1Id = model1Id;
        HardwareAccelerator1 = hardwareAccelerator1.ToString();
        Model2Id = model2Id ?? string.Empty;
        HardwareAccelerator2 = hardwareAccelerator2?.ToString() ?? string.Empty;
        CustomInfo = customInfo ?? string.Empty;
    }

    public override void ReplaceSensitiveStrings(Func<string?, string?> replaceSensitiveStrings)
    {
        // No sensitive strings to replace.
    }

    public static void Log(string sampleId, string model1Id, HardwareAccelerator hardwareAccelerator1, string? model2Id, HardwareAccelerator? hardwareAccelerator2, string? customInfo = null)
    {
        TelemetryFactory.Get<ITelemetry>().Log("SampleInteraction_Event", LogLevel.Critical, new SampleInteractionEvent(sampleId, model1Id, hardwareAccelerator1, model2Id, hardwareAccelerator2, customInfo));
    }

    public static void SendSampleInteractedEvent(IChatClient? chatClient, ScenarioType scenario, string? customInfo = null)
    {
        SendSampleInteractedEvent(chatClient?.Metadata.ProviderUri, scenario, customInfo);
    }

    public static void SendSampleInteractedEvent(EmbeddingGeneratorMetadata embeddingGeneratorMetadata, ScenarioType scenario, string? customInfo = null)
    {
        SendSampleInteractedEvent(embeddingGeneratorMetadata.ProviderUri, scenario, customInfo);
    }

    private static void SendSampleInteractedEvent(Uri? providerUri, ScenarioType scenario, string? customInfo = null)
    {
        var modelPath = providerUri?.ToString();

        if (modelPath != null && modelPath.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
        {
            modelPath = modelPath
                .Substring(8) // Remove "file:///"
                .Replace('/', '\\'); // Convert to Windows path

            var cachedModel = App.ModelCache.GetCachedModelByPath(modelPath);
            if (cachedModel != null)
            {
                var modelDetails = cachedModel.Details;
                var sample = Samples.SampleDetails.Samples.First(s => s.Scenario == scenario);
                Log(sample.Id, modelDetails.Id, modelDetails.HardwareAccelerators.First(), null, null, customInfo);
            }
        }
    }
}