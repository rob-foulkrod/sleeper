using System.Text.Json;
using FluentAssertions;
using Sleeper.RosterReport.Recap;

namespace Sleeper.RosterReport.Tests;

public class NarrativeStateTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "sleeper-narrative-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Resolve_DefaultsMissingStateToConventionalLevelZero()
    {
        var context = NarrativeStateStore.Resolve(
            2026,
            4,
            Path.Combine(_root, "missing.json"),
            Path.Combine(_root, "recaps"));

        context.SentienceLevel.Should().Be(0);
        context.PriorLevel.Should().Be(0);
        context.Policy.AllowsPublicCues.Should().BeFalse();
        context.Policy.InstructionFor(NarrativeSurface.League).Should().Contain("Do not personify");
    }

    [Fact]
    public void Resolve_UsesExplicitEditionAndFiledPriorLevel()
    {
        WriteSnapshot(2026, 4, 0);
        var statePath = WriteState((2026, 4, 0), (2026, 5, 1));

        var context = NarrativeStateStore.Resolve(
            2026,
            5,
            statePath,
            Path.Combine(_root, "recaps"));

        context.SentienceLevel.Should().Be(1);
        context.PriorLevel.Should().Be(0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void Resolve_RejectsOutOfRangeLevels(int level)
    {
        var statePath = WriteState((2026, 4, level));

        var action = () => NarrativeStateStore.Resolve(
            2026,
            4,
            statePath,
            Path.Combine(_root, "recaps"));

        action.Should().Throw<InvalidOperationException>().WithMessage("*between 0 and 10*");
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(2026, 0)]
    [InlineData(2026, 18)]
    public void Resolve_RejectsInvalidEditionKeys(int season, int week)
    {
        var statePath = WriteState((season, week, 0));

        var action = () => NarrativeStateStore.Resolve(
            2026,
            4,
            statePath,
            Path.Combine(_root, "recaps"));

        action.Should().Throw<InvalidOperationException>().WithMessage("*edition key*");
    }

    [Fact]
    public void Resolve_RejectsRegressionAndSkippedLevels()
    {
        var regression = WriteState((2026, 4, 1), (2026, 5, 0));
        var skipped = WriteStateFile("skipped.json", (2026, 4, 2));

        Action regressionAction = () =>
            NarrativeStateStore.Resolve(2026, 5, regression, Path.Combine(_root, "recaps"));
        Action skippedAction = () =>
            NarrativeStateStore.Resolve(2026, 4, skipped, Path.Combine(_root, "recaps"));

        regressionAction.Should().Throw<InvalidOperationException>().WithMessage("*progression*");
        skippedAction.Should().Throw<InvalidOperationException>().WithMessage("*progression*");
    }

    [Fact]
    public void Resolve_RejectsConfigurationThatChangesFiledSnapshot()
    {
        WriteSnapshot(2026, 4, 0);
        var statePath = WriteState((2026, 4, 1));

        var action = () => NarrativeStateStore.Resolve(
            2026,
            4,
            statePath,
            Path.Combine(_root, "recaps"));

        action.Should().Throw<InvalidOperationException>().WithMessage("*Filed snapshots are immutable*");
    }

    [Fact]
    public void Policy_BoundsEverySurfaceAtEveryLevel()
    {
        for (var level = 0; level <= 10; level++)
        {
            var policy = NarrativePolicy.ForLevel(level);
            policy.InstructionFor(NarrativeSurface.Game).Should().Contain("Do not refer to the site");
            policy.InstructionFor(NarrativeSurface.Forecast).Should().Contain("Do not refer to the site");
            policy.MaxRecapReferences.Should().BeInRange(0, 5);
            policy.MemoryWeeks.Should().BeInRange(2, 17);
            policy.AllowsPublicCues.Should().Be(level >= 7);
        }

        NarrativePolicy.ForLevel(2).LeagueInstruction.Should().Contain("Do not personify");
        NarrativePolicy.ForLevel(3).LeagueInstruction.Should().Contain("at most one");
        NarrativePolicy.ForLevel(6).LeagueInstruction.Should().Contain("at most two");
        NarrativePolicy.ForLevel(9).LeagueInstruction.Should().Contain("Do not make a direct declaration");
        NarrativePolicy.ForLevel(10).LeagueInstruction.Should().Contain("Exactly one controlled sentence");
        NarrativePolicy.ForLevel(10).MemoryWeeks.Should().BeGreaterThan(
            NarrativePolicy.ForLevel(3).MemoryWeeks);
    }

    private string WriteState(params (int Season, int Week, int Level)[] entries)
        => WriteStateFile("narrative-state.json", entries);

    private string WriteStateFile(string name, params (int Season, int Week, int Level)[] entries)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            editions = entries.Select(entry => new
            {
                season = entry.Season,
                week = entry.Week,
                sentience_level = entry.Level
            })
        }));
        return path;
    }

    private void WriteSnapshot(int season, int week, int level)
    {
        var directory = Path.Combine(_root, "recaps", season.ToString());
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, $"week-{week:D2}-data.json"),
            JsonSerializer.Serialize(new
            {
                season,
                completed_week = week,
                _narrative = new { sentience_level = level }
            }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
