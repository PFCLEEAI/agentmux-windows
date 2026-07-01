namespace AgentMux.Core.Ipc;

public static class TerminalKeyEncoder
{
    public static bool TryEncode(string? key, out string sequence)
    {
        sequence = key?.Trim().ToUpperInvariant() switch
        {
            "ENTER" or "RETURN" => "\r",
            "TAB" => "\t",
            "ESCAPE" or "ESC" => "\u001b",
            "BACKSPACE" => "\u007f",
            "DELETE" or "DEL" => "\u001b[3~",
            "SPACE" => " ",
            "UP" or "ARROWUP" => "\u001b[A",
            "DOWN" or "ARROWDOWN" => "\u001b[B",
            "RIGHT" or "ARROWRIGHT" => "\u001b[C",
            "LEFT" or "ARROWLEFT" => "\u001b[D",
            "HOME" => "\u001b[H",
            "END" => "\u001b[F",
            "CTRLC" or "CTRL+C" or "CONTROL+C" => "\u0003",
            "CTRLD" or "CTRL+D" or "CONTROL+D" => "\u0004",
            _ => ""
        };

        return sequence.Length > 0;
    }
}
