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
    private const string InputMessageType = "input";

    private readonly WebView2 _webView;
    private readonly TextBox _fallback;
    private readonly Queue<TerminalScriptCall> _pendingScripts = [];
    private string _screenText = EmptyText;
    private bool _isFlushingScripts;
    private bool _webViewInitializing;
    private bool _webViewReady;
    private bool _webViewFailed;

    public event EventHandler<string>? InputReceived;

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
        var normalizedText = string.IsNullOrEmpty(text) ? EmptyText : text;
        if (string.Equals(_screenText, normalizedText, StringComparison.Ordinal))
        {
            return;
        }

        _screenText = normalizedText;
        _fallback.Text = _screenText;
        _fallback.ScrollToEnd();

        if (_webViewReady)
        {
            QueueTerminalScript("agentmuxSetText", _screenText);
        }
    }

    public void AppendScreenText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        _screenText = _screenText == EmptyText ? text : string.Concat(_screenText, text);
        _fallback.Text = _screenText;
        _fallback.ScrollToEnd();

        if (_webViewReady)
        {
            QueueTerminalScript("agentmuxAppendText", text);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_webViewReady || _webViewFailed || _webViewInitializing)
        {
            return;
        }

        _webViewInitializing = true;
        try
        {
            var terminalHtmlPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Terminal", "terminal.html");
            if (!File.Exists(terminalHtmlPath))
            {
                _webViewInitializing = false;
                UseFallback();
                return;
            }

            await _webView.EnsureCoreWebView2Async().ConfigureAwait(true);
            _webView.CoreWebView2.WebMessageReceived += (_, args) => HandleWebMessage(args.WebMessageAsJson);
            _webView.NavigationCompleted += async (_, _) =>
            {
                _webViewInitializing = false;
                _webViewReady = true;
                _webView.Visibility = Visibility.Visible;
                _fallback.Visibility = Visibility.Collapsed;
                QueueTerminalScript("agentmuxSetText", _screenText);
                await FlushTerminalScriptsAsync().ConfigureAwait(true);
            };
            _webView.Source = new Uri(terminalHtmlPath);
        }
        catch
        {
            _webViewInitializing = false;
            UseFallback();
        }
    }

    private void HandleWebMessage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || !string.Equals(type.GetString(), InputMessageType, StringComparison.Ordinal)
                || !root.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.String)
            {
                return;
            }

            var input = data.GetString();
            if (!string.IsNullOrEmpty(input))
            {
                InputReceived?.Invoke(this, input);
            }
        }
        catch (JsonException)
        {
        }
    }

    private void QueueTerminalScript(string functionName, string text)
    {
        _pendingScripts.Enqueue(new TerminalScriptCall(functionName, text));
        if (!_isFlushingScripts)
        {
            _ = FlushTerminalScriptsAsync();
        }
    }

    private async Task FlushTerminalScriptsAsync()
    {
        if (_isFlushingScripts)
        {
            return;
        }

        _isFlushingScripts = true;
        try
        {
            while (_webViewReady && _pendingScripts.Count > 0)
            {
                var call = _pendingScripts.Dequeue();
                await ExecuteTerminalFunctionAsync(call.FunctionName, call.Text).ConfigureAwait(true);
            }
        }
        catch
        {
            _pendingScripts.Clear();
            UseFallback();
        }
        finally
        {
            _isFlushingScripts = false;
            if (_webViewReady && _pendingScripts.Count > 0)
            {
                _ = FlushTerminalScriptsAsync();
            }
        }
    }

    private async Task ExecuteTerminalFunctionAsync(string functionName, string text)
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(text);
        await _webView.ExecuteScriptAsync($"window.{functionName}({json});").ConfigureAwait(true);
    }

    private void UseFallback()
    {
        _webViewReady = false;
        _webViewFailed = true;
        _pendingScripts.Clear();
        _webView.Visibility = Visibility.Collapsed;
        _fallback.Visibility = Visibility.Visible;
    }

    private readonly record struct TerminalScriptCall(string FunctionName, string Text);
}
