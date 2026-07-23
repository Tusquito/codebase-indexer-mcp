namespace CodebaseIndexer.Domain.Results;

/// <summary>
/// Generic success/failure result carrying a success value of type <typeparamref name="T"/>.
/// Implemented as a <c>readonly struct</c> for allocation-conscious hot paths (ADR 0033 Phase 1).
/// </summary>
/// <typeparam name="T">Type of the success value.</typeparam>
/// <remarks>
/// <para>
/// Create instances only via <see cref="Success"/> or <see cref="Failure"/>. A default
/// <c>default(Result{T})</c> is invalid: <see cref="IsSuccess"/> is <c>false</c>, but accessing
/// <see cref="Value"/> or <see cref="Error"/> throws <see cref="InvalidOperationException"/>.
/// </para>
/// <para>
/// Expected failures return <see cref="Failure"/>; cooperative cancellation throws
/// <see cref="OperationCanceledException"/>; programmer bugs throw (or rare
/// <see cref="ErrorKind.Internal"/> only at the outermost Host catch). See
/// <see cref="ErrorKind"/> policy.
/// </para>
/// </remarks>
public readonly struct Result<T>
{
    private readonly bool _isSuccess;
    private readonly T? _value;
    private readonly Error? _error;

    private Result(bool isSuccess, T? value, Error? error)
    {
        _isSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    /// <summary>Gets a value indicating whether this instance represents success.</summary>
    public bool IsSuccess => _isSuccess;

    /// <summary>
    /// Gets the success value. Valid only when <see cref="IsSuccess"/> is <c>true</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when accessed on a failed result, or on an invalid <c>default(Result{T})</c>.
    /// </exception>
    public T Value
    {
        get
        {
            if (!_isSuccess)
            {
                throw new InvalidOperationException("Cannot access Value on a failed Result.");
            }

            return _value!;
        }
    }

    /// <summary>
    /// Gets the failure payload. Valid only when <see cref="IsSuccess"/> is <c>false</c>
    /// and the instance was created via <see cref="Failure"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when accessed on a successful result, or on an invalid <c>default(Result{T})</c>.
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
                    "Cannot access Error on an invalid default(Result<T>); create via Success(T) or Failure(Error).");
        }
    }

    /// <summary>Creates a successful result with the given value.</summary>
    /// <param name="value">Success payload.</param>
    /// <returns>A successful <see cref="Result{T}"/>.</returns>
    public static Result<T> Success(T value) => new(isSuccess: true, value, error: null);

    /// <summary>Creates a failed result with the given error.</summary>
    /// <param name="error">Typed failure payload; must not be <c>null</c>.</param>
    /// <returns>A failed <see cref="Result{T}"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <c>null</c>.</exception>
    public static Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(isSuccess: false, value: default, error);
    }

    /// <summary>Invokes exactly one of the delegates based on success or failure.</summary>
    /// <param name="onSuccess">Invoked with <see cref="Value"/> when successful.</param>
    /// <param name="onFailure">Invoked with <see cref="Error"/> when failed.</param>
    /// <exception cref="ArgumentNullException">A required delegate is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">This instance is an invalid default result.</exception>
    public void Match(Action<T> onSuccess, Action<Error> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        if (_isSuccess)
        {
            onSuccess(Value);
        }
        else
        {
            onFailure(Error);
        }
    }

    /// <summary>Maps this result to a value by invoking exactly one of the delegates.</summary>
    /// <typeparam name="TResult">Type of the mapped value.</typeparam>
    /// <param name="onSuccess">Invoked with <see cref="Value"/> when successful.</param>
    /// <param name="onFailure">Invoked with <see cref="Error"/> when failed.</param>
    /// <returns>The value produced by the invoked delegate.</returns>
    /// <exception cref="ArgumentNullException">A required delegate is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">This instance is an invalid default result.</exception>
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<Error, TResult> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return _isSuccess ? onSuccess(Value) : onFailure(Error);
    }
}
