using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using Sleeper.Api.NflData.Services;
using Sleeper.Api.Services;

namespace Sleeper.McpServer.Tools;

[McpServerToolType]
public class KeeperTools
{
    [McpServerTool, Description("Analyze keeper candidates for a user's roster. Returns detailed analytics including weighted PPG, VORP, aging curves, consistency, durability, trend analysis, and composite keeper scores with letter grades. Use this when asked about keepers, who to keep, or keeper value.")]
    public static async Task<string> AnalyzeKeepers(
        IAnalysisService analysis,
        [Description("Sleeper username to analyze")] string username,
        [Description("Sleeper league ID")] string league_id = "1312539280601522176")
    {
        var report = await analysis.GetKeeperAnalysisAsync(league_id, username);
        if (report.Analyses.Count == 0) return $"No roster found for '{username}' in league {league_id}.";

        var sb = new StringBuilder();
        sb.AppendLine($"## Keeper Analysis -- {report.LeagueName} ({report.Season})");
        sb.AppendLine($"**Owner:** {report.OwnerName} | **Teams:** {report.Teams} | **Max Keepers:** {report.MaxKeepers}");
        sb.AppendLine($"**Last Completed Season:** {report.LastCompletedSeason}");
        sb.AppendLine();

        var fantasy = new[] { "QB", "RB", "WR", "TE", "K" };
        var relevantLevels = report.ReplacementLevels.Where(r => fantasy.Contains(r.Key));
        sb.AppendLine($"**Replacement Levels:** {string.Join(", ", relevantLevels.Select(r => $"{r.Key}={r.Value:F1}"))}");
        sb.AppendLine();

        sb.AppendLine("| Player | Pos | Rank | Age | Rd | wPPG | Proj | VORP | Trend | Dur% | Grade | Score |");
        sb.AppendLine("|--------|-----|------|-----|-----|------|------|------|-------|------|-------|-------|");

        foreach (var a in report.Analyses)
        {
            var rank = report.Rankings.GetRankLabel(a.SleeperId) ?? "--";
            var rd = a.KeeperCostRound.HasValue ? $"R{a.KeeperCostRound}" : "--";
            var grade = a.CanBeKept ? a.KeeperGrade : "N/A";
            var score = a.CanBeKept ? $"{a.KeeperScore:F1}" : "--";
            sb.AppendLine($"| {a.PlayerName} | {a.Position} | {rank} | {a.Age} | {rd} | {a.WeightedPpg:F1} | {a.ProjectedSeasonPoints:F0} | {a.Vorp:F1} | {a.TrendDirection} | {a.DurabilityPct:F0} | {grade} | {score} |");
        }

        // Top candidates
        var keepable = report.Analyses.Where(a => a.CanBeKept && a.KeeperScore > 0 && a.Position != "K").Take(10).ToList();
        if (keepable.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"### Top {Math.Min(10, keepable.Count)} Keeper Candidates");
            foreach (var k in keepable)
            {
                var rank = report.Rankings.GetRankLabel(k.SleeperId) ?? "N/R";
                sb.AppendLine($"- **{k.PlayerName}** ({k.Position}, {rank}, age {k.Age}) -- Grade: {k.KeeperGrade}, Score: {k.KeeperScore:F1}, Cost: Rd {k.KeeperCostRound}");
                sb.AppendLine($"  Projected {k.ProjectedSeasonPoints:F0} pts ({k.AgeAdjustedPpg:F1} PPG) | VORP: {k.Vorp:F1} | Surplus: {k.KeeperSurplus:F1} | Trend: {k.TrendDirection} | Consistency: {k.ConsistencyScore:F0}/100");
                if (k.SeasonHistory.Count > 0)
                    sb.AppendLine($"  History: {string.Join(" | ", k.SeasonHistory.OrderBy(s => s.Season).Select(s => $"{s.Season}: {s.Ppg:F1} PPG ({s.GamesPlayed}g)"))}");
            }
        }

        return sb.ToString();
    }

    [McpServerTool, Description("Get the keepers each owner has actually declared for the upcoming season (the 'lock' selections shown on the Sleeper UI). For most of the offseason every team will show no declared keepers until the keeper deadline. Use this to see what each team has committed to, NOT recommendations -- use AnalyzeKeepers for recommendations.")]
    public static async Task<string> GetDeclaredKeepers(
        ISleeperService sleeperService,
        [Description("Sleeper league ID")] string league_id = "1312539280601522176",
        [Description("Optional username to limit results to a single team")] string? username = null)
    {
        var teams = username is null
            ? await sleeperService.GetDeclaredKeepersAsync(league_id)
            : await GetSingleTeamAsync(sleeperService, league_id, username);

        if (teams is null) return $"No roster found for '{username}' in league {league_id}.";
        if (teams.Count == 0) return $"No rosters found in league {league_id}.";

        var sb = new StringBuilder();
        sb.AppendLine($"## Declared Keepers -- League {league_id}");
        sb.AppendLine();

        var totalDeclared = teams.Sum(t => t.Keepers.Count);
        if (totalDeclared == 0)
        {
            sb.AppendLine("_No team has declared any keepers yet. This is normal until the keeper deadline._");
            sb.AppendLine();
        }

        sb.AppendLine("| Team | Owner | Keepers |");
        sb.AppendLine("|------|-------|---------|");
        foreach (var t in teams.OrderBy(t => t.RosterId))
        {
            var team = string.IsNullOrWhiteSpace(t.TeamName) ? "(no team name)" : t.TeamName;
            var owner = t.DisplayName ?? t.Username ?? "(unknown)";
            var keepers = t.Keepers.Count == 0
                ? "_(none)_"
                : string.Join(", ", t.Keepers.Select(p => $"{p.FirstName} {p.LastName} ({p.Position})"));
            sb.AppendLine($"| {team} | {owner} | {keepers} |");
        }
        return sb.ToString();
    }

    private static async Task<List<Sleeper.Api.Models.DeclaredKeeperTeam>?> GetSingleTeamAsync(
        ISleeperService sleeperService, string leagueId, string username)
    {
        var team = await sleeperService.GetDeclaredKeepersForUserAsync(leagueId, username);
        return team is null ? null : [team];
    }
}
