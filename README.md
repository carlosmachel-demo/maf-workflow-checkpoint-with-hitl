# maf-workflow-checkpoint-with-hitl

Demo em console de workflow com:

- `Microsoft.Agents.AI` `1.4.0`
- `Microsoft.Agents.AI.OpenAI` `1.4.0`
- `Microsoft.Agents.AI.Workflows` `1.4.0`
- checkpoint persistido em JSON
- human-in-the-loop (HITL) com aprovacao no console

## Fluxo

1. `ContractAnalyzerExecutor` recebe o contrato e gera `AnalysisDone`.
2. `HumanApprovalRequestExecutor` transforma em `ApprovalRequest`.
3. `RequestPort<ApprovalRequest, ApprovalResponse>` pausa o workflow e emite `RequestInfoEvent`.
4. O console coleta a decisao humana (`APROVAR`, `REVISAR`, `REJEITAR`) com re-prompt ate entrada valida.
5. `DecisionRecorderExecutor` registra e gera `ContractDecision` como output do workflow.

## Checkpoints

- Pasta fixa: `.checkpoints/` na raiz do repositorio.
- A cada superstep, o app imprime o `checkpointId`.
- Para retomar, use `--resume <checkpointId>` com o mesmo `WORKFLOW_SESSION_ID`.

## Executar

Defina ambiente (exemplo):

```zsh
export AZURE_OPENAI_ENDPOINT="https://<seu-endpoint>.openai.azure.com/"
export AZURE_OPENAI_DEPLOYMENT="gpt-4o-mini"
export WORKFLOW_SESSION_ID="contract-approval-demo"
```

Build:

```zsh
dotnet build ./src/WorkflowCheckpointWithHumanInTheLoop/WorkflowCheckpointWithHumanInTheLoop.csproj
```

Run normal:

```zsh
dotnet run --project ./src/WorkflowCheckpointWithHumanInTheLoop/WorkflowCheckpointWithHumanInTheLoop.csproj
```

Run retomando de checkpoint:

```zsh
dotnet run --project ./src/WorkflowCheckpointWithHumanInTheLoop/WorkflowCheckpointWithHumanInTheLoop.csproj -- --resume <checkpointId>
```
