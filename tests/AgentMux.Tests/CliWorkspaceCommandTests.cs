using System.Text.Json;
using AgentMux.Cli;
using AgentMux.Core.Ipc;

namespace AgentMux.Tests;

public sealed class CliWorkspaceCommandTests
{
    [Theory]
    [InlineData("list")]
    [InlineData("ls")]
    public void WorkspaceCommandParsesList(string command)
    {
        var request = Program.ParseWorkspaceRequestForTests([command], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceList, request.Method);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("new")]
    public void WorkspaceCommandParsesCreate(string command)
    {
        var request = Program.ParseWorkspaceRequestForTests([command, "--title", "API", "--cwd", "C:\\src\\api"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceCreate, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("API", parameters.GetProperty("title").GetString());
        Assert.Equal("C:\\src\\api", parameters.GetProperty("cwd").GetString());
    }

    [Fact]
    public void WorkspaceCommandParsesSelectByNamedIndex()
    {
        var request = Program.ParseWorkspaceRequestForTests(["select", "--index", "2"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceSelect, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(2, parameters.GetProperty("index").GetInt32());
    }

    [Fact]
    public void WorkspaceCommandParsesSelectByPositionalIndex()
    {
        var request = Program.ParseWorkspaceRequestForTests(["select", "1"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceSelect, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(1, parameters.GetProperty("index").GetInt32());
    }

    [Fact]
    public void WorkspaceCommandParsesSelectByZeroIndex()
    {
        var request = Program.ParseWorkspaceRequestForTests(["select", "--index", "0"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceSelect, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(0, parameters.GetProperty("index").GetInt32());
    }

    [Fact]
    public void WorkspaceCommandParsesSelectById()
    {
        var request = Program.ParseWorkspaceRequestForTests(["select", "--id", "workspace-1"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceSelect, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("workspace-1", parameters.GetProperty("id").GetString());
    }

    [Fact]
    public void WorkspaceCommandParsesUseAlias()
    {
        var request = Program.ParseWorkspaceRequestForTests(["use", "--id", "workspace-1"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceSelect, request.Method);
    }

    [Fact]
    public void WorkspaceCommandParsesUseAliasWithPositionalIndex()
    {
        var request = Program.ParseWorkspaceRequestForTests(["use", "1"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceSelect, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(1, parameters.GetProperty("index").GetInt32());
    }

    [Fact]
    public void WorkspaceCommandParsesPortsByPosition()
    {
        var request = Program.ParseWorkspaceRequestForTests(["ports", "5173", "3000", "3000"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceSetPorts, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(new[] { 3000, 5173 }, ReadPorts(parameters));
    }

    [Fact]
    public void WorkspaceCommandParsesPortsCsvWithTarget()
    {
        var request = Program.ParseWorkspaceRequestForTests(["ports", "--index", "1", "--ports", "5173,3000"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceSetPorts, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(1, parameters.GetProperty("index").GetInt32());
        Assert.Equal(new[] { 3000, 5173 }, ReadPorts(parameters));
    }

    [Fact]
    public void WorkspaceCommandParsesPortsClearWithId()
    {
        var request = Program.ParseWorkspaceRequestForTests(["ports", "clear", "--id", "workspace-1"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceSetPorts, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("workspace-1", parameters.GetProperty("id").GetString());
        Assert.Empty(ReadPorts(parameters));
    }

    [Fact]
    public void WorkspaceCommandRejectsMissingAction()
    {
        var request = Program.ParseWorkspaceRequestForTests([], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux workspace <list|create|select>", error);
    }

    [Fact]
    public void WorkspaceCommandRejectsSelectWithoutTarget()
    {
        var request = Program.ParseWorkspaceRequestForTests(["select"], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux workspace select --index <n>|--id <workspace-id>", error);
    }

    [Fact]
    public void WorkspaceCommandRejectsBareIdFlag()
    {
        var request = Program.ParseWorkspaceRequestForTests(["select", "--id"], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux workspace select --index <n>|--id <workspace-id>", error);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("true")]
    public void WorkspaceCommandRejectsInvalidSelectIndex(string index)
    {
        var request = Program.ParseWorkspaceRequestForTests(["select", "--index", index], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux workspace select --index <n>|--id <workspace-id>", error);
    }

    [Fact]
    public void WorkspaceCommandRejectsAmbiguousSelectTarget()
    {
        var request = Program.ParseWorkspaceRequestForTests(["select", "--index", "0", "--id", "workspace-1"], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux workspace select --index <n>|--id <workspace-id>", error);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("abc")]
    public void WorkspaceCommandRejectsInvalidPort(string port)
    {
        var request = Program.ParseWorkspaceRequestForTests(["ports", port], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux workspace ports [--index <n>|--id <workspace-id>] <port...>|--ports <csv>|clear", error);
    }

    [Fact]
    public void WorkspaceCommandRejectsTooManyPorts()
    {
        var args = new[] { "ports" }.Concat(Enumerable.Range(3000, 21).Select(port => port.ToString())).ToArray();
        var request = Program.ParseWorkspaceRequestForTests(args, out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux workspace ports [--index <n>|--id <workspace-id>] <port...>|--ports <csv>|clear", error);
    }

    [Fact]
    public void WorkspaceCommandRejectsAmbiguousPortTarget()
    {
        var request = Program.ParseWorkspaceRequestForTests(["ports", "--index", "0", "--id", "workspace-1", "3000"], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux workspace ports [--index <n>|--id <workspace-id>] <port...>|--ports <csv>|clear", error);
    }

    [Fact]
    public void WorkspaceCommandRejectsBarePortIdFlag()
    {
        var request = Program.ParseWorkspaceRequestForTests(["ports", "--id"], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux workspace ports [--index <n>|--id <workspace-id>] <port...>|--ports <csv>|clear", error);
    }

    [Fact]
    public void WorkspaceCommandRejectsUnknownAction()
    {
        var request = Program.ParseWorkspaceRequestForTests(["rename"], out var error);

        Assert.Null(request);
        Assert.Equal("Unknown workspace command: rename", error);
    }

    private static int[] ReadPorts(JsonElement parameters)
    {
        return parameters.GetProperty("ports").EnumerateArray().Select(port => port.GetInt32()).ToArray();
    }
}
