using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;

namespace AgentMux.Win.App.Controls;

internal sealed class TerminalPaneView : Grid
{
    private const string EmptyText = "No terminal output yet.";

    private readonly WebView2 _webView;
    private readonly TextBox _fallback;
    private string _screenText = EmptyText;
    private bool _webViewReady;
    private bool _webViewFailed;

    public TerminalPaneView()
    {
        _fallback = new TextBox
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 19, 26)),
            Foreground = new SolidColorBrush(Color.FromRgb(238, 242, 248)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(45, 52, 64)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            AcceptsReturn = true,
            IsReadOnly = true,
            Text = _screenText
        };
        _fallback.Loaded += (_, _) => _fallback.ScrollToEnd();

        _webView = new WebView2
        {
            Visibility = Visibility.Collapsed
        };

        Children.Add(_fallback);
        Children.Add(_webView);
        Loaded += OnLoaded;
    }

    public void SetScreenText(string? text)
    {
        _screenText = string.IsNullOrEmpty(text) ? EmptyText : text;
        _fallback.Text = _screenText;
        _fallback.ScrollToEnd();

        if (_webViewReady)
        {
            _ = PushScreenTextOrFallbackAsync();
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_webViewReady || _webViewFailed)
        {
            return;
        }

        try
        {
            await _webView.EnsureCoreWebView2Async().ConfigureAwait(true);
            _webView.NavigationCompleted += async (_, _) =>
            {
                _webViewReady = true;
                _webView.Visibility = Visibility.Visible;
                _fallback.Visibility = Visibility.Collapsed;
                await PushScreenTextOrFallbackAsync().ConfigureAwait(true);
            };
            _webView.NavigateToString(TerminalHtml);
        }
        catch
        {
            _webViewFailed = true;
            _webView.Visibility = Visibility.Collapsed;
            _fallback.Visibility = Visibility.Visible;
        }
    }

    private async Task PushScreenTextOrFallbackAsync()
    {
        try
        {
            await PushScreenTextAsync().ConfigureAwait(true);
        }
        catch
        {
            _webViewReady = false;
            _webViewFailed = true;
            _webView.Visibility = Visibility.Collapsed;
            _fallback.Visibility = Visibility.Visible;
        }
    }

    private async Task PushScreenTextAsync()
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(_screenText);
        await _webView.ExecuteScriptAsync($"window.agentmuxSetText({json});").ConfigureAwait(true);
    }

    private const string TerminalHtml = """
<!doctype html>
<html>
<head>
  <meta charset="utf-8" />
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'" />
  <style>
    html, body {
      height: 100%;
      width: 100%;
      margin: 0;
      background: #0f131a;
      color: #eef2f8;
      overflow: hidden;
      font: 13px Consolas, "Cascadia Mono", "Courier New", monospace;
    }

    #terminal {
      box-sizing: border-box;
      height: 100%;
      width: 100%;
      margin: 0;
      padding: 8px;
      overflow: auto;
      white-space: pre-wrap;
      word-break: break-word;
    }
  </style>
</head>
<body>
  <pre id="terminal"></pre>
  <script>
    const terminal = document.getElementById('terminal');
    window.agentmuxSetText = value => {
      terminal.textContent = value || 'No terminal output yet.';
      terminal.scrollTop = terminal.scrollHeight;
    };
    window.agentmuxSetText('');
  </script>
</body>
</html>
""";
}
