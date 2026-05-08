namespace WorkflowCheckpointWithHumanInTheLoop.Models;

/// <summary>Human review request emitted when the workflow pauses.</summary>
public record HumanReviewRequest(string ContractId, string Summary, string SuggestedDecision);