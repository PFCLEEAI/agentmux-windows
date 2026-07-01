using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace AgentMux.Win.App.Notifications;

internal sealed class WindowsNativeToastService : INativeToastService
{
    private const string ToastAppId = "AgentMux.Windows";
    private const int MaxTitleLength = 128;
    private const int MaxSubtitleLength = 192;
    private const int MaxBodyLength = 512;

    public NativeToastResult TryShow(NativeToastRequest request)
    {
        if (!OperatingSystem.IsWindows())
        {
            return NativeToastResult.Skipped("not running on Windows");
        }

        try
        {
            var toastXml = new XmlDocument();
            toastXml.LoadXml(BuildToastXml(request));
            var toast = new ToastNotification(toastXml);
            ToastNotificationManager.CreateToastNotifier(ToastAppId).Show(toast);
            return NativeToastResult.Sent();
        }
        catch (Exception ex) when (IsExpectedToastFailure(ex))
        {
            return NativeToastResult.Failed(ex.GetType().Name);
        }
    }

    internal static string BuildToastXml(NativeToastRequest request)
    {
        var title = NormalizeToastText(request.Title, MaxTitleLength);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = "AgentMux";
        }

        var subtitle = NormalizeToastText(request.Subtitle, MaxSubtitleLength);
        var body = NormalizeToastText(request.Body, MaxBodyLength);
        var builder = new StringBuilder();
        builder.Append("<toast><visual><binding template=\"ToastGeneric\">");
        AppendText(builder, title);

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            AppendText(builder, subtitle);
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            AppendText(builder, body);
        }

        builder.Append("</binding></visual></toast>");
        return builder.ToString();
    }

    private static bool IsExpectedToastFailure(Exception ex) =>
        ex is ArgumentException
            or COMException
            or FileNotFoundException
            or InvalidOperationException
            or NotSupportedException
            or UnauthorizedAccessException;

    private static void AppendText(StringBuilder builder, string value)
    {
        builder.Append("<text>");
        AppendXmlEscaped(builder, value);
        builder.Append("</text>");
    }

    private static string NormalizeToastText(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var builder = new StringBuilder(Math.Min(value.Length, maxLength));
        foreach (var character in value)
        {
            if (builder.Length >= maxLength)
            {
                break;
            }

            if (character == '\r' || character == '\n' || character == '\t')
            {
                builder.Append(' ');
                continue;
            }

            if (!char.IsControl(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Trim();
    }

    private static void AppendXmlEscaped(StringBuilder builder, string value)
    {
        foreach (var character in value)
        {
            switch (character)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                case '"':
                    builder.Append("&quot;");
                    break;
                case '\'':
                    builder.Append("&apos;");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }
    }
}
