using System.Text.Json;
using AgentMux.Cli;
using AgentMux.Core.Ipc;

namespace AgentMux.Tests;

public sealed class CliWorkspaceCommandTests
{
    private const string PullRequestUsage = "Usage: agentmux workspace pr [--index <n>|--id <workspace-id>] <number>|set <number>|--number <n>|clear [--status <unknown|open|draft|merged|closed>] [--url <url>]";
    private const string WorkspaceLogUsage = "Usage: agentmux log <message> [--level <info|warn|error|debug>] [--source <text>] [--workspace <id-or-index>]";
    private const string WorkspaceListLogUsage = "Usage: agentmux list-log [--limit <count>] [--workspace <id-or-index>]";
    private const string WorkspaceClearLogUsage = "Usage: agentmux clear-log [--workspace <id-or-index>]";
    private const string WorkspaceSetStatusUsage = "Usage: agentmux set-status <key> <text> [--icon <text>] [--color <text>] [--workspace <id-or-index>]";
    private const string WorkspaceListStatusUsage = "Usage: agentmux list-status [--workspace <id-or-index>]";
    private const string WorkspaceClearStatusUsage = "Usage: agentmux clear-status <key|--all> [--workspace <id-or-index>]";
    private const string WorkspaceSetProgressUsage = "Usage: agentmux set-progress <0..1> [--label <text>] [--workspace <id-or-index>]";
    private const string WorkspaceClearProgressUsage = "Usage: agentmux clear-progress [--workspace <id-or-index>]";

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
    public void WorkspaceCommandParsesPullRequestByPosition()
    {
        var request = Program.ParseWorkspaceRequestForTests(["pr", "123"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceSetPullRequest, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(123, parameters.GetProperty("number").GetInt32());
        Assert.False(parameters.TryGetProperty("status", out _));
        Assert.False(parameters.TryGetProperty("url", out _));
    }

    [Fact]
    public void WorkspaceCommandParsesPullRequestSetWithMetadataAndTarget()
    {
        var request = Program.ParseWorkspaceRequestForTests(
            ["pull-request", "set", "--index", "1", "123", "--status", "OPEN", "--url", "https://github.com/example/repo/pull/123"],
            out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceSetPullRequest, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(1, parameters.GetProperty("index").GetInt32());
        Assert.Equal(123, parameters.GetProperty("number").GetInt32());
        Assert.Equal("open", parameters.GetProperty("status").GetString());
        Assert.Equal("https://github.com/example/repo/pull/123", parameters.GetProperty("url").GetString());
    }

    [Fact]
    public void WorkspaceCommandParsesPullRequestNamedNumberWithId()
    {
        var request = Program.ParseWorkspaceRequestForTests(["pull", "--id", "workspace-1", "--number", "456"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceSetPullRequest, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("workspace-1", parameters.GetProperty("id").GetString());
        Assert.Equal(456, parameters.GetProperty("number").GetInt32());
    }

    [Fact]
    public void WorkspaceCommandParsesPullRequestClearWithId()
    {
        var request = Program.ParseWorkspaceRequestForTests(["pr", "clear", "--id", "workspace-1"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceSetPullRequest, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("workspace-1", parameters.GetProperty("id").GetString());
        Assert.True(parameters.GetProperty("clear").GetBoolean());
    }

    [Fact]
    public void WorkspaceLogCommandParsesPositionalMessageWithDefaults()
    {
        var request = Program.ParseWorkspaceLogRequestForTests(["server", "started"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceLog, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("server started", parameters.GetProperty("message").GetString());
        Assert.Equal("info", parameters.GetProperty("level").GetString());
        Assert.False(parameters.TryGetProperty("source", out _));
        Assert.False(parameters.TryGetProperty("index", out _));
        Assert.False(parameters.TryGetProperty("id", out _));
    }

    [Fact]
    public void WorkspaceLogCommandParsesNamedFieldsAndWorkspaceIndex()
    {
        var request = Program.ParseWorkspaceLogRequestForTests(
            ["--message", "Server ready", "--level", "WARN", "--source", "server", "--workspace", "1"],
            out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceLog, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("Server ready", parameters.GetProperty("message").GetString());
        Assert.Equal("warn", parameters.GetProperty("level").GetString());
        Assert.Equal("server", parameters.GetProperty("source").GetString());
        Assert.Equal(1, parameters.GetProperty("index").GetInt32());
        Assert.False(parameters.TryGetProperty("id", out _));
    }

    [Fact]
    public void WorkspaceLogCommandParsesWorkspaceId()
    {
        var request = Program.ParseWorkspaceLogRequestForTests(["ready", "--workspace", "workspace-1"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("workspace-1", parameters.GetProperty("id").GetString());
        Assert.False(parameters.TryGetProperty("index", out _));
    }

    [Fact]
    public void WorkspaceListLogCommandParsesLimitAndWorkspace()
    {
        var request = Program.ParseWorkspaceListLogRequestForTests(["--limit", "20", "--workspace", "workspace-1"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceListLog, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(20, parameters.GetProperty("limit").GetInt32());
        Assert.Equal("workspace-1", parameters.GetProperty("id").GetString());
    }

    [Fact]
    public void WorkspaceClearLogCommandParsesWorkspaceIndex()
    {
        var request = Program.ParseWorkspaceClearLogRequestForTests(["--workspace", "0"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceClearLog, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(0, parameters.GetProperty("index").GetInt32());
    }

    [Fact]
    public void WorkspaceSetStatusCommandParsesPositionalsWithMetadataAndWorkspaceIndex()
    {
        var request = Program.ParseWorkspaceSetStatusRequestForTests(
            ["build", "Running", "tests", "--icon", "checkmark", "--color", "#22c55e", "--workspace", "1"],
            out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceSetStatus, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("build", parameters.GetProperty("key").GetString());
        Assert.Equal("Running tests", parameters.GetProperty("text").GetString());
        Assert.Equal("checkmark", parameters.GetProperty("icon").GetString());
        Assert.Equal("#22c55e", parameters.GetProperty("color").GetString());
        Assert.Equal(1, parameters.GetProperty("index").GetInt32());
        Assert.False(parameters.TryGetProperty("id", out _));
    }

    [Fact]
    public void WorkspaceSetStatusCommandParsesNamedFieldsAndWorkspaceId()
    {
        var request = Program.ParseWorkspaceSetStatusRequestForTests(
            ["--key", "deploy", "--text", "Queued", "--workspace", "workspace-1"],
            out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceSetStatus, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("deploy", parameters.GetProperty("key").GetString());
        Assert.Equal("Queued", parameters.GetProperty("text").GetString());
        Assert.Equal("workspace-1", parameters.GetProperty("id").GetString());
        Assert.False(parameters.TryGetProperty("index", out _));
    }

    [Fact]
    public void WorkspaceListStatusCommandParsesWorkspaceId()
    {
        var request = Program.ParseWorkspaceListStatusRequestForTests(["--workspace", "workspace-1"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceListStatus, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("workspace-1", parameters.GetProperty("id").GetString());
    }

    [Fact]
    public void WorkspaceClearStatusCommandParsesKeyAndWorkspaceIndex()
    {
        var request = Program.ParseWorkspaceClearStatusRequestForTests(["build", "--workspace", "0"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceClearStatus, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("build", parameters.GetProperty("key").GetString());
        Assert.Equal(0, parameters.GetProperty("index").GetInt32());
        Assert.False(parameters.TryGetProperty("all", out _));
    }

    [Fact]
    public void WorkspaceClearStatusCommandParsesAll()
    {
        var request = Program.ParseWorkspaceClearStatusRequestForTests(["--all"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceClearStatus, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.True(parameters.GetProperty("all").GetBoolean());
        Assert.False(parameters.TryGetProperty("key", out _));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData(".5", 0.5)]
    [InlineData("0.5", 0.5)]
    [InlineData("1", 1)]
    public void WorkspaceSetProgressCommandParsesValue(string valueText, double expected)
    {
        var request = Program.ParseWorkspaceSetProgressRequestForTests([valueText], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceSetProgress, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(expected, parameters.GetProperty("value").GetDouble(), 3);
        Assert.False(parameters.TryGetProperty("label", out _));
        Assert.False(parameters.TryGetProperty("index", out _));
        Assert.False(parameters.TryGetProperty("id", out _));
    }

    [Fact]
    public void WorkspaceSetProgressCommandParsesLabelAndWorkspaceIndex()
    {
        var request = Program.ParseWorkspaceSetProgressRequestForTests(
            ["0.75", "--label", "Deploying app", "--workspace", "1"],
            out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceSetProgress, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(0.75, parameters.GetProperty("value").GetDouble(), 3);
        Assert.Equal("Deploying app", parameters.GetProperty("label").GetString());
        Assert.Equal(1, parameters.GetProperty("index").GetInt32());
        Assert.False(parameters.TryGetProperty("id", out _));
    }

    [Fact]
    public void WorkspaceSetProgressCommandParsesNamedValueAndWorkspaceId()
    {
        var request = Program.ParseWorkspaceSetProgressRequestForTests(["--value", "1", "--workspace", "workspace-1"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceSetProgress, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(1, parameters.GetProperty("value").GetDouble(), 3);
        Assert.Equal("workspace-1", parameters.GetProperty("id").GetString());
        Assert.False(parameters.TryGetProperty("index", out _));
        Assert.False(parameters.TryGetProperty("label", out _));
    }

    [Fact]
    public void WorkspaceClearProgressCommandParsesWorkspaceIndex()
    {
        var request = Program.ParseWorkspaceClearProgressRequestForTests(["--workspace", "0"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.WorkspaceClearProgress, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(0, parameters.GetProperty("index").GetInt32());
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

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("10000000")]
    public void WorkspaceCommandRejectsInvalidPullRequestNumber(string number)
    {
        var request = Program.ParseWorkspaceRequestForTests(["pr", number], out var error);

        Assert.Null(request);
        Assert.Equal(PullRequestUsage, error);
    }

    [Fact]
    public void WorkspaceCommandRejectsInvalidPullRequestStatus()
    {
        var request = Program.ParseWorkspaceRequestForTests(["pr", "123", "--status", "stale"], out var error);

        Assert.Null(request);
        Assert.Equal(PullRequestUsage, error);
    }

    [Theory]
    [InlineData("ftp://github.com/example/repo/pull/123")]
    [InlineData("https://token@example.com/repo/pull/123")]
    [InlineData("not-a-url")]
    public void WorkspaceCommandRejectsInvalidPullRequestUrl(string url)
    {
        var request = Program.ParseWorkspaceRequestForTests(["pr", "123", "--url", url], out var error);

        Assert.Null(request);
        Assert.Equal(PullRequestUsage, error);
    }

    [Fact]
    public void WorkspaceCommandRejectsAmbiguousPullRequestTarget()
    {
        var request = Program.ParseWorkspaceRequestForTests(["pr", "--index", "0", "--id", "workspace-1", "123"], out var error);

        Assert.Null(request);
        Assert.Equal(PullRequestUsage, error);
    }

    [Fact]
    public void WorkspaceCommandRejectsBarePullRequestIdFlag()
    {
        var request = Program.ParseWorkspaceRequestForTests(["pr", "--id"], out var error);

        Assert.Null(request);
        Assert.Equal(PullRequestUsage, error);
    }

    [Fact]
    public void WorkspaceCommandRejectsAmbiguousPullRequestNumberSources()
    {
        var request = Program.ParseWorkspaceRequestForTests(["pr", "123", "--number", "456"], out var error);

        Assert.Null(request);
        Assert.Equal(PullRequestUsage, error);
    }

    [Fact]
    public void WorkspaceCommandRejectsPullRequestClearWithMetadata()
    {
        var request = Program.ParseWorkspaceRequestForTests(["pr", "clear", "--status", "open"], out var error);

        Assert.Null(request);
        Assert.Equal(PullRequestUsage, error);
    }

    [Theory]
    [InlineData()]
    [InlineData("--message")]
    public void WorkspaceLogCommandRejectsMissingMessage(params string[] args)
    {
        var request = Program.ParseWorkspaceLogRequestForTests(args, out var error);

        Assert.Null(request);
        Assert.Equal(WorkspaceLogUsage, error);
    }

    [Theory]
    [InlineData("--level")]
    [InlineData("--level", "trace")]
    public void WorkspaceLogCommandRejectsInvalidLevel(params string[] args)
    {
        var request = Program.ParseWorkspaceLogRequestForTests(["ready", .. args], out var error);

        Assert.Null(request);
        Assert.Equal(WorkspaceLogUsage, error);
    }

    [Fact]
    public void WorkspaceLogCommandRejectsBareSourceFlag()
    {
        var request = Program.ParseWorkspaceLogRequestForTests(["ready", "--source"], out var error);

        Assert.Null(request);
        Assert.Equal(WorkspaceLogUsage, error);
    }

    [Fact]
    public void WorkspaceLogCommandRejectsAmbiguousWorkspaceTarget()
    {
        var request = Program.ParseWorkspaceLogRequestForTests(["ready", "--workspace", "0", "--id", "workspace-1"], out var error);

        Assert.Null(request);
        Assert.Equal(WorkspaceLogUsage, error);
    }

    [Fact]
    public void WorkspaceLogCommandRejectsBareWorkspaceFlag()
    {
        var request = Program.ParseWorkspaceLogRequestForTests(["ready", "--workspace"], out var error);

        Assert.Null(request);
        Assert.Equal(WorkspaceLogUsage, error);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    public void WorkspaceListLogCommandRejectsInvalidLimit(string limit)
    {
        var request = Program.ParseWorkspaceListLogRequestForTests(["--limit", limit], out var error);

        Assert.Null(request);
        Assert.Equal(WorkspaceListLogUsage, error);
    }

    [Fact]
    public void WorkspaceListLogCommandRejectsPositionals()
    {
        var request = Program.ParseWorkspaceListLogRequestForTests(["extra"], out var error);

        Assert.Null(request);
        Assert.Equal(WorkspaceListLogUsage, error);
    }

    [Fact]
    public void WorkspaceClearLogCommandRejectsPositionals()
    {
        var request = Program.ParseWorkspaceClearLogRequestForTests(["extra"], out var error);

        Assert.Null(request);
        Assert.Equal(WorkspaceClearLogUsage, error);
    }

    [Theory]
    [InlineData()]
    [InlineData("--key", "build")]
    [InlineData("build")]
    [InlineData("--key")]
    [InlineData("build", "--text")]
    public void WorkspaceSetStatusCommandRejectsMissingKeyOrText(params string[] args)
    {
        var request = Program.ParseWorkspaceSetStatusRequestForTests(args, out var error);

        Assert.Null(request);
        Assert.Equal(WorkspaceSetStatusUsage, error);
    }

    [Theory]
    [InlineData("--icon")]
    [InlineData("--color")]
    public void WorkspaceSetStatusCommandRejectsBareMetadataFlags(params string[] args)
    {
        var request = Program.ParseWorkspaceSetStatusRequestForTests(["build", "ready", .. args], out var error);

        Assert.Null(request);
        Assert.Equal(WorkspaceSetStatusUsage, error);
    }

    [Fact]
    public void WorkspaceSetStatusCommandRejectsAmbiguousWorkspaceTarget()
    {
        var request = Program.ParseWorkspaceSetStatusRequestForTests(["build", "ready", "--workspace", "0", "--id", "workspace-1"], out var error);

        Assert.Null(request);
        Assert.Equal(WorkspaceSetStatusUsage, error);
    }

    [Theory]
    [InlineData("build", "Running", "--text", "Passed")]
    [InlineData("--key", "build", "--text", "Passed", "extra")]
    public void WorkspaceSetStatusCommandRejectsMixedNamedTextAndExtraPositionals(params string[] args)
    {
        var request = Program.ParseWorkspaceSetStatusRequestForTests(args, out var error);

        Assert.Null(request);
        Assert.Equal(WorkspaceSetStatusUsage, error);
    }

    [Fact]
    public void WorkspaceListStatusCommandRejectsPositionals()
    {
        var request = Program.ParseWorkspaceListStatusRequestForTests(["extra"], out var error);

        Assert.Null(request);
        Assert.Equal(WorkspaceListStatusUsage, error);
    }

    [Fact]
    public void WorkspaceListStatusCommandRejectsBareWorkspaceFlag()
    {
        var request = Program.ParseWorkspaceListStatusRequestForTests(["--workspace"], out var error);

        Assert.Null(request);
        Assert.Equal(WorkspaceListStatusUsage, error);
    }

    [Theory]
    [InlineData()]
    [InlineData("--all", "build")]
    [InlineData("build", "deploy")]
    [InlineData("--key")]
    [InlineData("--all", "false")]
    public void WorkspaceClearStatusCommandRejectsInvalidKeyOrAll(params string[] args)
    {
        var request = Program.ParseWorkspaceClearStatusRequestForTests(args, out var error);

        Assert.Null(request);
        Assert.Equal(WorkspaceClearStatusUsage, error);
    }

    [Theory]
    [InlineData()]
    [InlineData("-0.1")]
    [InlineData("1.1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("abc")]
    [InlineData("0,5")]
    [InlineData("0.5", "extra")]
    [InlineData("--value")]
    [InlineData("--value", "0.5", "extra")]
    public void WorkspaceSetProgressCommandRejectsInvalidValue(params string[] args)
    {
        var request = Program.ParseWorkspaceSetProgressRequestForTests(args, out var error);

        Assert.Null(request);
        Assert.Equal(WorkspaceSetProgressUsage, error);
    }

    [Fact]
    public void WorkspaceSetProgressCommandRejectsBareLabelFlag()
    {
        var request = Program.ParseWorkspaceSetProgressRequestForTests(["0.5", "--label"], out var error);

        Assert.Null(request);
        Assert.Equal(WorkspaceSetProgressUsage, error);
    }

    [Fact]
    public void WorkspaceSetProgressCommandRejectsAmbiguousWorkspaceTarget()
    {
        var request = Program.ParseWorkspaceSetProgressRequestForTests(["0.5", "--workspace", "0", "--id", "workspace-1"], out var error);

        Assert.Null(request);
        Assert.Equal(WorkspaceSetProgressUsage, error);
    }

    [Fact]
    public void WorkspaceSetProgressCommandRejectsUnknownFlag()
    {
        var request = Program.ParseWorkspaceSetProgressRequestForTests(["0.5", "--workspce", "1"], out var error);

        Assert.Null(request);
        Assert.Equal(WorkspaceSetProgressUsage, error);
    }

    [Fact]
    public void WorkspaceClearProgressCommandRejectsPositionals()
    {
        var request = Program.ParseWorkspaceClearProgressRequestForTests(["0"], out var error);

        Assert.Null(request);
        Assert.Equal(WorkspaceClearProgressUsage, error);
    }

    [Fact]
    public void WorkspaceClearProgressCommandRejectsBareWorkspaceFlag()
    {
        var request = Program.ParseWorkspaceClearProgressRequestForTests(["--workspace"], out var error);

        Assert.Null(request);
        Assert.Equal(WorkspaceClearProgressUsage, error);
    }

    [Fact]
    public void WorkspaceClearProgressCommandRejectsUnknownFlag()
    {
        var request = Program.ParseWorkspaceClearProgressRequestForTests(["--workspce", "1"], out var error);

        Assert.Null(request);
        Assert.Equal(WorkspaceClearProgressUsage, error);
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
