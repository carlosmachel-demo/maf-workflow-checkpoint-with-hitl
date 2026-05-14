namespace ContractReview.Core.Models;

/// <summary>Human response sent back to the workflow.</summary>
public record HumanReviewResponse(string ContractId, ReviewDecision Decision, string? Comments);
