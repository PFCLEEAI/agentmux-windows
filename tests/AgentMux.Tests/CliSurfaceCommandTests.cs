using AgentMux.Cli;
using AgentMux.Core.Ipc;
using System.Text.Json;

namespace AgentMux.Tests;

public sealed class CliSurfaceCommandTests
{
    [Theory]
    [InlineData("list")]
    [InlineData("ls")]
    public void SurfaceCommandParsesList(string command)
    {
        var request = Program.ParseSurfaceRequestForTests([command], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.SurfaceList, request.Method);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("new")]
    public void SurfaceCommandParsesCreate(string command)
    {
        var request = Program.ParseSurfaceRequestForTests([command, "--title", "Tests"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.SurfaceCreate, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("Tests", parameters.GetProperty("title").GetString());
    }

    [Fact]
    public void SurfaceCommandParsesSelectByNamedIndex()
    {
        var request = Program.ParseSurfaceRequestForTests(["select", "--index", "2"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.SurfaceSelect, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(2, parameters.GetProperty("index").GetInt32());
    }

    [Fact]
    public void SurfaceCommandParsesSelectByPositionalIndex()
    {
        var request = Program.ParseSurfaceRequestForTests(["select", "1"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.SurfaceSelect, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(1, parameters.GetProperty("index").GetInt32());
    }

    [Fact]
    public void SurfaceCommandParsesSelectByZeroIndex()
    {
        var request = Program.ParseSurfaceRequestForTests(["select", "--index", "0"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.SurfaceSelect, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(0, parameters.GetProperty("index").GetInt32());
    }

    [Fact]
    public void SurfaceCommandParsesSelectById()
    {
        var request = Program.ParseSurfaceRequestForTests(["select", "--id", "surface-1"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.SurfaceSelect, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("surface-1", parameters.GetProperty("id").GetString());
    }

    [Fact]
    public void SurfaceCommandParsesUseAlias()
    {
        var request = Program.ParseSurfaceRequestForTests(["use", "--id", "surface-1"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.SurfaceSelect, request.Method);
    }

    [Fact]
    public void SurfaceCommandRejectsMissingAction()
    {
        var request = Program.ParseSurfaceRequestForTests([], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux surface <list|create|select>", error);
    }

    [Fact]
    public void SurfaceCommandRejectsSelectWithoutTarget()
    {
        var request = Program.ParseSurfaceRequestForTests(["select"], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux surface select --index <n>|--id <surface-id>", error);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("true")]
    public void SurfaceCommandRejectsInvalidSelectIndex(string index)
    {
        var request = Program.ParseSurfaceRequestForTests(["select", "--index", index], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux surface select --index <n>|--id <surface-id>", error);
    }

    [Fact]
    public void SurfaceCommandRejectsAmbiguousSelectTarget()
    {
        var request = Program.ParseSurfaceRequestForTests(["select", "--index", "0", "--id", "surface-1"], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux surface select --index <n>|--id <surface-id>", error);
    }

    [Fact]
    public void SurfaceCommandRejectsUnknownAction()
    {
        var request = Program.ParseSurfaceRequestForTests(["rename"], out var error);

        Assert.Null(request);
        Assert.Equal("Unknown surface command: rename", error);
    }
}
