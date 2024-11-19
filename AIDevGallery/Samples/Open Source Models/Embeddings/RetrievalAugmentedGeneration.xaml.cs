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
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace AIDevGallery.Samples.OpenSourceModels.SentenceEmbeddings.Embeddings
{
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
            this.Unloaded += (s, e) =>
            {
                CleanUp();
            };
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
        }

        private async void IndexPDFButton_Click(object sender, RoutedEventArgs e)
        {
            if (_embeddings == null)
            {
                return;
            }

            IndexPDFButton.IsEnabled = false;
            LoadPDFProgressRing.IsActive = true;
            LoadPDFProgressRing.Visibility = Visibility.Visible;
            PdfProblemTextBlock.Text = string.Empty;
            IndexPDFText.Text = "Selecting PDF...";

            var window = new Window();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            // Set the file type filter
            picker.FileTypeFilter.Add(".pdf");

            // Pick a file
            _pdfFile = await picker.PickSingleFileAsync();
            if (_pdfFile == null)
            {
                IndexPDFButton.IsEnabled = true;
                LoadPDFProgressRing.IsActive = false;
                LoadPDFProgressRing.Visibility = Visibility.Collapsed;
                IndexPDFProgressStackPanel.Visibility = Visibility.Collapsed;
                return;
            }

            IndexPDFText.Text = "Indexing PDF...";

            var contents = new List<(string Text, uint Page)>();

            // 1) Read the PDF file
            using (PdfDocument document = PdfDocument.Open(_pdfFile.Path))
            {
                foreach (var page in document.GetPages())
                {
                    var words = page.GetWords();
                    var builder = string.Join(" ", words);

                    var range = builder
                            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => ((string Text, uint Page))(x, page.Number));

                    contents.AddRange(range);
                }
            }

            if (contents.Count == 0)
            {
                IndexPDFButton.IsEnabled = true;
                LoadPDFProgressRing.IsActive = false;
                LoadPDFProgressRing.Visibility = Visibility.Collapsed;
                IndexPDFText.Text = "Select PDF";
                PdfProblemTextBlock.Text = "We weren't able to read this PDF. Please try another.";
                return;
            }

            // 2) Split the text into chunks to make sure they are
            // smaller than what the Embeddings model supports
            var maxLength = 1024 / 2;
            List<(string Text, uint Page)> chunkedContents = [];
            foreach (var content in contents)
            {
                chunkedContents.AddRange(SplitInChunks(content, maxLength));
            }

            contents = chunkedContents;

            IndexPDFProgressBar.Minimum = 0;
            IndexPDFProgressBar.Maximum = contents.Count;
            IndexPDFProgressBar.Value = 0;

            Stopwatch sw = Stopwatch.StartNew();

            void UpdateProgress(float progress)
            {
                var elapsed = sw.Elapsed;
                if (progress == 0)
                {
                    progress = 0.0001f;
                }

                var remaining = TimeSpan.FromSeconds((long)(elapsed.TotalSeconds / progress * (1 - progress) / 5) * 5);

                LoadPDFProgressRing.Value = progress * contents.Count;
                IndexPDFText.Text = $"Indexing PDF... {progress:P0} ({remaining})";
            }

            if (_cts != null)
            {
                _cts.Cancel();
                _cts = null;
                AskSLMButton.Content = "Answer";
                return;
            }

            _cts = new CancellationTokenSource();

            // 3) Index the chunks
#pragma warning disable SKEXP0020 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            _vectorStore = new InMemoryVectorStore();
#pragma warning restore SKEXP0020 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            _pdfPages = _vectorStore.GetCollection<int, PdfPageData>("pages");
            await _pdfPages.CreateCollectionIfNotExistsAsync(_cts.Token).ConfigureAwait(false);

            var total = contents.Count;
            try
            {
                int i = 0;
                await foreach (var embedding in _embeddings.GenerateStreamingAsync(contents.Select(c => c.Text), null, _cts.Token).ConfigureAwait(false))
                {
                    await _pdfPages.UpsertAsync(
                        new PdfPageData
                        {
                            Key = i,
                            Page = contents[i].Page,
                            Text = contents[i].Text,
                            Vector = embedding.Vector
                        },
                        null,
                        _cts.Token).ConfigureAwait(false);
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        UpdateProgress((float)i / total);
                    });
                    i++;
                }
            }
            catch (OperationCanceledException)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    IndexPDFButton.IsEnabled = true;
                    LoadPDFProgressRing.IsActive = false;
                    LoadPDFProgressRing.Visibility = Visibility.Collapsed;
                    IndexPDFProgressStackPanel.Visibility = Visibility.Collapsed;
                    IndexPDFText.Text = "Select PDF";
                });

                return;
            }
            finally
            {
                _cts = null;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                ShowPDFPage.IsEnabled = true;
                IndexPDFText.Text = "Indexing PDF... Done!";
                IndexPDFProgressStackPanel.Visibility = Visibility.Collapsed;
                IndexPDFGrid.Visibility = Visibility.Collapsed;
                ChatGrid.Visibility = Visibility.Visible;
            });
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
    }
}