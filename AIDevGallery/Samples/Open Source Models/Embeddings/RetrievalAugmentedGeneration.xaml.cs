// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using AIDevGallery.Models;
using AIDevGallery.Samples.Attributes;
using AIDevGallery.Samples.SharedCode;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.InMemory;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace AIDevGallery.Samples.OpenSourceModels.SentenceEmbeddings.Embeddings;

[GallerySample(
    Name = "Retrieval Augmented Generation",
    Model1Types = [ModelType.LanguageModels],
    Model2Types = [ModelType.EmbeddingModel],
    Scenario = ScenarioType.TextRetrievalAugmentedGeneration,
    SharedCode = [
        SharedCodeEnum.EmbeddingGenerator,
        SharedCodeEnum.EmbeddingModelInput,
        SharedCodeEnum.GenAIModel,
        SharedCodeEnum.TokenizerExtensions,
        SharedCodeEnum.DeviceUtils,
        SharedCodeEnum.StringData
    ],
    NugetPackageReferences = [
        "PdfPig",
        "Microsoft.ML.Tokenizers",
        "System.Numerics.Tensors",
        "Microsoft.ML.OnnxRuntimeGenAI.DirectML",
        "Microsoft.ML.OnnxRuntime.DirectML",
        "Microsoft.Extensions.AI.Abstractions",
        "Microsoft.SemanticKernel.Connectors.InMemory"
    ],
    Id = "9C1FB14D-4841-449C-9563-4551106BB693",
    Icon = "\uE8D4")]
internal sealed partial class RetrievalAugmentedGeneration : BaseSamplePage
{
    private readonly ChatOptions _chatOptions = GenAIModel.GetDefaultChatOptions();
    private EmbeddingGenerator? _embeddings;
    private IChatClient? _chatClient;
    private IVectorStore? _vectorStore;
    private IVectorStoreRecordCollection<int, PdfPageData>? _pdfPages;
    private StorageFile? _pdfFile;
    private InMemoryRandomAccessStream? _inMemoryRandomAccessStream;
    private CancellationTokenSource? _cts;
    private bool _isCancellable;

    private List<uint>? selectedPages;
    private int selectedPageIndex = -1;
    private string searchTextBoxInitialText = string.Empty;

    public class PdfPageData
    {
        [VectorStoreRecordKey]
        public required int Key { get; init; }
        [VectorStoreRecordData]
        public required uint Page { get; init; }
        [VectorStoreRecordData]
        public required string Text { get; init; }
        [VectorStoreRecordVector(384, DistanceFunction.CosineSimilarity)]
        public required ReadOnlyMemory<float> Vector { get; init; }
    }

    public RetrievalAugmentedGeneration()
    {
        this.InitializeComponent();
        this.Unloaded += (s, e) => CleanUp();
        this.Loaded += (s, e) => Page_Loaded(); // <exclude-line>
    }

    protected override async Task LoadModelAsync(MultiModelSampleNavigationParameters sampleParams)
    {
        _embeddings = new EmbeddingGenerator(sampleParams.ModelPaths[1], sampleParams.HardwareAccelerators[1]);
        _chatClient = await sampleParams.GetIChatClientAsync();
        _chatOptions.MaxOutputTokens = 2048;

        sampleParams.NotifyCompletion();

        IndexPDFButton.IsEnabled = true;
        IndexPDFText.Text = "Select a PDF";
    }

    // <exclude>
    private void Page_Loaded()
    {
        IndexPDFButton.Focus(FocusState.Programmatic);
    }

    // </exclude>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        CleanUp();
    }

    private void CleanUp()
    {
        _cts?.Cancel();
        _cts = null;
        _chatClient?.Dispose();
        _vectorStore = null;
        _pdfPages = null;
        _embeddings?.Dispose();
        _pdfFile = null;
        _inMemoryRandomAccessStream?.Dispose();
        _cts?.Cancel();
        _cts = null;
    }

    private async void IndexPDFButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCancellable)
        {
            _cts?.Cancel();
            _cts = null;
            ToSelectState();
            return;
        }

        if (_embeddings == null)
        {
            return;
        }

        ToSelectingState();

        _pdfFile = await SelectPDFFromFileSystem();
        if (_pdfFile == null)
        {
            ToSelectState();
            return;
        }

        await IndexPDF();
    }

    private async Task IndexPDF()
    {
        if (_pdfFile == null || _embeddings == null)
        {
            return;
        }

        ToIndexingState();
        _cts = new CancellationTokenSource();
        CancellationToken ct = _cts.Token;

#pragma warning disable SKEXP0020 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        _vectorStore = new InMemoryVectorStore();
#pragma warning restore SKEXP0020 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        _pdfPages = _vectorStore.GetCollection<int, PdfPageData>("pages");
        await _pdfPages.CreateCollectionIfNotExistsAsync(ct).ConfigureAwait(false);
        int chunksProcessedCount = 0;

        try
        {
            await Task.Run(
            async () =>
            {
                using PdfDocument document = PdfDocument.Open(_pdfFile.Path);
                foreach (var page in document.GetPages())
                {
                    string pageText = string.Join(" ", page.GetWords());

                    if(pageText == string.Empty)
                    {
                        continue;
                    }

                    List<(string Text, uint Page)> pageChunks = SplitInChunks((pageText, (uint)page.Number), 512).ToList();
                    int i = 0;
                    await foreach(var embedding in _embeddings.GenerateStreamingAsync(pageChunks.Select(c => c.Text), null, ct).ConfigureAwait(false))
                    {
                        await _pdfPages.UpsertAsync(
                        new PdfPageData
                        {
                            Key = chunksProcessedCount,
                            Page = pageChunks[i].Page,
                            Text = pageChunks[i].Text,
                            Vector = embedding.Vector
                        },
                        null,
                        ct).ConfigureAwait(false);
                        i++;
                        chunksProcessedCount++;
                    }

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        UpdateProgress(page.Number, document.NumberOfPages);
                    });
                }
            },
            ct);

            ct.ThrowIfCancellationRequested();
        }
        catch (Exception ex)
        {
            ToSelectState();

            if (ex is not OperationCanceledException)
            {
                PdfProblemTextBlock.Text = "We weren't able to read this PDF. Please try another.";
            }

            return;
        }

        if(chunksProcessedCount == 0)
        {
            ToSelectState();
            PdfProblemTextBlock.Text = "We weren't able to read this PDF. Please try another.";
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            ShowPDFPage.IsEnabled = true;
            IndexPDFGrid.Visibility = Visibility.Collapsed;
            ChatGrid.Visibility = Visibility.Visible;
            SelectNewPDFButton.Visibility = Visibility.Visible;
        });
    }

    private async Task DoRAG()
    {
        if (_embeddings == null || _chatClient == null || _pdfPages == null)
        {
            return;
        }

        if (_cts != null)
        {
            _cts.Cancel();
            _cts = null;
            AskSLMButton.Content = "Answer";
            return;
        }

        selectedPageIndex = 0;
        AskSLMButton.Content = "Cancel";
        SearchTextBox.IsEnabled = false;
        _cts = new CancellationTokenSource();

        const string systemPrompt = "You are a knowledgeable assistant specialized in answering questions based solely on information from specific PDF pages provided by the user. " +
            "When responding, focus on delivering clear, accurate answers drawn only from the content in these pages, avoiding outside information or assumptions.";

        var searchPrompt = this.SearchTextBox.Text;

        // 4) Search the chunks using the user's prompt, with the same model used for indexing
        var searchVector = await _embeddings.GenerateAsync([searchPrompt], null, _cts.Token);
        var vectorSearchResults = await _pdfPages.VectorizedSearchAsync(
                searchVector[0].Vector,
                new VectorSearchOptions
                {
                    Top = 5,
                    VectorPropertyName = nameof(PdfPageData.Vector)
                },
                _cts.Token);

        var contents = vectorSearchResults.Results.ToBlockingEnumerable()
                .Select(r => r.Record)
                .DistinctBy(c => c.Page)
                .OrderBy(c => c.Page);

        selectedPages = contents.Select(c => c.Page).ToList();

        PagesUsedRun.Text = $"Using page(s) : {string.Join(", ", selectedPages)}";
        InformationSV.Visibility = Visibility.Visible;

        var pagesChunks = contents.GroupBy(c => c.Page)
            .Select(g => $"Page {g.Key}: {string.Join(' ', g.Select(c => c.Text))}");

        AnswerRun.Text = string.Empty;
        var fullResult = string.Empty;

        await Task.Run(
            async () =>
            {
                await foreach (var partialResult in _chatClient.CompleteStreamingAsync(
                    [
                        new ChatMessage(ChatRole.System, systemPrompt),
                        .. pagesChunks.Select(c => new ChatMessage(ChatRole.User, c)),
                        new ChatMessage(ChatRole.User, searchPrompt),
                    ],
                    _chatOptions,
                    _cts.Token))
                {
                    fullResult += partialResult;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        AnswerRun.Text = fullResult;
                    });
                }
            },
            _cts.Token);

        _cts = null;

        AskSLMButton.Content = "Answer";
        SearchTextBox.IsEnabled = true;
    }

    private void Grid_Loaded(object sender, RoutedEventArgs e)
    {
        searchTextBoxInitialText = SearchTextBox.Text;
    }

    private async Task UpdatePdfImageAsync()
    {
        if (_pdfFile == null || selectedPages == null || selectedPages.Count == 0)
        {
            return;
        }

        var pdfDocument = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(_pdfFile).AsTask().ConfigureAwait(false);
        var pageId = selectedPages[selectedPageIndex];
        if (pageId < 0 || pdfDocument.PageCount < pageId)
        {
            return;
        }

        var page = pdfDocument.GetPage(pageId - 1);
        _inMemoryRandomAccessStream?.Dispose();
        _inMemoryRandomAccessStream = new();
        var rect = page.Dimensions.TrimBox;
        await page.RenderToStreamAsync(_inMemoryRandomAccessStream).AsTask().ConfigureAwait(false);

        DispatcherQueue.TryEnqueue(() =>
        {
            BitmapImage bitmapImage = new();
            bitmapImage.SetSource(_inMemoryRandomAccessStream);

            PdfImage.Source = bitmapImage;
            PdfImageGrid.Visibility = Visibility.Visible;
            SelectNewPDFButton.Visibility = Visibility.Collapsed;
            UpdatePreviousAndNextPageButtonEnabled();
        });
    }

    private void UpdatePreviousAndNextPageButtonEnabled()
    {
        if (selectedPages == null || selectedPages.Count == 0)
        {
            PreviousPageButton.IsEnabled = false;
            NextPageButton.IsEnabled = false;
            return;
        }

        PreviousPageButton.IsEnabled = selectedPageIndex > 0;
        NextPageButton.IsEnabled = selectedPageIndex < selectedPages.Count - 1;
    }

    private void SearchTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (SearchTextBox.Text == searchTextBoxInitialText)
        {
            SearchTextBox.Text = string.Empty;
        }
    }

    private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SearchTextBox.Text))
        {
            SearchTextBox.Text = searchTextBoxInitialText;
        }
    }

    private async void ShowPDFPage_Click(object sender, RoutedEventArgs e)
    {
        await UpdatePdfImageAsync().ConfigureAwait(false);
    }

    private void PdfImage_Tapped(object sender, TappedRoutedEventArgs e)
    {
        PdfImageGrid.Visibility = Visibility.Collapsed;
        SelectNewPDFButton.Visibility = Visibility.Visible;
    }

    private async void PreviousPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedPageIndex <= 0)
        {
            return;
        }

        selectedPageIndex--;
        await UpdatePdfImageAsync().ConfigureAwait(false);
    }

    private async void NextPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedPages == null || selectedPageIndex >= selectedPages.Count - 1)
        {
            return;
        }

        selectedPageIndex++;
        await UpdatePdfImageAsync().ConfigureAwait(false);
    }

    private void ClosePdfButton_Click(object sender, RoutedEventArgs e)
    {
        PdfImageGrid.Visibility = Visibility.Collapsed;
        SelectNewPDFButton.Visibility = Visibility.Visible;
    }

    private IEnumerable<(string Text, uint Page)> SplitInChunks((string Text, uint Page) input, int maxLength)
    {
        var sentences = input.Text.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        StringBuilder currentChunk = new();

        foreach (var sentence in sentences)
        {
            if (sentence.Length > maxLength)
            {
                if (currentChunk.Length > 0)
                {
                    yield return (currentChunk.ToString(), input.Page);
                    currentChunk.Clear();
                }

                var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var word in words)
                {
                    if (currentChunk.Length + word.Length + 1 > maxLength)
                    {
                        yield return (currentChunk.ToString(), input.Page);
                        currentChunk.Clear();
                    }

                    currentChunk.Append(word);
                    currentChunk.Append(' ');
                }

                continue;
            }

            if (currentChunk.Length + sentence.Length + 2 > maxLength)
            {
                yield return (currentChunk.ToString(), input.Page);

                currentChunk.Clear();
            }

            currentChunk.Append(sentence);
            currentChunk.Append(". ");
        }

        if (currentChunk.Length > 0)
        {
            yield return (currentChunk.ToString(), input.Page);
        }
    }

    private void ToSelectState()
    {
        _pdfPages?.DeleteCollectionAsync();
        HideProgress();
        _isCancellable = false;
        ShowPDFPage.IsEnabled = false;
        SelectNewPDFButton.Visibility = Visibility.Collapsed;
        IndexPDFGrid.Visibility = Visibility.Visible;
        ChatGrid.Visibility = Visibility.Collapsed;
        IndexPDFButton.IsEnabled = true;
        LoadPDFProgressRing.IsActive = false;
        LoadPDFProgressRing.Visibility = Visibility.Collapsed;
        IndexPDFText.Text = "Select PDF";
    }

    private void ToSelectingState()
    {
        IndexPDFButton.IsEnabled = false;
        LoadPDFProgressRing.IsActive = true;
        LoadPDFProgressRing.Visibility = Visibility.Visible;
        PdfProblemTextBlock.Text = string.Empty;
        IndexPDFText.Text = "Selecting PDF...";
    }

    private void ToIndexingState()
    {
        IndexPDFButton.IsEnabled = true;
        IndexPDFText.Text = "Cancel";
        _isCancellable = true;
        ProgressPanel.Visibility = Visibility.Visible;
        PdfProblemTextBlock.Text = string.Empty;
    }

    private async void AskSLMButton_Click(object sender, RoutedEventArgs e)
    {
        if (SearchTextBox.Text.Length > 0)
        {
            await DoRAG();
        }
    }

    private async void TextBox_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && sender is TextBox && SearchTextBox.Text.Length > 0)
        {
            await DoRAG();
        }
    }

    private async Task<StorageFile> SelectPDFFromFileSystem()
    {
        var window = new Window();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".pdf");
        return await picker.PickSingleFileAsync();
    }

    private async void SelectNewPDF_Click(object sender, RoutedEventArgs e)
    {
        StorageFile pdfFile = await SelectPDFFromFileSystem();
        if(pdfFile != null)
        {
            _pdfFile = pdfFile;
            ToSelectState();
            await IndexPDF();
        }
    }

    private void UpdateProgress(int currentPage, int totalPages)
    {
        int progressValue = (int)Math.Floor((float)currentPage / (float)totalPages * 100);
        string progressString = $"Indexed {currentPage} of {totalPages} pages ({progressValue}%)";

        IndexingProgressBar.Value = progressValue;
        ProgressStatusTextBlock.Text = progressString;
    }

    private void HideProgress()
    {
        ProgressPanel.Visibility = Visibility.Collapsed;
        IndexingProgressBar.Value = 0;
        ProgressStatusTextBlock.Text = string.Empty;
    }
}