# Contract Review Workflow with Checkpoints and Human-in-the-Loop

This repository demonstrates how to build a **durable, Human-in-the-Loop (HITL) workflow** using [`Microsoft.Agents.AI.Workflows`](https://www.nuget.org/packages/Microsoft.Agents.AI.Workflows) and Azure OpenAI structured output.

The same workflow core is shared across two host applications: a **console app** for interactive CLI demos and a **Blazor Server web app** for browser-based demos.

---

## Repository structure

```
src/
  ContractReview.Core/           ← Shared class library (models, executors, workflow factory)
  WorkflowCheckpointWithHumanInTheLoop/  ← Console host
  WorkflowDemoWeb/               ← Blazor Server host
```

---

## What the workflow does

The workflow implements a **contract review pipeline** with the following steps:

```
ContractSubmission
       │
       ▼
ContractReviewAgentExecutor      ← AI agent reviews the contract, returns Summary + SuggestedDecision
       │
       ▼
HumanReviewRequestExecutor       ← Packages the AI review into a human-facing request
       │
       ▼
RequestPort<HumanReviewRequest, HumanReviewResponse>   ← WORKFLOW PAUSES HERE (HITL checkpoint)
       │
       ▼
ReviewOutcomeRecorderExecutor    ← Records the human decision (APPROVE / REVIEW / REJECT)
       │
       ▼
ContractReviewOutcome            ← Final workflow output
```

Key behaviors:

- **Structured output** — the AI agent is called with `RunAsync<StructuredContractReview>()`. If the model returns invalid output it retries once, then falls back to `REVIEW` with a safe summary.
- **Checkpointing** — after each completed superstep the workflow saves a checkpoint to disk. This allows the process to be killed and resumed later without losing progress.
- **HITL pause** — when the workflow reaches the `RequestPort`, it emits a `RequestInfoEvent` and suspends until a `HumanReviewResponse` is sent back. Each host wires this bridge differently (console prompt vs. web form).

---

## Console app (`WorkflowCheckpointWithHumanInTheLoop`)

The console app runs a single workflow instance end-to-end in one terminal session.

**High-level flow:**

1. Loads environment variables from `.env` (Azure OpenAI endpoint, deployment name, optional checkpoint dir and session id).
2. Creates an `AzureOpenAIClient` and an `AIAgent`.
3. Calls `ContractReviewWorkflowFactory.Build(agent)` to get the `Workflow`.
4. Starts (`RunStreamingAsync`) or resumes (`ResumeStreamingAsync`) a `StreamingRun` depending on whether `--resume <checkpointId>` was passed on the command line.
5. Iterates `WatchStreamAsync()` and handles events:
   - `RequestInfoEvent` — prints the AI summary and prompted decision to the console, reads the user's input, and calls `run.SendResponseAsync(...)` to unblock the workflow.
   - `SuperStepCompletedEvent` — prints the checkpoint id and folder so the user can copy it for a resume run.
   - `WorkflowOutputEvent` — prints the final `ContractReviewOutcome` and exits.
   - `WorkflowErrorEvent` / `ExecutorFailedEvent` — prints the error and exits.

**How to run:**

```zsh
export AZURE_OPENAI_ENDPOINT="https://<your-resource>.openai.azure.com/"
export AZURE_OPENAI_DEPLOYMENT="gpt-4o-mini"

dotnet run --project ./src/WorkflowCheckpointWithHumanInTheLoop
```

Resume from a saved checkpoint:

```zsh
dotnet run --project ./src/WorkflowCheckpointWithHumanInTheLoop -- --resume <checkpointId>
```

---

## Web app (`WorkflowDemoWeb`)

The Blazor Server app lets multiple contracts be submitted and reviewed simultaneously through a browser UI.

**High-level flow:**

1. On startup, loads environment variables, registers all services in DI, and calls `RestoreCheckpointedRunsAsync()` to resume any runs that were paused before the last restart.
2. A user navigates to **New Review**, fills in a contract ID and contract text, and submits.
3. `WorkflowRunnerService.StartRunAsync` creates an in-memory `WorkflowRunState`, fires the workflow in a background `Task.Run`, and returns immediately.
4. The background task runs the same event loop as the console:
   - `SuperStepCompletedEvent` — saves the checkpoint id to `WorkflowRunState` and persists all runs to `runs.json`.
   - `RequestInfoEvent` — sets the run status to `AwaitingHumanReview`, stores the AI summary, and creates a `TaskCompletionSource<HumanReviewResponse>` that the task awaits.
   - `WorkflowOutputEvent` — stores the `ContractReviewOutcome`, sets status to `Completed`, persists, and completes.
5. A reviewer opens the **Review** page for the paused run, sees the AI summary and suggested decision, selects their decision, and submits.
6. `WorkflowRunnerService.SubmitDecision` resolves the `TaskCompletionSource`, unblocking the background task, which sends the response to the workflow and continues.
7. The **Outcome** page shows the final recorded decision.

The **Dashboard** (`/`) polls every 3 seconds and shows all runs with their current status and action links.

Minimal API endpoints are also available for curl/Postman use:
- `POST /api/reviews` — start a new run
- `GET /api/reviews` — list all runs
- `GET /api/reviews/{runId}` — full run detail
- `POST /api/reviews/{runId}/decision` — submit a human decision

**How to run:**

```zsh
export AZURE_OPENAI_ENDPOINT="https://<your-resource>.openai.azure.com/"
export AZURE_OPENAI_DEPLOYMENT="gpt-4o-mini"

dotnet run --project ./src/WorkflowDemoWeb
```

Then open `https://localhost:5001` in a browser.

---

## Shared library (`ContractReview.Core`)

Both host projects reference `ContractReview.Core`, which contains:

| Folder | Contents |
|---|---|
| `Models/` | `ContractSubmission`, `ContractReview`, `StructuredContractReview`, `HumanReviewRequest`, `HumanReviewResponse`, `ReviewDecision`, `ContractReviewOutcome` |
| `Executors/` | `ContractReviewAgentExecutor`, `HumanReviewRequestExecutor`, `ReviewOutcomeRecorderExecutor` |
| root | `ContractReviewWorkflowFactory` |

---

## Prerequisites

- .NET 10
- Azure OpenAI resource with a `gpt-4o-mini` (or compatible) deployment
- Azure CLI authenticated (`az login`) — the apps use `AzureCliCredential`

