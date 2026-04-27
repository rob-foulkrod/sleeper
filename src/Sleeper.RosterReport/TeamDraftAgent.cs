using Azure;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using OpenAI.Responses;

#pragma warning disable OPENAI001 // Responses API is in preview

namespace Sleeper.RosterReport;

/// <summary>
/// Uses Azure AI Foundry Responses API to provide draft-aware opinions on
/// each player in a team deep dive, incorporating current ADP, news, and
/// draft stock information via web search.
/// </summary>
internal sealed class TeamDraftAgent
{
    private readonly ProjectResponsesClient _responsesClient;
    private readonly string _modelDeployment;
    private readonly int _upcomingSeason;

    private TeamDraftAgent(ProjectResponsesClient responsesClient, string modelDeployment, int upcomingSeason)
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
    public static Task<TeamDraftAgent?> TryCreateAsync(int upcomingSeason)
    {
        var config = FoundryResponsesClientFactory.TryCreate("Team Draft Agent");
        return Task.FromResult(config is null
            ? null
            : new TeamDraftAgent(config.Client, config.ModelDeployment, upcomingSeason));
    }

    /// <summary>
    /// Gets a draft-aware opinion for a player, considering their stats profile,
    /// current ADP, news, and trade/draft stock.
    /// </summary>
    public async Task<string> GetDraftOpinionAsync(
        string playerName, string? position, int? age,
        decimal weightedPpg, decimal projectedPoints, decimal vorp,
        string trendDirection, decimal trendPerYear,
        decimal durabilityPct, decimal consistencyScore,
        CancellationToken ct = default)
    {
        var prompt =
            $"Player: {playerName} ({position ?? "?"}, age {age?.ToString() ?? "?"})\n" +
            $"Upcoming {_upcomingSeason} fantasy football season.\n" +
            $"Statistical profile:\n" +
            $"- Weighted PPG: {weightedPpg:F1}\n" +
            $"- Projected season points: {projectedPoints:F0}\n" +
            $"- VORP: {vorp:F1}\n" +
            $"- Trend: {trendDirection} ({trendPerYear:+0.0;-0.0} PPG/yr)\n" +
            $"- Durability: {durabilityPct:F0}%\n" +
            $"- Consistency: {consistencyScore:F0}/100\n\n" +
            $"Search the web for this player's {_upcomingSeason} fantasy football ADP (average draft position). " +
            $"Only use {_upcomingSeason} ADP data — ignore any prior-year ADP as those seasons are completed. " +
            $"Also search for {_upcomingSeason} NFL news, injury updates, depth chart changes, and offseason moves. " +
            $"Then give a 3-5 sentence draft outlook: Is this player being drafted too high, too low, or about right? " +
            $"What's the key upside and risk? Any news that changes the outlook?";

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
