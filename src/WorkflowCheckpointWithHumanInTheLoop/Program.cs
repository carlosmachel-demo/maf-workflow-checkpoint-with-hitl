// Demo: workflow de aprovacao de contrato com checkpoint + HITL no console.

using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using OpenAI.Chat;

// --------------- Tipos de mensagem do workflow ---------------

/// <summary>Dispara o início do workflow com o texto do contrato.</summary>
record ContractReceived(string ContractText, string ContractId);

/// <summary>Resultado da análise do agente, enviado ao aprovador humano.</summary>
record AnalysisDone(string ContractId, string Summary, string Recommendation);

/// <summary>Pedido de input humano emitido pelo workflow ao parar.</summary>
record ApprovalRequest(string ContractId, string Summary, string Recommendation);

enum ApprovalDecision
{
    APROVAR,
    REVISAR,
    REJEITAR
}

/// <summary>Resposta do humano ao pedido de aprovacao.</summary>
record ApprovalResponse(string ContractId, ApprovalDecision Decision, string? Comments);

/// <summary>Resultado final do workflow.</summary>
record ContractDecision(string ContractId, string Decision, string? Comments);


record ContractAnalysisResult(string Summary, string Recommendation);

// --------------- Executor 1: Agente que analisa o contrato ---------------

sealed class ContractAnalyzerExecutor : Executor<ContractReceived, AnalysisDone>
{
    private readonly AIAgent _analyzerAgent;

    public ContractAnalyzerExecutor(AIAgent analyzerAgent)
        : base("ContractAnalyzer")
    {
        _analyzerAgent = analyzerAgent;
    }

    public override async ValueTask<AnalysisDone> HandleAsync(
        ContractReceived input,
        IWorkflowContext ctx,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"\n[Analyzer] Analisando contrato {input.ContractId}...");

        var prompt = $"""
            Analise o contrato abaixo e responda com JSON estruturado.
            recommendation deve ser APROVAR, REVISAR ou REJEITAR.

            Contrato:
            {input.ContractText}
            """;

        AgentResponse<ContractAnalysisResult> response = await _analyzerAgent
            .RunAsync<ContractAnalysisResult>(prompt, cancellationToken: cancellationToken);

        ContractAnalysisResult result = response.Result
            ?? throw new InvalidOperationException("O agente nao retornou analise estruturada.");

        string normalizedRecommendation = NormalizeRecommendation(result.Recommendation);
        Console.WriteLine($"[Analyzer] Recomendacao: {normalizedRecommendation}");

        return new AnalysisDone(input.ContractId, result.Summary, normalizedRecommendation);
    }

    private static string NormalizeRecommendation(string recommendation)
    {
        if (string.IsNullOrWhiteSpace(recommendation))
            return nameof(ApprovalDecision.REVISAR);

        var value = recommendation.Trim().ToUpperInvariant();
        return value switch
        {
            "APROVAR" => "APROVAR",
            "REJEITAR" => "REJEITAR",
            _ => "REVISAR"
        };
    }
}


// --------------- Executor 2: Ponto de aprovação humana (HITL) ---------------

sealed class HumanApprovalRequestExecutor : Executor<AnalysisDone, ApprovalRequest>
{
    public HumanApprovalRequestExecutor()
        : base("HumanApprovalRequest")
    {
    }

    public override ValueTask<ApprovalRequest> HandleAsync(
        AnalysisDone analysis,
        IWorkflowContext ctx,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"\n[HITL] Contrato {analysis.ContractId} enviado para aprovacao humana.");
        return ValueTask.FromResult(
            new ApprovalRequest(analysis.ContractId, analysis.Summary, analysis.Recommendation));
    }
}


// --------------- Executor 3: Finaliza e registra decisão ---------------

sealed class DecisionRecorderExecutor : Executor<ApprovalResponse, ContractDecision>
{
    public DecisionRecorderExecutor()
        : base("DecisionRecorder")
    {
    }

    public override ValueTask<ContractDecision> HandleAsync(
        ApprovalResponse response,
        IWorkflowContext ctx,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"\n[Recorder] Registrando decisao para contrato {response.ContractId}");
        Console.WriteLine($"[Recorder] Decisao: {response.Decision}");

        if (!string.IsNullOrWhiteSpace(response.Comments))
            Console.WriteLine($"[Recorder] Comentarios: {response.Comments}");

        var decision = new ContractDecision(
            response.ContractId,
            response.Decision.ToString(),
            response.Comments);
        return ValueTask.FromResult(decision);
    }
}


// --------------- Fábrica do workflow ---------------

static class WorkflowFactory
{
    public const string HumanApprovalPortId = "HumanApprovalPort";

    public static Workflow Build(AIAgent analyzerAgent)
    {
        var analyzer = new ContractAnalyzerExecutor(analyzerAgent);
        var approvalRequest = new HumanApprovalRequestExecutor();
        var decisionRecorder = new DecisionRecorderExecutor();
        var approvalPort = RequestPort.Create<ApprovalRequest, ApprovalResponse>(HumanApprovalPortId);

        return new WorkflowBuilder(analyzer)
            .AddEdge(analyzer, approvalRequest)
            .AddEdge(approvalRequest, approvalPort)
            .AddEdge(approvalPort, decisionRecorder)
            .WithOutputFrom(decisionRecorder)
            .Build(validateOrphans: true);
    }
}


// --------------- Programa principal ---------------

class Program
{
    private const string DefaultSessionId = "contract-approval-demo";

    static async Task Main(string[] args)
    {
        var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
            ?? throw new InvalidOperationException("Defina AZURE_OPENAI_ENDPOINT");
        var deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? "gpt-4o-mini";
        string checkpointDirectory = Path.Combine(Directory.GetCurrentDirectory(), ".checkpoints");
        Directory.CreateDirectory(checkpointDirectory);

        using var checkpointStore = new FileSystemJsonCheckpointStore(new DirectoryInfo(checkpointDirectory));
        CheckpointManager checkpointManager = CheckpointManager.CreateJson(checkpointStore);

        var openAiClient = new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential());
        ChatClient chatClient = openAiClient.GetChatClient(deployment);

        AIAgent analyzerAgent = chatClient.AsAIAgent(
            name: "ContractAnalyzerAgent",
            instructions:
            "Voce e um especialista em contratos. Sempre retorne JSON valido com summary e recommendation.",
            description: "Analisa contratos e sugere decisao inicial.");

        Workflow workflow = WorkflowFactory.Build(analyzerAgent);
        string sessionId = Environment.GetEnvironmentVariable("WORKFLOW_SESSION_ID") ?? DefaultSessionId;

        Console.WriteLine("=== WORKFLOW: Analise de Contrato com HITL ===");
        Console.WriteLine($"SessionId     : {sessionId}");
        Console.WriteLine($"Checkpoints   : {checkpointDirectory}");

        string? resumeCheckpointId = TryGetResumeCheckpointId(args);

        await using StreamingRun run = resumeCheckpointId is null
            ? await InProcessExecution.RunStreamingAsync(
                workflow,
                CreateDemoInput(),
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

    private static ContractReceived CreateDemoInput()
    {
        return new ContractReceived(
            ContractId: "CTR-2026-0042",
            ContractText: """
                CONTRATO DE PRESTAÇÃO DE SERVIÇOS DE TECNOLOGIA

                Partes: Contratante: Empresa Alpha Ltda (CNPJ 00.000.000/0001-00)
                Contratada: Tech Solutions S.A. (CNPJ 11.111.111/0001-11)

                Objeto: Desenvolvimento de sistema ERP customizado para gestão financeira.

                Valor: R$ 850.000,00 pagos em 12 parcelas mensais.
                Prazo: 18 meses a partir da assinatura.

                Cláusulas críticas:
                - Multa de 20% sobre o valor total em caso de rescisão sem justa causa
                - Propriedade intelectual transferida integralmente ao Contratante após quitação
                - SLA de 99,5% de disponibilidade com penalidade de 0,5% por hora de indisponibilidade
                - Confidencialidade por 5 anos após encerramento do contrato
                """
        );
    }

    private static async Task ProcessRunAsync(StreamingRun run, string checkpointDirectory)
    {
        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            switch (evt)
            {
                case RequestInfoEvent rie when rie.Request.TryGetDataAs<ApprovalRequest>(out var req):
                    Console.WriteLine("\n------------------------------------------------------------");
                    Console.WriteLine("WORKFLOW PAUSADO: AGUARDANDO APROVACAO HUMANA");
                    Console.WriteLine("------------------------------------------------------------");
                    Console.WriteLine($"Contrato      : {req.ContractId}");
                    Console.WriteLine($"Resumo        : {req.Summary}");
                    Console.WriteLine($"Recomendacao  : {req.Recommendation}");

                    ApprovalDecision decision = AskDecisionUntilValid();
                    Console.Write("Comentarios (opcional, Enter para pular): ");
                    var comments = Console.ReadLine()?.Trim();

                    var approvalResponse = new ApprovalResponse(
                        req.ContractId,
                        decision,
                        string.IsNullOrEmpty(comments) ? null : comments
                    );
                    await run.SendResponseAsync(rie.Request.CreateResponse(approvalResponse));
                    break;

                case SuperStepCompletedEvent sse:
                    CheckpointInfo? cp = sse.CompletionInfo?.Checkpoint;
                    if (cp is not null)
                    {
                        Console.WriteLine(
                            $"[Checkpoint] id={cp.CheckpointId} (use: --resume {cp.CheckpointId})");
                        Console.WriteLine($"[Checkpoint] folder={checkpointDirectory}");
                    }
                    break;

                case WorkflowOutputEvent woe when woe.Data is ContractDecision final:
                    Console.WriteLine("\n------------------------------------------------------------");
                    Console.WriteLine("WORKFLOW CONCLUIDO");
                    Console.WriteLine("------------------------------------------------------------");
                    Console.WriteLine($"Contrato : {final.ContractId}");
                    Console.WriteLine($"Decisao  : {final.Decision}");
                    if (final.Comments != null)
                        Console.WriteLine($"Comentario: {final.Comments}");
                    return;

                case WorkflowErrorEvent err:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine(err.Exception?.ToString() ?? "Erro desconhecido no workflow.");
                    Console.ResetColor();
                    return;

                case ExecutorFailedEvent ef:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"Executor '{ef.ExecutorId}' falhou: {ef.Data}");
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

    private static ApprovalDecision AskDecisionUntilValid()
    {
        while (true)
        {
            Console.Write("Sua decisao [APROVAR/REVISAR/REJEITAR]: ");
            string? raw = Console.ReadLine();

            if (Enum.TryParse<ApprovalDecision>(raw?.Trim(), ignoreCase: true, out var decision))
                return decision;

            Console.WriteLine("Entrada invalida. Informe APROVAR, REVISAR ou REJEITAR.");
        }
    }
}