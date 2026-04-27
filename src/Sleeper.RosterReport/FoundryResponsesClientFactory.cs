using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;

namespace Sleeper.RosterReport;

internal static class FoundryResponsesClientFactory
{
    public static FoundryResponsesClientConfig? TryCreate(string label)
    {
        var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
            ?? Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT");
        if (string.IsNullOrWhiteSpace(endpoint)) return null;

        var modelDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";

        try
        {
            var credential = new DefaultAzureCredential();
            var client = new ProjectResponsesClient(new Uri(endpoint), credential, null);

            Console.WriteLine($"  ({label}: model '{modelDeployment}' with web search)");
            return new FoundryResponsesClientConfig(client, modelDeployment);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ({label} init failed: {ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }
}

internal sealed record FoundryResponsesClientConfig(ProjectResponsesClient Client, string ModelDeployment);
