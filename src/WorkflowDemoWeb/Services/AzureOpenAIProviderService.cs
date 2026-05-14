using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using OpenAI.Chat;

namespace WorkflowDemoWeb.Services;

public sealed class AzureOpenAIProviderService
{
    public AIAgent ReviewAgent { get; }

    public AzureOpenAIProviderService(IConfiguration configuration)
    {
        var section = configuration.GetSection("AzureOpenAI");

        var endpoint = section["Endpoint"]
            ?? throw new InvalidOperationException("Set AzureOpenAI:Endpoint in appsettings.");
        var deployment = section["Deployment"] ?? "gpt-4o-mini";
        var tenantId = section["TenantId"]
            ?? throw new InvalidOperationException("Set AzureOpenAI:TenantId in appsettings.");
        var clientId = section["ClientId"]
            ?? throw new InvalidOperationException("Set AzureOpenAI:ClientId in appsettings.");
        var clientSecret = section["ClientSecret"]
            ?? throw new InvalidOperationException("Set AzureOpenAI:ClientSecret in appsettings.");

        var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        var openAiClient = new AzureOpenAIClient(new Uri(endpoint), credential);
        var chatClient = openAiClient.GetChatClient(deployment);

        ReviewAgent = BuildReviewAgent(chatClient);
    }

    private static AIAgent BuildReviewAgent(ChatClient chatClient) =>
        chatClient.AsAIAgent(
            name: "ContractReviewAgent",
            instructions: "You are a contract reviewer. Always return valid structured output with Summary and SuggestedDecision.",
            description: "Reviews contracts and suggests an initial decision.");
}
