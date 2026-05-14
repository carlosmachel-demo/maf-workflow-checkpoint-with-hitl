using Microsoft.Agents.AI.Workflows;
using ContractReview.Core.Models;
using ContractReviewModel = ContractReview.Core.Models.ContractReview;

namespace ContractReview.Core.Executors;

public sealed class HumanReviewRequestExecutor : Executor<ContractReviewModel, HumanReviewRequest>
{
    public HumanReviewRequestExecutor()
        : base("HumanReviewRequest")
    {
    }

    public override ValueTask<HumanReviewRequest> HandleAsync(
        ContractReviewModel review,
        IWorkflowContext ctx,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"\n[HITL] Contract {review.ContractId} sent for human review.");
        return ValueTask.FromResult(
            new HumanReviewRequest(review.ContractId, review.Summary, review.SuggestedDecision));
    }
}
