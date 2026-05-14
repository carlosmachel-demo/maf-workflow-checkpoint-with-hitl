namespace ContractReview.Core.Models;

/// <summary>Final workflow output.</summary>
public record ContractReviewOutcome(string ContractId, string Decision, string? Comments);
