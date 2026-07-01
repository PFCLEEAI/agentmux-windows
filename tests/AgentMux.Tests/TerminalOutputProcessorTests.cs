using AgentMux.Core.Notifications;

namespace AgentMux.Tests;

public sealed class TerminalOutputProcessorTests
{
    [Fact]
    public void ProcessPassesThroughOrdinaryText()
    {
        var processor = new TerminalOutputProcessor();

        var chunk = processor.Process("hello");

        Assert.Equal("hello", chunk.Text);
        Assert.Empty(chunk.Events);
    }

    [Fact]
    public void ProcessStripsBellTerminatedOsc9Notification()
    {
        var processor = new TerminalOutputProcessor();

        var chunk = processor.Process("before\u001b]9;Agent needs input\u0007after");

        Assert.Equal("beforeafter", chunk.Text);
        var notification = Assert.Single(chunk.Events);
        Assert.Equal(OscEventKind.Notification, notification.Kind);
        Assert.Equal("Terminal", notification.Title);
        Assert.Equal("Agent needs input", notification.Body);
    }

    [Fact]
    public void ProcessStripsStTerminatedOsc99Notification()
    {
        var processor = new TerminalOutputProcessor();

        var chunk = processor.Process("a\u001b]99;t=Codex;s=Plan;b=Waiting\u001b\\b");

        Assert.Equal("ab", chunk.Text);
        var notification = Assert.Single(chunk.Events);
        Assert.Equal(OscEventKind.Notification, notification.Kind);
        Assert.Equal("Codex", notification.Title);
        Assert.Equal("Plan", notification.Subtitle);
        Assert.Equal("Waiting", notification.Body);
    }

    [Fact]
    public void ProcessBuffersChunkSplitOsc777Notification()
    {
        var processor = new TerminalOutputProcessor();

        var first = processor.Process("one\u001b]777;notify;Claude;");
        var second = processor.Process("Done\u0007two");

        Assert.Equal("one", first.Text);
        Assert.Empty(first.Events);
        Assert.Equal("two", second.Text);
        var notification = Assert.Single(second.Events);
        Assert.Equal(OscEventKind.Notification, notification.Kind);
        Assert.Equal("Claude", notification.Title);
        Assert.Equal("Done", notification.Body);
    }

    [Fact]
    public void ProcessReturnsTitleAndWorkingDirectoryEvents()
    {
        var processor = new TerminalOutputProcessor();

        var chunk = processor.Process("\u001b]0;Build pane\u0007\u001b]7;file://localhost/C:/src/app\u001b\\");

        Assert.Equal("", chunk.Text);
        Assert.Collection(
            chunk.Events,
            title =>
            {
                Assert.Equal(OscEventKind.Title, title.Kind);
                Assert.Equal("Build pane", title.Value);
            },
            cwd =>
            {
                Assert.Equal(OscEventKind.WorkingDirectory, cwd.Kind);
                Assert.Contains("app", cwd.Value, StringComparison.OrdinalIgnoreCase);
            });
    }

    [Fact]
    public void FlushDiscardsIncompleteOscWithoutLeakingPayload()
    {
        var processor = new TerminalOutputProcessor();

        var first = processor.Process("before\u001b]9;secret");
        var second = processor.Flush();

        Assert.Equal("before", first.Text);
        Assert.Empty(first.Events);
        Assert.Equal("", second.Text);
        Assert.Empty(second.Events);
    }
}
