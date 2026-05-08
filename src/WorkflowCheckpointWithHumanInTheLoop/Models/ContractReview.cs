namespace WorkflowCheckpointWithHumanInTheLoop.Models;

/// <summary>Structured contract review produced by the agent.</summary>
public record ContractReview(string ContractId, string Summary, string SuggestedDecision);