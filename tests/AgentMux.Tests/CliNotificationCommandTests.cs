using System.Text.Json;
using AgentMux.Cli;
using AgentMux.Core.Ipc;

namespace AgentMux.Tests;

public sealed class CliNotificationCommandTests
{
    [Theory]
    [InlineData("list")]
    [InlineData("ls")]
    public void NotificationsListParsesLimit(string command)
    {
        var request = Program.ParseNotificationsRequestForTests([command, "--limit", "20"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.NotificationsList, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(20, parameters.GetProperty("limit").GetInt32());
    }

    [Fact]
    public void NotificationsListAllowsDefaultLimit()
    {
        var request = Program.ParseNotificationsRequestForTests(["list"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.NotificationsList, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.True(parameters.TryGetProperty("limit", out var limit));
        Assert.Equal(JsonValueKind.Null, limit.ValueKind);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("many")]
    public void NotificationsListRejectsInvalidLimit(string limit)
    {
        var request = Program.ParseNotificationsRequestForTests(["list", "--limit", limit], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux notifications list [--limit <count>]", error);
    }

    [Theory]
    [InlineData("clear", AgentMuxMethods.NotificationsClear)]
    [InlineData("clear-all", AgentMuxMethods.NotificationsClear)]
    [InlineData("jump-latest", AgentMuxMethods.NotificationsJumpLatest)]
    [InlineData("jump", AgentMuxMethods.NotificationsJumpLatest)]
    [InlineData("open-latest", AgentMuxMethods.NotificationsJumpLatest)]
    public void NotificationsActionsParse(string command, string method)
    {
        var request = Program.ParseNotificationsRequestForTests([command], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(method, request.Method);
    }

    [Fact]
    public void NotificationsRejectsMissingAction()
    {
        var request = Program.ParseNotificationsRequestForTests([], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux notifications <list|clear|jump-latest>", error);
    }

    [Fact]
    public void NotificationsRejectsUnknownAction()
    {
        var request = Program.ParseNotificationsRequestForTests(["dismiss"], out var error);

        Assert.Null(request);
        Assert.Equal("Unknown notifications command: dismiss", error);
    }
}
