// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using AIDevGallery.Models;
using AIDevGallery.Samples.Attributes;
using AIDevGallery.Samples.SharedCode;
using Microsoft.Graphics.Imaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.AI.Generative;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace AIDevGallery.Samples.WCRAPIs;

[GallerySample(
    Name = "WCR Describe Image",
    Model1Types = [ModelType.ImageDescription],
    Scenario = ScenarioType.ImageDescribeImageWcr,
    Id = "a1b1f64f-bc57-41a3-8fb3-ac8f1536d757",
    SharedCode = [SharedCodeEnum.WcrModelDownloaderCs, SharedCodeEnum.WcrModelDownloaderXaml],
    Icon = "\uEE6F")]

internal sealed partial class ImageDescription : BaseSamplePage
{
    private ImageDescriptionGenerator? _imageDescriptor;

    public ImageDescription()
    {
        this.InitializeComponent();
    }

    protected override async Task LoadModelAsync(SampleNavigationParameters sampleParams)
    {
        sampleParams.ShowWcrModelLoadingMessage = true;
        if (ImageDescriptionGenerator.IsAvailable())
        {
            WcrModelDownloader.State = WcrApiDownloadState.Downloaded;
        }

        sampleParams.NotifyCompletion();
    }

    private async void WcrModelDownloader_DownloadClicked(object sender, EventArgs e)
    {
        var operation = ImageDescriptionGenerator.MakeAvailableAsync();

        await WcrModelDownloader.SetDownloadOperation(operation);
    }

    private async void LoadImage_Click(object sender, RoutedEventArgs e)
    {
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
        var package = Clipboard.GetContent();
        if (package.Contains(StandardDataFormats.Bitmap))
        {
            var streamRef = await package.GetBitmapAsync();

            IRandomAccessStream stream = await streamRef.OpenReadAsync();
            await SetImage(stream);
        }
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
        var bitmapSource = new SoftwareBitmapSource();

        // This conversion ensures that the image is Bgra8 and Premultiplied
        SoftwareBitmap convertedImage = SoftwareBitmap.Convert(inputBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        await bitmapSource.SetBitmapAsync(convertedImage);
        ImageSrc.Source = bitmapSource;
        DescribeImage(inputBitmap);
    }

    private async void DescribeImage(SoftwareBitmap bitmap)
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            Loader.Visibility = Visibility.Visible;
            OutputTxt.Visibility = Visibility.Collapsed;
        });

        var isFirstWord = true;
        using var bitmapBuffer = ImageBuffer.CreateCopyFromBitmap(bitmap);
        _imageDescriptor ??= await ImageDescriptionGenerator.CreateAsync();
        var describeTask = _imageDescriptor.DescribeAsync(bitmapBuffer);
        if (describeTask != null)
        {
            describeTask.Progress += (asyncInfo, delta) =>
            {
                var result = asyncInfo.GetResults().Response;

                DispatcherQueue?.TryEnqueue(() =>
                {
                    if (isFirstWord)
                    {
                        Loader.Visibility = Visibility.Collapsed;
                        OutputTxt.Visibility = Visibility.Visible;
                        isFirstWord = false;
                    }

                    ResponseTxt.Text = result;
                });
            };
            await describeTask;
        }

        Loader.Visibility = Visibility.Collapsed;
    }
}