namespace AgentMux.Core.Notifications;

public sealed record OscEvent(OscEventKind Kind, string? Title, string? Subtitle, string? Body, string? Value);

public enum OscEventKind
{
    Title,
    WorkingDirectory,
    Notification,
    PromptMarker,
    Unknown
}

public static class OscNotificationParser
{
    public static OscEvent Parse(string oscPayload)
    {
        if (string.IsNullOrWhiteSpace(oscPayload))
        {
            return Unknown();
        }

        var separatorIndex = oscPayload.IndexOf(';');
        var codeText = separatorIndex >= 0 ? oscPayload[..separatorIndex] : oscPayload;
        var body = separatorIndex >= 0 ? oscPayload[(separatorIndex + 1)..] : "";

        if (!int.TryParse(codeText, out var code))
        {
            return Unknown();
        }

        return code switch
        {
            0 or 2 => new OscEvent(OscEventKind.Title, null, null, null, body),
            7 => new OscEvent(OscEventKind.WorkingDirectory, null, null, null, NormalizeWorkingDirectory(body)),
            9 => new OscEvent(OscEventKind.Notification, "Terminal", null, body, null),
            99 => ParseOsc99(body),
            133 => new OscEvent(OscEventKind.PromptMarker, null, null, null, body),
            777 => ParseOsc777(body),
            _ => Unknown()
        };
    }

    private static OscEvent ParseOsc99(string payload)
    {
        if (!payload.Contains('=', StringComparison.Ordinal))
        {
            return new OscEvent(OscEventKind.Notification, "Terminal", null, payload, null);
        }

        string? title = null;
        string? subtitle = null;
        string? notificationBody = null;

        foreach (var pair in payload.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var equalsIndex = pair.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            var key = pair[..equalsIndex].Trim();
            var value = pair[(equalsIndex + 1)..].Trim();

            switch (key)
            {
                case "t":
                    title = value;
                    break;
                case "s":
                    subtitle = value;
                    break;
                case "b":
                    notificationBody = value;
                    break;
            }
        }

        return new OscEvent(
            OscEventKind.Notification,
            string.IsNullOrWhiteSpace(title) ? "Terminal" : title,
            subtitle,
            notificationBody ?? title ?? "",
            null);
    }

    private static OscEvent ParseOsc777(string payload)
    {
        var parts = payload.Split(';', 3);
        if (parts.Length > 0 && string.Equals(parts[0], "notify", StringComparison.OrdinalIgnoreCase))
        {
            var title = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1] : "Terminal";
            var notificationBody = parts.Length > 2 ? parts[2] : "";
            return new OscEvent(OscEventKind.Notification, title, null, notificationBody, null);
        }

        return new OscEvent(OscEventKind.Notification, "Terminal", null, payload, null);
    }

    private static string NormalizeWorkingDirectory(string payload)
    {
        if (!payload.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return payload;
        }

        if (Uri.TryCreate(payload, UriKind.Absolute, out var uri))
        {
            return Uri.UnescapeDataString(uri.LocalPath);
        }

        return payload;
    }

    private static OscEvent Unknown() => new(OscEventKind.Unknown, null, null, null, null);
}
