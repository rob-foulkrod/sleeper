using Sleeper.RosterReport.Agents;
using Sleeper.RosterReport.Recap;

namespace Sleeper.RosterReport.Copilot;

/// <summary>
/// Builds the recap agents against the signed-in GitHub Copilot CLI session.
/// </summary>
/// <remarks>
/// The live Copilot model roster is authoritative. If a preferred model is not
/// available, <see cref="CopilotAgentHost"/> selects another live Copilot model.
/// When Copilot is unavailable the caller writes a data-only dump rather than
/// switching to a different AI backend.
/// </remarks>
internal sealed class ReportAgentProvider : IAsyncDisposable
{
    private const string ProofreaderInstructions =
        "You are a strict, fast factual proofreader for a fantasy football recap newsletter. " +
        "Compare the supplied draft markdown recap against the game data and COMPUTED MATCHUP FACTS. " +
        "Check for: incorrect scores, wrong winner/loser, wrong owner or team names, mathematical mistakes, " +
        "roster misattributions, and ungrounded claims. " +
        "Return ONLY valid JSON matching: {\"Pass\": true/false, \"Defects\": [\"...\"]}. " +
        "No markdown fences or commentary outside the JSON.";

    private readonly CopilotAgentHost? _host;

    public string Backend { get; }
    public CopilotUsageLedger Usage { get; }

    private ReportAgentProvider(
        CopilotAgentHost? host,
        CopilotUsageLedger usage,
        string backend)
    {
        _host = host;
        Usage = usage;
        Backend = backend;
    }

    public static async Task<ReportAgentProvider> CreateAsync(
        CopilotAgentSettings copilotSettings,
        CancellationToken ct = default)
    {
        var usage = new CopilotUsageLedger();
        try
        {
            var host = await CopilotAgentHost.StartAsync(
                copilotSettings,
                usage,
                ct,
                requireEvaluatorModel: false).ConfigureAwait(false);
            Console.WriteLine($"  (Copilot agents online: game writer '{host.WriterModel}', league writer '{host.LeagueWriterModel}', proofreader '{host.ProofreaderModel}')");
            return new ReportAgentProvider(host, usage, "copilot");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  (Copilot unavailable: {ex.Message})");
            return new ReportAgentProvider(null, usage, "none");
        }
    }

    /// <summary>Returns null when no backend is available, so the caller can dump data instead.</summary>
    public RecapAgent? TryCreateRecapAgent()
    {
        if (_host is not null)
        {
            return RecapAgent.Create(
                _host.CreateWriter(FoundryAgentInstructions.WeeklyGameAnalyst),
                _host.CreateWriter(FoundryAgentInstructions.WeeklyLeagueAnalyst, _host.LeagueWriterModel),
                $"copilot:{_host.WriterModel}+{_host.LeagueWriterModel}",
                _host.CreateProofreader(ProofreaderInstructions, _host.ProofreaderModel));
        }

        return null;
    }

    /// <summary>Returns null when no backend is available, so the caller can skip the prose pass.</summary>
    public SeasonAgent? TryCreateSeasonAgent()
    {
        if (_host is not null)
        {
            return SeasonAgent.Create(
                _host.CreateWriter(FoundryAgentInstructions.SeasonAnalyst, _host.LeagueWriterModel),
                $"copilot:{_host.LeagueWriterModel}");
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync().ConfigureAwait(false);
    }
}
