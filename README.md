# maf-workflow-checkpoint-with-hitl

Console demo for a contract review workflow with:

- `Microsoft.Agents.AI` `1.4.0`
- `Microsoft.Agents.AI.OpenAI` `1.4.0`
- `Microsoft.Agents.AI.Workflows` `1.4.0`
- JSON-based persisted checkpoints
- human-in-the-loop (HITL) approval in the console
- structured output via `RunAsync<StructuredContractReview>`
- retry + `REVIEW` fallback when structured output is invalid

## Flow

1. `ContractReviewAgentExecutor` receives `ContractSubmission` and asks the AI agent for structured output.
2. The agent returns `StructuredContractReview` with `Summary` and `SuggestedDecision`.
3. `HumanReviewRequestExecutor` converts that into `HumanReviewRequest`.
4. `RequestPort<HumanReviewRequest, HumanReviewResponse>` pauses the workflow and emits `RequestInfoEvent`.
5. The console collects the human decision (`APPROVE`, `REVIEW`, `REJECT`) and optional comments.
6. `ReviewOutcomeRecorderExecutor` records the final result as `ContractReviewOutcome`.

## Structured output

Yes — this sample uses structured output with an `AIAgent`.

The AI call is made with a typed response model:

- `RunAsync<StructuredContractReview>(...)`
- `StructuredContractReview` contains `Summary` and `SuggestedDecision`

If the agent does not return valid structured output:

1. the app retries once more,
2. then falls back to `REVIEW`,
3. and sends the contract to the human reviewer with a safe summary.

## Checkpoints

- Default folder: `<app-base>/.checkpoints` (with `dotnet run`, typically `src/WorkflowCheckpointWithHumanInTheLoop/bin/Debug/net10.0/.checkpoints`).
- Optional override: set `WORKFLOW_CHECKPOINT_DIR` to force a custom folder.
- After each superstep, the app prints the `checkpointId` and checkpoint folder.
- To resume, use `--resume <checkpointId>` with the same `WORKFLOW_SESSION_ID`.

## Run

Set environment variables:

```zsh
export AZURE_OPENAI_ENDPOINT="https://<your-endpoint>.openai.azure.com/"
export AZURE_OPENAI_DEPLOYMENT="gpt-4o-mini"
export WORKFLOW_SESSION_ID="contract-review-demo"
export WORKFLOW_CHECKPOINT_DIR="/absolute/path/to/.checkpoints" # optional
```

Build:

```zsh
dotnet build ./src/WorkflowCheckpointWithHumanInTheLoop/WorkflowCheckpointWithHumanInTheLoop.csproj
```

Run normally:

```zsh
dotnet run --project ./src/WorkflowCheckpointWithHumanInTheLoop/WorkflowCheckpointWithHumanInTheLoop.csproj
```

Resume from a checkpoint:

```zsh
dotnet run --project ./src/WorkflowCheckpointWithHumanInTheLoop/WorkflowCheckpointWithHumanInTheLoop.csproj -- --resume <checkpointId>
```

## End-to-end test example

### 1. Build the project

```zsh
dotnet build ./src/WorkflowCheckpointWithHumanInTheLoop/WorkflowCheckpointWithHumanInTheLoop.csproj
```

### 2. Start the workflow

```zsh
dotnet run --project ./src/WorkflowCheckpointWithHumanInTheLoop/WorkflowCheckpointWithHumanInTheLoop.csproj
```

### 3. Expected console flow

You should see output similar to this:

```text
=== WORKFLOW: Contract Review with HITL ===
SessionId     : contract-review-demo
Checkpoints   : /.../.checkpoints

[AgentReview] Reviewing contract CTR-2026-0042...
[AgentReview] Suggested decision: REVIEW
[Checkpoint] id=<checkpoint-1> (use: --resume <checkpoint-1>)
[Checkpoint] folder=/.../.checkpoints

------------------------------------------------------------
WORKFLOW PAUSED: WAITING FOR HUMAN REVIEW
------------------------------------------------------------
Contract           : CTR-2026-0042
Summary            : ...
Suggested decision : REVIEW
Your decision [APPROVE/REVIEW/REJECT]:
```

### 4. Enter the human decision

Example input:

```text
REVIEW
Need legal review on the SLA penalty clause.
```

### 5. Expected completion output

```text
[Recorder] Recording final decision for contract CTR-2026-0042
[Recorder] Decision: REVIEW
[Recorder] Comments: Need legal review on the SLA penalty clause.

------------------------------------------------------------
WORKFLOW COMPLETED
------------------------------------------------------------
Contract : CTR-2026-0042
Decision : REVIEW
Comments : Need legal review on the SLA penalty clause.
```

## Checkpoint resume test

To test resume behavior:

1. run the app until it prints a checkpoint id,
2. stop the process,
3. restart with the same `WORKFLOW_SESSION_ID`,
4. pass the printed checkpoint id.

Example:

```zsh
dotnet run --project ./src/WorkflowCheckpointWithHumanInTheLoop/WorkflowCheckpointWithHumanInTheLoop.csproj -- --resume <checkpointId>
```

If the checkpoint is valid, the workflow resumes from the paused human review step instead of starting from scratch.

