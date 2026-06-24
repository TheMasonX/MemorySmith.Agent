namespace Agent.Core;

/// <summary>
/// Structured result from a replan attempt (TSK-0104).
///
/// Replaces the nullable-IPlan return pattern where <c>null</c> silently meant
/// "replan failed". This type makes success/failure explicit and preserves the
/// error message for diagnostics and logging.
///
/// Use <see cref="Success"/> and <see cref="Failure"/> factory methods to construct.
/// </summary>
public sealed record ReplanResult
{
    /// <summary>The replanned plan, or <c>null</c> when replanning failed.</summary>
    public IPlan? Plan { get; }

    /// <summary>True when replanning succeeded and <see cref="Plan"/> is available.</summary>
    public bool IsSuccess { get; }

    /// <summary>Human-readable error message when <see cref="IsSuccess"/> is false.</summary>
    public string? ErrorMessage { get; }

    private ReplanResult(IPlan? plan, bool isSuccess, string? errorMessage)
    {
        Plan = plan;
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }

    /// <summary>Creates a successful replan result.</summary>
    public static ReplanResult Success(IPlan plan) => new(plan, true, null);

    /// <summary>Creates a failed replan result with a descriptive message.</summary>
    public static ReplanResult Failure(string message) => new(null, false, message);
}
