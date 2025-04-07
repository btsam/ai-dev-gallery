﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using AIDevGallery.Models;
using Microsoft.Extensions.AI;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AIDevGallery.Utils;

internal interface IExternalModelProvider
{
    string Name { get; }
    string UrlPrefix { get; }
    string LightIcon { get; }
    string DarkIcon { get; }
    HardwareAccelerator ModelHardwareAccelerator { get; }
    List<string> NugetPackageReferences { get; }
    private static List<string>? ToolCallingModelNames { get; }
    string ProviderDescription { get; }
    Task<IEnumerable<ModelDetails>> GetModelsAsync(bool useToolCalling = false, CancellationToken cancelationToken = default);
    Task InitializeAsync(CancellationToken cancelationToken = default);
    IChatClient? GetIChatClient(string url);
    string? IChatClientImplementationNamespace { get; }
    string? GetIChatClientString(string url);
    string? GetDetailsUrl(ModelDetails details);
    string Url { get; }
}