using System.IO;
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
            var terminalHtmlPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Terminal", "terminal.html");
            if (!File.Exists(terminalHtmlPath))
            {
                UseFallback();
                return;
            }

            await _webView.EnsureCoreWebView2Async().ConfigureAwait(true);
            _webView.NavigationCompleted += async (_, _) =>
            {
                _webViewReady = true;
                _webView.Visibility = Visibility.Visible;
                _fallback.Visibility = Visibility.Collapsed;
                await PushScreenTextOrFallbackAsync().ConfigureAwait(true);
            };
            _webView.Source = new Uri(terminalHtmlPath);
        }
        catch
        {
            UseFallback();
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
            UseFallback();
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

    private void UseFallback()
    {
        _webViewReady = false;
        _webViewFailed = true;
        _webView.Visibility = Visibility.Collapsed;
        _fallback.Visibility = Visibility.Visible;
    }
}
