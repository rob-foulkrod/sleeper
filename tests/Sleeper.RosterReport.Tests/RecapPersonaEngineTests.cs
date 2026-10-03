using Sleeper.RosterReport.Recap;
using Xunit;

namespace Sleeper.RosterReport.Tests;

public class RecapPersonaEngineTests
{
    [Fact]
    public void DetermineStyle_IdentifiesStatementWin_WhenMarginIs30OrMore()
    {
        var card = CreateSampleCard(margin: 35.0m, isBlowout: true);
        var style = RecapPersonaEngine.DetermineStyle(card);

        Assert.Equal(GameNarrativeAngle.StatementWin, style.Angle);
        Assert.Equal(ExcitementLevel.Notable, style.Drama.Level);
        Assert.Contains("Statement Win", style.AngleInstruction);
        Assert.Contains("2-3 compact paragraphs", style.StructureInstruction);
    }

    [Fact]
    public void DetermineStyle_IdentifiesCloseFinish_WhenMarginIs5OrLess()
    {
        var card = CreateSampleCard(margin: 3.5m, isBlowout: false);
        var style = RecapPersonaEngine.DetermineStyle(card);

        Assert.Equal(GameNarrativeAngle.CloseFinish, style.Angle);
        Assert.Equal(ExcitementLevel.Major, style.Drama.Level);
        Assert.Contains("Close Finish", style.AngleInstruction);
        Assert.Contains("do not invent late-game chronology", style.Drama.RecommendedLead);
    }

    [Fact]
    public void DetermineStyle_IdentifiesResultFlippingDecision_WhenBenchFlipIsPossible()
    {
        var card = CreateSampleCard(margin: 12.0m, isBlowout: false, benchFlipPossible: true);
        var style = RecapPersonaEngine.DetermineStyle(card);

        Assert.Equal(GameNarrativeAngle.ResultFlippingBenchDecision, style.Angle);
        Assert.Equal(ExcitementLevel.Major, style.Drama.Level);
        Assert.Contains("Result-Flipping Bench Decision", style.AngleInstruction);
        Assert.Contains("result-flipping lineup fact", style.Drama.RecommendedLead);
    }

    private static MatchupFactCard CreateSampleCard(decimal margin, bool isBlowout, bool benchFlipPossible = false)
    {
        return new MatchupFactCard(
            Week: 1,
            IsTie: false,
            WinnerRealName: "Rob",
            WinnerTeamName: "Unstoppable Farce",
            WinnerScore: 125.0m,
            WinnerScoreRankInWeek: 2,
            WinnerNewRecord: "1-0",
            WinnerStandingsShift: "Climbed from #4 to #2",
            WinnerStreak: "W1",
            LoserRealName: "Brian",
            LoserTeamName: "First in and First Out?",
            LoserScore: 125.0m - margin,
            LoserScoreRankInWeek: 6,
            LoserNewRecord: "0-1",
            LoserStandingsShift: "Dropped from #2 to #6",
            LoserStreak: "L1",
            Margin: margin,
            IsBlowout: isBlowout,
            TopScorerInGame: "Josh Allen (32.4 pts)",
            WinnerTopScorer: "Josh Allen (32.4 pts)",
            LoserTopScorer: "CeeDee Lamb (21.0 pts)",
            LineupOptimalityGapExceedsThreshold: false,
            LineupOptimalityNote: "Omitted",
            LoserBenchFlipPossible: benchFlipPossible,
            LoserBenchFlipDetails: benchFlipPossible ? "Bench flip possible" : "None",
            ExactConsequenceCloser: "Rob moves to 1-0",
            PriorForecastGradingNote: null
        );
    }
}
