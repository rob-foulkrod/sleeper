using Azure;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using OpenAI.Responses;
using Sleeper.Api.NflData.Analytics;

#pragma warning disable OPENAI001 // Responses API is in preview

namespace Sleeper.RosterReport;

/// <summary>
/// Uses Azure AI Foundry Responses API to invoke a server-side agent
/// that provides a "second opinion" on each top-10 keeper recommendation,
/// confirming or countering the statistical analysis using recent NFL news.
/// </summary>
internal sealed class KeeperSecondOpinionAgent
{
    private readonly ProjectResponsesClient _responsesClient;
    private readonly string _modelDeployment;
    private readonly int _upcomingSeason;

    private KeeperSecondOpinionAgent(ProjectResponsesClient responsesClient, string modelDeployment, int upcomingSeason)
    {
        _responsesClient = responsesClient;
        _modelDeployment = modelDeployment;
        _upcomingSeason = upcomingSeason;
    }

    /// <summary>
    /// Try to build the agent from environment configuration.
    /// Uses a ProjectResponsesClient with web search tool for per-request AI lookups.
    /// Returns null if required env vars are not set or connection fails.
    /// </summary>
    public static Task<KeeperSecondOpinionAgent?> TryCreateAsync(int upcomingSeason)
    {
        var config = FoundryResponsesClientFactory.TryCreate("Keeper Second Opinion");
        return Task.FromResult(config is null
            ? null
            : new KeeperSecondOpinionAgent(config.Client, config.ModelDeployment, upcomingSeason));
    }

    public async Task<string> GetSecondOpinionAsync(PlayerAnalysis a, string rankLabel, int lastCompletedSeason, int numTeams = 12, CancellationToken ct = default)
    {
        var keeperPickEst = a.KeeperCostRound.HasValue ? (a.KeeperCostRound.Value - 1) * numTeams + numTeams / 2 : 0;
        var trend = $"{a.TrendDirection} ({a.TrendPerYear:+0.0;-0.0} PPG/yr)";
        var prompt =
            $"Player: {a.PlayerName} ({a.Position}, age {a.Age?.ToString() ?? "?"})\n" +
            $"Upcoming season: {_upcomingSeason}\n" +
            $"League: {numTeams}-team league\n" +
            $"Keeper cost: Round {a.KeeperCostRound} (~Pick {keeperPickEst} in a {numTeams}-team draft)\n" +
            $"Stat profile ({lastCompletedSeason} and prior):\n" +
            $"- Last-season rank: {rankLabel}\n" +
            $"- Weighted PPG: {a.WeightedPpg:F1}, Age-adjusted PPG: {a.AgeAdjustedPpg:F1}\n" +
            $"- Projected next season: {a.ProjectedSeasonPoints:F0} pts\n" +
            $"- VORP: {a.Vorp:F1}, Surplus vs cost: {a.KeeperSurplus:F1}\n" +
            $"- Trend: {trend}\n" +
            $"- Durability: {a.DurabilityPct:F0}%, Consistency: {a.ConsistencyScore:F0}/100\n" +
            $"- Grade: {a.KeeperGrade}, Score: {a.KeeperScore:F1}\n\n" +
            $"Search the web for {_upcomingSeason} news and {_upcomingSeason} ADP data on this player. " +
            $"Only use {_upcomingSeason} data — ignore prior-year ADP as those seasons are completed. " +
            "Give a 2-4 sentence CONFIRM/COUNTER/CAUTION verdict.";

        // Retry on HTTP 429 rate-limit with exponential backoff
        var delays = new[] { TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60) };
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var options = new CreateResponseOptions
                {
                    Model = _modelDeployment,
                    Instructions = FoundryAgentProvisioner.GetDefaultInstructions(_upcomingSeason),
                    Tools = { ResponseTool.CreateWebSearchTool() }
                };
                options.InputItems.Add(ResponseItem.CreateUserMessageItem(prompt));
                ResponseResult response = await _responsesClient.CreateResponseAsync(options, cancellationToken: ct);
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
