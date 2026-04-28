using FluentAssertions;
using NSubstitute;
using Sleeper.Api.Models;
using Sleeper.Api.NflData.Services;
using Sleeper.McpServer.Tools;

namespace Sleeper.McpServer.Tests;

public class PlayerToolsTests
{
    [Fact]
    public async Task SearchPlayers_ReturnsValidationError_WhenQueryIsBlank()
    {
        var analysis = Substitute.For<IAnalysisService>();

        var result = await PlayerTools.SearchPlayers(analysis, "   ");

        result.Should().Be("Error: Query is required.");
        await analysis.DidNotReceiveWithAnyArgs().SearchPlayersAsync(default!, default, default);
    }

    [Fact]
    public async Task SearchPlayers_PassesCancellationTokenToAnalysisService()
    {
        var analysis = Substitute.For<IAnalysisService>();
        using var cts = new CancellationTokenSource();
        var player = new Player("p1", "Patrick", "Mahomes", "QB", "KC", 30, "Active", 15, null, null, ["QB"], null, null, null, "patrickmahomes", "patrick", "mahomes", 1, null, null, "nfl", null, null, null, null, null, null, null, null, null, null);
        analysis.SearchPlayersAsync("mahomes", "QB", cts.Token).Returns([player]);

        var result = await PlayerTools.SearchPlayers(analysis, "mahomes", "QB", cts.Token);

        result.Should().Contain("Patrick Mahomes");
        await analysis.Received(1).SearchPlayersAsync("mahomes", "QB", cts.Token);
    }

    [Fact]
    public async Task SearchPlayers_DoesNotSwallowCancellation()
    {
        var analysis = Substitute.For<IAnalysisService>();
        using var cts = new CancellationTokenSource();
        analysis.SearchPlayersAsync("mahomes", null, cts.Token)
            .Returns<Task<List<Player>>>(_ => throw new OperationCanceledException(cts.Token));

        Func<Task> act = async () => await PlayerTools.SearchPlayers(analysis, "mahomes", ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}