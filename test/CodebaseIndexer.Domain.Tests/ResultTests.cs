using CodebaseIndexer.Domain.Results;

namespace CodebaseIndexer.Domain.Tests;

/// <summary>
/// Unit coverage for ADR 0033 Phase 1 Result primitives.
/// Representation choice: <see cref="Result"/> / <see cref="Result{T}"/> are <c>readonly struct</c>;
/// <see cref="Error"/> is a <c>sealed record</c>.
/// </summary>
public sealed class ResultTests
{
    private static Error SampleError(
        ErrorKind kind = ErrorKind.Validation,
        string code = "validation.sample",
        string message = "sample failure",
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(kind, code, message, metadata);

    /// <summary><see cref="Result.Success"/> sets <see cref="Result.IsSuccess"/> and rejects <see cref="Result.Error"/> access.</summary>
    [Fact]
    public void Result_Success_is_success_and_rejects_Error_access()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    /// <summary><see cref="Result.Failure"/> stores the error and rejects success-only access patterns via Match.</summary>
    [Fact]
    public void Result_Failure_exposes_Error_and_rejects_success_Match()
    {
        var error = SampleError(ErrorKind.NotFound, "job.not_found", "Job missing");
        var result = Result.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
    }

    /// <summary>Accessing <see cref="Result.Error"/> on success throws.</summary>
    [Fact]
    public void Result_Error_on_success_throws()
    {
        var result = Result.Success();
        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    /// <summary><c>default(Result)</c> is invalid: not success, and <see cref="Result.Error"/> throws.</summary>
    [Fact]
    public void Result_default_is_invalid()
    {
        Result result = default;

        Assert.False(result.IsSuccess);
        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    /// <summary><see cref="Result.Failure"/> rejects a null error.</summary>
    [Fact]
    public void Result_Failure_null_error_throws()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));
    }

    /// <summary><see cref="Result.Match"/> rejects null delegates.</summary>
    [Fact]
    public void Result_Match_null_delegates_throw()
    {
        var success = Result.Success();
        var failure = Result.Failure(SampleError());

        Assert.Throws<ArgumentNullException>(() => success.Match(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => success.Match(() => { }, null!));
        Assert.Throws<ArgumentNullException>(() => failure.Match(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => failure.Match(() => { }, null!));

        Assert.Throws<ArgumentNullException>(() => success.Match<string>(null!, _ => "f"));
        Assert.Throws<ArgumentNullException>(() => success.Match(() => "s", null!));
        Assert.Throws<ArgumentNullException>(() => failure.Match<string>(null!, _ => "f"));
        Assert.Throws<ArgumentNullException>(() => failure.Match(() => "s", null!));
    }

    /// <summary>Void <see cref="Result.Match"/> invokes the success delegate exactly once.</summary>
    [Fact]
    public void Result_Match_routes_to_success()
    {
        var result = Result.Success();
        var successCount = 0;
        var failureCount = 0;

        result.Match(
            onSuccess: () => successCount++,
            onFailure: _ => failureCount++);

        Assert.Equal(1, successCount);
        Assert.Equal(0, failureCount);
    }

    /// <summary>Void <see cref="Result.Match"/> invokes the failure delegate exactly once with the error.</summary>
    [Fact]
    public void Result_Match_routes_to_failure()
    {
        var error = SampleError();
        var result = Result.Failure(error);
        Error? captured = null;
        var successCount = 0;

        result.Match(
            onSuccess: () => successCount++,
            onFailure: e => captured = e);

        Assert.Equal(0, successCount);
        Assert.Same(error, captured);
    }

    /// <summary>Generic <see cref="Result.Match{TResult}"/> maps success and failure payloads.</summary>
    [Fact]
    public void Result_Match_TResult_maps_both_arms()
    {
        Assert.Equal("ok", Result.Success().Match(() => "ok", _ => "fail"));
        Assert.Equal(
            "fail:validation.sample",
            Result.Failure(SampleError()).Match(() => "ok", e => $"fail:{e.Code}"));
    }

    /// <summary><see cref="Result{T}.Success"/> exposes <see cref="Result{T}.Value"/> and rejects <see cref="Result{T}.Error"/>.</summary>
    [Fact]
    public void ResultT_Success_exposes_Value()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    /// <summary><see cref="Result{T}.Failure"/> exposes <see cref="Result{T}.Error"/> and rejects <see cref="Result{T}.Value"/>.</summary>
    [Fact]
    public void ResultT_Failure_exposes_Error_and_rejects_Value()
    {
        var error = SampleError(ErrorKind.Conflict, "job.conflict", "Already running");
        var result = Result<string>.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    /// <summary><c>default(Result{T})</c> is invalid for both accessors.</summary>
    [Fact]
    public void ResultT_default_is_invalid()
    {
        Result<string> result = default;

        Assert.False(result.IsSuccess);
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    /// <summary><see cref="Result{T}.Failure"/> rejects a null error.</summary>
    [Fact]
    public void ResultT_Failure_null_error_throws()
    {
        Assert.Throws<ArgumentNullException>(() => Result<int>.Failure(null!));
    }

    /// <summary><see cref="Result{T}.Match"/> rejects null delegates.</summary>
    [Fact]
    public void ResultT_Match_null_delegates_throw()
    {
        var success = Result<int>.Success(1);
        var failure = Result<int>.Failure(SampleError());

        Assert.Throws<ArgumentNullException>(() => success.Match(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => success.Match(_ => { }, null!));
        Assert.Throws<ArgumentNullException>(() => failure.Match(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => failure.Match(_ => { }, null!));

        Assert.Throws<ArgumentNullException>(() => success.Match<string>(null!, _ => "f"));
        Assert.Throws<ArgumentNullException>(() => success.Match(_ => "s", null!));
        Assert.Throws<ArgumentNullException>(() => failure.Match<string>(null!, _ => "f"));
        Assert.Throws<ArgumentNullException>(() => failure.Match(_ => "s", null!));
    }

    /// <summary>Void <see cref="Result{T}.Match"/> routes success and failure with correct payloads.</summary>
    [Fact]
    public void ResultT_Match_routes_correctly()
    {
        var success = Result<string>.Success("value");
        var failure = Result<string>.Failure(SampleError(ErrorKind.Dependency, "dep.down", "Qdrant down"));

        string? successPayload = null;
        Error? failurePayload = null;

        success.Match(v => successPayload = v, _ => Assert.Fail("success arm expected"));
        failure.Match(_ => Assert.Fail("failure arm expected"), e => failurePayload = e);

        Assert.Equal("value", successPayload);
        Assert.Equal(ErrorKind.Dependency, failurePayload!.Kind);
        Assert.Equal("dep.down", failurePayload.Code);
    }

    /// <summary>Generic <see cref="Result{T}.Match{TResult}"/> maps both arms.</summary>
    [Fact]
    public void ResultT_Match_TResult_maps_both_arms()
    {
        Assert.Equal(84, Result<int>.Success(42).Match(v => v * 2, _ => -1));
        Assert.Equal(
            -1,
            Result<int>.Failure(SampleError()).Match(v => v * 2, _ => -1));
    }

    /// <summary><see cref="Error"/> round-trips kind, code, message, and optional metadata.</summary>
    [Fact]
    public void Error_round_trips_fields_and_optional_metadata()
    {
        var withoutMetadata = new Error(ErrorKind.Transient, "http.retry", "Try again");
        Assert.Equal(ErrorKind.Transient, withoutMetadata.Kind);
        Assert.Equal("http.retry", withoutMetadata.Code);
        Assert.Equal("Try again", withoutMetadata.Message);
        Assert.Null(withoutMetadata.Metadata);

        var metadata = new Dictionary<string, string> { ["status"] = "503", ["host"] = "qdrant" };
        var withMetadata = new Error(ErrorKind.Dependency, "qdrant.unavailable", "Store down", metadata);
        Assert.Equal(ErrorKind.Dependency, withMetadata.Kind);
        Assert.Equal("qdrant.unavailable", withMetadata.Code);
        Assert.Equal("Store down", withMetadata.Message);
        Assert.Equal(metadata, withMetadata.Metadata);
    }

    /// <summary><see cref="Result"/> and <see cref="Result{T}"/> share success/failure factory and Match parity.</summary>
    [Fact]
    public void Result_and_ResultT_share_factory_and_Match_parity()
    {
        var error = SampleError(ErrorKind.Internal, "internal", "boom");

        var nonGeneric = Result.Failure(error);
        var generic = Result<object>.Failure(error);

        Assert.False(nonGeneric.IsSuccess);
        Assert.False(generic.IsSuccess);
        Assert.Same(error, nonGeneric.Error);
        Assert.Same(error, generic.Error);

        Assert.Equal("s", Result.Success().Match(() => "s", _ => "f"));
        Assert.Equal("s", Result<int>.Success(1).Match(_ => "s", _ => "f"));
        Assert.Equal("f", nonGeneric.Match(() => "s", _ => "f"));
        Assert.Equal("f", generic.Match(_ => "s", _ => "f"));
    }

    /// <summary>Result types are value types (readonly structs); Error is a sealed record (ADR Phase 1 choice).</summary>
    [Fact]
    public void Representation_is_struct_Result_and_sealed_record_Error()
    {
        Assert.True(typeof(Result).IsValueType);
        Assert.True(typeof(Result<>).IsValueType);

        var errorType = typeof(Error);
        Assert.True(errorType.IsClass);
        Assert.True(errorType.IsSealed);
        Assert.NotNull(errorType.GetMethod(
            "<Clone>$",
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic));
    }
}
