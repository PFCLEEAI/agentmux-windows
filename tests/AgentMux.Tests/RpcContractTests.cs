using System.Text.Json;
using AgentMux.Core.Ipc;

namespace AgentMux.Tests;

public sealed class RpcContractTests
{
    [Fact]
    public void RequestRoundTripsWithParams()
    {
        var request = new AgentMuxRequest
        {
            Id = "req-1",
            Method = AgentMuxMethods.Notify,
            Params = JsonSerializer.SerializeToElement(new { title = "Codex", body = "Waiting" }, AgentMuxJson.Options)
        };

        var json = JsonSerializer.Serialize(request, AgentMuxJson.Options);
        var parsed = JsonSerializer.Deserialize<AgentMuxRequest>(json, AgentMuxJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal("req-1", parsed.Id);
        Assert.Equal(AgentMuxMethods.Notify, parsed.Method);
        Assert.Equal("Codex", parsed.Params?.GetProperty("title").GetString());
    }

    [Fact]
    public void PipeNameIsUserScoped()
    {
        var pipe = AgentMuxPipe.ForCurrentUser();

        Assert.StartsWith("agentmux-", pipe, StringComparison.Ordinal);
        Assert.DoesNotContain('\\', pipe);
        Assert.DoesNotContain('/', pipe);
    }

    [Fact]
    public void SendKeyMethodIsStable()
    {
        Assert.Equal("surface.send_key", AgentMuxMethods.SendKey);
    }

    [Fact]
    public void FocusPaneMethodIsStable()
    {
        Assert.Equal("surface.focus_pane", AgentMuxMethods.FocusPane);
    }

    [Fact]
    public void OpenUrlMethodIsStable()
    {
        Assert.Equal("surface.open_url", AgentMuxMethods.OpenUrl);
    }

    [Fact]
    public void BrowserAutomationMethodsAreStable()
    {
        Assert.Equal("surface.eval_js", AgentMuxMethods.BrowserEval);
        Assert.Equal("surface.click_selector", AgentMuxMethods.BrowserClick);
        Assert.Equal("surface.fill_selector", AgentMuxMethods.BrowserFill);
        Assert.Equal("surface.capture_screenshot", AgentMuxMethods.BrowserScreenshot);
    }
}
