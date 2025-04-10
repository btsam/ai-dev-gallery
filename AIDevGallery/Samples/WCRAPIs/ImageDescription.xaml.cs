// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using AIDevGallery.Models;
using AIDevGallery.Samples.Attributes;
using Microsoft.Graphics.Imaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.AI.Generative;
using Microsoft.Windows.Management.Deployment;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace AIDevGallery.Samples.WCRAPIs;

[GallerySample(
    Name = "Describe Image WCR",
    Model1Types = [ModelType.ImageDescription],
    Scenario = ScenarioType.ImageDescribeImage,
    Id = "a1b1f64f-bc57-41a3-8fb3-ac8f1536d757",
    AssetFilenames = [
        "Road.png"
    ],
    Icon = "\uEE6F")]

internal sealed partial class ImageDescription : BaseSamplePage
{
    private readonly Dictionary<string, ImageDescriptionScenario> _scenarioDictionary = new Dictionary<string, ImageDescriptionScenario>
    {
        { "Accessible", ImageDescriptionScenario.Accessibility },
        { "Caption", ImageDescriptionScenario.Caption },
        { "Detailed", ImageDescriptionScenario.DetailedNarration },
        { "OfficeCharts", ImageDescriptionScenario.OfficeCharts },
    };

    private ImageDescriptionGenerator? _imageDescriptor;
    private CancellationTokenSource? _cts;
    private SoftwareBitmap? _currentBitmap;
    private ImageDescriptionScenario _currentScenario = ImageDescriptionScenario.Caption;

    public ImageDescription()
    {
        this.InitializeComponent();
    }

    protected override async Task LoadModelAsync(SampleNavigationParameters sampleParams)
    {
        if (!ImageDescriptionGenerator.IsAvailable())
        {
            var operation = await ImageDescriptionGenerator.MakeAvailableAsync();

            if (operation.Status != PackageDeploymentStatus.CompletedSuccess)
            {
                // TODO: handle error
            }
        }

        _ = LoadDefaultImage();
        sampleParams.NotifyCompletion();
    }

    private async Task LoadDefaultImage()
    {
        var file = await StorageFile.GetFileFromPathAsync(Path.Join(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Assets", "Road.png"));
        using var stream = await file.OpenReadAsync();
        await SetImage(stream);
    }

    private async void LoadImage_Click(object sender, RoutedEventArgs e)
    {
        SendSampleInteractedEvent("LoadImageClicked");
        var window = new Window();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

        var picker = new FileOpenPicker();

        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".jpg");

        picker.ViewMode = PickerViewMode.Thumbnail;

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            using var stream = await file.OpenReadAsync();
            await SetImage(stream);
        }
    }

    private async void PasteImage_Click(object sender, RoutedEventArgs e)
    {
        SendSampleInteractedEvent("PasteImageClick");
        var package = Clipboard.GetContent();
        if (package.Contains(StandardDataFormats.Bitmap))
        {
            var streamRef = await package.GetBitmapAsync();

            using var stream = await streamRef.OpenReadAsync();
            await SetImage(stream);
        }
        else if (package.Contains(StandardDataFormats.StorageItems))
        {
            var storageItems = await package.GetStorageItemsAsync();
            if (IsImageFile(storageItems[0].Path))
            {
                try
                {
                    var storageFile = await StorageFile.GetFileFromPathAsync(storageItems[0].Path);
                    using var stream = await storageFile.OpenReadAsync();
                    await SetImage(stream);
                }
                catch
                {
                    Console.WriteLine("Invalid Image File");
                }
            }
        }
    }

    private static bool IsImageFile(string fileName)
    {
        string[] imageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif"];
        return imageExtensions.Contains(Path.GetExtension(fileName)?.ToLowerInvariant());
    }

    private async Task SetImage(IRandomAccessStream stream)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream);
        SoftwareBitmap inputBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        if (inputBitmap == null)
        {
            return;
        }

        ResponseTxt.Text = string.Empty;
        _currentBitmap = inputBitmap;
        var bitmapSource = new SoftwareBitmapSource();

        // This conversion ensures that the image is Bgra8 and Premultiplied
        SoftwareBitmap convertedImage = SoftwareBitmap.Convert(inputBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        await bitmapSource.SetBitmapAsync(convertedImage);
        ImageSrc.Source = bitmapSource;
        DescribeImage(inputBitmap, _currentScenario);
    }

    private async void DescribeImage(SoftwareBitmap bitmap, ImageDescriptionScenario scenario)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        DispatcherQueue?.TryEnqueue(() =>
        {
            Loader.Visibility = Visibility.Visible;
            StopBtn.Visibility = Visibility.Visible;
            ScenarioSelectButton.Visibility = Visibility.Collapsed;
            ResponseTxt.Visibility = Visibility.Collapsed;
        });

        var isFirstWord = true;
        try
        {
            using var bitmapBuffer = ImageBuffer.CreateCopyFromBitmap(bitmap);
            _imageDescriptor ??= await ImageDescriptionGenerator.CreateAsync();
            var describeTask = _imageDescriptor.DescribeAsync(bitmapBuffer, scenario);
            if (describeTask != null)
            {
                describeTask.Progress += (asyncInfo, delta) =>
                {
                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        if (isFirstWord)
                        {
                            Loader.Visibility = Visibility.Collapsed;
                            ResponseTxt.Visibility = Visibility.Visible;
                            isFirstWord = false;
                        }

                        ResponseTxt.Text = delta;
                    });
                    if (_cts?.IsCancellationRequested == true)
                    {
                        describeTask.Cancel();
                    }
                };

                await describeTask.AsTask(_cts.Token);
            }
        }
        catch (TaskCanceledException)
        {
            // Don't do anything
        }
        catch (Exception ex)
        {
            ShowException(ex);
        }

        Loader.Visibility = Visibility.Collapsed;
        StopBtn.Visibility = Visibility.Collapsed;
        ScenarioSelectButton.Visibility = Visibility.Visible;
        _cts?.Dispose();
        _cts = null;
    }

    private void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }

    private void MenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        MenuFlyoutItem flyoutItem = (sender as MenuFlyoutItem)!;

        string? scenarioString = flyoutItem.Tag as string;
        ImageDescriptionScenario newScenario;
        if (!string.IsNullOrEmpty(scenarioString) && _scenarioDictionary.TryGetValue(scenarioString, out newScenario) && newScenario != _currentScenario)
        {
            _currentScenario = newScenario;
            ScenarioSelectTextblock.Text = flyoutItem.Text;
            DescribeImage(_currentBitmap!, _currentScenario);
        }
    }
}