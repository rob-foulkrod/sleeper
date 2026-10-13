using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Sleeper.RosterReport.Copilot;

namespace Sleeper.RosterReport.Tests;

public class CopilotRecapReplayRunnerTests
{
    [Fact]
    public void SeedReplayContext_CopiesOnlyPrecedingRecapsAndHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sleeper-replay-{Guid.NewGuid():N}");
        var canonical = Path.Combine(root, "canonical");
        var context = Path.Combine(root, "context");

        try
        {
            Directory.CreateDirectory(canonical);
            File.WriteAllText(Path.Combine(canonical, "week-01.md"), "week one");
            File.WriteAllText(Path.Combine(canonical, "week-02.md"), "week two");
            File.WriteAllText(Path.Combine(canonical, "week-03.md"), "week three");
            const string history = """
                {
                  "Season": 2025,
                  "Snapshots": [
                    { "Week": 1 },
                    { "Week": 3 }
                  ]
                }
                """;
            File.WriteAllText(Path.Combine(canonical, "power-history.json"), history);
            File.WriteAllText(Path.Combine(canonical, "team-name-history.json"), history);

            CopilotRecapReplayRunner.SeedReplayContext(canonical, context, startWeek: 3);

            File.Exists(Path.Combine(context, "week-01.md")).Should().BeTrue();
            File.Exists(Path.Combine(context, "week-02.md")).Should().BeTrue();
            File.Exists(Path.Combine(context, "week-03.md")).Should().BeFalse();
            File.Exists(Path.Combine(context, "power-history.json")).Should().BeTrue();
            File.Exists(Path.Combine(context, "team-name-history.json")).Should().BeTrue();
            File.ReadAllText(Path.Combine(context, "power-history.json")).Should().Contain("\"Week\": 1");
            File.ReadAllText(Path.Combine(context, "power-history.json")).Should().NotContain("\"Week\": 3");
            File.ReadAllText(Path.Combine(context, "team-name-history.json")).Should().Contain("\"Week\": 1");
            File.ReadAllText(Path.Combine(context, "team-name-history.json")).Should().NotContain("\"Week\": 3");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AgentSettings_UseQualityOrientedDefaults()
    {
        var configuration = new ConfigurationBuilder().Build();

        var settings = CopilotAgentSettings.FromConfiguration(configuration);

        settings.WriterModel.Should().Be("claude-sonnet-5.5");
        settings.LeagueWriterModel.Should().Be("claude-opus-5.5");
        settings.ProofreaderModel.Should().Be("gpt-5-mini");
        settings.EvaluatorModel.Should().Be("gpt-5.6-sol");
    }

    [Fact]
    public void AgentSettings_AllowEveryModelRoleToBeOverridden()
    {
        var values = new Dictionary<string, string?>
        {
            ["Copilot:WriterModel"] = "game-model",
            ["Copilot:LeagueWriterModel"] = "league-model",
            ["Copilot:ProofreaderModel"] = "proofreader-model",
            ["Copilot:EvaluatorModel"] = "evaluator-model"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var settings = CopilotAgentSettings.FromConfiguration(configuration);

        settings.WriterModel.Should().Be("game-model");
        settings.LeagueWriterModel.Should().Be("league-model");
        settings.ProofreaderModel.Should().Be("proofreader-model");
        settings.EvaluatorModel.Should().Be("evaluator-model");
    }

    [Fact]
    public void ResolveModelSelection_UsesAlternateLiveCopilotModel_WhenPreferredIsUnavailable()
    {
        var settings = new CopilotAgentSettings(
            WriterModel: "claude-sonnet-5.5",
            LeagueWriterModel: "claude-opus-5.5",
            ProofreaderModel: "gpt-5-mini",
            EvaluatorModel: "gpt-5.6-sol",
            ReasoningEffort: "medium",
            TimeoutSeconds: 180,
            MaxConcurrency: 3);

        var available = new[]
        {
            "fallback-writer",
            "fallback-league",
            "fallback-proofreader",
            "fallback-evaluator"
        };

        var selection = CopilotAgentHost.ResolveModelSelection(settings, available, requireEvaluatorModel: false);

        selection.WriterModel.Should().Be("fallback-writer");
        selection.LeagueWriterModel.Should().Be("fallback-writer");
        selection.ProofreaderModel.Should().Be("fallback-writer");
        selection.EvaluatorModel.Should().Be("fallback-writer");
    }

    [Fact]
    public void ResolveModelSelection_PrefersAnotherConfiguredCopilotModel_WhenRolePreferenceIsUnavailable()
    {
        var settings = new CopilotAgentSettings(
            WriterModel: "writer-live",
            LeagueWriterModel: "league-unavailable",
            ProofreaderModel: "proofreader-live",
            EvaluatorModel: "evaluator-live",
            ReasoningEffort: "medium",
            TimeoutSeconds: 180,
            MaxConcurrency: 3);

        var selection = CopilotAgentHost.ResolveModelSelection(
            settings,
            ["auto", "proofreader-live", "writer-live", "evaluator-live"],
            requireEvaluatorModel: false);

        selection.WriterModel.Should().Be("writer-live");
        selection.LeagueWriterModel.Should().Be("writer-live");
        selection.ProofreaderModel.Should().Be("proofreader-live");
    }

    [Fact]
    public void ResolveModelSelection_ThrowsWhenCopilotReportsNoModels()
    {
        var settings = CopilotAgentSettings.FromConfiguration(new ConfigurationBuilder().Build());

        var act = () => CopilotAgentHost.ResolveModelSelection(settings, [], requireEvaluatorModel: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no available models*");
    }

    [Fact]
    public void ResolveModelSelection_RequiresSeparateEvaluatorForBlindReplay()
    {
        var settings = new CopilotAgentSettings(
            WriterModel: "writer-live",
            LeagueWriterModel: "writer-live",
            ProofreaderModel: "writer-live",
            EvaluatorModel: "evaluator-unavailable",
            ReasoningEffort: "medium",
            TimeoutSeconds: 180,
            MaxConcurrency: 3);

        var act = () => CopilotAgentHost.ResolveModelSelection(
            settings,
            ["writer-live"],
            requireEvaluatorModel: true);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*requires another available model*");
    }
}
