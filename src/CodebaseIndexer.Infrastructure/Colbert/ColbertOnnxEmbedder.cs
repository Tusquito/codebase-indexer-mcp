using System.Collections.Concurrent;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Domain.Results;
using CodebaseIndexer.Infrastructure.Configuration;
using CodebaseIndexer.Infrastructure.Embedding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace CodebaseIndexer.Infrastructure.Colbert;

/// <summary>In-process ColBERT multivector embedder using ONNX Runtime + fastembed cache artifacts.</summary>
public sealed class ColbertOnnxEmbedder : IColbertEmbedder, IDisposable
{
    private static readonly ConcurrentDictionary<string, Lazy<SharedColbertModel>> SharedModels = new(StringComparer.Ordinal);

    private readonly EmbeddingOptions _embedding;
    private readonly ColbertOptions _colbert;
    private readonly ILogger<ColbertOnnxEmbedder> _logger;
    private bool _ready;
    private SharedColbertModel? _model;

    /// <summary>Creates an ONNX ColBERT embedder.</summary>
    public ColbertOnnxEmbedder(
        IOptions<EmbeddingOptions> embedding,
        IOptions<ColbertOptions> colbert,
        ILogger<ColbertOnnxEmbedder> logger)
    {
        _embedding = embedding.Value;
        _colbert = colbert.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public int TokenDimension => KnownColbertModels.ResolveTokenDimension(
        string.IsNullOrWhiteSpace(_colbert.EmbedModel) ? _embedding.ColbertEmbedModel : _colbert.EmbedModel);

    /// <inheritdoc />
    public bool IsLoaded => _ready && _model is not null;

    /// <inheritdoc />
    public Task<Result> PreloadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _model = GetOrCreateShared();
            if (_colbert.UseCuda && !_model.UsesCuda)
            {
                return Task.FromResult(Result.Failure(new Error(
                    ErrorKind.Dependency,
                    EmbedErrorCodes.Colbert,
                    "Colbert:UseCuda=true but CUDAExecutionProvider is not available "
                    + $"(providers=[{string.Join(", ", _model.ExecutionProviders)}]).")));
            }

            _ready = true;
            _logger.LogInformation(
                "colbert_onnx_ready model={Model} dim={Dim} device={Device} providers={Providers}",
                _model.ModelName,
                TokenDimension,
                _model.UsesCuda ? "cuda" : "cpu",
                string.Join(',', _model.ExecutionProviders));
            return Task.FromResult(Result.Success());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.Failure(new Error(
                ErrorKind.Dependency,
                EmbedErrorCodes.Colbert,
                $"ColBERT ONNX preload failed: {ex.Message}")));
        }
    }

    /// <inheritdoc />
    public void Release()
    {
        _ready = false;
        _model = null;
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<IReadOnlyList<IReadOnlyList<float>>>>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
        {
            return Task.FromResult(Result<IReadOnlyList<IReadOnlyList<IReadOnlyList<float>>>>.Success(
                Array.Empty<IReadOnlyList<IReadOnlyList<float>>>()));
        }

        try
        {
            var model = _model ?? GetOrCreateShared();
            _model = model;
            _ready = true;

            var results = new List<IReadOnlyList<IReadOnlyList<float>>>(texts.Count);
            var batchSize = Math.Max(1, _colbert.EmbedBatchSize);
            for (var offset = 0; offset < texts.Count; offset += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(batchSize, texts.Count - offset);
                var batch = new string[count];
                for (var i = 0; i < count; i++)
                {
                    batch[i] = texts[offset + i];
                }

                results.AddRange(model.Embed(batch, TokenDimension));
            }

            return Task.FromResult(Result<IReadOnlyList<IReadOnlyList<IReadOnlyList<float>>>>.Success(results));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<IReadOnlyList<IReadOnlyList<IReadOnlyList<float>>>>.Failure(new Error(
                ErrorKind.Dependency,
                EmbedErrorCodes.Colbert,
                $"ColBERT ONNX embed failed: {ex.Message}")));
        }
    }

    /// <inheritdoc />
    public void Dispose() => Release();

    /// <summary>Active ORT providers after load (for worker /health).</summary>
    public IReadOnlyList<string> ExecutionProviders =>
        _model?.ExecutionProviders ?? Array.Empty<string>();

    /// <summary>Active device label after load.</summary>
    public string ActiveDevice => _model is null ? (_colbert.UseCuda ? "cuda" : "cpu") : (_model.UsesCuda ? "cuda" : "cpu");

    private SharedColbertModel GetOrCreateShared()
    {
        var modelName = string.IsNullOrWhiteSpace(_colbert.EmbedModel)
            ? _embedding.ColbertEmbedModel
            : _colbert.EmbedModel;
        var key = $"{_embedding.CachePath}|{modelName}|{_colbert.UseCuda}|{_colbert.DeviceIds}|{_colbert.GpuMemLimitBytes}";
        return SharedModels.GetOrAdd(
            key,
            _ => new Lazy<SharedColbertModel>(
                () => SharedColbertModel.Load(
                    _embedding.CachePath,
                    modelName,
                    _colbert.UseCuda,
                    ParseDeviceIds(_colbert.DeviceIds),
                    _colbert.GpuMemLimitBytes,
                    ResolveMaxTokens(modelName),
                    _logger),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private int ResolveMaxTokens(string modelName)
    {
        if (_colbert.MaxQueryTokens > 0)
        {
            return _colbert.MaxQueryTokens;
        }

        if (_embedding.RerankMaxQueryTokens > 0)
        {
            return _embedding.RerankMaxQueryTokens;
        }

        return KnownColbertModels.MaxTokens.TryGetValue(modelName, out var max) ? max : 512;
    }

    private static int[]? ParseDeviceIds(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse)
            .ToArray();
    }

    private sealed class SharedColbertModel : IDisposable
    {
        private readonly InferenceSession _session;
        private readonly SessionOptions _sessionOptions;
        private readonly OrtCUDAProviderOptions? _cudaProviderOptions;
        private readonly Tokenizer _tokenizer;
        private readonly int _maxTokens;
        private readonly string _inputIdsName;
        private readonly string _attentionMaskName;
        private readonly string? _tokenTypeIdsName;
        private readonly string _outputName;

        private SharedColbertModel(
            string modelName,
            InferenceSession session,
            SessionOptions sessionOptions,
            OrtCUDAProviderOptions? cudaProviderOptions,
            Tokenizer tokenizer,
            int maxTokens,
            string inputIdsName,
            string attentionMaskName,
            string? tokenTypeIdsName,
            string outputName,
            IReadOnlyList<string> providers)
        {
            ModelName = modelName;
            _session = session;
            _sessionOptions = sessionOptions;
            _cudaProviderOptions = cudaProviderOptions;
            _tokenizer = tokenizer;
            _maxTokens = maxTokens;
            _inputIdsName = inputIdsName;
            _attentionMaskName = attentionMaskName;
            _tokenTypeIdsName = tokenTypeIdsName;
            _outputName = outputName;
            ExecutionProviders = providers;
            UsesCuda = providers.Any(p => p.Contains("CUDA", StringComparison.OrdinalIgnoreCase));
        }

        public string ModelName { get; }
        public IReadOnlyList<string> ExecutionProviders { get; }
        public bool UsesCuda { get; }

        public static SharedColbertModel Load(
            string cacheRoot,
            string modelName,
            bool useCuda,
            int[]? deviceIds,
            long gpuMemLimitBytes,
            int maxTokens,
            ILogger logger)
        {
            var modelDir = SparseModelCacheResolver.ResolveModelDirectory(cacheRoot, modelName);
            var onnxPath = FindOnnx(modelDir)
                ?? throw new InvalidOperationException($"ColBERT ONNX model not found under '{modelDir}'.");

            var tokenizer = LoadTokenizer(modelDir)
                ?? throw new InvalidOperationException($"ColBERT tokenizer not found under '{modelDir}'.");

            OrtCUDAProviderOptions? cudaOpts = null;
            SessionOptions so;
            if (useCuda)
            {
                try
                {
                    var deviceId = deviceIds is { Length: > 0 } ? deviceIds[0] : 0;
                    cudaOpts = new OrtCUDAProviderOptions();
                    var cudaProviderOptionsDict = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["device_id"] = deviceId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["cudnn_conv_use_max_workspace"] = "1",
                    };
                    if (gpuMemLimitBytes > 0)
                    {
                        cudaProviderOptionsDict["gpu_mem_limit"] = gpuMemLimitBytes.ToString(
                            System.Globalization.CultureInfo.InvariantCulture);
                    }

                    cudaOpts.UpdateOptions(cudaProviderOptionsDict);
                    so = SessionOptions.MakeSessionOptionWithCudaProvider(cudaOpts);
                }
                catch (Exception ex)
                {
                    cudaOpts?.Dispose();
                    throw new InvalidOperationException(
                        "Colbert:UseCuda=true but CUDAExecutionProvider could not be registered.", ex);
                }
            }
            else
            {
                so = new SessionOptions();
            }

            so.AppendExecutionProvider_CPU();

            InferenceSession session;
            try
            {
                session = new InferenceSession(onnxPath, so);
            }
            catch
            {
                so.Dispose();
                cudaOpts?.Dispose();
                throw;
            }

            // ORT C# 1.22 has no InferenceSession.GetProviders(); verify via OrtEnv
            // after session create (same fail-fast contract as PreloadAsync).
            var availableProviders = OrtEnv.Instance().GetAvailableProviders();
            if (useCuda && !availableProviders.Any(p =>
                    p.Contains("CUDA", StringComparison.OrdinalIgnoreCase)))
            {
                session.Dispose();
                so.Dispose();
                cudaOpts?.Dispose();
                throw new InvalidOperationException(
                    "Colbert:UseCuda=true but CUDAExecutionProvider is not active after session create "
                    + $"(available_providers=[{string.Join(", ", availableProviders)}]).");
            }

            var providers = BuildReportedProviders(useCuda, availableProviders);

            var inputNames = session.InputMetadata.Keys.ToArray();
            var outputNames = session.OutputMetadata.Keys.ToArray();
            var inputIds = FindName(inputNames, "input_ids", "input_ids:0")
                ?? inputNames[0];
            var attention = FindName(inputNames, "attention_mask", "attention_mask:0")
                ?? (inputNames.Length > 1 ? inputNames[1] : inputNames[0]);
            var tokenTypes = FindName(inputNames, "token_type_ids", "token_type_ids:0");
            var output = outputNames[0];

            logger.LogInformation(
                "loading_colbert_model model={Model} path={Path} cuda={Cuda} gpu_mem_limit={GpuMemLimit} providers={Providers}",
                modelName,
                onnxPath,
                useCuda,
                gpuMemLimitBytes > 0 ? gpuMemLimitBytes : -1,
                string.Join(',', providers));

            return new SharedColbertModel(
                modelName,
                session,
                so,
                cudaOpts,
                tokenizer,
                maxTokens,
                inputIds,
                attention,
                tokenTypes,
                output,
                providers);
        }

        private static IReadOnlyList<string> BuildReportedProviders(bool useCuda, string[] available)
        {
            var list = new List<string>();
            if (useCuda)
            {
                var cuda = available.FirstOrDefault(p =>
                    p.Contains("CUDA", StringComparison.OrdinalIgnoreCase));
                list.Add(cuda ?? "CUDAExecutionProvider");
            }

            var cpu = available.FirstOrDefault(p =>
                p.Contains("CPU", StringComparison.OrdinalIgnoreCase));
            list.Add(cpu ?? "CPUExecutionProvider");
            return list;
        }

        public IReadOnlyList<IReadOnlyList<IReadOnlyList<float>>> Embed(IReadOnlyList<string> texts, int tokenDim)
        {
            var encoded = texts.Select(t => Encode(t)).ToArray();
            var batch = encoded.Length;
            var seq = encoded.Max(e => e.Ids.Length);
            var inputIds = new DenseTensor<long>(new[] { batch, seq });
            var attention = new DenseTensor<long>(new[] { batch, seq });
            DenseTensor<long>? tokenTypes = _tokenTypeIdsName is null
                ? null
                : new DenseTensor<long>(new[] { batch, seq });

            for (var b = 0; b < batch; b++)
            {
                var (ids, mask) = encoded[b];
                for (var t = 0; t < seq; t++)
                {
                    inputIds[b, t] = t < ids.Length ? ids[t] : 0;
                    attention[b, t] = t < mask.Length ? mask[t] : 0;
                    if (tokenTypes is not null)
                    {
                        tokenTypes[b, t] = 0;
                    }
                }
            }

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_inputIdsName, inputIds),
                NamedOnnxValue.CreateFromTensor(_attentionMaskName, attention),
            };
            if (_tokenTypeIdsName is not null && tokenTypes is not null)
            {
                inputs.Add(NamedOnnxValue.CreateFromTensor(_tokenTypeIdsName, tokenTypes));
            }

            using var results = _session.Run(inputs);
            var output = results.First(r => r.Name == _outputName || results.Count == 1).AsTensor<float>();
            var dims = output.Dimensions.ToArray();
            if (dims.Length != 3)
            {
                throw new InvalidOperationException(
                    $"ColBERT ONNX output rank {dims.Length} unexpected; expected [batch, seq, dim].");
            }

            var outDim = dims[2];
            if (outDim != tokenDim)
            {
                throw new InvalidOperationException(
                    $"ColBERT ONNX output dim {outDim} does not match expected {tokenDim}.");
            }

            var multivectors = new List<IReadOnlyList<IReadOnlyList<float>>>(batch);
            for (var b = 0; b < batch; b++)
            {
                var tokens = new List<IReadOnlyList<float>>();
                for (var t = 0; t < seq; t++)
                {
                    if (attention[b, t] == 0)
                    {
                        continue;
                    }

                    var vec = new float[outDim];
                    for (var d = 0; d < outDim; d++)
                    {
                        vec[d] = output[b, t, d];
                    }

                    tokens.Add(vec);
                }

                if (tokens.Count == 0)
                {
                    tokens.Add(new float[outDim]);
                }

                multivectors.Add(tokens);
            }

            return multivectors;
        }

        public void Dispose()
        {
            _session.Dispose();
            _sessionOptions.Dispose();
            _cudaProviderOptions?.Dispose();
        }

        private (long[] Ids, long[] Mask) Encode(string text)
        {
            var ids = _tokenizer.EncodeToIds(text, _maxTokens, out _, out _).Select(i => (long)i).ToArray();
            if (ids.Length == 0)
            {
                ids = [0];
            }

            var mask = Enumerable.Repeat(1L, ids.Length).ToArray();
            return (ids, mask);
        }

        private static string? FindOnnx(string modelDir)
        {
            foreach (var name in new[] { "model.onnx", "model_optimized.onnx", "onnx/model.onnx" })
            {
                var path = Path.Combine(modelDir, name);
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return Directory.EnumerateFiles(modelDir, "*.onnx", SearchOption.AllDirectories)
                .OrderBy(p => p.Length)
                .FirstOrDefault();
        }

        private static Tokenizer? LoadTokenizer(string modelDir)
        {
            var tokenizerJson = Path.Combine(modelDir, "tokenizer.json");
            if (File.Exists(tokenizerJson))
            {
                try
                {
                    return LoadFromTokenizerJson(tokenizerJson);
                }
                catch
                {
                    // fall through to vocab.txt
                }
            }

            var vocabTxt = Path.Combine(modelDir, "vocab.txt");
            if (File.Exists(vocabTxt))
            {
                using var stream = File.OpenRead(vocabTxt);
                return BertTokenizer.Create(stream, new BertOptions());
            }

            return null;
        }

        private static Tokenizer LoadFromTokenizerJson(string tokenizerJsonPath)
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(tokenizerJsonPath));
            var model = document.RootElement.GetProperty("model");
            if (!model.TryGetProperty("vocab", out var vocabElement))
            {
                throw new InvalidOperationException("tokenizer.json missing vocab.");
            }

            // WordPiece (ColBERT / BERT): write vocab.txt lines ordered by id.
            if (!model.TryGetProperty("merges", out _))
            {
                var byId = vocabElement.EnumerateObject()
                    .Select(p => (Token: p.Name, Id: p.Value.GetInt32()))
                    .OrderBy(x => x.Id)
                    .ToArray();
                using var vocabStream = new MemoryStream();
                using (var writer = new StreamWriter(vocabStream, leaveOpen: true))
                {
                    foreach (var (token, _) in byId)
                    {
                        writer.WriteLine(token);
                    }

                    writer.Flush();
                }

                vocabStream.Position = 0;
                return BertTokenizer.Create(vocabStream, new BertOptions());
            }

            // BPE fallback
            var vocabDict = vocabElement.EnumerateObject()
                .ToDictionary(static p => p.Name, static p => p.Value.GetInt32());
            using var bpeVocab = new MemoryStream();
            System.Text.Json.JsonSerializer.Serialize(bpeVocab, vocabDict);
            bpeVocab.Position = 0;
            using var mergesStream = new MemoryStream();
            using (var writer = new StreamWriter(mergesStream, leaveOpen: true))
            {
                writer.WriteLine("#version: 0.2");
                foreach (var merge in model.GetProperty("merges").EnumerateArray())
                {
                    writer.WriteLine(merge.GetString());
                }

                writer.Flush();
            }

            mergesStream.Position = 0;
            return BpeTokenizer.Create(bpeVocab, mergesStream);
        }

        private static string? FindName(IReadOnlyList<string> names, params string[] candidates)
        {
            foreach (var c in candidates)
            {
                var match = names.FirstOrDefault(n => string.Equals(n, c, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    return match;
                }
            }

            return null;
        }
    }
}
