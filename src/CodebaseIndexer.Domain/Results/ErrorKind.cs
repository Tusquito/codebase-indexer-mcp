namespace CodebaseIndexer.Domain.Results;

/// <summary>
/// Category of an expected failure returned via <see cref="Result"/> / <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Failure vs exception policy (ADR 0033):
/// </para>
/// <list type="table">
/// <listheader>
/// <term>Situation</term>
/// <description>Use</description>
/// </listheader>
/// <item>
/// <term>Validation / bad tool args</term>
/// <description><see cref="Result"/> + <see cref="Validation"/></description>
/// </item>
/// <item>
/// <term>Missing collection, job, chunk</term>
/// <description><see cref="Result"/> + <see cref="NotFound"/> (replace nullable returns)</description>
/// </item>
/// <item>
/// <term>Job already running / illegal state transition</term>
/// <description><see cref="Result"/> + <see cref="Conflict"/></description>
/// </item>
/// <item>
/// <term>TEI / Qdrant / Neo4j unreachable or reject</term>
/// <description><see cref="Result"/> + <see cref="Dependency"/> or <see cref="Transient"/></description>
/// </item>
/// <item>
/// <term>Partial pipeline step failure (batch embed)</term>
/// <description>Prefer <see cref="Result"/> per step or typed error entries — not untyped string lists long-term</description>
/// </item>
/// <item>
/// <term><c>CancellationToken</c> canceled</term>
/// <description>Throw <see cref="OperationCanceledException"/> — do not encode as a success <see cref="Result"/></description>
/// </item>
/// <item>
/// <term>Programmer bug / invariant broken</term>
/// <description>Throw (or <see cref="Internal"/> only at the outermost Host catch)</description>
/// </item>
/// </list>
/// <para>
/// Prefer throwing <see cref="OperationCanceledException"/> for cooperative cancel; <see cref="Cancelled"/> is rare
/// and reserved for cases where a cancel signal must surface as a typed error value at a job boundary.
/// </para>
/// </remarks>
public enum ErrorKind
{
    /// <summary>Invalid input or tool arguments that the caller can correct.</summary>
    Validation,

    /// <summary>Requested entity (collection, job, chunk, etc.) does not exist.</summary>
    NotFound,

    /// <summary>Illegal state transition or resource conflict (e.g. job already running).</summary>
    Conflict,

    /// <summary>External dependency unreachable or rejected the request (TEI, Qdrant, Neo4j).</summary>
    Dependency,

    /// <summary>Transient dependency or transport failure that may succeed on retry.</summary>
    Transient,

    /// <summary>
    /// Rare typed cancel signal at a job boundary; prefer throwing <see cref="OperationCanceledException"/> instead.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Unexpected failure mapped at the outermost Host boundary; prefer throwing for programmer bugs.
    /// </summary>
    Internal,
}
