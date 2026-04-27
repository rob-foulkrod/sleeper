using Azure;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Core;
using OpenAI.Responses;

#pragma warning disable OPENAI001 // Responses API is in preview

namespace Sleeper.RosterReport;

/// <summary>
/// Ensures the Foundry prompt agent exists in the project, creating it with
/// web-search capability when not found.
/// </summary>
internal static class FoundryAgentProvisioner
{
    public const string DefaultInstructions =
        "You are an expert fantasy football analyst. The current date is 2026. " +
        "When asked about a player, search the web for 2026 fantasy football " +
        "ADP (average draft position) data, 2026 NFL news, injury reports, " +
        "depth chart updates, and offseason moves. " +
        "IMPORTANT: Only use 2026 ADP and rankings data. Ignore any 2025 or " +
        "earlier ADP data — those seasons are already completed. " +
        "Provide concise, data-driven analysis to help fantasy managers " +
        "evaluate keepers and draft picks for the 2026 season.";

    /// <summary>
    /// Checks whether the named agent exists in the Foundry project.
    /// If missing, creates a new prompt agent with web search tool enabled.
    /// Uses TokenCredential (e.g. DefaultAzureCredential) — API key auth
    /// is not supported by AIProjectClient.
    /// </summary>
    public static async Task EnsureAgentExistsAsync(
        string projectEndpoint,
        string agentName,
        string modelDeployment,
        TokenCredential credential,
        CancellationToken ct = default)
    {
        var projectClient = new AIProjectClient(
            endpoint: new Uri(projectEndpoint),
            tokenProvider: credential);

        var admin = projectClient.AgentAdministrationClient;

        // Check if agent already exists
        try
        {
            await admin.GetAgentAsync(agentName, ct);
            Console.WriteLine($"  (Foundry agent '{agentName}' found)");
            return;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Agent doesn't exist – create below
        }

        // Create the agent with web search tool
        Console.WriteLine($"  (Foundry agent '{agentName}' not found – creating...)");

        var definition = new DeclarativeAgentDefinition(model: modelDeployment)
        {
            Instructions = DefaultInstructions,
            Tools = { ResponseTool.CreateWebSearchTool() }
        };

        var created = await admin.CreateAgentVersionAsync(
            agentName: agentName,
            options: new(definition),
            cancellationToken: ct);

        Console.WriteLine($"  (Created Foundry agent '{agentName}' v{created.Value.Version})");
    }
}
