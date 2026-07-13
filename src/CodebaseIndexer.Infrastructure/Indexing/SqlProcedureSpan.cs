namespace CodebaseIndexer.Infrastructure.Indexing;

internal sealed record SqlProcedureSpan(int StartLine, int EndLine, string Name);
