namespace AgentMux.Core.Ipc;

public static class TerminalKeyEncoder
{
    public static bool TryEncode(string? key, out string sequence)
    {
        sequence = "";
        var trimmed = key?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        if (TryReadModifier(trimmed, ["ALT", "META"], out var altTarget))
        {
            if (TryEncodeWithoutMeta(altTarget, out var metaSequence))
            {
                sequence = "\u001b" + metaSequence;
                return true;
            }

            if (altTarget.Length == 1 && !char.IsControl(altTarget[0]))
            {
                sequence = "\u001b" + altTarget;
                return true;
            }

            return false;
        }

        return TryEncodeWithoutMeta(trimmed, out sequence);
    }

    private static bool TryEncodeWithoutMeta(string key, out string sequence)
    {
        var normalized = NormalizeKey(key);
        sequence = normalized switch
        {
            "ENTER" or "RETURN" => "\r",
            "TAB" => "\t",
            "SHIFTTAB" or "SHIFT+TAB" or "SHIFT-TAB" => "\u001b[Z",
            "ESCAPE" or "ESC" => "\u001b",
            "BACKSPACE" => "\u007f",
            "DELETE" or "DEL" => "\u001b[3~",
            "INSERT" or "INS" => "\u001b[2~",
            "SPACE" => " ",
            "UP" or "ARROWUP" => "\u001b[A",
            "DOWN" or "ARROWDOWN" => "\u001b[B",
            "RIGHT" or "ARROWRIGHT" => "\u001b[C",
            "LEFT" or "ARROWLEFT" => "\u001b[D",
            "HOME" => "\u001b[H",
            "END" => "\u001b[F",
            "PAGEUP" or "PGUP" or "PRIOR" => "\u001b[5~",
            "PAGEDOWN" or "PGDN" or "NEXT" => "\u001b[6~",
            "F1" => "\u001bOP",
            "F2" => "\u001bOQ",
            "F3" => "\u001bOR",
            "F4" => "\u001bOS",
            "F5" => "\u001b[15~",
            "F6" => "\u001b[17~",
            "F7" => "\u001b[18~",
            "F8" => "\u001b[19~",
            "F9" => "\u001b[20~",
            "F10" => "\u001b[21~",
            "F11" => "\u001b[23~",
            "F12" => "\u001b[24~",
            _ => ""
        };

        return sequence.Length > 0 || TryEncodeControlKey(key, out sequence);
    }

    private static bool TryEncodeControlKey(string key, out string sequence)
    {
        sequence = "";
        if (!TryReadModifier(key, ["CTRL", "CONTROL"], out var target))
        {
            var normalized = NormalizeKey(key);
            if (normalized.StartsWith("CTRL", StringComparison.Ordinal) && normalized.Length > "CTRL".Length)
            {
                target = normalized["CTRL".Length..];
            }
            else if (normalized.StartsWith("CONTROL", StringComparison.Ordinal) && normalized.Length > "CONTROL".Length)
            {
                target = normalized["CONTROL".Length..];
            }
            else
            {
                return false;
            }
        }

        var normalizedTarget = NormalizeKey(target);
        if (normalizedTarget is "SPACE" or "@" or "`")
        {
            sequence = "\u0000";
            return true;
        }

        if (normalizedTarget.Length != 1)
        {
            return false;
        }

        var character = normalizedTarget[0];
        if (character is >= 'A' and <= 'Z')
        {
            sequence = ((char)(character - 'A' + 1)).ToString();
            return true;
        }

        sequence = character switch
        {
            '[' => "\u001b",
            '\\' => "\u001c",
            ']' => "\u001d",
            '^' => "\u001e",
            '_' => "\u001f",
            '?' => "\u007f",
            _ => ""
        };
        return sequence.Length > 0;
    }

    private static bool TryReadModifier(string key, string[] modifiers, out string target)
    {
        var trimmed = key.Trim();
        foreach (var modifier in modifiers)
        {
            foreach (var separator in new[] { '+', '-' })
            {
                var prefix = modifier + separator;
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    target = trimmed[prefix.Length..].Trim();
                    return target.Length > 0;
                }
            }
        }

        target = "";
        return false;
    }

    private static string NormalizeKey(string key)
    {
        return key.Trim().Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
    }
}
