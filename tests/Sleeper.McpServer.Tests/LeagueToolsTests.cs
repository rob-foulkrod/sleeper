using FluentAssertions;
using NSubstitute;
using Sleeper.Api;
using Sleeper.McpServer.Tools;

namespace Sleeper.McpServer.Tests;

public class LeagueToolsTests
{
    [Theory]
    [InlineData("watch")]
    [InlineData("")]
    public async Task GetTrendingPlayers_ReturnsValidationError_WhenTypeIsInvalid(string type)
    {
        var client = Substitute.For<ISleeperClient>();

        var result = await LeagueTools.GetTrendingPlayers(client, type, 10);

        result.Should().Be("Error: Type must be 'add' or 'drop'.");
        await client.DidNotReceiveWithAnyArgs().GetTrendingPlayersAsync(default!, default!, default, default, default);
    }

    [Fact]
    public async Task GetLeagueRankings_ReturnsValidationError_WhenTopIsNotPositive()
    {
        var analysis = Substitute.For<Sleeper.Api.NflData.Services.IAnalysisService>();

        var result = await LeagueTools.GetLeagueRankings(analysis, 2025, top: 0);

        result.Should().Be("Error: Top must be greater than zero.");
    }
}