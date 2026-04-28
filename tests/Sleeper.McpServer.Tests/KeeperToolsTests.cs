using FluentAssertions;
using NSubstitute;
using Sleeper.Api.Exceptions;
using Sleeper.Api.NflData.Services;
using Sleeper.McpServer.Tools;

namespace Sleeper.McpServer.Tests;

public class KeeperToolsTests
{
    [Fact]
    public async Task AnalyzeKeepers_ReturnsFriendlyError_WhenRateLimited()
    {
        var analysis = Substitute.For<IAnalysisService>();
        analysis.GetKeeperAnalysisAsync("league", "rob", 3, Arg.Any<CancellationToken>())
            .Returns<Task<KeeperReport>>(_ => throw new SleeperRateLimitException());

        var result = await KeeperTools.AnalyzeKeepers(analysis, "rob", "league");

        result.Should().Be("Error: Sleeper API rate limit exceeded. Try again shortly.");
    }
}