using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using ContractReview.Core.Models;
using ContractReviewModel = ContractReview.Core.Models.ContractReview;

namespace ContractReview.Core.Executors;

// --------------- Executor 1: AI agent contract review ---------------
public sealed class ContractReviewAgentExecutor : Executor<ContractSubmission, ContractReviewModel>
{
    private const int StructuredOutputAttempts = 2;
    private readonly AIAgent _reviewAgent;

    public ContractReviewAgentExecutor(AIAgent reviewAgent)
        : base("ContractReviewAgent")
    {
        _reviewAgent = reviewAgent;
    }

    public override async ValueTask<ContractReviewModel> HandleAsync(
        ContractSubmission input,
        IWorkflowContext ctx,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"\n[AgentReview] Reviewing contract {input.ContractId}...");

        for (var attempt = 1; attempt <= StructuredOutputAttempts; attempt++)
        {
            AgentResponse<StructuredContractReview> response = await _reviewAgent
                .RunAsync<StructuredContractReview>(
                    BuildReviewPrompt(input, attempt),
                    cancellationToken: cancellationToken);

            if (TryCreateReview(input.ContractId, response.Result, out ContractReviewModel review))
            {
                Console.WriteLine($"[AgentReview] Suggested decision: {review.SuggestedDecision}");
                return review;
            }

            Console.WriteLine(
                $"[AgentReview] Attempt {attempt} returned invalid structured output. Retrying...");
        }

        Console.WriteLine(
            "[AgentReview] Falling back to REVIEW because the agent did not return valid structured output.");

        return new ContractReviewModel(
            input.ContractId,
            "The agent did not return valid structured output after 2 attempts. Manual review is required.",
            ToDecisionToken(ReviewDecision.Review));
    }

    private static string BuildReviewPrompt(ContractSubmission input, int attempt)
    {
        string retryInstruction = attempt == 1
            ? string.Empty
            : "Previous output was invalid. Return only a valid JSON object matching the requested shape.";

        return $"""
                Review the contract below and return structured JSON only.
                The JSON must contain exactly these fields:
                - Summary: a concise review summary for a human approver.
                - SuggestedDecision: APPROVE, REVIEW, or REJECT.

                {retryInstruction}

                Contract:
                {input.ContractText}
                """;
    }

    private static bool TryCreateReview(
        string contractId,
        StructuredContractReview? structuredReview,
        out ContractReviewModel review)
    {
        if (structuredReview is null || string.IsNullOrWhiteSpace(structuredReview.Summary))
        {
            review = default!;
            return false;
        }

        review = new ContractReviewModel(
            contractId,
            structuredReview.Summary.Trim(),
            NormalizeSuggestedDecision(structuredReview.SuggestedDecision));

        return true;
    }

    private static string NormalizeSuggestedDecision(string? suggestedDecision)
    {
        if (string.IsNullOrWhiteSpace(suggestedDecision))
            return ToDecisionToken(ReviewDecision.Review);

        var value = suggestedDecision.Trim().ToUpperInvariant();
        return value switch
        {
            "APPROVE" => ToDecisionToken(ReviewDecision.Approve),
            "REJECT" => ToDecisionToken(ReviewDecision.Reject),
            _ => ToDecisionToken(ReviewDecision.Review)
        };
    }

    private static string ToDecisionToken(ReviewDecision decision) =>
        decision.ToString().ToUpperInvariant();
}
