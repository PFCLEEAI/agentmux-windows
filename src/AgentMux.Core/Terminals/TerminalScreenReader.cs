namespace AgentMux.Core.Terminals;

public static class TerminalScreenReader
{
    public static TerminalScreenReadResult Read(string? text, int? lines)
    {
        var source = text ?? "";
        if (lines is not > 0)
        {
            return new TerminalScreenReadResult(source, null, false);
        }

        var normalized = NormalizeLineEndings(source);
        var logicalLines = SplitLogicalLines(normalized);
        if (logicalLines.Count <= lines.Value)
        {
            return new TerminalScreenReadResult(string.Join('\n', logicalLines), lines, false);
        }

        var tail = logicalLines.Skip(logicalLines.Count - lines.Value);
        return new TerminalScreenReadResult(string.Join('\n', tail), lines, true);
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    private static List<string> SplitLogicalLines(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        if (text[^1] == '\n')
        {
            text = text[..^1];
        }

        return text.Length == 0 ? [] : text.Split('\n').ToList();
    }
}

public sealed record TerminalScreenReadResult(string Text, int? Lines, bool Truncated);
