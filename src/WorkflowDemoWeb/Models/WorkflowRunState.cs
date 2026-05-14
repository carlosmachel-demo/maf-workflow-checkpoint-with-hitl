using ContractReview.Core.Models;

namespace WorkflowDemoWeb.Models;

public enum RunStatus
{
    Running,
    AwaitingHumanReview,
    Completed,
    Failed
}

public sealed class WorkflowRunState
{
    public required string RunId { get; init; }
    public required string SessionId { get; init; }
    public required string ContractId { get; init; }
    public RunStatus Status { get; set; } = RunStatus.Running;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public string? AiSummary { get; set; }
    public string? SuggestedDecision { get; set; }
    public string? CheckpointId { get; set; }
    public ContractReviewOutcome? Outcome { get; set; }
    public string? ErrorMessage { get; set; }

    // HITL bridge: background task awaits this; API endpoint resolves it
    public TaskCompletionSource<HumanReviewResponse>? HumanResponseTcs { get; set; }
}
