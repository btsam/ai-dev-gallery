﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using AIDevGallery.Models;
using Microsoft.Extensions.AI;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AIDevGallery.Utils;

internal static class ExternalModelHelper
{
    private static List<IExternalModelProvider> _modelProviders = [
        new OllamaModelProvider(),
        new OpenAIModelProvider()
    ];

    public static async Task<IEnumerable<ModelDetails>> GetAllModelsAsync(bool toolCallingModelsOnly = false)
    {
        var tasks = _modelProviders.Select(provider => provider.GetModelsAsync(toolCallingModelsOnly));

        // Run in parallel and wait for all tasks to complete
        var results = await Task.WhenAll(tasks);

        var allModels = new List<ModelDetails>();

        // This ensures that we keep the order of the models as they are returned
        foreach (var models in results)
        {
            if (models != null)
            {
                allModels.AddRange(models);
            }
        }

        return allModels;
    }

    public static IEnumerable<HardwareAccelerator> HardwareAccelerators =>
        _modelProviders == null || _modelProviders.Count == 0
                ? []
                : _modelProviders
                    .Select(provider => provider.ModelHardwareAccelerator)
                    .Distinct();

    private static IExternalModelProvider? GetProvider(HardwareAccelerator hardwareAccelerator)
    {
        return _modelProviders?.FirstOrDefault(p => p.ModelHardwareAccelerator == hardwareAccelerator);
    }

    private static IExternalModelProvider? GetProvider(ModelDetails details)
    {
        return _modelProviders?.FirstOrDefault(p => details.HardwareAccelerators.Contains(p.ModelHardwareAccelerator));
    }

    public static string? GetName(HardwareAccelerator hardwareAccelerator)
    {
        return GetProvider(hardwareAccelerator)?.Name;
    }

    public static string? GetDescription(HardwareAccelerator hardwareAccelerator)
    {
        return GetProvider(hardwareAccelerator)?.ProviderDescription;
    }

    public static List<string> GetPackageReferences(HardwareAccelerator hardwareAccelerator)
    {
        return GetProvider(hardwareAccelerator)?.NugetPackageReferences ?? [];
    }

    internal static string? GetModelUrl(ModelDetails details)
    {
        return GetProvider(details)?.Url;
    }

    public static bool IsUrlFromExternalProvider(string url)
    {
        return _modelProviders.Any(provider => url.StartsWith(provider.UrlPrefix, StringComparison.InvariantCultureIgnoreCase));
    }

    internal static string? GetModelDetailsUrl(ModelDetails details)
    {
        return GetProvider(details.HardwareAccelerators.FirstOrDefault(h => HardwareAccelerators.Contains(h)))?.GetDetailsUrl(details);
    }

    public static ImageSource GetBitmapIcon(string url)
    {
        var icon = GetIcon(url);
        var fullPath = $"ms-appx:///Assets/ModelIcons/{icon}";
        if (fullPath.EndsWith(".svg", StringComparison.InvariantCultureIgnoreCase))
        {
            return new SvgImageSource(new Uri(fullPath));
        }

        return new BitmapImage(new Uri(fullPath));
    }

    public static string GetIcon(string url)
    {
        foreach (var provider in _modelProviders)
        {
            if (url.StartsWith(provider.UrlPrefix, StringComparison.InvariantCultureIgnoreCase))
            {
                if (App.Current.RequestedTheme == Microsoft.UI.Xaml.ApplicationTheme.Light)
                {
                    return provider.LightIcon;
                }
                else
                {
                    return provider.DarkIcon;
                }
            }
        }

        return "HuggingFace.svg";
    }

    public static IChatClient? GetIChatClient(string url)
    {
        foreach (var provider in _modelProviders)
        {
            if (url.StartsWith(provider.UrlPrefix, StringComparison.InvariantCultureIgnoreCase))
            {
                return provider.GetIChatClient(url);
            }
        }

        return null;
    }

    public static string? GetIChatClientString(string url)
    {
        foreach (var provider in _modelProviders)
        {
            if (url.StartsWith(provider.UrlPrefix, StringComparison.InvariantCultureIgnoreCase))
            {
                return provider.GetIChatClientString(url);
            }
        }

        return null;
    }
}