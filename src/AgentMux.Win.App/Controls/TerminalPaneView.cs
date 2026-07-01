using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AgentMux.Win.App.Controls;

internal sealed class TerminalPaneView : Grid
{
    private const string EmptyText = "No terminal output yet.";
    private const string InputMessageType = "input";

    private readonly WebView2 _webView;
    private readonly TextBox _fallback;
    private readonly Queue<string> _pendingScripts = [];
    private TaskCompletionSource? _runtimeReady;
    private string _screenText = EmptyText;
    private int _cols = 120;
    private int _rows = 30;
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
            CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = WebViewUserDataFolder()
            },
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

    public void ResizeTerminal(int cols, int rows)
    {
        if (cols <= 0 || rows <= 0)
        {
            return;
        }

        _cols = cols;
        _rows = rows;

        if (_webViewReady)
        {
            QueueTerminalResize();
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
                QueueTerminalResize();
                QueueTerminalScript("agentmuxSetText", _screenText);
                await FlushTerminalScriptsAsync().ConfigureAwait(true);
                _runtimeReady?.TrySetResult();
            };
            _webView.Source = new Uri(terminalHtmlPath);
        }
        catch
        {
            _webViewInitializing = false;
            UseFallback();
        }
    }

    internal async Task EnsureRuntimeReadyForSmokeTestAsync()
    {
        if (!_webViewReady && !_webViewFailed)
        {
            _runtimeReady ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_webViewInitializing)
            {
                OnLoaded(this, new RoutedEventArgs(LoadedEvent));
            }

            await _runtimeReady.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
        }

        if (!_webViewReady || _webView.CoreWebView2 is null)
        {
            throw new InvalidOperationException("terminal WebView2 runtime is not ready");
        }
    }

    internal async Task<string> ExecuteRuntimeScriptForSmokeTestAsync(string script)
    {
        await EnsureRuntimeReadyForSmokeTestAsync().ConfigureAwait(true);
        return await _webView.CoreWebView2!.ExecuteScriptAsync(script).ConfigureAwait(true);
    }

    internal async Task<string> WaitForRuntimeTextForSmokeTestAsync(string expectedText)
    {
        await EnsureRuntimeReadyForSmokeTestAsync().ConfigureAwait(true);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        var lastText = string.Empty;

        while (DateTimeOffset.UtcNow < deadline)
        {
            lastText = await ReadRuntimeTextForSmokeTestAsync().ConfigureAwait(true);
            if (lastText.Contains(expectedText, StringComparison.Ordinal))
            {
                return lastText;
            }

            await Task.Delay(50).ConfigureAwait(true);
        }

        throw new InvalidOperationException($"terminal WebView2 runtime did not render expected text. Last text: {lastText}");
    }

    internal async Task WaitForRuntimeGeometryForSmokeTestAsync(int cols, int rows)
    {
        await EnsureRuntimeReadyForSmokeTestAsync().ConfigureAwait(true);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        var lastGeometry = string.Empty;

        while (DateTimeOffset.UtcNow < deadline)
        {
            lastGeometry = await ExecuteRuntimeScriptForSmokeTestAsync("""
                (() => typeof window.agentmuxGetGeometryForSmoke === "function"
                    ? window.agentmuxGetGeometryForSmoke()
                    : { cols: 0, rows: 0 })()
                """).ConfigureAwait(true);

            using var document = JsonDocument.Parse(lastGeometry);
            var root = document.RootElement;
            if (root.GetProperty("cols").GetInt32() == cols && root.GetProperty("rows").GetInt32() == rows)
            {
                return;
            }

            await Task.Delay(50).ConfigureAwait(true);
        }

        throw new InvalidOperationException($"terminal WebView2 runtime did not resize to {cols}x{rows}. Last geometry: {lastGeometry}");
    }

    internal async Task<string> CapturePngForSmokeTestAsync(string path)
    {
        await EnsureRuntimeReadyForSmokeTestAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("screenshot path is required");
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(fullPath);
        await _webView.CoreWebView2!.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream).ConfigureAwait(true);
        return fullPath;
    }

    internal async Task<bool> EmitInputForSmokeTestAsync(string input)
    {
        await EnsureRuntimeReadyForSmokeTestAsync().ConfigureAwait(true);
        var json = JsonSerializer.Serialize(input);
        var resultJson = await ExecuteRuntimeScriptForSmokeTestAsync($"""
            (() => typeof window.agentmuxEmitInputForSmoke === "function"
                ? window.agentmuxEmitInputForSmoke({json})
                : false)()
            """).ConfigureAwait(true);

        return JsonSerializer.Deserialize<bool>(resultJson);
    }

    private async Task<string> ReadRuntimeTextForSmokeTestAsync()
    {
        var json = await ExecuteRuntimeScriptForSmokeTestAsync("""
            (() => typeof window.agentmuxGetTextForSmoke === "function"
                ? window.agentmuxGetTextForSmoke()
                : "")()
            """).ConfigureAwait(true);

        return JsonSerializer.Deserialize<string>(json) ?? string.Empty;
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
        var json = JsonSerializer.Serialize(text);
        QueueRawTerminalScript($"window.{functionName}({json});");
    }

    private void QueueTerminalResize()
    {
        QueueRawTerminalScript($"window.agentmuxResize({_cols}, {_rows});");
    }

    private void QueueRawTerminalScript(string script)
    {
        _pendingScripts.Enqueue(script);
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
                var script = _pendingScripts.Dequeue();
                await ExecuteTerminalScriptAsync(script).ConfigureAwait(true);
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

    private async Task ExecuteTerminalScriptAsync(string script)
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        await _webView.ExecuteScriptAsync(script).ConfigureAwait(true);
    }

    private void UseFallback()
    {
        _webViewReady = false;
        _webViewFailed = true;
        _pendingScripts.Clear();
        _runtimeReady?.TrySetException(new InvalidOperationException("terminal WebView2 runtime is not ready"));
        _webView.Visibility = Visibility.Collapsed;
        _fallback.Visibility = Visibility.Visible;
    }

    private static string WebViewUserDataFolder() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentMux",
            "WebView2");

}
