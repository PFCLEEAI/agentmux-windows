using AgentMux.Core.Notifications;

namespace AgentMux.Tests;

public sealed class OscNotificationParserTests
{
    [Fact]
    public void ParseOsc9ReturnsNotification()
    {
        var parsed = OscNotificationParser.Parse("9;Agent needs input");

        Assert.Equal(OscEventKind.Notification, parsed.Kind);
        Assert.Equal("Terminal", parsed.Title);
        Assert.Equal("Agent needs input", parsed.Body);
    }

    [Fact]
    public void ParseOsc99KeyValueReturnsNotificationFields()
    {
        var parsed = OscNotificationParser.Parse("99;t=Codex;s=Plan;b=Waiting for input");

        Assert.Equal(OscEventKind.Notification, parsed.Kind);
        Assert.Equal("Codex", parsed.Title);
        Assert.Equal("Plan", parsed.Subtitle);
        Assert.Equal("Waiting for input", parsed.Body);
    }

    [Fact]
    public void ParseOsc777NotifyReturnsNotification()
    {
        var parsed = OscNotificationParser.Parse("777;notify;Claude;Done");

        Assert.Equal(OscEventKind.Notification, parsed.Kind);
        Assert.Equal("Claude", parsed.Title);
        Assert.Equal("Done", parsed.Body);
    }

    [Fact]
    public void ParseOsc7ReturnsWorkingDirectory()
    {
        var parsed = OscNotificationParser.Parse("7;file://localhost/C:/Users/dev/project");

        Assert.Equal(OscEventKind.WorkingDirectory, parsed.Kind);
        Assert.Contains("project", parsed.Value, StringComparison.OrdinalIgnoreCase);
    }
}
