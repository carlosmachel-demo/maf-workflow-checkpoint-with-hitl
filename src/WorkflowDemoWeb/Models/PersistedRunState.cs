using ContractReview.Core.Models;

namespace WorkflowDemoWeb.Models;

/// <summary>
/// Serializable snapshot of a workflow run used to persist state across restarts.
/// Only fields needed to rehydrate a run are included (no in-memory-only fields like TCS).
/// </summary>
public sealed class PersistedRunState
{
    public required string RunId { get; init; }
    public required string SessionId { get; init; }
    public required string ContractId { get; init; }
    public RunStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? AiSummary { get; set; }
    public string? SuggestedDecision { get; set; }

    /// <summary>
    /// Checkpoint saved by the last <c>SuperStepCompletedEvent</c>.
    /// Required to resume a paused run after restart.
    /// </summary>
    public string? CheckpointId { get; set; }

    public ContractReviewOutcome? Outcome { get; set; }
    public string? ErrorMessage { get; set; }
}
