using Microsoft.Extensions.DependencyInjection;
using Sleeper.Api;
using Sleeper.Api.Extensions;
using Sleeper.Api.Models;
using Sleeper.Api.NflData;
using Sleeper.Api.NflData.Analytics;
using Sleeper.Api.NflData.Models;
using Sleeper.Api.NflData.Scoring;
using Sleeper.Api.Services;
using Sleeper.RosterReport;

const string DefaultLeagueId = "1312539280601522176";
const int HistoryYears = 3;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = args[0].ToLowerInvariant();

// Wire up DI
var services = new ServiceCollection();
services.AddSleeperApi();
services.AddNflData();
var sp = services.BuildServiceProvider();

var client = sp.GetRequiredService<ISleeperClient>();
var sleeperService = sp.GetRequiredService<ISleeperService>();
var nflData = sp.GetRequiredService<INflDataClient>();
var fantasyService = sp.GetRequiredService<IFantasyService>();

return await (command switch
{
    "keepers" => RunKeeperAnalyzer(args),
    "board" => RunLeagueBoard(args),
    "player" => RunPlayerDeepDive(args),
    "matchup" => RunMatchupScoreboard(args),
    _ => Task.FromResult(PrintUsage())
});

int PrintUsage()
{
    Console.WriteLine("Sleeper Fantasy Football Reports");
    Console.WriteLine("================================");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  keepers <username> [league_id]     Keeper analysis with recommendations");
    Console.WriteLine("  board [league_id]                  All teams' keeper candidates");
    Console.WriteLine("  player <name> [league_id]          Player deep dive with 3-year trend");
    Console.WriteLine("  matchup <week> [league_id]         Weekly matchup scoreboard");
    Console.WriteLine();
    Console.WriteLine($"  Default league: {DefaultLeagueId}");
    return 1;
}

// ======================================================================
// REPORT 1: KEEPER ANALYZER
// ======================================================================
async Task<int> RunKeeperAnalyzer(string[] a)
{
    var username = a.Length > 1 ? a[1] : null;
    var leagueId = a.Length > 2 ? a[2] : DefaultLeagueId;
    if (username is null) { Console.WriteLine("Usage: keepers <username> [league_id]"); return 1; }

    Console.WriteLine($"Analyzing keepers for '{username}'...");

    var user = await client.GetUserAsync(username);
    if (user is null) { Console.WriteLine($"User '{username}' not found."); return 1; }

    var league = await client.GetLeagueAsync(leagueId);
    if (league is null) { Console.WriteLine($"League not found."); return 1; }

    var config = LeagueRosterConfig.FromLeague(league);
    var keeperValues = await sleeperService.GetRosterKeeperValuesAsync(leagueId, username);
    if (keeperValues.Count == 0) { Console.WriteLine("No roster found."); return 1; }

    // Build scorer for this league
    var scorer = new FantasyScorer(league.ScoringSettings ?? new());
    var currentSeason = int.TryParse(league.Season, out var s) ? s : DateTime.UtcNow.Year;

    // Fetch multi-year stats -- use last completed season as baseline
    // (league.Season might be future/pre-draft, nflverse only has completed seasons)
    var nflState = await client.GetNflStateAsync();
    var lastCompletedSeason = nflState is not null
        ? int.Parse(nflState.PreviousSeason ?? (nflState.Season ?? currentSeason.ToString()))
        : currentSeason - 1;

    // If we're in the offseason, the "current" season has no data
    if (nflState?.SeasonType == "off") lastCompletedSeason = int.Parse(nflState.PreviousSeason ?? (currentSeason - 1).ToString());

    Console.WriteLine($"Fetching {HistoryYears}-year stats ({lastCompletedSeason - HistoryYears + 1}-{lastCompletedSeason})...");
    var seasonStatsTasks = new Dictionary<int, Task<Dictionary<string, SeasonPlayerStats>>>();
    var weeklyStatsTasks = new Dictionary<int, Task<Dictionary<string, List<WeeklyPlayerStats>>>>();

    for (int y = lastCompletedSeason; y > lastCompletedSeason - HistoryYears; y--)
    {
        seasonStatsTasks[y] = nflData.GetSeasonStatsBySleeperIdAsync(y, "reg");
        weeklyStatsTasks[y] = nflData.GetWeeklyStatsBySleeperIdAsync(y);
    }

    await Task.WhenAll(
        Task.WhenAll(seasonStatsTasks.Values),
        Task.WhenAll(weeklyStatsTasks.Values));

    // Calculate real replacement levels from league-wide scoring data
    Console.WriteLine("Ranking all NFL players by league scoring...");
    var lastSeasonAllStats = await nflData.GetSeasonStatsAsync(lastCompletedSeason, "reg");
    var sleeperToGsis = await nflData.GetSleeperToGsisMapAsync();
    var ranker = new LeagueRanker(scorer);
    var rankings = ranker.RankBySleeperId(lastSeasonAllStats, sleeperToGsis);

    var replacementLevels = rankings.CalculateReplacementLevels(
        config.Teams, config.StarterSlots, config.FlexSlots);

    var fantasyPositions = new HashSet<string> { "QB", "RB", "WR", "TE", "K" };
    var fantasyReplacements = replacementLevels.Where(r => fantasyPositions.Contains(r.Key));
    Console.WriteLine($"Replacement levels ({lastCompletedSeason} actual): {string.Join(", ", fantasyReplacements.Select(r => $"{r.Key}={r.Value:F1}"))}");

    var gamesNextSeason = DurabilityCalculator.GamesInSeason(currentSeason);

    // Analyze each player
    var analyzer = new KeeperAnalyzer(replacementLevels);
    var analyses = new List<PlayerAnalysis>();

    foreach (var kv in keeperValues)
    {
        var p = kv.Player;
        if (p.Position is "DEF") continue; // skip defenses for now

        var seasonHistory = new List<SeasonSummary>();
        var recentWeekly = new List<decimal>();

        for (int y = lastCompletedSeason; y > lastCompletedSeason - HistoryYears; y--)
        {
            var seasonStats = seasonStatsTasks[y].Result;
            var weeklyStats = weeklyStatsTasks[y].Result;

            if (seasonStats.TryGetValue(p.PlayerId, out var ss))
            {
                var scored = scorer.ScoreSeason(ss);
                var games = ss.Games ?? 0;
                var ppg = games > 0 ? Math.Round(scored.TotalPoints / games, 2) : 0m;

                // Get weekly points for stddev
                var weeklyPts = new List<decimal>();
                if (weeklyStats.TryGetValue(p.PlayerId, out var weeks))
                {
                    weeklyPts = weeks
                        .Where(w => w.SeasonType == "REG")
                        .Select(w => scorer.ScoreWeekly(w).TotalPoints)
                        .ToList();

                    if (y == lastCompletedSeason)
                        recentWeekly = weeklyPts;
                }

                var stdDev = weeklyPts.Count >= 2 ? ConsistencyCalculator.CalculateStdDev(weeklyPts) : 0m;
                seasonHistory.Add(new SeasonSummary(y, games, scored.TotalPoints, ppg, Math.Round(stdDev, 2)));
            }
        }

        var analysis = analyzer.Analyze(
            p.PlayerId, p.FullName, p.Position, p.Age,
            kv.KeeperCostRound, kv.CanBeKept,
            seasonHistory, recentWeekly, gamesNextSeason);

        analyses.Add(analysis);
    }

    // Sort by keeper score descending
    analyses = analyses.OrderByDescending(x => x.KeeperScore).ToList();

    // Print report
    Console.WriteLine();
    Console.WriteLine("===================================================================================");
    Console.WriteLine($"  KEEPER ANALYSIS -- {league.Name} ({league.Season})");
    Console.WriteLine($"  Owner: {user.DisplayName ?? username}");
    Console.WriteLine($"  League: {config.Teams} teams, {config.MaxKeepers} keepers allowed");
    Console.WriteLine("===================================================================================");

    // Full roster table
    Console.WriteLine();
    Console.WriteLine($"  {"Player",-24} {"Pos",-4} {"Rank",-6} {"Age",-4} {"Rd",-4} {"wPPG",-7} {"Proj",-7} {"VORP",-7} {"Trend",-12} {"Dur%",-6} {"Grade",-6} {"Score",-7}");
    Console.WriteLine($"  {"------",-24} {"---",-4} {"----",-6} {"---",-4} {"--",-4} {"----",-7} {"----",-7} {"----",-7} {"-----",-12} {"----",-6} {"-----",-6} {"-----",-7}");

    foreach (var pa in analyses)
    {
        var rd = pa.KeeperCostRound.HasValue ? $"R{pa.KeeperCostRound}" : "--";
        var age = pa.Age?.ToString() ?? "?";
        var grade = pa.CanBeKept ? pa.KeeperGrade : "N/A";
        var score = pa.CanBeKept ? $"{pa.KeeperScore:F1}" : "--";
        var rank = rankings.GetRankLabel(pa.SleeperId) ?? "--";

        Console.WriteLine($"  {pa.PlayerName,-24} {pa.Position,-4} {rank,-6} {age,-4} {rd,-4} {pa.WeightedPpg,-7:F1} {pa.ProjectedSeasonPoints,-7:F0} {pa.Vorp,-7:F1} {pa.TrendDirection,-12} {pa.DurabilityPct,-6:F0} {grade,-6} {score,-7}");
    }

    // Top 10 keeper candidates -- positional diversity aware, excludes kickers
    // Kickers have near-zero strategic value as keepers (volatile, easily replaced in draft)
    const int TopCandidates = 10;
    var recommended = new List<PlayerAnalysis>();
    var positionCounts = new Dictionary<string, int>();
    var candidates = analyses.Where(a => a.CanBeKept && a.KeeperScore > 0 && a.Position != "K").ToList();

    foreach (var candidate in candidates)
    {
        if (recommended.Count >= TopCandidates) break;

        var pos = candidate.Position?.ToUpperInvariant() ?? "";
        var currentCount = positionCounts.GetValueOrDefault(pos);
        var starterSlots = config.StarterSlots.GetValueOrDefault(pos, 1);

        // Cap keepers at the number of direct starter slots for that position
        // (2-QB league = allow 2 QB keepers, 4-RB league = allow 4 RB keepers)
        // But never let one position consume more than maxKeepers - 1
        // so there's always room for at least one other position
        var keeperCap = Math.Min(starterSlots, config.MaxKeepers - 1);
        keeperCap = Math.Max(1, keeperCap);

        if (currentCount < keeperCap)
        {
            recommended.Add(candidate);
            positionCounts[pos] = currentCount + 1;
        }
    }

    Console.WriteLine();
    Console.WriteLine("-----------------------------------------------------------------------------------");
    Console.WriteLine($"  TOP {TopCandidates} KEEPER CANDIDATES (you keep {config.MaxKeepers} -- your call)");
    Console.WriteLine("-----------------------------------------------------------------------------------");

    // Try to build the AI second-opinion agent (requires AZURE_OPENAI_ENDPOINT)
    var secondOpinionAgent = await KeeperSecondOpinionAgent.TryCreateAsync(currentSeason);
    if (secondOpinionAgent is null)
    {
        Console.WriteLine();
        Console.WriteLine("  (AI second opinion disabled -- set AZURE_OPENAI_ENDPOINT and AZURE_OPENAI_DEPLOYMENT_NAME to enable)");
    }

    for (int i = 0; i < recommended.Count; i++)
    {
        var r = recommended[i];
        var rankLabel = rankings.GetRankLabel(r.SleeperId) ?? "N/R";
        Console.WriteLine();
        Console.WriteLine($"  {i + 1}. {r.PlayerName} ({r.Position}, age {r.Age}) -- {rankLabel} in {lastCompletedSeason}");
        Console.WriteLine($"     Keeper Cost: Round {r.KeeperCostRound}  |  Grade: {r.KeeperGrade}  |  Score: {r.KeeperScore:F1}");
        Console.WriteLine($"     Projected: {r.ProjectedSeasonPoints:F0} pts ({r.AgeAdjustedPpg:F1} PPG)  |  VORP: {r.Vorp:F1}  |  Surplus: {r.KeeperSurplus:F1}");
        Console.WriteLine($"     Trend: {r.TrendDirection} ({r.TrendPerYear:+0.0;-0.0}/yr)  |  Durability: {r.DurabilityPct:F0}%  |  Consistency: {r.ConsistencyScore:F0}/100");

        if (r.SeasonHistory.Count > 0)
        {
            Console.Write("     History: ");
            Console.WriteLine(string.Join(" | ", r.SeasonHistory.OrderBy(s => s.Season)
                .Select(s => $"{s.Season}: {s.Ppg:F1} PPG ({s.GamesPlayed}g)")));
        }

        // Reasoning — lead with distinctive qualities, surplus last (it's always present)
        var reasons = new List<string>();

        // Lead with positional rank — the most concrete fact
        if (rankLabel != "N/R")
        {
            var posRank = rankings.BySleeperId.GetValueOrDefault(r.SleeperId)?.PositionalRank ?? 0;
            if (posRank <= 10)
                reasons.Add($"Elite talent -- ranked {rankLabel} league-wide last season");
            else if (posRank <= 20)
                reasons.Add($"Starter-caliber -- ranked {rankLabel} league-wide");
        }

        // Trend and trajectory
        if (r.TrendPerYear > 1) reasons.Add($"Ascending trajectory -- gaining {r.TrendPerYear:F1} PPG/year");
        if (r.Vorp > 5) reasons.Add($"Elite positional scarcity -- {r.Vorp:F1} pts above replacement {r.Position}");
        if (r.ConsistencyScore > 70) reasons.Add($"Reliable weekly scorer -- {r.ConsistencyScore:F0}/100 consistency");
        if (r.DurabilityPct >= 90) reasons.Add("Iron man -- plays every game");

        // Surplus as supporting context (not the headline)
        if (r.KeeperSurplus > 5) reasons.Add($"Massive cost value -- projects {r.AgeAdjustedPpg:F1} PPG at Rd {r.KeeperCostRound} cost");
        else if (r.KeeperSurplus > 2) reasons.Add($"Good value -- worth more than Rd {r.KeeperCostRound} pick");

        // Warnings last
        if (r.Age.HasValue && r.Position == "RB" && r.Age >= 28) reasons.Add("WARNING: RB age cliff approaching");
        if (r.DurabilityPct < 70) reasons.Add($"RISK: Injury-prone -- only {r.DurabilityPct:F0}% games played");
        if (r.TrendPerYear < -1) reasons.Add($"CONCERN: Declining -- losing {Math.Abs(r.TrendPerYear):F1} PPG/year");
        if (r.SeasonHistory.Count == 1) reasons.Add("NOTE: Only 1 season of data -- projection has high uncertainty");

        if (reasons.Count > 0)
        {
            Console.WriteLine($"     Why: {reasons[0]}");
            foreach (var reason in reasons.Skip(1))
                Console.WriteLine($"          {reason}");
        }

        // AI second opinion with recent news (web search)
        if (secondOpinionAgent is not null)
        {
            try
            {
                // Small pacing delay between candidates to avoid rate-limit bursts
                if (i > 0) await Task.Delay(TimeSpan.FromSeconds(3));

                Console.Write("     AI Second Opinion: (searching recent news...)");
                var verdict = await secondOpinionAgent.GetSecondOpinionAsync(r, rankLabel, lastCompletedSeason);
                // Clear the "searching..." line
                Console.Write("\r     AI Second Opinion:                              \n");
                foreach (var line in verdict.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    Console.WriteLine($"       {line.Trim()}");
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine($"     AI Second Opinion: (unavailable -- {ex.Message})");
            }
        }
    }

    // Don't-keep list
    var dontKeep = analyses
        .Where(a => a.CanBeKept && a.KeeperScore <= 0)
        .OrderBy(a => a.KeeperScore)
        .ToList();

    if (dontKeep.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("-----------------------------------------------------------------------------------");
        Console.WriteLine("  NOT RECOMMENDED (negative value -- draft position more valuable)");
        Console.WriteLine("-----------------------------------------------------------------------------------");
        foreach (var d in dontKeep.Take(5))
        {
            Console.WriteLine($"    {d.PlayerName,-24} {d.Position,-4} Rd {d.KeeperCostRound,-3} -> Grade: {d.KeeperGrade}  Score: {d.KeeperScore:F1}  ({d.TrendDirection})");
        }
    }

    Console.WriteLine();
    Console.WriteLine("===================================================================================");
    return 0;
}

// ======================================================================
// REPORT 2: LEAGUE KEEPER BOARD
// ======================================================================
async Task<int> RunLeagueBoard(string[] a)
{
    var leagueId = a.Length > 1 ? a[1] : DefaultLeagueId;

    Console.WriteLine("Generating league-wide keeper board...");

    var league = await client.GetLeagueAsync(leagueId);
    if (league is null) { Console.WriteLine("League not found."); return 1; }

    var config = LeagueRosterConfig.FromLeague(league);
    var rostersWithOwners = await sleeperService.GetRostersWithOwnersAsync(leagueId);
    var scorer = new FantasyScorer(league.ScoringSettings ?? new());
    var currentSeason = int.TryParse(league.Season, out var s) ? s : DateTime.UtcNow.Year;

    // Fetch last year's stats (most relevant for keeper decisions)
    var lastSeason = currentSeason - 1;
    Console.WriteLine($"Fetching {lastSeason} season stats...");
    var seasonStats = await nflData.GetSeasonStatsBySleeperIdAsync(lastSeason, "reg");

    Console.WriteLine();
    Console.WriteLine("===================================================================================");
    Console.WriteLine($"  LEAGUE KEEPER BOARD -- {league.Name} ({league.Season})");
    Console.WriteLine($"  {config.Teams} teams, {config.MaxKeepers} keepers each = {config.Teams * config.MaxKeepers} players kept");
    Console.WriteLine("===================================================================================");

    var allKeptPlayers = new List<(string Owner, PlayerAnalysis Analysis)>();
    var allUnkeptTalent = new List<(string Owner, KeeperValue Kv, decimal LastSeasonPpg)>();

    foreach (var rwo in rostersWithOwners.OrderBy(r => r.OwnerDisplayName))
    {
        var ownerName = rwo.OwnerDisplayName ?? rwo.OwnerUsername ?? $"Roster {rwo.Roster.RosterId}";

        // Get keeper values for this roster
        // We need to manually compute since GetRosterKeeperValuesAsync takes username
        var keeperValues = await sleeperService.GetRosterKeeperValuesAsync(leagueId,
            rwo.OwnerUsername ?? rwo.OwnerDisplayName ?? "");

        if (keeperValues.Count == 0) continue;

        var teamAnalyses = new List<PlayerAnalysis>();
        foreach (var kv in keeperValues.Where(k => k.CanBeKept && k.Player.Position != "DEF"))
        {
            decimal ppg = 0;
            if (seasonStats.TryGetValue(kv.Player.PlayerId, out var ss))
            {
                var scored = scorer.ScoreSeason(ss);
                var games = ss.Games ?? 1;
                ppg = games > 0 ? Math.Round(scored.TotalPoints / games, 2) : 0;
            }

            var history = ppg > 0
                ? new List<SeasonSummary> { new(lastSeason, 17, ppg * 17, ppg, 0) }
                : new List<SeasonSummary>();

            var analyzer = new KeeperAnalyzer();
            var analysis = analyzer.Analyze(kv.Player.PlayerId, kv.Player.FullName,
                kv.Player.Position, kv.Player.Age, kv.KeeperCostRound, true, history, []);

            teamAnalyses.Add(analysis);
        }

        var topKeepers = teamAnalyses.OrderByDescending(t => t.KeeperScore).Take(config.MaxKeepers).ToList();

        Console.WriteLine();
        Console.WriteLine($"  {ownerName}");
        Console.WriteLine($"  {"  Player",-26} {"Pos",-4} {"Age",-4} {"Rd",-4} {"PPG",-7} {"VORP",-6} {"Grade",-6}");

        foreach (var tk in topKeepers)
        {
            var marker = tk.KeeperGrade is "S" or "A" ? "*" : " ";
            Console.WriteLine($"  {marker} {tk.PlayerName,-24} {tk.Position,-4} {tk.Age?.ToString() ?? "?",-4} R{tk.KeeperCostRound,-3} {tk.WeightedPpg,-7:F1} {tk.Vorp,-6:F1} {tk.KeeperGrade,-6}");
            allKeptPlayers.Add((ownerName, tk));
        }

        // Track unkept talent
        var unkept = teamAnalyses.Except(topKeepers).Where(t => t.WeightedPpg > 5).ToList();
        foreach (var u in unkept)
        {
            var kv = keeperValues.First(k => k.Player.PlayerId == u.SleeperId);
            allUnkeptTalent.Add((ownerName, kv, u.WeightedPpg));
        }
    }

    // Draft pool analysis
    Console.WriteLine();
    Console.WriteLine("-----------------------------------------------------------------------------------");
    Console.WriteLine("  BEST TALENT RETURNING TO DRAFT POOL");
    Console.WriteLine("-----------------------------------------------------------------------------------");
    Console.WriteLine($"  {"Player",-24} {"Pos",-4} {"PPG",-7} {"From",-20}");

    foreach (var u in allUnkeptTalent.OrderByDescending(u => u.LastSeasonPpg).Take(15))
    {
        Console.WriteLine($"  {u.Kv.Player.FullName,-24} {u.Kv.Player.Position,-4} {u.LastSeasonPpg,-7:F1} {u.Owner,-20}");
    }

    // Positional scarcity
    Console.WriteLine();
    Console.WriteLine("-----------------------------------------------------------------------------------");
    Console.WriteLine("  KEEPER IMPACT BY POSITION");
    Console.WriteLine("-----------------------------------------------------------------------------------");

    foreach (var pos in new[] { "QB", "RB", "WR", "TE" })
    {
        var kept = allKeptPlayers.Count(k => k.Analysis.Position == pos);
        var totalStarters = config.GetEffectiveStarters(pos) * config.Teams;
        Console.WriteLine($"  {pos}: {kept} kept / {totalStarters} starter slots ({kept * 100 / Math.Max(1, totalStarters)}% of starters locked up)");
    }

    Console.WriteLine();
    Console.WriteLine("===================================================================================");
    return 0;
}

// ======================================================================
// REPORT 3: PLAYER DEEP DIVE
// ======================================================================
async Task<int> RunPlayerDeepDive(string[] a)
{
    var playerName = a.Length > 1 ? a[1] : null;
    var leagueId = a.Length > 2 ? a[2] : DefaultLeagueId;
    if (playerName is null) { Console.WriteLine("Usage: player <name> [league_id]"); return 1; }

    Console.WriteLine($"Looking up '{playerName}'...");

    var league = await client.GetLeagueAsync(leagueId);
    if (league is null) { Console.WriteLine("League not found."); return 1; }

    var scorer = new FantasyScorer(league.ScoringSettings ?? new());
    var currentSeason = int.TryParse(league.Season, out var s) ? s : DateTime.UtcNow.Year;

    // Find the player in Sleeper data
    var allPlayers = await client.GetAllPlayersAsync();
    var searchName = playerName.ToLowerInvariant();
    var match = allPlayers.Values.FirstOrDefault(p =>
        p.FullName.ToLowerInvariant().Contains(searchName) ||
        (p.SearchFullName?.Contains(searchName.Replace(" ", "")) ?? false));

    if (match is null) { Console.WriteLine($"Player '{playerName}' not found."); return 1; }

    Console.WriteLine();
    Console.WriteLine("===================================================================================");
    Console.WriteLine($"  PLAYER DEEP DIVE: {match.FullName}");
    Console.WriteLine($"  {match.Position} | {match.Team ?? "FA"} | Age {match.Age} | {match.YearsExp ?? 0} yrs exp");
    Console.WriteLine("===================================================================================");

    // Multi-year analysis -- use last completed seasons
    var nflState2 = await client.GetNflStateAsync();
    var lastCompleted = nflState2 is not null
        ? int.Parse(nflState2.PreviousSeason ?? (currentSeason - 1).ToString())
        : currentSeason - 1;

    var seasonHistory = new List<SeasonSummary>();
    var allWeeklyPoints = new Dictionary<int, List<decimal>>();

    for (int y = lastCompleted; y >= lastCompleted - HistoryYears; y--)
    {
        var seasonStats = await nflData.GetSeasonStatsBySleeperIdAsync(y, "reg");
        var weeklyStats = await nflData.GetWeeklyStatsBySleeperIdAsync(y);

        if (seasonStats.TryGetValue(match.PlayerId, out var ss))
        {
            var scored = scorer.ScoreSeason(ss);
            var games = ss.Games ?? 0;
            var ppg = games > 0 ? Math.Round(scored.TotalPoints / games, 2) : 0m;

            var weeklyPts = new List<decimal>();
            if (weeklyStats.TryGetValue(match.PlayerId, out var weeks))
            {
                weeklyPts = weeks.Where(w => w.SeasonType == "REG")
                    .OrderBy(w => w.Week)
                    .Select(w => scorer.ScoreWeekly(w).TotalPoints)
                    .ToList();
                allWeeklyPoints[y] = weeklyPts;
            }

            var stdDev = weeklyPts.Count >= 2 ? ConsistencyCalculator.CalculateStdDev(weeklyPts) : 0m;
            seasonHistory.Add(new SeasonSummary(y, games, scored.TotalPoints, ppg, Math.Round(stdDev, 2)));
        }
    }

    if (seasonHistory.Count == 0) { Console.WriteLine("\n  No scoring data found for this player."); return 0; }

    // Season-by-season table
    Console.WriteLine();
    Console.WriteLine($"  {"Season",-8} {"Games",-7} {"Total",-8} {"PPG",-7} {"StdDev",-8} {"Floor",-7} {"Ceil",-7} {"Boom%",-7} {"Bust%",-7}");
    Console.WriteLine($"  {"------",-8} {"-----",-7} {"-----",-8} {"---",-7} {"------",-8} {"-----",-7} {"----",-7} {"-----",-7} {"-----",-7}");

    var posAvg = VorpCalculator.DefaultReplacementPpg.GetValueOrDefault(match.Position?.ToUpperInvariant() ?? "", 8m);
    foreach (var sh in seasonHistory.OrderBy(s => s.Season))
    {
        var weeklyPts = allWeeklyPoints.GetValueOrDefault(sh.Season, []);
        var floor = ConsistencyCalculator.CalculateFloor(weeklyPts);
        var ceil = ConsistencyCalculator.CalculateCeiling(weeklyPts);
        var boom = ConsistencyCalculator.CalculateBoomRate(weeklyPts, posAvg);
        var bust = ConsistencyCalculator.CalculateBustRate(weeklyPts, posAvg);

        Console.WriteLine($"  {sh.Season,-8} {sh.GamesPlayed,-7} {sh.TotalPoints,-8:F1} {sh.Ppg,-7:F1} {sh.StdDev,-8:F1} {floor,-7:F1} {ceil,-7:F1} {boom,-7:F0} {bust,-7:F0}");
    }

    // Weekly sparkline for most recent season
    var recentYear = seasonHistory.OrderByDescending(s => s.Season).First().Season;
    if (allWeeklyPoints.TryGetValue(recentYear, out var recentWeekly) && recentWeekly.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"  {recentYear} Weekly Scoring:");
        var max = recentWeekly.Max();
        var scale = max > 0 ? 30m / max : 1m;
        for (int w = 0; w < recentWeekly.Count; w++)
        {
            var pts = recentWeekly[w];
            var bar = new string('#', (int)(pts * scale));
            Console.WriteLine($"  Wk {w + 1,2}: {pts,6:F1} |{bar}");
        }
    }

    // Projections
    var analyzer = new KeeperAnalyzer();
    var recentWkPts = allWeeklyPoints.GetValueOrDefault(recentYear, []);
    var analysis = analyzer.Analyze(match.PlayerId, match.FullName, match.Position, match.Age,
        null, false, seasonHistory, recentWkPts);

    Console.WriteLine();
    Console.WriteLine("-----------------------------------------------------------------------------------");
    Console.WriteLine("  PROJECTIONS & ANALYSIS");
    Console.WriteLine("-----------------------------------------------------------------------------------");
    Console.WriteLine($"  Weighted PPG:        {analysis.WeightedPpg:F1}");
    Console.WriteLine($"  Age-Adjusted PPG:    {analysis.AgeAdjustedPpg:F1} (age factor: {AgingCurve.GetFactor(match.Position, match.Age):F2})");
    Console.WriteLine($"  Projected Season:    {analysis.ProjectedSeasonPoints:F0} pts");
    Console.WriteLine($"  VORP:                {analysis.Vorp:F1} (vs {analysis.ReplacementPpg:F1} replacement PPG)");
    Console.WriteLine($"  Trend:               {analysis.TrendDirection} ({analysis.TrendPerYear:+0.0;-0.0} PPG/year)");
    Console.WriteLine($"  Durability:          {analysis.DurabilityPct:F0}%");
    Console.WriteLine($"  Consistency:         {analysis.ConsistencyScore:F0}/100 (Boom: {analysis.BoomRate:F0}% / Bust: {analysis.BustRate:F0}%)");

    Console.WriteLine();
    Console.WriteLine("===================================================================================");
    return 0;
}

// ======================================================================
// REPORT 4: MATCHUP SCOREBOARD
// ======================================================================
async Task<int> RunMatchupScoreboard(string[] a)
{
    var weekStr = a.Length > 1 ? a[1] : null;
    var leagueId = a.Length > 2 ? a[2] : DefaultLeagueId;

    var league = await client.GetLeagueAsync(leagueId);
    if (league is null) { Console.WriteLine("League not found."); return 1; }

    int week;
    if (weekStr is null)
    {
        var nflState = await client.GetNflStateAsync();
        week = nflState?.Week ?? 1;
        Console.WriteLine($"Using current week: {week}");
    }
    else if (!int.TryParse(weekStr, out week))
    {
        Console.WriteLine("Invalid week number."); return 1;
    }

    Console.WriteLine($"Fetching Week {week} matchups...");

    var scoreboard = await sleeperService.GetWeekScoreboardAsync(leagueId, week);

    Console.WriteLine();
    Console.WriteLine("===================================================================================");
    Console.WriteLine($"  WEEK {week} SCOREBOARD -- {league.Name} ({league.Season})");
    Console.WriteLine("===================================================================================");
    Console.WriteLine();

    foreach (var m in scoreboard.OrderByDescending(m => (m.Team1Points ?? 0) + (m.Team2Points ?? 0)))
    {
        var t1Name = m.Team1TeamName ?? m.Team1DisplayName ?? $"Team {m.Team1RosterId}";
        var t2Name = m.Team2TeamName ?? m.Team2DisplayName ?? $"Team {m.Team2RosterId}";
        var t1Pts = m.Team1Points?.ToString("F2") ?? "0.00";
        var t2Pts = m.Team2Points?.ToString("F2") ?? "0.00";

        var winner = (m.Team1Points ?? 0) >= (m.Team2Points ?? 0) ? "<<<" : "   ";
        var winner2 = (m.Team2Points ?? 0) >= (m.Team1Points ?? 0) ? ">>>" : "   ";

        Console.WriteLine($"  {t1Name,-20} {t1Pts,8} {winner}  vs  {winner2} {t2Pts,-8} {t2Name}");
    }

    Console.WriteLine();
    Console.WriteLine("===================================================================================");
    return 0;
}

