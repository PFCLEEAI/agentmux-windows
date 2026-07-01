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

    private ShortcutSettings(Dictionary<ShortcutAction, ShortcutGesture> bindings)
    {
        _bindings = bindings;
    }

    public static ShortcutSettings Load()
    {
        var configuredPath = Environment.GetEnvironmentVariable("AGENTMUX_SHORTCUTS_PATH");
        return LoadFromFile(string.IsNullOrWhiteSpace(configuredPath) ? DefaultFilePath() : configuredPath);
    }

    internal static ShortcutSettings Default() => new(CloneDefaultBindings());

    internal static ShortcutSettings LoadFromFile(string path)
    {
        var bindings = CloneDefaultBindings();
        if (!File.Exists(path))
        {
            return new ShortcutSettings(bindings);
        }

        try
        {
            var json = File.ReadAllText(path);
            var configured = JsonSerializer.Deserialize<Dictionary<string, string>>(json, ShortcutJson.Options);
            if (configured is null)
            {
                return new ShortcutSettings(bindings);
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

        return new ShortcutSettings(bindings);
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
