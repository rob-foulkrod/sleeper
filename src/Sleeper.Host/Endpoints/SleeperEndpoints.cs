using Microsoft.AspNetCore.Mvc;
using Sleeper.Api;
using Sleeper.Api.NflData.Scoring;
using Sleeper.Api.NflData.Services;
using Sleeper.Api.Services;
using Sleeper.McpServer.Tools;

namespace Sleeper.Host.Endpoints;

internal static class SleeperEndpoints
{
    private const string Markdown = "text/markdown; charset=utf-8";

    public static IEndpointRouteBuilder MapSleeperApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").WithTags("Sleeper");

        // League
        api.MapGet("/league/{leagueId}", async (
                string leagueId,
                [FromServices] ISleeperClient client) =>
                Md(await LeagueTools.GetLeagueInfo(client, leagueId)))
            .WithName("GetLeagueInfo")
            .WithSummary("League info, roster config, scoring, keeper rules.");

        api.MapGet("/league/{leagueId}/scoreboard/{week:int}", async (
                string leagueId, int week,
                [FromServices] ISleeperClient client,
                [FromServices] ISleeperService sleeperService) =>
                Md(await LeagueTools.GetMatchupScoreboard(client, sleeperService, week, leagueId)))
            .WithName("GetMatchupScoreboard");

        api.MapGet("/league/{leagueId}/rankings/{season:int}", async (
                string leagueId, int season,
                [FromServices] IAnalysisService analysis,
                [FromQuery] string? position,
                [FromQuery] int? top) =>
                Md(await LeagueTools.GetLeagueRankings(analysis, season, position ?? "all", top ?? 20, leagueId)))
            .WithName("GetLeagueRankings");

        api.MapGet("/league/{leagueId}/draft", async (
                string leagueId,
                [FromServices] ISleeperClient client,
                [FromQuery] int? maxRounds) =>
                Md(await LeagueTools.GetDraftHistory(client, leagueId, maxRounds ?? 5)))
            .WithName("GetDraftHistory");

        api.MapGet("/league/{leagueId}/score/{playerId}/{season:int}/{week:int}", async (
                string leagueId, string playerId, int season, int week,
                [FromServices] IFantasyService fantasy) =>
                Md(await LeagueTools.ScorePlayerWeek(fantasy, playerId, season, week, leagueId)))
            .WithName("ScorePlayerWeek");

        api.MapGet("/trending", async (
                [FromServices] ISleeperClient client,
                [FromQuery] string? type,
                [FromQuery] int? limit) =>
                Md(await LeagueTools.GetTrendingPlayers(client, type ?? "add", limit ?? 15)))
            .WithName("GetTrendingPlayers");

        // Keepers / Roster / Player
        api.MapGet("/league/{leagueId}/keepers/{username}", async (
                string leagueId, string username,
                [FromServices] IAnalysisService analysis) =>
                Md(await KeeperTools.AnalyzeKeepers(analysis, username, leagueId)))
            .WithName("AnalyzeKeepers");

        api.MapGet("/league/{leagueId}/keepers-declared", async (
                string leagueId,
                [FromServices] ISleeperService sleeperService) =>
                Md(await KeeperTools.GetDeclaredKeepers(sleeperService, leagueId, null)))
            .WithName("GetDeclaredKeepers")
            .WithSummary("Actual declared keepers for every team. Empty for most of the offseason.");

        api.MapGet("/league/{leagueId}/keepers-declared/{username}", async (
                string leagueId, string username,
                [FromServices] ISleeperService sleeperService) =>
                Md(await KeeperTools.GetDeclaredKeepers(sleeperService, leagueId, username)))
            .WithName("GetDeclaredKeepersForUser");

        api.MapGet("/league/{leagueId}/roster/{username}", async (
                string leagueId, string username,
                [FromServices] IAnalysisService analysis) =>
                Md(await RosterTools.EvaluateRoster(analysis, username, leagueId)))
            .WithName("EvaluateRoster");

        api.MapGet("/league/{leagueId}/player/{playerName}", async (
                string leagueId, string playerName,
                [FromServices] IAnalysisService analysis) =>
                Md(await PlayerTools.PlayerDeepDive(analysis, playerName, leagueId)))
            .WithName("PlayerDeepDive");

        api.MapGet("/players/search", async (
                [FromServices] IAnalysisService analysis,
                [FromQuery] string q,
                [FromQuery] string? position) =>
                Md(await PlayerTools.SearchPlayers(analysis, q, position)))
            .WithName("SearchPlayers");

        return app;
    }

    private static IResult Md(string content) => Results.Text(content, Markdown);
}
