using Microsoft.Extensions.Configuration;

namespace CodebaseIndexer.Infrastructure.Configuration;

internal static class SettingsBinder
{
    public static Settings Bind(IConfiguration configuration)
    {
        var section = configuration.GetSection(Settings.SectionName);
        var settings = section.Get<Settings>()
            ?? throw new InvalidOperationException(
                $"Configuration section '{Settings.SectionName}' is missing or invalid.");

        return new Settings
        {
            QdrantUrl = configuration.GetConnectionString("qdrant") ?? settings.QdrantUrl,
            QdrantTimeoutSeconds = settings.QdrantTimeoutSeconds,
            QdrantCollection = settings.QdrantCollection,
            HybridSearch = settings.HybridSearch,
            DenseEmbedModel = settings.DenseEmbedModel,
            SparseEmbedModel = settings.SparseEmbedModel,
            DenseEmbedVectorSize = settings.DenseEmbedVectorSize,
            TeiUrl = configuration.GetConnectionString("tei") ?? settings.TeiUrl,
            TeiEmbedBatchSize = settings.TeiEmbedBatchSize,
            TeiTimeoutSeconds = settings.TeiTimeoutSeconds,
            MrlDimensions = settings.MrlDimensions,
            QueryInstruction = settings.QueryInstruction,
            NormalizeOutput = settings.NormalizeOutput,
            RerankEnabled = settings.RerankEnabled,
            PayloadIndexes = settings.PayloadIndexes,
            VectorsOnDisk = settings.VectorsOnDisk,
            SparseOnDisk = settings.SparseOnDisk,
        };
    }
}
