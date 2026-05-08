using Microsoft.Agents.AI.Workflows;
using WorkflowCheckpointWithHumanInTheLoop.Models;

namespace WorkflowCheckpointWithHumanInTheLoop.Executors;

public sealed class ReviewOutcomeRecorderExecutor : Executor<HumanReviewResponse, ContractReviewOutcome>
{
    public ReviewOutcomeRecorderExecutor()
        : base("ReviewOutcomeRecorder")
    {
    }

    public override ValueTask<ContractReviewOutcome> HandleAsync(
        HumanReviewResponse response,
        IWorkflowContext ctx,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"\n[Recorder] Recording final decision for contract {response.ContractId}");
        Console.WriteLine($"[Recorder] Decision: {response.Decision}");

        if (!string.IsNullOrWhiteSpace(response.Comments))
            Console.WriteLine($"[Recorder] Comments: {response.Comments}");

        var decision = new ContractReviewOutcome(
            response.ContractId,
            response.Decision.ToString().ToUpperInvariant(),
            response.Comments);
        return ValueTask.FromResult(decision);
    }
}