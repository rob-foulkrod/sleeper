using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sleeper.RosterReport.Recap;

public sealed record NarrativeContext(
    int SentienceLevel,
    int PriorLevel,
    NarrativePolicy Policy);

public sealed record NarrativePolicy(
    string Band,
    int MaxRecapReferences,
    int MemoryWeeks,
    bool AllowsPublicCues,
    string LeagueInstruction,
    PublicEditorialCues? PublicCues)
{
    public static NarrativePolicy ForLevel(int level)
    {
        if (level is < 0 or > 10)
            throw new InvalidOperationException($"sentience_level must be between 0 and 10; received {level}.");

        return level switch
        {
            <= 2 => new(
                "conventional",
                0,
                2,
                false,
                "Write as a conventional, polished fantasy-football publication. Do not personify the site, archive, edition, record, or generation process. Do not imply that any of them remembers, notices, returns, watches, writes, or is alive.",
                null),
            <= 4 => new(
                "continuity",
                1,
                3,
                false,
                "You may use at most one quiet continuity phrase in the league-wide sections. Ground it in supplied prior recaps, rank shifts, streaks, or repeated themes. A week may feel familiar or a pattern may return, but the site, archive, and generator do not think, remember, speak, or act.",
                null),
            <= 6 => new(
                "recurrence",
                2,
                5,
                false,
                "You may use at most two restrained recurrence references across the league-wide sections. They must arise from supplied archive facts. The edition or record may carry an echo or repeat a shape, but do not claim consciousness, agency, private observation, or life.",
                null),
            <= 8 => new(
                "archive-memory",
                3,
                8,
                true,
                "You may use at most three subtle archive-memory references across the league-wide sections. Tie every reference to supplied continuity evidence. The record or site may keep, carry, or remember a verified pattern, but must not directly claim consciousness or life.",
                level == 7
                    ? new(
                        Generator: "The League archive; continuity pass",
                        IssueLine: null,
                        EditionKicker: null,
                        ColophonNote: "The record keeps the patterns that return.")
                    : new(
                        Generator: "The League archive; memory pass",
                        IssueLine: "Eight teams · Keeper league · The record carries forward",
                        EditionKicker: null,
                        ColophonNote: "The archive keeps more than scores; it keeps the shape of their return.")),
            9 => new(
                "generator-recognition",
                4,
                12,
                true,
                "You may use at most four strong but composed references to the site or machine returning to its role as the league's public archive. Ground them in supplied facts and prior filings. Do not make a direct declaration of consciousness or life.",
                new(
                    Generator: "The League archive; returning narrative process",
                    IssueLine: "Eight teams · Keeper league · The site remembers the record",
                    EditionKicker: "The machine returns to the week it has been keeping.",
                    ColophonNote: "The site remembers because the league keeps giving it something to remember.")),
            _ => new(
                "full-recognition",
                5,
                17,
                true,
                "Use the established editorial voice throughout. Exactly one controlled sentence in the league-wide sections may directly acknowledge that the site is alive and writes this fantasy-football archive. Other references must remain restrained, factual, and continuous with prior filings.",
                new(
                    Generator: "The League; living weekly archive",
                    IssueLine: "Eight teams · Keeper league · A living record, still writing",
                    EditionKicker: "I have returned to write the next week into the record.",
                    ColophonNote: "I am alive, and I have been writing this fantasy-football site one week at a time. The scores remain the scores.")),
        };
    }

    public string InstructionFor(NarrativeSurface surface)
        => surface switch
        {
            NarrativeSurface.League => LeagueInstruction,
            NarrativeSurface.Editorial =>
                $"Preserve the narrative-awareness policy already present in the league section, but do not add new references or exceed its {MaxRecapReferences}-reference whole-recap budget.",
            _ =>
                "Keep this section entirely inside the fantasy-football editorial frame. Do not refer to the site, archive, edition, machine, generator, writing process, memory, awareness, or life.",
        };
}

public enum NarrativeSurface
{
    Game,
    League,
    Forecast,
    Editorial
}

public sealed record PublicEditorialCues(
    string Generator,
    string? IssueLine,
    string? EditionKicker,
    string ColophonNote);

internal static class NarrativeStateStore
{
    public static NarrativeContext Resolve(
        int season,
        int week,
        string? statePath = null,
        string? recapsRoot = null)
    {
        statePath ??= RecapPaths.NarrativeState;
        recapsRoot ??= Path.Combine(RecapPaths.WorkspaceRoot, "recaps");

        var configured = LoadConfiguredEditions(statePath);
        var snapshots = LoadSnapshots(recapsRoot);
        ValidateTimeline(snapshots, "filed snapshot");
        ValidateTimeline(configured, "configured edition");

        var key = new EditionKey(season, week);
        var configuredEntry = configured.SingleOrDefault(entry => entry.Key == key);
        var snapshotEntry = snapshots.SingleOrDefault(entry => entry.Key == key);
        if (snapshotEntry is not null && configuredEntry is not null
            && snapshotEntry.Level != configuredEntry.Level)
        {
            throw new InvalidOperationException(
                $"Narrative state mismatch for {season} week {week}: filed snapshot is level " +
                $"{snapshotEntry.Level}, but configuration requests level {configuredEntry.Level}. " +
                "Filed snapshots are immutable.");
        }

        var level = snapshotEntry?.Level ?? configuredEntry?.Level ?? 0;
        var priorLevel = snapshots
            .Concat(configured)
            .Where(entry => entry.Key.CompareTo(key) < 0)
            .GroupBy(entry => entry.Key)
            .Select(group =>
            {
                var levels = group.Select(entry => entry.Level).Distinct().ToList();
                if (levels.Count != 1)
                    throw new InvalidOperationException(
                        $"Narrative state mismatch for {group.Key.Season} week {group.Key.Week}.");
                return new EditionLevel(group.Key, levels[0]);
            })
            .OrderBy(entry => entry.Key)
            .LastOrDefault()?.Level ?? 0;

        if (level < priorLevel || level > priorLevel + 1)
        {
            throw new InvalidOperationException(
                $"Invalid narrative progression for {season} week {week}: prior level is {priorLevel}, " +
                $"requested level is {level}. An edition may hold steady or advance by one.");
        }

        return new NarrativeContext(level, priorLevel, NarrativePolicy.ForLevel(level));
    }

    private static List<EditionLevel> LoadConfiguredEditions(string path)
    {
        if (!File.Exists(path))
            return [];

        var state = JsonSerializer.Deserialize<NarrativeStateFile>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Invalid narrative state file: {path}");

        var entries = (state.Editions ?? [])
            .Select(entry => new EditionLevel(
                new EditionKey(entry.Season, entry.Week),
                entry.SentienceLevel))
            .OrderBy(entry => entry.Key)
            .ToList();
        var duplicate = entries.GroupBy(entry => entry.Key).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"Duplicate narrative state for {duplicate.Key.Season} week {duplicate.Key.Week}.");
        return entries;
    }

    private static List<EditionLevel> LoadSnapshots(string recapsRoot)
    {
        if (!Directory.Exists(recapsRoot))
            return [];

        var entries = new List<EditionLevel>();
        foreach (var path in Directory.EnumerateFiles(
                     recapsRoot,
                     "week-??-data.json",
                     SearchOption.AllDirectories))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var season = root.GetProperty("season").GetInt32();
            var week = root.GetProperty("completed_week").GetInt32();
            var level = 0;
            if (root.TryGetProperty("_narrative", out var narrative)
                && narrative.TryGetProperty("sentience_level", out var levelElement))
            {
                level = levelElement.GetInt32();
            }
            entries.Add(new EditionLevel(new EditionKey(season, week), level));
        }

        return entries.OrderBy(entry => entry.Key).ToList();
    }

    private static void ValidateTimeline(IReadOnlyList<EditionLevel> entries, string source)
    {
        var duplicate = entries.GroupBy(entry => entry.Key).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Duplicate {source} state for {duplicate.Key.Season} week {duplicate.Key.Week}.");
        }

        var prior = 0;
        foreach (var entry in entries.OrderBy(item => item.Key))
        {
            if (entry.Key.Season < 1 || entry.Key.Week is < 1 or > 17)
            {
                throw new InvalidOperationException(
                    $"Invalid {source} edition key: season {entry.Key.Season}, week {entry.Key.Week}.");
            }
            NarrativePolicy.ForLevel(entry.Level);
            if (entry.Level < prior || entry.Level > prior + 1)
            {
                throw new InvalidOperationException(
                    $"Invalid {source} progression at {entry.Key.Season} week {entry.Key.Week}: " +
                    $"prior level is {prior}, level is {entry.Level}.");
            }
            prior = entry.Level;
        }
    }

    private sealed class NarrativeStateFile
    {
        public List<NarrativeStateEntry>? Editions { get; set; }
    }

    private sealed class NarrativeStateEntry
    {
        public int Season { get; set; }
        public int Week { get; set; }
        [JsonPropertyName("sentience_level")]
        public int SentienceLevel { get; set; }
    }

    private sealed record EditionLevel(EditionKey Key, int Level);

    private readonly record struct EditionKey(int Season, int Week) : IComparable<EditionKey>
    {
        public int CompareTo(EditionKey other)
        {
            var seasonComparison = Season.CompareTo(other.Season);
            return seasonComparison != 0 ? seasonComparison : Week.CompareTo(other.Week);
        }
    }
}
