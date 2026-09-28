namespace ApimPolicyVisualizer.Api.Azure;

/// <summary>
/// A mapped ARM failure carrying a descriptive, caller-safe message (never the raw ARM response body).
/// Shared by every ARM-calling component so endpoints map failures consistently.
/// </summary>
public abstract record ArmFailure(string Message);

/// <summary>ARM returned 404 (or the resource could not be located). Maps to HTTP 404.</summary>
public sealed record ArmNotFound(string Message) : ArmFailure(Message);

/// <summary>ARM returned 403. Maps to HTTP 403.</summary>
public sealed record ArmForbidden(string Message) : ArmFailure(Message);

/// <summary>Either a success payload or a typed <see cref="ArmFailure"/>.</summary>
public sealed class ArmResult<T>
{
    private ArmResult(T? value, ArmFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public T? Value { get; }

    public ArmFailure? Failure { get; }

    public bool IsSuccess => Failure is null;

    public static ArmResult<T> Success(T value) => new(value ?? throw new ArgumentNullException(nameof(value)), null);

    public static ArmResult<T> Fail(ArmFailure failure) => new(default, failure ?? throw new ArgumentNullException(nameof(failure)));

    public static implicit operator ArmResult<T>(ArmFailure failure) => Fail(failure);
}

public static class ArmFailureMessages
{
    public static ArmNotFound NotFound(string resourceKind, string identifier) =>
        new($"{resourceKind} '{identifier}' was not found.");

    public static ArmForbidden Forbidden(string identifier) =>
        new($"Access to '{identifier}' was denied. Verify the caller's role assignment.");
}
