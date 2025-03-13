﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using AIDevGallery.Helpers;
using AIDevGallery.Models;
using AIDevGallery.Samples;
using AIDevGallery.Telemetry.Events;
using AIDevGallery.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;

namespace AIDevGallery.Pages;

internal sealed partial class APIPage : Page
{
    public ModelFamily? ModelFamily { get; set; }
    private ModelType? modelFamilyType;
    private ModelDetails? modelDetails;
    private DispatcherTimer webViewTimer;

    public APIPage()
    {
        this.InitializeComponent();
        this.SizeChanged += APIPage_SizeChanged;
        webViewTimer = new DispatcherTimer();
        webViewTimer.Interval = TimeSpan.FromMilliseconds(500);
        webViewTimer.Tick += (s, e) =>
        {
            MyWebView.IsHitTestVisible = true;
            webViewTimer.Stop();
        };
        MyWebView.PointerWheelChanged += MyWebView_PointerWheelChanged;
    }

    private void MyWebView_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        MyWebView.IsHitTestVisible = false;
        webViewTimer.Stop();
        webViewTimer.Start();
    }

    private async void APIPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (MyWebView.CoreWebView2 == null)
        {
            return;
        }

        var script = "document.body.scrollHeight;";
        var heightString = await MyWebView.ExecuteScriptAsync(script);
        if (float.TryParse(heightString, out float height))
        {
            MyWebView.Height = Math.Round(height, MidpointRounding.AwayFromZero);
            Debug.WriteLine($"WebView height: {height}");
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is MostRecentlyUsedItem mru)
        {
            var modelFamilyId = mru.ItemId;
        }
        else if (e.Parameter is ModelType modelType && ModelTypeHelpers.ModelFamilyDetails.TryGetValue(modelType, out var modelFamilyDetails))
        {
            modelFamilyType = modelType;
            ModelFamily = modelFamilyDetails;
            ModelTypeHelpers.ModelDetails.TryGetValue(modelType, out modelDetails);
        }
        else if (e.Parameter is ModelDetails details)
        {
            ModelFamily = new ModelFamily
            {
                Id = details.Id,
                DocsUrl = details.ReadmeUrl ?? string.Empty,
                ReadmeUrl = details.ReadmeUrl ?? string.Empty,
                Name = details.Name
            };
            modelDetails = details;
        }
        else if (e.Parameter is ModelType apiType && ModelTypeHelpers.ApiDefinitionDetails.TryGetValue(apiType, out var apiDefinition))
        {
            // API
            modelFamilyType = apiType;
            modelDetails = ModelDetailsHelper.GetModelDetailsFromApiDefinition(apiType, apiDefinition);

            ModelFamily = new ModelFamily
            {
                Id = apiDefinition.Id,
                ReadmeUrl = apiDefinition.ReadmeUrl,
                DocsUrl = apiDefinition.ReadmeUrl,
                Name = apiDefinition.Name,
            };

            if (!string.IsNullOrWhiteSpace(apiDefinition.SampleIdToShowInDocs))
            {
                var sample = SampleDetails.Samples.FirstOrDefault(s => s.Id == apiDefinition.SampleIdToShowInDocs);
                if (sample != null)
                {
                    _ = sampleContainer.LoadSampleAsync(sample, [modelDetails]);
                }
            }
            else
            {
                sampleContainerRoot.Visibility = Visibility.Collapsed;
            }

            WcrApiCodeSnippet.Snippets.TryGetValue(apiType, out var snippet);
            if (snippet != null)
            {
                CodeSampleTextBlock.Text = $"```csharp\r\n{snippet}\r\n```";
            }
            else
            {
                CodeCard.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            throw new InvalidOperationException("Invalid navigation parameter");
        }

        if (ModelFamily != null && !string.IsNullOrWhiteSpace(ModelFamily.ReadmeUrl))
        {
            var loadReadme = LoadReadme(ModelFamily.ReadmeUrl);
        }
        else
        {
            DocumentationCard.Visibility = Visibility.Collapsed;
        }

        GetSamples();
    }

    private void GetSamples()
    {
        // if we don't have a modelType, we are in a user added language model, use same samples as Phi
        var modelType = modelFamilyType ?? ModelType.Phi3Mini;

        var samples = SampleDetails.Samples.Where(s => s.Model1Types.Contains(modelType) || s.Model2Types?.Contains(modelType) == true).ToList();
        if (ModelTypeHelpers.ParentMapping.Values.Any(parent => parent.Contains(modelType)))
        {
            var parent = ModelTypeHelpers.ParentMapping.FirstOrDefault(parent => parent.Value.Contains(modelType)).Key;
            samples.AddRange(SampleDetails.Samples.Where(s => s.Model1Types.Contains(parent) || s.Model2Types?.Contains(parent) == true));
        }

        SampleList.ItemsSource = samples;
    }

    private async Task LoadReadme(string url)
    {
        string readmeContents = await GithubApi.GetContentsOfTextFile(url);
        if (!string.IsNullOrWhiteSpace(readmeContents))
        {
            readmeContents = MarkdownHelper.PreprocessMarkdown(readmeContents);

            var html = File.ReadAllText(Path.Join(Package.Current.InstalledLocation.Path, "Markdown", "index.html"));
            html = html.Replace("{MARKDOWN}", readmeContents.Replace(@"`", @"\`"));
            await MyWebView.EnsureCoreWebView2Async();
            MyWebView.CoreWebView2.NavigateToString(html);

            //markdownTextBlock.Text = readmeContents;
        }

        readmeProgressRing.IsActive = false;
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (ModelFamily == null || ModelFamily.Id == null)
        {
            return;
        }

        var dataPackage = new DataPackage();
        dataPackage.SetText($"aidevgallery://apis/{ModelFamily.Id}");
        Clipboard.SetContentWithOptions(dataPackage, null);
    }

    private void MarkdownTextBlock_LinkClicked(object sender, CommunityToolkit.WinUI.UI.Controls.LinkClickedEventArgs e)
    {
        string link = e.Link;

        if (!URLHelper.IsValidUrl(link))
        {
            link = URLHelper.FixWcrReadmeLink(link);
        }

        ModelDetailsLinkClickedEvent.Log(link);
        Process.Start(new ProcessStartInfo()
        {
            FileName = link,
            UseShellExecute = true
        });
    }

    private void SampleList_ItemInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is Sample sample)
        {
            App.MainWindow.Navigate("Samples", new SampleNavigationArgs(sample, modelDetails));
        }
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        BackgroundShadow.Receivers.Add(ShadowCastGrid);
    }
}