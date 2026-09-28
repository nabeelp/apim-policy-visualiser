namespace ApimPolicyVisualizer.Api.Policy;

/// <summary>
/// Raised when effective policy XML cannot be loaded (empty input, DTD, malformed markup or a non-<c>&lt;policies&gt;</c>
/// root). The controller maps it to HTTP 422 with <see cref="Exception.Message"/> as the ProblemDetails detail.
/// </summary>
public sealed class PolicyParseException(string message, Exception? innerException = null)
    : Exception(message, innerException);
