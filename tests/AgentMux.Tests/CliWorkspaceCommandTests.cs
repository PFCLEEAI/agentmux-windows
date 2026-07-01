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

    [Fact]
    public void WorkspaceCommandRejectsUnknownAction()
    {
        var request = Program.ParseWorkspaceRequestForTests(["rename"], out var error);

        Assert.Null(request);
        Assert.Equal("Unknown workspace command: rename", error);
    }
}
