namespace SshManager.Core;

/// <summary>
/// Represents the result of an operation that can either succeed with a value or fail with an error.
/// This pattern provides a functional approach to error handling without exceptions.
/// </summary>
/// <typeparam name="T">The type of the success value.</typeparam>
public readonly struct Result<T>
{
    private readonly T? _value;
    private readonly string? _error;
    private readonly Exception? _exception;

    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the success value. Throws if the result is a failure.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when accessing Value on a failed result.</exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot access Value on a failed result. Error: {_error}");

    /// <summary>
    /// Gets the error message. Returns null if the result is a success.
    /// </summary>
    public string? Error => _error;

    /// <summary>
    /// Gets the exception that caused the failure, if any.
    /// </summary>
    public Exception? Exception => _exception;

    private Result(bool isSuccess, T? value, string? error, Exception? exception)
    {
        IsSuccess = isSuccess;
        _value = value;
        _error = error;
        _exception = exception;
    }

    /// <summary>
    /// Creates a successful result with the specified value.
    /// </summary>
    /// <param name="value">The success value.</param>
    /// <returns>A successful result containing the value.</returns>
    public static Result<T> Success(T value) => new(true, value, null, null);

    /// <summary>
    /// Creates a failed result with the specified error message.
    /// </summary>
    /// <param name="error">The error message describing the failure.</param>
    /// <returns>A failed result with the error message.</returns>
    public static Result<T> Failure(string error) => new(false, default, error, null);

    /// <summary>
    /// Creates a failed result from an exception.
    /// </summary>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <returns>A failed result with the exception's message and reference.</returns>
    public static Result<T> Failure(Exception exception) =>
        new(false, default, exception.Message, exception);

    /// <summary>
    /// Creates a failed result with an error message and exception.
    /// </summary>
    /// <param name="error">The error message describing the failure.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <returns>A failed result with both the error message and exception.</returns>
    public static Result<T> Failure(string error, Exception exception) =>
        new(false, default, error, exception);

    /// <summary>
    /// Gets the value if successful, or the default value if failed.
    /// </summary>
    /// <param name="defaultValue">The default value to return on failure.</param>
    /// <returns>The success value or the default value.</returns>
    public T GetValueOrDefault(T defaultValue = default!) =>
        IsSuccess ? _value! : defaultValue;

    /// <summary>
    /// Transforms the success value using the specified function.
    /// If the result is a failure, the failure is propagated.
    /// </summary>
    /// <typeparam name="TNew">The type of the transformed value.</typeparam>
    /// <param name="mapper">The function to transform the value.</param>
    /// <returns>A new result with the transformed value or the original failure.</returns>
    public Result<TNew> Map<TNew>(Func<T, TNew> mapper)
    {
        return IsSuccess
            ? Result<TNew>.Success(mapper(_value!))
            : Result<TNew>.Failure(_error!, _exception);
    }

    /// <summary>
    /// Transforms the success value using the specified function that returns a Result.
    /// If the result is a failure, the failure is propagated.
    /// </summary>
    /// <typeparam name="TNew">The type of the transformed value.</typeparam>
    /// <param name="binder">The function to transform the value into a new Result.</param>
    /// <returns>The result of the binder function or the original failure.</returns>
    public Result<TNew> Bind<TNew>(Func<T, Result<TNew>> binder)
    {
        return IsSuccess
            ? binder(_value!)
            : Result<TNew>.Failure(_error!, _exception);
    }

    /// <summary>
    /// Executes the success action if the result is successful, or the failure action if failed.
    /// </summary>
    /// <param name="onSuccess">Action to execute on success.</param>
    /// <param name="onFailure">Action to execute on failure.</param>
    public void Match(Action<T> onSuccess, Action<string> onFailure)
    {
        if (IsSuccess)
            onSuccess(_value!);
        else
            onFailure(_error!);
    }

    /// <summary>
    /// Executes the success function if the result is successful, or the failure function if failed.
    /// </summary>
    /// <typeparam name="TResult">The return type of both functions.</typeparam>
    /// <param name="onSuccess">Function to execute on success.</param>
    /// <param name="onFailure">Function to execute on failure.</param>
    /// <returns>The result of whichever function was executed.</returns>
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<string, TResult> onFailure)
    {
        return IsSuccess
            ? onSuccess(_value!)
            : onFailure(_error!);
    }

    /// <summary>
    /// Implicitly converts a value to a successful Result.
    /// </summary>
    public static implicit operator Result<T>(T value) => Success(value);

    /// <summary>
    /// Deconstructs the result into its components.
    /// </summary>
    public void Deconstruct(out bool isSuccess, out T? value, out string? error)
    {
        isSuccess = IsSuccess;
        value = _value;
        error = _error;
    }

    public override string ToString()
    {
        return IsSuccess
            ? $"Success({_value})"
            : $"Failure({_error})";
    }
}

/// <summary>
/// Represents the result of an operation that can either succeed or fail with an error.
/// Use this for operations that don't return a value on success.
/// </summary>
public readonly struct Result
{
    private readonly string? _error;
    private readonly Exception? _exception;

    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the error message. Returns null if the result is a success.
    /// </summary>
    public string? Error => _error;

    /// <summary>
    /// Gets the exception that caused the failure, if any.
    /// </summary>
    public Exception? Exception => _exception;

    private Result(bool isSuccess, string? error, Exception? exception)
    {
        IsSuccess = isSuccess;
        _error = error;
        _exception = exception;
    }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <returns>A successful result.</returns>
    public static Result Success() => new(true, null, null);

    /// <summary>
    /// Creates a failed result with the specified error message.
    /// </summary>
    /// <param name="error">The error message describing the failure.</param>
    /// <returns>A failed result with the error message.</returns>
    public static Result Failure(string error) => new(false, error, null);

    /// <summary>
    /// Creates a failed result from an exception.
    /// </summary>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <returns>A failed result with the exception's message and reference.</returns>
    public static Result Failure(Exception exception) =>
        new(false, exception.Message, exception);

    /// <summary>
    /// Creates a failed result with an error message and exception.
    /// </summary>
    /// <param name="error">The error message describing the failure.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <returns>A failed result with both the error message and exception.</returns>
    public static Result Failure(string error, Exception exception) =>
        new(false, error, exception);

    /// <summary>
    /// Executes the success action if the result is successful, or the failure action if failed.
    /// </summary>
    /// <param name="onSuccess">Action to execute on success.</param>
    /// <param name="onFailure">Action to execute on failure.</param>
    public void Match(Action onSuccess, Action<string> onFailure)
    {
        if (IsSuccess)
            onSuccess();
        else
            onFailure(_error!);
    }

    /// <summary>
    /// Executes the success function if the result is successful, or the failure function if failed.
    /// </summary>
    /// <typeparam name="TResult">The return type of both functions.</typeparam>
    /// <param name="onSuccess">Function to execute on success.</param>
    /// <param name="onFailure">Function to execute on failure.</param>
    /// <returns>The result of whichever function was executed.</returns>
    public TResult Match<TResult>(Func<TResult> onSuccess, Func<string, TResult> onFailure)
    {
        return IsSuccess
            ? onSuccess()
            : onFailure(_error!);
    }

    /// <summary>
    /// Converts a value-less Result to a Result&lt;T&gt; with the specified success value.
    /// </summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <param name="value">The value to use if successful.</param>
    /// <returns>A Result&lt;T&gt; with the value if successful, or the original failure.</returns>
    public Result<T> WithValue<T>(T value)
    {
        return IsSuccess
            ? Result<T>.Success(value)
            : Result<T>.Failure(_error!, _exception);
    }

    public override string ToString()
    {
        return IsSuccess
            ? "Success"
            : $"Failure({_error})";
    }
}

/// <summary>
/// Extension methods for working with Result types.
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// Converts a nullable value to a Result, treating null as a failure.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The nullable value.</param>
    /// <param name="errorIfNull">The error message if the value is null.</param>
    /// <returns>A Result containing the value or a failure.</returns>
    public static Result<T> ToResult<T>(this T? value, string errorIfNull) where T : class
    {
        return value is not null
            ? Result<T>.Success(value)
            : Result<T>.Failure(errorIfNull);
    }

    /// <summary>
    /// Converts a nullable value type to a Result, treating null as a failure.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The nullable value.</param>
    /// <param name="errorIfNull">The error message if the value is null.</param>
    /// <returns>A Result containing the value or a failure.</returns>
    public static Result<T> ToResult<T>(this T? value, string errorIfNull) where T : struct
    {
        return value.HasValue
            ? Result<T>.Success(value.Value)
            : Result<T>.Failure(errorIfNull);
    }

    /// <summary>
    /// Executes an async operation and wraps exceptions in a Result.
    /// </summary>
    /// <typeparam name="T">The type of the result value.</typeparam>
    /// <param name="operation">The async operation to execute.</param>
    /// <returns>A Result containing the value or the exception.</returns>
    public static async Task<Result<T>> TryAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            var result = await operation();
            return Result<T>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<T>.Failure(ex);
        }
    }

    /// <summary>
    /// Executes an async operation and wraps exceptions in a Result.
    /// </summary>
    /// <param name="operation">The async operation to execute.</param>
    /// <returns>A Result indicating success or containing the exception.</returns>
    public static async Task<Result> TryAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(ex);
        }
    }

    /// <summary>
    /// Executes a synchronous operation and wraps exceptions in a Result.
    /// </summary>
    /// <typeparam name="T">The type of the result value.</typeparam>
    /// <param name="operation">The operation to execute.</param>
    /// <returns>A Result containing the value or the exception.</returns>
    public static Result<T> Try<T>(Func<T> operation)
    {
        try
        {
            return Result<T>.Success(operation());
        }
        catch (Exception ex)
        {
            return Result<T>.Failure(ex);
        }
    }

    /// <summary>
    /// Executes a synchronous operation and wraps exceptions in a Result.
    /// </summary>
    /// <param name="operation">The operation to execute.</param>
    /// <returns>A Result indicating success or containing the exception.</returns>
    public static Result Try(Action operation)
    {
        try
        {
            operation();
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(ex);
        }
    }

    /// <summary>
    /// Combines multiple results into a single result. Fails if any result fails.
    /// </summary>
    /// <param name="results">The results to combine.</param>
    /// <returns>A success if all results are successful, or the first failure.</returns>
    public static Result Combine(params Result[] results)
    {
        foreach (var result in results)
        {
            if (result.IsFailure)
                return result;
        }
        return Result.Success();
    }

    /// <summary>
    /// Combines multiple results into a single result containing all values.
    /// Fails if any result fails.
    /// </summary>
    /// <typeparam name="T">The type of the values.</typeparam>
    /// <param name="results">The results to combine.</param>
    /// <returns>A result containing all values if all are successful, or the first failure.</returns>
    public static Result<IReadOnlyList<T>> Combine<T>(params Result<T>[] results)
    {
        var values = new List<T>(results.Length);
        foreach (var result in results)
        {
            if (result.IsFailure)
                return Result<IReadOnlyList<T>>.Failure(result.Error!, result.Exception);
            values.Add(result.Value);
        }
        return Result<IReadOnlyList<T>>.Success(values);
    }

    /// <summary>
    /// Asynchronously transforms the success value using the specified async function.
    /// </summary>
    public static async Task<Result<TNew>> MapAsync<T, TNew>(this Result<T> result, Func<T, Task<TNew>> mapper)
    {
        if (result.IsFailure)
            return Result<TNew>.Failure(result.Error!, result.Exception);

        var newValue = await mapper(result.Value);
        return Result<TNew>.Success(newValue);
    }

    /// <summary>
    /// Asynchronously transforms the success value using the specified async function that returns a Result.
    /// </summary>
    public static async Task<Result<TNew>> BindAsync<T, TNew>(this Result<T> result, Func<T, Task<Result<TNew>>> binder)
    {
        if (result.IsFailure)
            return Result<TNew>.Failure(result.Error!, result.Exception);

        return await binder(result.Value);
    }

    /// <summary>
    /// Asynchronously transforms the success value of a Task&lt;Result&gt;.
    /// </summary>
    public static async Task<Result<TNew>> MapAsync<T, TNew>(this Task<Result<T>> resultTask, Func<T, TNew> mapper)
    {
        var result = await resultTask;
        return result.Map(mapper);
    }

    /// <summary>
    /// Asynchronously binds the success value of a Task&lt;Result&gt;.
    /// </summary>
    public static async Task<Result<TNew>> BindAsync<T, TNew>(this Task<Result<T>> resultTask, Func<T, Result<TNew>> binder)
    {
        var result = await resultTask;
        return result.Bind(binder);
    }

    /// <summary>
    /// Executes an action if the result is successful.
    /// </summary>
    public static Result<T> OnSuccess<T>(this Result<T> result, Action<T> action)
    {
        if (result.IsSuccess)
            action(result.Value);
        return result;
    }

    /// <summary>
    /// Executes an action if the result is a failure.
    /// </summary>
    public static Result<T> OnFailure<T>(this Result<T> result, Action<string> action)
    {
        if (result.IsFailure)
            action(result.Error!);
        return result;
    }

    /// <summary>
    /// Executes an action if the result is successful.
    /// </summary>
    public static Result OnSuccess(this Result result, Action action)
    {
        if (result.IsSuccess)
            action();
        return result;
    }

    /// <summary>
    /// Executes an action if the result is a failure.
    /// </summary>
    public static Result OnFailure(this Result result, Action<string> action)
    {
        if (result.IsFailure)
            action(result.Error!);
        return result;
    }
}
