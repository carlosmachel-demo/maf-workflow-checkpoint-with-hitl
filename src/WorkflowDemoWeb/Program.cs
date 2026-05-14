using Azure.AI.OpenAI;
using Azure.Identity;
using ContractReview.Core;
using ContractReview.Core.Models;
using dotenv.net;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using OpenAI.Chat;
using WorkflowDemoWeb.Components;
using WorkflowDemoWeb.Models;
using WorkflowDemoWeb.Services;

// Load .env (probe up to 6 levels for dev convenience)
DotEnv.Load(options: new DotEnvOptions(probeForEnv: true, probeLevelsToSearch: 6, overwriteExistingVars: false));

var builder = WebApplication.CreateBuilder(args);

// --- Blazor Server ---
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// --- Checkpoint store (files on disk) ---
var checkpointDirectory = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, ".checkpoints"));
Directory.CreateDirectory(checkpointDirectory);

var checkpointStore = new FileSystemJsonCheckpointStore(new DirectoryInfo(checkpointDirectory));
var checkpointManager = CheckpointManager.CreateJson(checkpointStore);

// --- Azure OpenAI ---
var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
    ?? throw new InvalidOperationException("Set AZURE_OPENAI_ENDPOINT environment variable.");
var deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? "gpt-4o-mini";

var openAiClient = new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential());
ChatClient chatClient = openAiClient.GetChatClient(deployment);

AIAgent reviewAgent = chatClient.AsAIAgent(
    name: "ContractReviewAgent",
    instructions: "You are a contract reviewer. Always return valid structured output with Summary and SuggestedDecision.",
    description: "Reviews contracts and suggests an initial decision.");

Workflow workflow = ContractReviewWorkflowFactory.Build(reviewAgent);

// --- DI registrations ---
builder.Services.AddSingleton(checkpointManager);
builder.Services.AddSingleton(workflow);
builder.Services.AddSingleton(new RunPersistenceService(checkpointDirectory));
builder.Services.AddSingleton<WorkflowStateService>();
builder.Services.AddSingleton<WorkflowRunnerService>();

var app = builder.Build();

app.MapStaticAssets();
app.UseAntiforgery();

// -----------------------------------------------------------------------
// Minimal API — useful for curl / Postman during demos
// -----------------------------------------------------------------------

// POST /api/reviews  →  start a new workflow run
app.MapPost("/api/reviews", async (StartReviewRequest req, WorkflowRunnerService runner) =>
{
    var runId = await runner.StartRunAsync(req.ContractId, req.ContractText);
    return Results.Ok(new { RunId = runId });
}).DisableAntiforgery();

// GET /api/reviews  →  list all runs (summary)
app.MapGet("/api/reviews", (WorkflowStateService stateService) =>
{
    var runs = stateService.GetAll().Select(r => new
    {
        r.RunId,
        r.ContractId,
        Status = r.Status.ToString(),
        r.CreatedAt,
        r.SuggestedDecision
    });
    return Results.Ok(runs);
});

// GET /api/reviews/{runId}  →  full run detail
app.MapGet("/api/reviews/{runId}", (string runId, WorkflowStateService stateService) =>
{
    var state = stateService.Get(runId);
    if (state is null) return Results.NotFound();
    return Results.Ok(new
    {
        state.RunId,
        state.ContractId,
        Status = state.Status.ToString(),
        state.AiSummary,
        state.SuggestedDecision,
        state.CheckpointId,
        state.Outcome,
        state.ErrorMessage
    });
});

// POST /api/reviews/{runId}/decision  →  resolve the HITL checkpoint
app.MapPost("/api/reviews/{runId}/decision", (string runId, SubmitDecisionRequest req, WorkflowRunnerService runner) =>
{
    if (!Enum.TryParse<ReviewDecision>(req.Decision, ignoreCase: true, out var decision))
        return Results.BadRequest(new { Error = "Invalid decision. Use Approve, Review, or Reject." });

    var success = runner.SubmitDecision(runId, decision, req.Comments);
    return success
        ? Results.Ok()
        : Results.Conflict(new { Error = "Run is not currently awaiting a human review." });
}).DisableAntiforgery();

// -----------------------------------------------------------------------
// Blazor
// -----------------------------------------------------------------------
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Restore incomplete workflow runs from disk before accepting traffic
await app.Services.GetRequiredService<WorkflowRunnerService>().RestoreCheckpointedRunsAsync();

app.Run();

// Request / response DTOs for Minimal API
record StartReviewRequest(string ContractId, string ContractText);
record SubmitDecisionRequest(string Decision, string? Comments);
