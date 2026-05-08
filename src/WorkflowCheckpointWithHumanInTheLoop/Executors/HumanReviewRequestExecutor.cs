using Microsoft.Agents.AI.Workflows;
using WorkflowCheckpointWithHumanInTheLoop.Models;

namespace WorkflowCheckpointWithHumanInTheLoop.Executors;

public sealed class HumanReviewRequestExecutor : Executor<ContractReview, HumanReviewRequest>
{
    public HumanReviewRequestExecutor()
        : base("HumanReviewRequest")
    {
    }

    public override ValueTask<HumanReviewRequest> HandleAsync(
        ContractReview review,
        IWorkflowContext ctx,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"\n[HITL] Contract {review.ContractId} sent for human review.");
        return ValueTask.FromResult(
            new HumanReviewRequest(review.ContractId, review.Summary, review.SuggestedDecision));
    }
}