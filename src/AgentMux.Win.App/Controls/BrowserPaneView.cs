using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AgentMux.Win.App.Controls;

internal sealed class BrowserPaneView : Grid
{
    private const string DefaultUrl = "about:blank";

    private readonly TextBox _addressBox;
    private readonly Button _goButton;
    private readonly TextBlock _fallback;
    private readonly WebView2 _webView;
    private Task? _readyTask;
    private TaskCompletionSource? _navigationCompletion;
    private ulong? _pendingNavigationId;
    private string _url = DefaultUrl;
    private bool _webViewEventsWired;
    private bool _webViewReady;
    private bool _webViewFailed;

    public event EventHandler<string>? NavigateRequested;

    public bool IsAutomationReady => _webViewReady && !_webViewFailed && _webView.CoreWebView2 is not null;

    public BrowserPaneView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new Grid
        {
            Margin = new Thickness(0, 0, 0, 6)
        };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _addressBox = new TextBox
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 19, 26)),
            Foreground = new SolidColorBrush(Color.FromRgb(238, 242, 248)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(45, 52, 64)),
            FontSize = 13,
            Height = 28,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = _url
        };
        _addressBox.KeyDown += AddressBox_KeyDown;
        toolbar.Children.Add(_addressBox);

        _goButton = new Button
        {
            Content = "Go",
            Width = 48,
            Height = 28,
            Margin = new Thickness(6, 0, 0, 0)
        };
        _goButton.Click += (_, _) => RequestNavigation();
        Grid.SetColumn(_goButton, 1);
        toolbar.Children.Add(_goButton);

        _fallback = new TextBlock
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 19, 26)),
            Foreground = new SolidColorBrush(Color.FromRgb(238, 242, 248)),
            Padding = new Thickness(10),
            TextWrapping = TextWrapping.Wrap,
            Text = FallbackText(_url)
        };

        _webView = new WebView2
        {
            CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = WebViewUserDataFolder()
            },
            Visibility = Visibility.Collapsed
        };

        Grid.SetRow(toolbar, 0);
        Grid.SetRow(_fallback, 1);
        Grid.SetRow(_webView, 1);
        Children.Add(toolbar);
        Children.Add(_fallback);
        Children.Add(_webView);

        Loaded += OnLoaded;
    }

    public void SetUrl(string? url)
    {
        var normalizedUrl = string.IsNullOrWhiteSpace(url) ? DefaultUrl : url;
        if (string.Equals(_url, normalizedUrl, StringComparison.Ordinal))
        {
            return;
        }

        _url = normalizedUrl;
        _addressBox.Text = _url;
        _fallback.Text = FallbackText(_url);

        if (_webViewReady)
        {
            NavigateWebView();
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await EnsureReadyAsync().ConfigureAwait(true);
        }
        catch
        {
        }
    }

    private void NavigateWebView()
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        if (!Uri.TryCreate(_url, UriKind.Absolute, out var uri))
        {
            UseFallback();
            return;
        }

        try
        {
            _navigationCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingNavigationId = null;
            _webView.CoreWebView2.Navigate(uri.AbsoluteUri);
        }
        catch
        {
            _navigationCompletion?.TrySetResult();
            UseFallback();
        }
    }

    public async Task<string> EvaluateScriptAsync(string script)
    {
        await EnsureReadyAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(script))
        {
            return "null";
        }

        var result = await _webView.CoreWebView2!.ExecuteScriptAsync(script).ConfigureAwait(true);
        await WaitForNavigationAsync(allowStartDelay: true).ConfigureAwait(true);
        return result;
    }

    public async Task<string> ClickAsync(string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return """{"ok":false,"reason":"selector is required"}""";
        }

        var targetJson = await EvaluateScriptAsync($$"""
            (() => {
              const selector = {{JsonSerializer.Serialize(selector)}};
              const element = document.querySelector(selector);
              if (!element) {
                return { ok: false, reason: "selector not found", selector };
              }

              element.scrollIntoView({ block: "center", inline: "center" });
              const rect = element.getBoundingClientRect();
              if (!rect || rect.width <= 0 || rect.height <= 0) {
                return { ok: false, reason: "selector is not visible", selector };
              }

              const x = Math.min(Math.max(rect.left + rect.width / 2, 0), window.innerWidth - 1);
              const y = Math.min(Math.max(rect.top + rect.height / 2, 0), window.innerHeight - 1);
              const hit = document.elementFromPoint(x, y);
              if (!hit || (hit !== element && !element.contains(hit) && !hit.contains(element))) {
                return { ok: false, reason: "selector center is covered", selector };
              }

              return { ok: true, selector, x, y };
            })()
            """).ConfigureAwait(true);

        var target = ParseAutomationResult(targetJson);
        if (!target.Ok)
        {
            return targetJson;
        }

        await DispatchMouseEventAsync("mouseMoved", target.X, target.Y).ConfigureAwait(true);
        await DispatchMouseEventAsync("mousePressed", target.X, target.Y).ConfigureAwait(true);
        await DispatchMouseEventAsync("mouseReleased", target.X, target.Y).ConfigureAwait(true);
        await WaitForNavigationAsync(allowStartDelay: true).ConfigureAwait(true);
        return JsonSerializer.Serialize(new { ok = true, selector, x = target.X, y = target.Y });
    }

    public async Task<string> FillAsync(string selector, string text)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return """{"ok":false,"reason":"selector is required"}""";
        }

        return await EvaluateScriptAsync($$"""
            (() => {
              const selector = {{JsonSerializer.Serialize(selector)}};
              const value = {{JsonSerializer.Serialize(text)}};
              const element = document.querySelector(selector);
              if (!element) {
                return { ok: false, reason: "selector not found", selector };
              }

              if ("value" in element) {
                element.value = value;
              } else {
                element.textContent = value;
              }

              element.dispatchEvent(new Event("input", { bubbles: true }));
              element.dispatchEvent(new Event("change", { bubbles: true }));
              return { ok: true, selector };
            })()
            """).ConfigureAwait(true);
    }

    public async Task<string> TypeAsync(string selector, string text)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return """{"ok":false,"reason":"selector is required"}""";
        }

        var clickResult = await ClickAsync(selector).ConfigureAwait(true);
        var click = ParseAutomationResult(clickResult);
        if (!click.Ok)
        {
            return clickResult;
        }

        var focusResult = await FocusForInputAsync(selector).ConfigureAwait(true);
        var focus = ParseAutomationResult(focusResult);
        if (!focus.Ok)
        {
            return focusResult;
        }

        await CallDevToolsInputAsync("Input.insertText", new Dictionary<string, object?>
        {
            ["text"] = text
        }).ConfigureAwait(true);
        await WaitForNavigationAsync(allowStartDelay: true).ConfigureAwait(true);
        return JsonSerializer.Serialize(new { ok = true, selector, textLength = text.Length });
    }

    public async Task<string> PressAsync(string key, string? selector)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return """{"ok":false,"reason":"key is required"}""";
        }

        var normalizedKey = key.Trim();
        if (normalizedKey.Contains('+') || normalizedKey.Contains(' ') || normalizedKey.Contains('\t'))
        {
            return JsonSerializer.Serialize(new { ok = false, reason = "unsupported key", key });
        }

        if (!string.IsNullOrWhiteSpace(selector))
        {
            var clickResult = await ClickAsync(selector).ConfigureAwait(true);
            var click = ParseAutomationResult(clickResult);
            if (!click.Ok)
            {
                return clickResult;
            }
        }
        else
        {
            await EnsureReadyAsync().ConfigureAwait(true);
        }

        if (!TryMapKey(normalizedKey, out var mappedKey))
        {
            return JsonSerializer.Serialize(new { ok = false, reason = "unsupported key", key });
        }

        await DispatchKeyEventAsync("keyDown", mappedKey).ConfigureAwait(true);
        await DispatchKeyEventAsync("keyUp", mappedKey).ConfigureAwait(true);
        await WaitForNavigationAsync(allowStartDelay: true).ConfigureAwait(true);
        return JsonSerializer.Serialize(new { ok = true, key = mappedKey.Key, code = mappedKey.Code });
    }

    private async Task<string> FocusForInputAsync(string selector)
    {
        return await EvaluateScriptAsync($$"""
            (() => {
              const selector = {{JsonSerializer.Serialize(selector)}};
              const element = document.querySelector(selector);
              if (!element) {
                return { ok: false, reason: "selector not found", selector };
              }

              const isEditable = node =>
                !!node && (node.matches?.("input:not([type='button']):not([type='submit']):not([type='reset']):not([type='checkbox']):not([type='radio']), textarea") || node.isContentEditable);
              const target = isEditable(element)
                ? element
                : element.querySelector?.("input:not([type='button']):not([type='submit']):not([type='reset']):not([type='checkbox']):not([type='radio']), textarea, [contenteditable=''], [contenteditable='true'], [contenteditable='plaintext-only']");

              if (!target || typeof target.focus !== "function") {
                return { ok: false, reason: "selector is not text-editable", selector };
              }

              target.focus({ preventScroll: true });
              const active = document.activeElement;
              if (active !== target && !target.contains(active)) {
                return { ok: false, reason: "selector did not receive focus", selector };
              }

              if (!isEditable(active)) {
                return { ok: false, reason: "focused element is not text-editable", selector };
              }

              return { ok: true, selector };
            })()
            """).ConfigureAwait(true);
    }

    public async Task<string> CapturePngAsync(string path)
    {
        await EnsureReadyAsync().ConfigureAwait(true);
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

    public async Task<string> GetFrameTreeAsync()
    {
        await EnsureReadyAsync().ConfigureAwait(true);
        try
        {
            var frameTreeJson = await _webView.CoreWebView2!.CallDevToolsProtocolMethodAsync("Page.getFrameTree", "{}").ConfigureAwait(true);
            using var document = JsonDocument.Parse(frameTreeJson);
            if (!document.RootElement.TryGetProperty("frameTree", out var frameTree)
                || frameTree.ValueKind != JsonValueKind.Object)
            {
                return JsonSerializer.Serialize(new { ok = false, reason = "frame tree unavailable" });
            }

            return JsonSerializer.Serialize(new
            {
                ok = true,
                frameTree = frameTree.Clone()
            });
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new { ok = false, reason = "frame tree unavailable" });
        }
    }

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        RequestNavigation();
    }

    private void RequestNavigation()
    {
        NavigateRequested?.Invoke(this, _addressBox.Text);
    }

    private async Task EnsureReadyAsync()
    {
        if (!IsAutomationReady)
        {
            if (_webViewFailed)
            {
                throw new InvalidOperationException("browser runtime is not ready");
            }

            _readyTask ??= InitializeWebViewAsync();
            await _readyTask.ConfigureAwait(true);
        }

        await WaitForNavigationAsync().ConfigureAwait(true);
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            await _webView.EnsureCoreWebView2Async().ConfigureAwait(true);
            WireWebViewEvents();
            _webViewReady = true;
            _webView.Visibility = Visibility.Visible;
            _fallback.Visibility = Visibility.Collapsed;
            NavigateWebView();
        }
        catch (Exception ex)
        {
            UseFallback();
            throw new InvalidOperationException("browser runtime is not ready", ex);
        }
    }

    private void WireWebViewEvents()
    {
        if (_webViewEventsWired || _webView.CoreWebView2 is null)
        {
            return;
        }

        _webView.CoreWebView2.NavigationStarting += (_, args) =>
        {
            _pendingNavigationId = args.NavigationId;
            if (_navigationCompletion is null || _navigationCompletion.Task.IsCompleted)
            {
                _navigationCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        };
        _webView.CoreWebView2.NavigationCompleted += (_, args) =>
        {
            if (_pendingNavigationId == args.NavigationId)
            {
                _pendingNavigationId = null;
                _navigationCompletion?.TrySetResult();
            }
        };
        _webViewEventsWired = true;
    }

    private async Task WaitForNavigationAsync(bool allowStartDelay = false)
    {
        if (allowStartDelay && (_navigationCompletion is null || _navigationCompletion.Task.IsCompleted))
        {
            await Task.Delay(100).ConfigureAwait(true);
        }

        var navigationTask = _navigationCompletion?.Task;
        if (navigationTask is null || navigationTask.IsCompleted)
        {
            return;
        }

        try
        {
            await navigationTask.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(true);
        }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException("browser navigation is still loading", ex);
        }
    }

    private void UseFallback()
    {
        _webViewReady = false;
        _webViewFailed = true;
        _navigationCompletion?.TrySetResult();
        _webView.Visibility = Visibility.Collapsed;
        _fallback.Visibility = Visibility.Visible;
    }

    private static string FallbackText(string url) => $"Browser pane: {url}";

    private async Task DispatchMouseEventAsync(string type, double x, double y)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["x"] = x,
            ["y"] = y,
            ["pointerType"] = "mouse"
        };

        if (type == "mousePressed")
        {
            payload["button"] = "left";
            payload["buttons"] = 1;
            payload["clickCount"] = 1;
        }
        else if (type == "mouseReleased")
        {
            payload["button"] = "left";
            payload["buttons"] = 0;
            payload["clickCount"] = 1;
        }

        await CallDevToolsInputAsync("Input.dispatchMouseEvent", payload).ConfigureAwait(true);
    }

    private async Task DispatchKeyEventAsync(string type, BrowserKey key)
    {
        await CallDevToolsInputAsync("Input.dispatchKeyEvent", new Dictionary<string, object?>
        {
            ["type"] = type,
            ["key"] = key.Key,
            ["code"] = key.Code,
            ["windowsVirtualKeyCode"] = key.VirtualKeyCode,
            ["nativeVirtualKeyCode"] = key.VirtualKeyCode
        }).ConfigureAwait(true);
    }

    private async Task CallDevToolsInputAsync(string method, Dictionary<string, object?> payload)
    {
        await EnsureReadyAsync().ConfigureAwait(true);
        var json = JsonSerializer.Serialize(payload);
        await _webView.CoreWebView2!.CallDevToolsProtocolMethodAsync(method, json).ConfigureAwait(true);
    }

    private static AutomationTarget ParseAutomationResult(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var ok = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("ok", out var okElement)
                && okElement.ValueKind == JsonValueKind.True;
            var x = root.TryGetProperty("x", out var xElement) && xElement.TryGetDouble(out var parsedX) ? parsedX : 0;
            var y = root.TryGetProperty("y", out var yElement) && yElement.TryGetDouble(out var parsedY) ? parsedY : 0;
            return new AutomationTarget(ok, x, y);
        }
        catch (JsonException)
        {
            return new AutomationTarget(false, 0, 0);
        }
    }

    private static bool TryMapKey(string value, out BrowserKey key)
    {
        key = value.Trim().ToLowerInvariant() switch
        {
            "enter" or "return" => new BrowserKey("Enter", "Enter", 13),
            "tab" => new BrowserKey("Tab", "Tab", 9),
            "escape" or "esc" => new BrowserKey("Escape", "Escape", 27),
            "backspace" => new BrowserKey("Backspace", "Backspace", 8),
            "delete" or "del" => new BrowserKey("Delete", "Delete", 46),
            "home" => new BrowserKey("Home", "Home", 36),
            "end" => new BrowserKey("End", "End", 35),
            "pageup" or "pgup" => new BrowserKey("PageUp", "PageUp", 33),
            "pagedown" or "pgdown" => new BrowserKey("PageDown", "PageDown", 34),
            "arrowleft" or "left" => new BrowserKey("ArrowLeft", "ArrowLeft", 37),
            "arrowright" or "right" => new BrowserKey("ArrowRight", "ArrowRight", 39),
            "arrowup" or "up" => new BrowserKey("ArrowUp", "ArrowUp", 38),
            "arrowdown" or "down" => new BrowserKey("ArrowDown", "ArrowDown", 40),
            "space" => new BrowserKey(" ", "Space", 32),
            _ => default
        };

        if (key.VirtualKeyCode != 0)
        {
            return true;
        }

        if (value.Length != 1)
        {
            return false;
        }

        var character = value[0];
        if (char.IsLetter(character))
        {
            var upper = char.ToUpperInvariant(character);
            key = new BrowserKey(character.ToString(), $"Key{upper}", upper);
            return true;
        }

        if (char.IsDigit(character))
        {
            key = new BrowserKey(character.ToString(), $"Digit{character}", character);
            return true;
        }

        return false;
    }

    private static string WebViewUserDataFolder() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentMux",
            "WebView2");

    private readonly record struct AutomationTarget(bool Ok, double X, double Y);

    private readonly record struct BrowserKey(string Key, string Code, int VirtualKeyCode);
}
