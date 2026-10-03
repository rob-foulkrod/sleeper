using System.Text;

namespace Sleeper.RosterReport.Recap;

public enum GameNarrativeAngle
{
    StakesResult,
    ResultFlippingBenchDecision,
    CloseFinish,
    StatementWin,
    ScoringSurge,
    LowOutputResult,
    StraightRecap
}

public enum ExcitementLevel
{
    Routine = 1,
    Notable = 2,
    Major = 3,
    Defining = 4
}

public sealed record MatchupDramaProfile(
    ExcitementLevel Level,
    string PrimarySignal,
    IReadOnlyList<string> SecondarySignals,
    string RecommendedLead,
    int HumorAllowance,
    bool ScopedSuperlativeAllowed,
    string ConsequenceWeight)
{
    public string RenderPromptBlock()
    {
        var secondary = SecondarySignals.Count == 0
            ? "none"
            : string.Join("; ", SecondarySignals);
        return $"""
            <drama_profile>
            Excitement level: {(int)Level} — {Level}
            Primary story signal: {PrimarySignal}
            Secondary signals: {secondary}
            Recommended lead: {RecommendedLead}
            Humor allowance: {HumorAllowance} sharp observation(s) maximum; zero is acceptable
            Scoped superlative allowed: {(ScopedSuperlativeAllowed ? "yes, but only with the exact scope supplied by the facts" : "no")}
            Consequence weight: {ConsequenceWeight}
            Follow this calibration. Do not raise or lower the excitement level on your own.
            </drama_profile>
            """;
    }
}

public sealed record MatchupNarrativeStyle(
    GameNarrativeAngle Angle,
    MatchupDramaProfile Drama,
    string AngleInstruction,
    string StructureInstruction
);

public sealed record WeekDramaProfile(
    ExcitementLevel Level,
    string HeadlineEvent,
    IReadOnlyList<string> RankedEvents)
{
    public string RenderPromptBlock()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<week_drama_profile>");
        sb.AppendLine($"Week excitement level: {(int)Level} — {Level}");
        sb.AppendLine($"Lead story: {HeadlineEvent}");
        sb.AppendLine("Ranked supported events:");
        foreach (var item in RankedEvents)
            sb.AppendLine($"- {item}");
        sb.AppendLine("Lead the Intro with the first event. Use later events only when they advance or complicate the weekly thesis.");
        sb.AppendLine("</week_drama_profile>");
        return sb.ToString();
    }
}

/// <summary>
/// Selects fact-grounded narrative emphasis and proportional excitement.
/// </summary>
public static class RecapPersonaEngine
{
    public static MatchupNarrativeStyle DetermineStyle(MatchupFactCard card)
        => DetermineStyle(card, null);

    public static MatchupNarrativeStyle DetermineStyle(MatchupFactCard card, GameRecap? game)
    {
        var drama = DetermineDrama(card, game);
        var angle = DetermineAngle(card, game);

        return new MatchupNarrativeStyle(
            angle,
            drama,
            GetAngleInstruction(angle),
            GetStructureInstruction(drama));
    }

    public static WeekDramaProfile DetermineWeekProfile(
        RecapEnvelope env,
        IReadOnlyList<(GameRecap Game, MatchupFactCard Facts, MatchupDramaProfile Drama)> games)
    {
        var ranked = games
            .OrderByDescending(item => item.Drama.Level)
            .ThenByDescending(item => item.Game.StoryImportance)
            .ThenBy(item => item.Facts.Margin)
            .ThenByDescending(item => item.Facts.WinnerScore)
            .Select(item =>
                $"{item.Facts.WinnerTeamName} over {item.Facts.LoserTeamName}: {item.Drama.PrimarySignal}; " +
                $"{item.Facts.ExactConsequenceCloser}")
            .ToList();

        if (ranked.Count == 0)
            return new WeekDramaProfile(ExcitementLevel.Routine, "No completed matchups were supplied.", []);

        var level = games.Max(item => item.Drama.Level);
        return new WeekDramaProfile(level, ranked[0], ranked);
    }

    private static MatchupDramaProfile DetermineDrama(MatchupFactCard card, GameRecap? game)
    {
        var signals = new List<string>();
        var importance = game?.StoryImportance ?? 1;
        var importanceReason = game?.StoryImportanceReason;
        var isDefiningStakes = importance >= 5
            || game?.SeasonContext == "playoffs_winners"
            || string.Equals(game?.PlayoffRound, "Championship", StringComparison.OrdinalIgnoreCase);
        var isMajorStakes = importance >= 4;
        var forecastUpset = card.PriorForecastGradingNote?.Contains("INCORRECTLY", StringComparison.Ordinal) == true;

        if (card.LoserBenchFlipPossible)
            signals.Add("a verified optimal lineup would have changed the winner");
        if (card.Margin <= 5m)
            signals.Add($"the margin was {card.Margin:F2} points");
        if (card.IsBlowout)
            signals.Add($"the margin was {card.Margin:F2} points");
        if (card.WinnerScoreRankInWeek == 1)
            signals.Add("the winner posted the week's highest score");
        if (forecastUpset)
            signals.Add("the result overturned the supplied prior forecast");
        if (!string.IsNullOrWhiteSpace(importanceReason))
            signals.Add(importanceReason);

        ExcitementLevel level;
        string primary;
        string lead;
        string consequence;

        if (isDefiningStakes)
        {
            level = ExcitementLevel.Defining;
            primary = importanceReason ?? "the result carried defining playoff or season stakes";
            lead = "Lead with the consequence, then state the result.";
            consequence = "The verified season consequence is the center of the story.";
        }
        else if (isMajorStakes || card.Margin <= 5m || card.LoserBenchFlipPossible || forecastUpset)
        {
            level = ExcitementLevel.Major;
            primary = card.LoserBenchFlipPossible
                ? "a verified result-flipping lineup decision"
                : card.Margin <= 5m
                    ? $"a {card.Margin:F2}-point finish"
                    : forecastUpset
                        ? "a result that overturned the supplied forecast"
                        : importanceReason ?? "meaningful standings or rivalry stakes";
            lead = card.LoserBenchFlipPossible
                ? "Lead with the result-flipping lineup fact."
                : card.Margin <= 5m
                    ? "Lead with the exact margin; do not invent late-game chronology."
                    : "Lead with the verified stakes or upset.";
            consequence = "Make the standings, streak, playoff, or keeper consequence prominent.";
        }
        else if (card.IsBlowout || card.WinnerScoreRankInWeek <= 2 || importance >= 3)
        {
            level = ExcitementLevel.Notable;
            primary = card.IsBlowout
                ? $"a decisive {card.Margin:F2}-point win"
                : card.WinnerScoreRankInWeek <= 2
                    ? $"the winner's score ranked #{card.WinnerScoreRankInWeek} this week"
                    : importanceReason ?? "a useful standings consequence";
            lead = card.WinnerScoreRankInWeek <= 2
                ? "Lead with the score or defining player performance."
                : "Lead with the result and its clearest consequence.";
            consequence = "Explain why the result matters next without overstating it.";
        }
        else
        {
            level = ExcitementLevel.Routine;
            primary = "a straightforward weekly result";
            lead = "Lead with the result or best player.";
            consequence = "Keep the consequence factual and brief.";
        }

        var secondary = signals
            .Where(signal => !primary.Contains(signal, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        return new MatchupDramaProfile(
            level,
            primary,
            secondary,
            lead,
            level switch
            {
                ExcitementLevel.Routine => 0,
                ExcitementLevel.Notable => 1,
                _ => 2
            },
            card.WinnerScoreRankInWeek == 1 || isDefiningStakes,
            consequence);
    }

    private static GameNarrativeAngle DetermineAngle(MatchupFactCard card, GameRecap? game)
    {
        if ((game?.StoryImportance ?? 0) >= 4
            || (game is not null && game.SeasonContext != "regular"))
            return GameNarrativeAngle.StakesResult;
        if (card.LoserBenchFlipPossible)
            return GameNarrativeAngle.ResultFlippingBenchDecision;
        if (card.Margin <= 5m)
            return GameNarrativeAngle.CloseFinish;
        if (card.IsBlowout)
            return GameNarrativeAngle.StatementWin;
        if (card.WinnerScoreRankInWeek <= 4 && card.LoserScoreRankInWeek <= 4)
            return GameNarrativeAngle.ScoringSurge;
        if (card.WinnerScoreRankInWeek >= 5 && card.LoserScoreRankInWeek >= 5)
            return GameNarrativeAngle.LowOutputResult;
        return GameNarrativeAngle.StraightRecap;
    }

    private static string GetAngleInstruction(GameNarrativeAngle angle) => angle switch
    {
        GameNarrativeAngle.StakesResult =>
            "NARRATIVE ANGLE (Stakes Result): Make the verified playoff, standings, streak, record, rivalry, or keeper consequence the organizing idea.",
        GameNarrativeAngle.ResultFlippingBenchDecision =>
            "NARRATIVE ANGLE (Result-Flipping Bench Decision): Explain the eligible start/sit alternative that would have changed the winner. Critique the result without inventing the owner's reasoning.",
        GameNarrativeAngle.CloseFinish =>
            "NARRATIVE ANGLE (Close Finish): Use the exact margin to create tension. Do not invent when the lead changed or which NFL play decided it.",
        GameNarrativeAngle.StatementWin =>
            "NARRATIVE ANGLE (Statement Win): Show the scale of the verified margin and the production that created it without pretending the winner led throughout.",
        GameNarrativeAngle.ScoringSurge =>
            "NARRATIVE ANGLE (Scoring Surge): Celebrate the verified production from both teams, then explain the separation.",
        GameNarrativeAngle.LowOutputResult =>
            "NARRATIVE ANGLE (Low-Output Result): Be concise and clear about which usable production separated two modest totals.",
        _ =>
            "NARRATIVE ANGLE (Straight Recap): Center the result, the most important player contribution, and the concrete consequence."
    };

    private static string GetStructureInstruction(MatchupDramaProfile drama) =>
        $"FORMAT & FLOW: Start with a specific bold headline and write 2-3 compact paragraphs. " +
        $"{drama.RecommendedLead} State the result within the first two sentences. " +
        "Vary the paragraph structure from adjacent matchup sections; do not mechanically march through every fact.";
}
