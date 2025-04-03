﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using AIDevGallery.Models;
using AIDevGallery.Samples;
using AIDevGallery.Utils;
using Microsoft.UI.Xaml;
using System.Collections.Generic;
using System.Linq;

namespace AIDevGallery.Helpers;

internal static class ModelDetailsHelper
{
    public static bool EqualOrParent(ModelType modelType, ModelType searchModelType)
    {
        if (modelType == searchModelType)
        {
            return true;
        }

        while (ModelTypeHelpers.ParentMapping.Values.Any(parent => parent.Contains(modelType)))
        {
            modelType = ModelTypeHelpers.ParentMapping.FirstOrDefault(parent => parent.Value.Contains(modelType)).Key;
            if (modelType == searchModelType)
            {
                return true;
            }
        }

        return false;
    }

    public static ModelDetails GetModelDetailsFromApiDefinition(ModelType modelType, ApiDefinition apiDefinition)
    {
        return new ModelDetails
        {
            Id = apiDefinition.Id,
            Icon = apiDefinition.Icon,
            Name = apiDefinition.Name,
            HardwareAccelerators = [HardwareAccelerator.WCRAPI],
            IsUserAdded = false,
            SupportedOnQualcomm = true,
            ReadmeUrl = apiDefinition.ReadmeUrl,
            Url = $"file://{modelType}",
            License = apiDefinition.License
        };
    }

    public static List<Dictionary<ModelType, List<ModelDetails>>> GetModelDetails(Sample sample)
    {
        Dictionary<ModelType, List<ModelDetails>> model1Details = [];
        foreach (ModelType modelType in sample.Model1Types)
        {
            model1Details[modelType] = GetSamplesForModelType(modelType);
        }

        List<Dictionary<ModelType, List<ModelDetails>>> listModelDetails = [model1Details];

        if (sample.Model2Types != null)
        {
            Dictionary<ModelType, List<ModelDetails>> model2Details = [];
            foreach (ModelType modelType in sample.Model2Types)
            {
                model2Details[modelType] = GetSamplesForModelType(modelType);
            }

            listModelDetails.Add(model2Details);
        }

        return listModelDetails;

        static List<ModelDetails> GetSamplesForModelType(ModelType initialModelType)
        {
            Queue<ModelType> leafs = new();
            leafs.Enqueue(initialModelType);
            bool added = true;

            do
            {
                added = false;
                int initialCount = leafs.Count;

                for (int i = 0; i < initialCount; i++)
                {
                    var leaf = leafs.Dequeue();
                    if (ModelTypeHelpers.ParentMapping.TryGetValue(leaf, out List<ModelType>? values))
                    {
                        if (values.Count > 0)
                        {
                            added = true;

                            foreach (var value in values)
                            {
                                leafs.Enqueue(value);
                            }
                        }
                        else
                        {
                            // Is API, just add back but don't mark as added
                            leafs.Enqueue(leaf);
                        }
                    }
                    else
                    {
                        // Re-enqueue the leaf since it's actually a leaf node
                        leafs.Enqueue(leaf);
                    }
                }
            }
            while (leafs.Count > 0 && added);

            var allModelDetails = new List<ModelDetails>();
            foreach (var modelType in leafs.ToList())
            {
                if (ModelTypeHelpers.ModelDetails.TryGetValue(modelType, out ModelDetails? modelDetails))
                {
                    allModelDetails.Add(modelDetails);
                }
                else if (ModelTypeHelpers.ApiDefinitionDetails.TryGetValue(modelType, out ApiDefinition? apiDefinition))
                {
                    allModelDetails.Add(GetModelDetailsFromApiDefinition(modelType, apiDefinition));
                }
            }

            if (initialModelType == ModelType.LanguageModels && App.ModelCache != null)
            {
                var userAddedModels = App.ModelCache.Models.Where(m => m.Details.IsUserAdded).ToList();
                allModelDetails.AddRange(userAddedModels.Select(c => c.Details));
            }

            return allModelDetails;
        }
    }

    public static bool IsApi(this ModelDetails modelDetails)
    {
        return modelDetails.HardwareAccelerators.Contains(HardwareAccelerator.WCRAPI) ||
               modelDetails.IsHttpApi() ||
               modelDetails.Size == 0;
    }

    public static bool IsHttpApi(this ModelDetails modelDetails)
    {
        return modelDetails.HardwareAccelerators.Any(h => ExternalModelHelper.HardwareAccelerators.Contains(h));
    }

    public static bool IsApi(this ExpandedModelDetails modelDetails)
    {
        return modelDetails.HardwareAccelerator == HardwareAccelerator.WCRAPI ||
            modelDetails.IsHttpApi();
    }

    public static bool IsHttpApi(this ExpandedModelDetails modelDetails)
    {
        return ExternalModelHelper.HardwareAccelerators.Contains(modelDetails.HardwareAccelerator);
    }

    public static Visibility ShowWhenWcrApi(ModelDetails modelDetails)
    {
        return modelDetails.HardwareAccelerators.Contains(HardwareAccelerator.WCRAPI) ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility ShowWhenHttpApi(ModelDetails modelDetails)
    {
        return modelDetails.IsHttpApi() ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility ShowWhenHttpWithSize(ModelDetails modelDetails)
    {
        return modelDetails.IsHttpApi() && modelDetails.Size != 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public static string GetHttpApiUrl(ModelDetails modelDetails)
    {
        return ExternalModelHelper.GetModelUrl(modelDetails) ?? string.Empty;
    }

    private static bool IsOnnxModel(ModelDetails modelDetails)
    {
        return modelDetails.HardwareAccelerators.Contains(HardwareAccelerator.CPU)
            || modelDetails.HardwareAccelerators.Contains(HardwareAccelerator.DML)
            || modelDetails.HardwareAccelerators.Contains(HardwareAccelerator.QNN);
    }

    public static Visibility ShowWhenOnnxModel(ModelDetails modelDetails)
    {
        return IsOnnxModel(modelDetails) ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility ShowWhenDownloadedModel(ModelDetails modelDetails)
    {
        return IsOnnxModel(modelDetails) && !modelDetails.IsUserAdded
            ? Visibility.Visible : Visibility.Collapsed;
    }
}