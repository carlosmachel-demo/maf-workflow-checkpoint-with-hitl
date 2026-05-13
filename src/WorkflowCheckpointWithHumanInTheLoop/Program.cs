// Demo: contract review workflow with checkpoints + console HITL.

using Azure.AI.OpenAI;
using Azure.Identity;
using dotenv.net;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using OpenAI.Chat;
using WorkflowCheckpointWithHumanInTheLoop.Models;

namespace WorkflowCheckpointWithHumanInTheLoop;

internal static class Program
{
    private const string DefaultSessionId = "contract-approval-demo";

    private static async Task Main(string[] args)
    {
        LoadEnvironmentVariables();
        
        var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
                       ?? throw new InvalidOperationException("Set AZURE_OPENAI_ENDPOINT");
        var deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? "gpt-4o-mini";
        var configuredCheckpointDirectory = Environment.GetEnvironmentVariable("WORKFLOW_CHECKPOINT_DIR");
        var checkpointDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(configuredCheckpointDirectory)
                ? Path.Combine(AppContext.BaseDirectory, ".checkpoints")
                : configuredCheckpointDirectory);
        Directory.CreateDirectory(checkpointDirectory);

        using var checkpointStore = new FileSystemJsonCheckpointStore(new DirectoryInfo(checkpointDirectory));
        CheckpointManager checkpointManager = CheckpointManager.CreateJson(checkpointStore);

        var openAiClient = new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential());
        ChatClient chatClient = openAiClient.GetChatClient(deployment);

        AIAgent reviewAgent = chatClient.AsAIAgent(
            name: "ContractReviewAgent",
            instructions:
            "You are a contract reviewer. Always return valid structured output with Summary and SuggestedDecision.",
            description: "Reviews contracts and suggests an initial decision.");

        Workflow workflow = ContractReviewWorkflowFactory.Build(reviewAgent);
        var sessionId = Environment.GetEnvironmentVariable("WORKFLOW_SESSION_ID") ?? DefaultSessionId;

        Console.WriteLine("=== WORKFLOW: Contract Review with HITL ===");
        Console.WriteLine($"SessionId     : {sessionId}");
        Console.WriteLine($"Checkpoints   : {checkpointDirectory}");

        string? resumeCheckpointId = TryGetResumeCheckpointId(args);

        await using StreamingRun run = resumeCheckpointId is null
            ? await InProcessExecution.RunStreamingAsync(
                workflow,
                CreateDemoContractSubmission(),
                checkpointManager,
                sessionId: sessionId,
                cancellationToken: CancellationToken.None)
            : await InProcessExecution.ResumeStreamingAsync(
                workflow,
                new CheckpointInfo(sessionId, resumeCheckpointId),
                checkpointManager,
                cancellationToken: CancellationToken.None);

        await ProcessRunAsync(run, checkpointDirectory);
    }

    private static void LoadEnvironmentVariables()
    {
        var candidatePaths = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), ".env"),
                Path.Combine(AppContext.BaseDirectory, ".env")
            }
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidatePaths.Length > 0)
        {
            DotEnv.Load(options: new DotEnvOptions(
                envFilePaths: candidatePaths,
                overwriteExistingVars: false));

            Console.WriteLine($"[Config] Loaded .env from: {string.Join(", ", candidatePaths)}");
            return;
        }

        DotEnv.Load(options: new DotEnvOptions(
            probeForEnv: true,
            probeLevelsToSearch: 6,
            overwriteExistingVars: false));

        Console.WriteLine("[Config] No local .env file found. Using probed/system environment variables.");
    }

    private static ContractSubmission CreateDemoContractSubmission()
    {
        return new ContractSubmission(
            ContractId: "CTR-2026-0042",
            ContractText: """
                CONTRATO DE PRESTACAO DE SERVICOS DE TECNOLOGIA

                Partes: Contratante: Empresa Alpha Ltda (CNPJ 00.000.000/0001-00)
                Contratada: Tech Solutions S.A. (CNPJ 11.111.111/0001-11)

                Objeto: Desenvolvimento de sistema ERP customizado para gestao financeira.

                Valor: R$ 850.000,00 pagos em 12 parcelas mensais.
                Prazo: 18 meses a partir da assinatura.

                Clausulas criticas:
                - Multa de 20% sobre o valor total em caso de rescisao sem justa causa
                - Propriedade intelectual transferida integralmente ao Contratante apos quitacao
                - SLA de 99,5% de disponibilidade com penalidade de 0,5% por hora de indisponibilidade
                - Confidencialidade por 5 anos apos encerramento do contrato
                """);
    }

    private static async Task ProcessRunAsync(StreamingRun run, string checkpointDirectory)
    {
        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            switch (evt)
            {
                case RequestInfoEvent rie when rie.Request.TryGetDataAs<HumanReviewRequest>(out var req):
                    Console.WriteLine("\n------------------------------------------------------------");
                    Console.WriteLine("WORKFLOW PAUSED: WAITING FOR HUMAN REVIEW");
                    Console.WriteLine("------------------------------------------------------------");
                    Console.WriteLine($"Contract           : {req.ContractId}");
                    Console.WriteLine($"Summary            : {req.Summary}");
                    Console.WriteLine($"Suggested decision : {req.SuggestedDecision}");

                    ReviewDecision decision = AskDecisionUntilValid();
                    Console.Write("Comments (optional, press Enter to skip): ");
                    string? comments = Console.ReadLine()?.Trim();

                    var humanReviewResponse = new HumanReviewResponse(
                        req.ContractId,
                        decision,
                        string.IsNullOrEmpty(comments) ? null : comments);

                    await run.SendResponseAsync(rie.Request.CreateResponse(humanReviewResponse));
                    break;

                case SuperStepCompletedEvent sse:
                    CheckpointInfo? cp = sse.CompletionInfo?.Checkpoint;
                    if (cp is not null)
                    {
                        Console.WriteLine($"[Checkpoint] id={cp.CheckpointId} (use: --resume {cp.CheckpointId})");
                        Console.WriteLine($"[Checkpoint] folder={checkpointDirectory}");
                    }

                    break;

                case WorkflowOutputEvent { Data: ContractReviewOutcome final }:
                    Console.WriteLine("\n------------------------------------------------------------");
                    Console.WriteLine("WORKFLOW COMPLETED");
                    Console.WriteLine("------------------------------------------------------------");
                    Console.WriteLine($"Contract : {final.ContractId}");
                    Console.WriteLine($"Decision : {final.Decision}");
                    if (final.Comments != null)
                        Console.WriteLine($"Comments : {final.Comments}");

                    return;

                case WorkflowErrorEvent err:
                    Console.ForegroundColor = ConsoleColor.Red;
                    await Console.Error.WriteLineAsync(err.Exception?.ToString() ?? "Unknown workflow error.");
                    Console.ResetColor();
                    return;

                case ExecutorFailedEvent ef:
                    Console.ForegroundColor = ConsoleColor.Red;
                    await Console.Error.WriteLineAsync($"Executor '{ef.ExecutorId}' failed: {ef.Data}");
                    Console.ResetColor();
                    return;
            }
        }
    }

    private static string? TryGetResumeCheckpointId(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--resume", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static ReviewDecision AskDecisionUntilValid()
    {
        while (true)
        {
            Console.Write("Your decision [APPROVE/REVIEW/REJECT]: ");
            string? raw = Console.ReadLine();

            if (Enum.TryParse<ReviewDecision>(raw?.Trim(), ignoreCase: true, out var decision))
                return decision;

            Console.WriteLine("Invalid input. Enter APPROVE, REVIEW, or REJECT.");
        }
    }
}
