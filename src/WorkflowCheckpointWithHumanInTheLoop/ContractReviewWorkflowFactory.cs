using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using WorkflowCheckpointWithHumanInTheLoop.Executors;
using WorkflowCheckpointWithHumanInTheLoop.Models;

namespace WorkflowCheckpointWithHumanInTheLoop;

public static class ContractReviewWorkflowFactory
{
    private const string HumanReviewPortId = "HumanReviewPort";

    public static Workflow Build(AIAgent reviewAgent)
    {
        var agentReview = new ContractReviewAgentExecutor(reviewAgent);
        var humanReviewRequest = new HumanReviewRequestExecutor();
        var outcomeRecorder = new ReviewOutcomeRecorderExecutor();
        var humanReviewPort = RequestPort.Create<HumanReviewRequest, HumanReviewResponse>(HumanReviewPortId);

        return new WorkflowBuilder(agentReview)
            .AddEdge(agentReview, humanReviewRequest)
            .AddEdge(humanReviewRequest, humanReviewPort)
            .AddEdge(humanReviewPort, outcomeRecorder)
            .WithOutputFrom(outcomeRecorder)
            .Build(validateOrphans: true);
    }
}