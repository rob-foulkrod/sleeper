using Azure.AI.Extensions.OpenAI;
using Azure.Identity;
using OpenAI.Responses;
using Sleeper.Api.NflData.Analytics;

#pragma warning disable OPENAI001 // Responses API is in preview

namespace Sleeper.RosterReport;

/// <summary>
/// Uses Azure AI Foundry Responses API to invoke a server-side agent
/// that provides a "second opinion" on each top-10 keeper recommendation,
/// confirming or countering the statistical analysis using recent NFL news.
/// The agent must already exist in the Foundry portal.
/// </summary>
internal sealed class KeeperSecondOpinionAgent
{
    private readonly ProjectResponsesClient _responsesClient;
    private readonly int _upcomingSeason;

    private KeeperSecondOpinionAgent(ProjectResponsesClient responsesClient, int upcomingSeason)
    {
        _responsesClient = responsesClient;
        _upcomingSeason = upcomingSeason;
    }

    /// <summary>
    /// Try to build the agent from environment configuration.
    /// Connects to an existing Foundry agent by name/version using the Responses API.
    /// Returns null if required env vars are not set or connection fails.
    /// </summary>
    public static Task<KeeperSecondOpinionAgent?> TryCreateAsync(int upcomingSeason)
    {
        var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
            ?? Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT");
        if (string.IsNullOrWhiteSpace(endpoint)) return Task.FromResult<KeeperSecondOpinionAgent?>(null);

        var agentName = Environment.GetEnvironmentVariable("FOUNDRY_AGENT_NAME") ?? "ffanalysts";
        var agentVersion = Environment.GetEnvironmentVariable("FOUNDRY_AGENT_VERSION") ?? "2";

        try
        {
            var agentRef = new AgentReference(name: agentName, version: agentVersion);
            var responsesClient = new ProjectResponsesClient(
                new Uri(endpoint), new DefaultAzureCredential(), agentRef);
            Console.WriteLine($"  (Using Foundry agent '{agentName}' v{agentVersion} via Responses API)");
            return Task.FromResult<KeeperSecondOpinionAgent?>(new KeeperSecondOpinionAgent(responsesClient, upcomingSeason));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  (AI agent init failed: {ex.GetType().Name}: {ex.Message})");
            return Task.FromResult<KeeperSecondOpinionAgent?>(null);
        }
    }

    public async Task<string> GetSecondOpinionAsync(PlayerAnalysis a, string rankLabel, int lastCompletedSeason, CancellationToken ct = default)
    {
        var trend = $"{a.TrendDirection} ({a.TrendPerYear:+0.0;-0.0} PPG/yr)";
        var prompt =
            $"Player: {a.PlayerName} ({a.Position}, age {a.Age?.ToString() ?? "?"})\n" +
            $"Upcoming season: {_upcomingSeason}\n" +
            $"Keeper cost: Round {a.KeeperCostRound}\n" +
            $"Stat profile ({lastCompletedSeason} and prior):\n" +
            $"- Last-season rank: {rankLabel}\n" +
            $"- Weighted PPG: {a.WeightedPpg:F1}, Age-adjusted PPG: {a.AgeAdjustedPpg:F1}\n" +
            $"- Projected next season: {a.ProjectedSeasonPoints:F0} pts\n" +
            $"- VORP: {a.Vorp:F1}, Surplus vs cost: {a.KeeperSurplus:F1}\n" +
            $"- Trend: {trend}\n" +
            $"- Durability: {a.DurabilityPct:F0}%, Consistency: {a.ConsistencyScore:F0}/100\n" +
            $"- Grade: {a.KeeperGrade}, Score: {a.KeeperScore:F1}\n\n" +
            "Search the web for recent news on this player and give a 2-4 sentence CONFIRM/COUNTER/CAUTION verdict.";

        // Retry on HTTP 429 rate-limit with exponential backoff
        var delays = new[] { TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60) };
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                ResponseResult response = await _responsesClient.CreateResponseAsync(prompt, cancellationToken: ct);
                return response.GetOutputText()?.Trim() ?? "(no response)";
            }
            catch (Exception ex) when (attempt < delays.Length && IsRateLimit(ex))
            {
                await Task.Delay(delays[attempt], ct);
            }
        }
    }

    private static bool IsRateLimit(Exception ex)
    {
        var msg = ex.Message ?? string.Empty;
        return msg.Contains("429") || msg.Contains("rate_limit", StringComparison.OrdinalIgnoreCase);
    }
}
