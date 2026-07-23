namespace CodebaseIndexer.Domain.Results;

/// <summary>
/// Non-generic success/failure result for operations with no success value.
/// Implemented as a <c>readonly struct</c> for allocation-conscious hot paths (ADR 0033 Phase 1).
/// </summary>
/// <remarks>
/// <para>
/// Create instances only via <see cref="Success"/> or <see cref="Failure"/>. A default
/// <c>default(Result)</c> is invalid: <see cref="IsSuccess"/> is <c>false</c>, but accessing
/// <see cref="Error"/> throws <see cref="InvalidOperationException"/>.
/// </para>
/// <para>
/// Expected failures return <see cref="Failure"/>; cooperative cancellation throws
/// <see cref="OperationCanceledException"/>; programmer bugs throw (or rare
/// <see cref="ErrorKind.Internal"/> only at the outermost Host catch). See
/// <see cref="ErrorKind"/> policy.
/// </para>
/// </remarks>
public readonly struct Result
{
    private readonly bool _isSuccess;
    private readonly Error? _error;

    private Result(bool isSuccess, Error? error)
    {
        _isSuccess = isSuccess;
        _error = error;
    }

    /// <summary>Gets a value indicating whether this instance represents success.</summary>
    public bool IsSuccess => _isSuccess;

    /// <summary>
    /// Gets the failure payload. Valid only when <see cref="IsSuccess"/> is <c>false</c>
    /// and the instance was created via <see cref="Failure"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when accessed on a successful result, or on an invalid <c>default(Result)</c>.
    /// </exception>
    public Error Error
    {
        get
        {
            if (_isSuccess)
            {
                throw new InvalidOperationException("Cannot access Error on a successful Result.");
            }

            return _error
                ?? throw new InvalidOperationException(
                    "Cannot access Error on an invalid default(Result); create via Success() or Failure(Error).");
        }
    }

    /// <summary>Creates a successful result with no value.</summary>
    /// <returns>A successful <see cref="Result"/>.</returns>
    public static Result Success() => new(isSuccess: true, error: null);

    /// <summary>Creates a failed result with the given error.</summary>
    /// <param name="error">Typed failure payload; must not be <c>null</c>.</param>
    /// <returns>A failed <see cref="Result"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <c>null</c>.</exception>
    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(isSuccess: false, error);
    }

    /// <summary>Invokes exactly one of the delegates based on success or failure.</summary>
    /// <param name="onSuccess">Invoked when <see cref="IsSuccess"/> is <c>true</c>.</param>
    /// <param name="onFailure">Invoked with <see cref="Error"/> when failed.</param>
    /// <exception cref="ArgumentNullException">A required delegate is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">This instance is an invalid default result.</exception>
    public void Match(Action onSuccess, Action<Error> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        if (_isSuccess)
        {
            onSuccess();
        }
        else
        {
            onFailure(Error);
        }
    }

    /// <summary>Maps this result to a value by invoking exactly one of the delegates.</summary>
    /// <typeparam name="TResult">Type of the mapped value.</typeparam>
    /// <param name="onSuccess">Invoked when <see cref="IsSuccess"/> is <c>true</c>.</param>
    /// <param name="onFailure">Invoked with <see cref="Error"/> when failed.</param>
    /// <returns>The value produced by the invoked delegate.</returns>
    /// <exception cref="ArgumentNullException">A required delegate is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">This instance is an invalid default result.</exception>
    public TResult Match<TResult>(Func<TResult> onSuccess, Func<Error, TResult> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return _isSuccess ? onSuccess() : onFailure(Error);
    }
}
