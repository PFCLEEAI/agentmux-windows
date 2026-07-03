using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace AgentMux.Win.App.Input;

internal sealed class ShortcutSettings
{
    private static readonly ShortcutAction[] ActionOrder =
    [
        ShortcutAction.ToggleZoom,
        ShortcutAction.ClosePane,
        ShortcutAction.FocusLeft,
        ShortcutAction.FocusRight,
        ShortcutAction.FocusUp,
        ShortcutAction.FocusDown,
        ShortcutAction.FocusPrevious,
        ShortcutAction.FocusNext
    ];

    private static readonly Dictionary<ShortcutAction, ShortcutGesture> DefaultBindings = new()
    {
        [ShortcutAction.ToggleZoom] = new(Key.Z, ModifierKeys.Control | ModifierKeys.Shift),
        [ShortcutAction.ClosePane] = new(Key.X, ModifierKeys.Control | ModifierKeys.Shift),
        [ShortcutAction.FocusLeft] = new(Key.Left, ModifierKeys.Control | ModifierKeys.Alt),
        [ShortcutAction.FocusRight] = new(Key.Right, ModifierKeys.Control | ModifierKeys.Alt),
        [ShortcutAction.FocusUp] = new(Key.Up, ModifierKeys.Control | ModifierKeys.Alt),
        [ShortcutAction.FocusDown] = new(Key.Down, ModifierKeys.Control | ModifierKeys.Alt),
        [ShortcutAction.FocusNext] = new(Key.Tab, ModifierKeys.Control),
        [ShortcutAction.FocusPrevious] = new(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift)
    };

    private readonly Dictionary<ShortcutAction, ShortcutGesture> _bindings;

    private ShortcutSettings(Dictionary<ShortcutAction, ShortcutGesture> bindings, string filePath)
    {
        _bindings = bindings;
        FilePath = filePath;
    }

    public string FilePath { get; }

    public static ShortcutSettings Load()
    {
        var configuredPath = Environment.GetEnvironmentVariable("AGENTMUX_SHORTCUTS_PATH");
        return LoadFromFile(string.IsNullOrWhiteSpace(configuredPath) ? DefaultFilePath() : configuredPath);
    }

    internal static ShortcutSettings Default() => new(CloneDefaultBindings(), DefaultFilePath());

    internal static ShortcutSettings LoadFromFile(string path)
    {
        var bindings = CloneDefaultBindings();
        if (!File.Exists(path))
        {
            return new ShortcutSettings(bindings, path);
        }

        try
        {
            var json = File.ReadAllText(path);
            var configured = JsonSerializer.Deserialize<Dictionary<string, string>>(json, ShortcutJson.Options);
            if (configured is null)
            {
                return new ShortcutSettings(bindings, path);
            }

            foreach (var (name, value) in configured)
            {
                if (TryParseAction(name, out var action) && TryParseGesture(value, out var gesture))
                {
                    bindings[action] = gesture;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        return new ShortcutSettings(bindings, path);
    }

    internal IReadOnlyList<ShortcutBindingDisplay> BindingsForDisplay()
    {
        var bindings = new List<ShortcutBindingDisplay>(ActionOrder.Length);
        foreach (var action in ActionOrder)
        {
            if (_bindings.TryGetValue(action, out var gesture))
            {
                bindings.Add(new ShortcutBindingDisplay(DisplayName(action), FormatGesture(gesture)));
            }
        }

        return bindings;
    }

    public bool TryMatch(Key key, ModifierKeys modifiers, out ShortcutAction action)
    {
        var normalizedModifiers = modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.Windows);
        foreach (var candidate in ActionOrder)
        {
            if (_bindings.TryGetValue(candidate, out var binding)
                && binding.Key == key
                && binding.Modifiers == normalizedModifiers)
            {
                action = candidate;
                return true;
            }
        }

        action = ShortcutAction.FocusNext;
        return false;
    }

    internal static bool TryParseGesture(string? value, out ShortcutGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var key = Key.None;
        var modifiers = ModifierKeys.None;
        foreach (var rawPart in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (rawPart.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModifierKeys.Control;
                    continue;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    continue;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    continue;
                case "win":
                case "windows":
                    modifiers |= ModifierKeys.Windows;
                    continue;
            }

            if (key != Key.None || !TryParseKey(rawPart, out key))
            {
                return false;
            }
        }

        if (key == Key.None)
        {
            return false;
        }

        if (IsTextKey(key) && !modifiers.HasFlag(ModifierKeys.Control) && !modifiers.HasFlag(ModifierKeys.Alt))
        {
            return false;
        }

        gesture = new ShortcutGesture(key, modifiers);
        return true;
    }

    internal static string DefaultFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentMux",
            "shortcuts.json");

    private static Dictionary<ShortcutAction, ShortcutGesture> CloneDefaultBindings() => new(DefaultBindings);

    private static string DisplayName(ShortcutAction action) => action switch
    {
        ShortcutAction.ToggleZoom => "Toggle zoom",
        ShortcutAction.ClosePane => "Close pane",
        ShortcutAction.FocusLeft => "Focus left",
        ShortcutAction.FocusRight => "Focus right",
        ShortcutAction.FocusUp => "Focus up",
        ShortcutAction.FocusDown => "Focus down",
        ShortcutAction.FocusPrevious => "Focus previous pane",
        ShortcutAction.FocusNext => "Focus next pane",
        _ => action.ToString()
    };

    private static string FormatGesture(ShortcutGesture gesture)
    {
        var parts = new List<string>(5);
        if (gesture.Modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (gesture.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (gesture.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (gesture.Modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(FormatKey(gesture.Key));
        return string.Join("+", parts);
    }

    private static string FormatKey(Key key) => key switch
    {
        Key.Left => "Left",
        Key.Right => "Right",
        Key.Up => "Up",
        Key.Down => "Down",
        Key.PageUp => "PageUp",
        Key.PageDown => "PageDown",
        Key.Back => "Backspace",
        Key.Return => "Enter",
        Key.D0 => "0",
        Key.D1 => "1",
        Key.D2 => "2",
        Key.D3 => "3",
        Key.D4 => "4",
        Key.D5 => "5",
        Key.D6 => "6",
        Key.D7 => "7",
        Key.D8 => "8",
        Key.D9 => "9",
        _ => key.ToString()
    };

    private static bool TryParseAction(string value, out ShortcutAction action)
    {
        var normalized = value.Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);
        foreach (var candidate in Enum.GetValues<ShortcutAction>())
        {
            if (string.Equals(normalized, candidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                action = candidate;
                return true;
            }
        }

        action = ShortcutAction.FocusNext;
        return false;
    }

    private static bool TryParseKey(string value, out Key key)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("Arrow", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["Arrow".Length..];
        }
        else if (normalized.EndsWith("Arrow", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^"Arrow".Length];
        }

        return Enum.TryParse(normalized, ignoreCase: true, out key) && key != Key.None;
    }

    private static bool IsTextKey(Key key) =>
        key is >= Key.A and <= Key.Z
            or >= Key.D0 and <= Key.D9
            or >= Key.NumPad0 and <= Key.NumPad9
            or Key.Space;

    private static class ShortcutJson
    {
        public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
    }
}

internal enum ShortcutAction
{
    FocusNext,
    FocusPrevious,
    FocusLeft,
    FocusRight,
    FocusUp,
    FocusDown,
    ToggleZoom,
    ClosePane
}

internal readonly record struct ShortcutGesture(Key Key, ModifierKeys Modifiers);

internal readonly record struct ShortcutBindingDisplay(string Action, string Gesture);
