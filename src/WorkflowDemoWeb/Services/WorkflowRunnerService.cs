using ContractReview.Core.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using WorkflowDemoWeb.Models;
using RunStatus = WorkflowDemoWeb.Models.RunStatus;

namespace WorkflowDemoWeb.Services;

public sealed class WorkflowRunnerService
{
    private readonly WorkflowStateService _stateService;
    private readonly RunPersistenceService _persistence;
    private readonly Workflow _workflow;
    private readonly CheckpointManager _checkpointManager;

    public WorkflowRunnerService(
        WorkflowStateService stateService,
        RunPersistenceService persistence,
        Workflow workflow,
        CheckpointManager checkpointManager)
    {
        _stateService = stateService;
        _persistence = persistence;
        _workflow = workflow;
        _checkpointManager = checkpointManager;
    }

    public async Task<string> StartRunAsync(string contractId, string contractText)
    {
        var runId = Guid.NewGuid().ToString("N")[..8];
        var sessionId = $"web-{runId}";

        var state = new WorkflowRunState
        {
            RunId = runId,
            SessionId = sessionId,
            ContractId = contractId
        };

        _stateService.Add(state);
        await _persistence.SaveAsync(_stateService.GetAll());

        var submission = new ContractSubmission(contractId, contractText);

        // Fire and forget — event loop runs in the background
        _ = Task.Run(async () =>
        {
            try
            {
                await using StreamingRun run = await InProcessExecution.RunStreamingAsync(
                    _workflow,
                    submission,
                    _checkpointManager,
                    sessionId: sessionId,
                    cancellationToken: CancellationToken.None);

                await ProcessRunAsync(run, state, _persistence, _stateService);
            }
            catch (Exception ex)
            {
                state.Status = RunStatus.Failed;
                state.ErrorMessage = ex.Message;
                await _persistence.SaveAsync(_stateService.GetAll());
            }
        });

        return await Task.FromResult(runId);
    }

    /// <summary>
    /// Resumes a run that was persisted across a restart using its saved checkpoint.
    /// </summary>
    public void ResumeRunAsync(PersistedRunState persisted)
    {
        // Restore in-memory state from persisted snapshot
        var state = new WorkflowRunState
        {
            RunId = persisted.RunId,
            SessionId = persisted.SessionId,
            ContractId = persisted.ContractId,
            Status = RunStatus.Running,
            CheckpointId = persisted.CheckpointId,
            AiSummary = persisted.AiSummary,
            SuggestedDecision = persisted.SuggestedDecision
        };

        _stateService.Add(state);

        var checkpointInfo = new CheckpointInfo(persisted.SessionId, persisted.CheckpointId!);

        _ = Task.Run(async () =>
        {
            try
            {
                await using StreamingRun run = await InProcessExecution.ResumeStreamingAsync(
                    _workflow,
                    checkpointInfo,
                    _checkpointManager,
                    cancellationToken: CancellationToken.None);

                await ProcessRunAsync(run, state, _persistence, _stateService);
            }
            catch (Exception ex)
            {
                state.Status = RunStatus.Failed;
                state.ErrorMessage = ex.Message;
                await _persistence.SaveAsync(_stateService.GetAll());
            }
        });
    }

    /// <summary>
    /// Loads persisted runs from disk and resumes any that have a saved checkpoint
    /// and were not yet completed or failed.
    /// </summary>
    public async Task RestoreCheckpointedRunsAsync()
    {
        var persisted = await _persistence.LoadAsync();

        foreach (var p in persisted)
        {
            if (p.CheckpointId is null)
                continue;

            if (p.Status is RunStatus.Completed or RunStatus.Failed)
            {
                // Restore terminal runs in-memory for display only — no workflow resumption
                _stateService.Add(new WorkflowRunState
                {
                    RunId = p.RunId,
                    SessionId = p.SessionId,
                    ContractId = p.ContractId,
                    Status = p.Status,
                    CheckpointId = p.CheckpointId,
                    AiSummary = p.AiSummary,
                    SuggestedDecision = p.SuggestedDecision,
                    Outcome = p.Outcome,
                    ErrorMessage = p.ErrorMessage
                });
                continue;
            }

            // Runs with a checkpoint that were not yet finished — resume the workflow
            ResumeRunAsync(p);
        }
    }

    /// <summary>
    /// Resolves the HITL checkpoint. The background task unblocks and calls SendResponseAsync.
    /// </summary>
    public bool SubmitDecision(string runId, ReviewDecision decision, string? comments)
    {
        var state = _stateService.Get(runId);
        if (state?.HumanResponseTcs is null || state.Status != RunStatus.AwaitingHumanReview)
            return false;

        var tcs = state.HumanResponseTcs;
        state.HumanResponseTcs = null;
        state.Status = RunStatus.Running;
        tcs.SetResult(new HumanReviewResponse(state.ContractId, decision, comments));
        return true;
    }

    private static async Task ProcessRunAsync(
        StreamingRun run,
        WorkflowRunState state,
        RunPersistenceService persistence,
        WorkflowStateService stateService)
    {
        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            switch (evt)
            {
                // HITL checkpoint: workflow pauses here waiting for human input
                case RequestInfoEvent rie when rie.Request.TryGetDataAs<HumanReviewRequest>(out var req):
                    state.AiSummary = req.Summary;
                    state.SuggestedDecision = req.SuggestedDecision;

                    var tcs = new TaskCompletionSource<HumanReviewResponse>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    state.HumanResponseTcs = tcs;
                    state.Status = RunStatus.AwaitingHumanReview;

                    // Persist so the run survives a restart while waiting for human review
                    await persistence.SaveAsync(stateService.GetAll());

                    // Block until the API endpoint resolves the TCS
                    HumanReviewResponse humanResponse = await tcs.Task;
                    await run.SendResponseAsync(rie.Request.CreateResponse(humanResponse));
                    break;

                case SuperStepCompletedEvent sse:
                    CheckpointInfo? cp = sse.CompletionInfo?.Checkpoint;
                    if (cp is not null)
                    {
                        state.CheckpointId = cp.CheckpointId;
                        await persistence.SaveAsync(stateService.GetAll());
                    }
                    break;

                case WorkflowOutputEvent { Data: ContractReviewOutcome final }:
                    state.Outcome = final;
                    state.Status = RunStatus.Completed;
                    await persistence.SaveAsync(stateService.GetAll());
                    return;

                case WorkflowErrorEvent err:
                    state.Status = RunStatus.Failed;
                    state.ErrorMessage = err.Exception?.Message ?? "Unknown workflow error.";
                    await persistence.SaveAsync(stateService.GetAll());
                    return;

                case ExecutorFailedEvent ef:
                    state.Status = RunStatus.Failed;
                    state.ErrorMessage = $"Executor '{ef.ExecutorId}' failed: {ef.Data}";
                    await persistence.SaveAsync(stateService.GetAll());
                    return;
            }
        }
    }
}
