using CodebaseIndexer.Domain.Results;
using System.Threading.Tasks;

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
    [Test]
    public async Task Result_Success_is_success_and_rejects_Error_access()
    {
        var result = Result.Success();

        await Assert.That(result.IsSuccess).IsTrue();
        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    /// <summary><see cref="Result.Failure"/> stores the error and rejects success-only access patterns via Match.</summary>
    [Test]
    public async Task Result_Failure_exposes_Error_and_rejects_success_Match()
    {
        var error = SampleError(ErrorKind.NotFound, "job.not_found", "Job missing");
        var result = Result.Failure(error);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).IsSameReferenceAs(error);
        await Assert.That(result.Error.Kind).IsEqualTo(ErrorKind.NotFound);
    }

    /// <summary>Accessing <see cref="Result.Error"/> on success throws.</summary>
    [Test]
    public void Result_Error_on_success_throws()
    {
        var result = Result.Success();
        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    /// <summary><c>default(Result)</c> is invalid: not success, and <see cref="Result.Error"/> throws.</summary>
    [Test]
    public async Task Result_default_is_invalid()
    {
        Result result = default;

        await Assert.That(result.IsSuccess).IsFalse();
        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    /// <summary><see cref="Result.Failure"/> rejects a null error.</summary>
    [Test]
    public void Result_Failure_null_error_throws()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));
    }

    /// <summary><see cref="Result.Match"/> rejects null delegates.</summary>
    [Test]
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
    [Test]
    public async Task Result_Match_routes_to_success()
    {
        var result = Result.Success();
        var successCount = 0;
        var failureCount = 0;

        result.Match(
            onSuccess: () => successCount++,
            onFailure: _ => failureCount++);

        await Assert.That(successCount).IsEqualTo(1);
        await Assert.That(failureCount).IsEqualTo(0);
    }

    /// <summary>Void <see cref="Result.Match"/> invokes the failure delegate exactly once with the error.</summary>
    [Test]
    public async Task Result_Match_routes_to_failure()
    {
        var error = SampleError();
        var result = Result.Failure(error);
        Error? captured = null;
        var successCount = 0;

        result.Match(
            onSuccess: () => successCount++,
            onFailure: e => captured = e);

        await Assert.That(successCount).IsEqualTo(0);
        await Assert.That(captured).IsSameReferenceAs(error);
    }

    /// <summary>Generic <see cref="Result.Match{TResult}"/> maps success and failure payloads.</summary>
    [Test]
    public async Task Result_Match_TResult_maps_both_arms()
    {
        await Assert.That(Result.Success().Match(() => "ok", _ => "fail")).IsEqualTo("ok");
        await Assert.That(Result.Failure(SampleError()).Match(() => "ok", e => $"fail:{e.Code}")).IsEqualTo("fail:validation.sample");
    }

    /// <summary><see cref="Result{T}.Success"/> exposes <see cref="Result{T}.Value"/> and rejects <see cref="Result{T}.Error"/>.</summary>
    [Test]
    public async Task ResultT_Success_exposes_Value()
    {
        var result = Result<int>.Success(42);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsEqualTo(42);
        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    /// <summary><see cref="Result{T}.Failure"/> exposes <see cref="Result{T}.Error"/> and rejects <see cref="Result{T}.Value"/>.</summary>
    [Test]
    public async Task ResultT_Failure_exposes_Error_and_rejects_Value()
    {
        var error = SampleError(ErrorKind.Conflict, "job.conflict", "Already running");
        var result = Result<string>.Failure(error);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).IsSameReferenceAs(error);
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    /// <summary><c>default(Result{T})</c> is invalid for both accessors.</summary>
    [Test]
    public async Task ResultT_default_is_invalid()
    {
        Result<string> result = default;

        await Assert.That(result.IsSuccess).IsFalse();
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    /// <summary><see cref="Result{T}.Failure"/> rejects a null error.</summary>
    [Test]
    public void ResultT_Failure_null_error_throws()
    {
        Assert.Throws<ArgumentNullException>(() => Result<int>.Failure(null!));
    }

    /// <summary><see cref="Result{T}.Match"/> rejects null delegates.</summary>
    [Test]
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
    [Test]
    public async Task ResultT_Match_routes_correctly()
    {
        var success = Result<string>.Success("value");
        var failure = Result<string>.Failure(SampleError(ErrorKind.Dependency, "dep.down", "Qdrant down"));

        string? successPayload = null;
        Error? failurePayload = null;

        success.Match(v => successPayload = v, _ => Assert.Fail("success arm expected"));
        failure.Match(_ => Assert.Fail("failure arm expected"), e => failurePayload = e);

        await Assert.That(successPayload).IsEqualTo("value");
        await Assert.That(failurePayload!.Kind).IsEqualTo(ErrorKind.Dependency);
        await Assert.That(failurePayload.Code).IsEqualTo("dep.down");
    }

    /// <summary>Generic <see cref="Result{T}.Match{TResult}"/> maps both arms.</summary>
    [Test]
    public async Task ResultT_Match_TResult_maps_both_arms()
    {
        await Assert.That(Result<int>.Success(42).Match(v => v * 2, _ => -1)).IsEqualTo(84);
        await Assert.That(Result<int>.Failure(SampleError()).Match(v => v * 2, _ => -1)).IsEqualTo(-1);
    }

    /// <summary><see cref="Error"/> round-trips kind, code, message, and optional metadata.</summary>
    [Test]
    public async Task Error_round_trips_fields_and_optional_metadata()
    {
        var withoutMetadata = new Error(ErrorKind.Transient, "http.retry", "Try again");
        await Assert.That(withoutMetadata.Kind).IsEqualTo(ErrorKind.Transient);
        await Assert.That(withoutMetadata.Code).IsEqualTo("http.retry");
        await Assert.That(withoutMetadata.Message).IsEqualTo("Try again");
        await Assert.That(withoutMetadata.Metadata).IsNull();

        var metadata = new Dictionary<string, string> { ["status"] = "503", ["host"] = "qdrant" };
        var withMetadata = new Error(ErrorKind.Dependency, "qdrant.unavailable", "Store down", metadata);
        await Assert.That(withMetadata.Kind).IsEqualTo(ErrorKind.Dependency);
        await Assert.That(withMetadata.Code).IsEqualTo("qdrant.unavailable");
        await Assert.That(withMetadata.Message).IsEqualTo("Store down");
        await Assert.That(withMetadata.Metadata).IsEqualTo(metadata);
    }

    /// <summary><see cref="Result"/> and <see cref="Result{T}"/> share success/failure factory and Match parity.</summary>
    [Test]
    public async Task Result_and_ResultT_share_factory_and_Match_parity()
    {
        var error = SampleError(ErrorKind.Internal, "internal", "boom");

        var nonGeneric = Result.Failure(error);
        var generic = Result<object>.Failure(error);

        await Assert.That(nonGeneric.IsSuccess).IsFalse();
        await Assert.That(generic.IsSuccess).IsFalse();
        await Assert.That(nonGeneric.Error).IsSameReferenceAs(error);
        await Assert.That(generic.Error).IsSameReferenceAs(error);

        await Assert.That(Result.Success().Match(() => "s", _ => "f")).IsEqualTo("s");
        await Assert.That(Result<int>.Success(1).Match(_ => "s", _ => "f")).IsEqualTo("s");
        await Assert.That(nonGeneric.Match(() => "s", _ => "f")).IsEqualTo("f");
        await Assert.That(generic.Match(_ => "s", _ => "f")).IsEqualTo("f");
    }

    /// <summary>Result types are value types (readonly structs); Error is a sealed record (ADR Phase 1 choice).</summary>
    [Test]
    public async Task Representation_is_struct_Result_and_sealed_record_Error()
    {
        await Assert.That(typeof(Result).IsValueType).IsTrue();
        await Assert.That(typeof(Result<>).IsValueType).IsTrue();

        var errorType = typeof(Error);
        await Assert.That(errorType.IsClass).IsTrue();
        await Assert.That(errorType.IsSealed).IsTrue();
        await Assert.That(errorType.GetMethod(
            "<Clone>$",
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic)).IsNotNull();
    }
}