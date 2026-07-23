namespace CodebaseIndexer.Domain.Results;

/// <summary>
/// Typed failure payload for <see cref="Result"/> / <see cref="Result{T}"/>.
/// </summary>
/// <param name="Kind">Failure category used for branching and MCP mapping.</param>
/// <param name="Code">
/// Stable machine-readable code string (closed vocabulary that grows in later phases;
/// callers should treat unknown codes as opaque).
/// </param>
/// <param name="Message">Human-readable description of the failure.</param>
/// <param name="Metadata">Optional structured details (paths, ids, HTTP status, etc.).</param>
/// <remarks>
/// Use with <see cref="Result.Failure"/> / <see cref="Result{T}.Failure"/> for expected domain and
/// application failures. Do not use <see cref="Error"/> to encode cooperative cancellation —
/// throw <see cref="OperationCanceledException"/> instead. Programmer bugs should throw; reserve
/// <see cref="ErrorKind.Internal"/> for rare outermost Host mapping (see <see cref="ErrorKind"/> policy).
/// </remarks>
public sealed record Error(
    ErrorKind Kind,
    string Code,
    string Message,
    IReadOnlyDictionary<string, string>? Metadata = null);
