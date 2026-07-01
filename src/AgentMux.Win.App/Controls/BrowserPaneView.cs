using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AgentMux.Core.Ipc;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AgentMux.Win.App.Controls;

internal sealed class BrowserPaneView : Grid, IDisposable
{
    private const string DefaultUrl = "about:blank";
    private const int MaxNetworkEventCount = 200;

    private readonly List<BrowserNetworkEvent> _networkEvents = [];
    private readonly TextBox _addressBox;
    private readonly Button _goButton;
    private readonly TextBlock _fallback;
    private readonly WebView2 _webView;
    private CoreWebView2DevToolsProtocolEventReceiver? _networkRequestWillBeSentReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? _networkResponseReceivedReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? _networkLoadingFailedReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? _networkLoadingFinishedReceiver;
    private Task? _readyTask;
    private TaskCompletionSource? _navigationCompletion;
    private ulong? _pendingNavigationId;
    private string _url = DefaultUrl;
    private long _networkEventSequence;
    private bool _webViewEventsWired;
    private bool _networkEventsEnabled;
    private bool _webViewReady;
    private bool _webViewFailed;
    private bool _disposed;

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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnwireNetworkEventLogging();
        _webView.Dispose();
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

    public async Task<string> ClickAsync(string selector, string? frame = null)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return """{"ok":false,"reason":"selector is required"}""";
        }

        var normalizedFrame = NormalizeFrame(frame);
        var targetJson = await EvaluateScriptAsync($$"""
            (() => {
              const selector = {{JsonSerializer.Serialize(selector)}};
              {{FrameScopeScript(normalizedFrame)}}
              const scope = resolveAutomationScope();
              if (!scope.ok) {
                return scope;
              }

              const element = scope.document.querySelector(selector);
              if (!element) {
                return { ok: false, reason: "selector not found", selector, frame: scope.frame };
              }

              element.scrollIntoView({ block: "center", inline: "center" });
              const rect = element.getBoundingClientRect();
              if (!rect || rect.width <= 0 || rect.height <= 0) {
                return { ok: false, reason: "selector is not visible", selector, frame: scope.frame };
              }

              const frameOffset = automationFrameOffset(scope);
              const localX = Math.min(Math.max(rect.left + rect.width / 2, 0), Math.max(scope.window.innerWidth - 1, 0));
              const localY = Math.min(Math.max(rect.top + rect.height / 2, 0), Math.max(scope.window.innerHeight - 1, 0));
              const hit = scope.document.elementFromPoint(localX, localY);
              if (!hit || (hit !== element && !element.contains(hit) && !hit.contains(element))) {
                return { ok: false, reason: "selector center is covered", selector, frame: scope.frame };
              }

              const x = Math.min(Math.max(frameOffset.x + localX, 0), Math.max(window.innerWidth - 1, 0));
              const y = Math.min(Math.max(frameOffset.y + localY, 0), Math.max(window.innerHeight - 1, 0));
              if (scope.offsetElement) {
                const topHit = document.elementFromPoint(x, y);
                if (topHit !== scope.offsetElement) {
                  return { ok: false, reason: "frame target is covered", selector, frame: scope.frame };
                }
              }

              return { ok: true, selector, frame: scope.frame, x, y };
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
        return JsonSerializer.Serialize(new { ok = true, selector, frame = normalizedFrame, x = target.X, y = target.Y });
    }

    public async Task<string> FillAsync(string selector, string text, string? frame = null)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return """{"ok":false,"reason":"selector is required"}""";
        }

        return await EvaluateScriptAsync($$"""
            (() => {
              const selector = {{JsonSerializer.Serialize(selector)}};
              const value = {{JsonSerializer.Serialize(text)}};
              {{FrameScopeScript(frame)}}
              const scope = resolveAutomationScope();
              if (!scope.ok) {
                return scope;
              }

              const element = scope.document.querySelector(selector);
              if (!element) {
                return { ok: false, reason: "selector not found", selector, frame: scope.frame };
              }

              if ("value" in element) {
                element.value = value;
              } else {
                element.textContent = value;
              }

              element.dispatchEvent(new Event("input", { bubbles: true }));
              element.dispatchEvent(new Event("change", { bubbles: true }));
              return { ok: true, selector, frame: scope.frame };
            })()
            """).ConfigureAwait(true);
    }

    public async Task<string> TypeAsync(string selector, string text, string? frame = null)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return """{"ok":false,"reason":"selector is required"}""";
        }

        var normalizedFrame = NormalizeFrame(frame);
        var clickResult = await ClickAsync(selector, normalizedFrame).ConfigureAwait(true);
        var click = ParseAutomationResult(clickResult);
        if (!click.Ok)
        {
            return clickResult;
        }

        var focusResult = await FocusForInputAsync(selector, normalizedFrame).ConfigureAwait(true);
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
        return JsonSerializer.Serialize(new { ok = true, selector, frame = normalizedFrame, textLength = text.Length });
    }

    public async Task<string> PressAsync(string key, string? selector, string? frame = null)
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

        var normalizedFrame = NormalizeFrame(frame);
        if (normalizedFrame is not null && string.IsNullOrWhiteSpace(selector))
        {
            return JsonSerializer.Serialize(new { ok = false, reason = "selector is required when frame is provided", frame = normalizedFrame });
        }

        if (!string.IsNullOrWhiteSpace(selector))
        {
            var clickResult = await ClickAsync(selector, normalizedFrame).ConfigureAwait(true);
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
        return JsonSerializer.Serialize(new { ok = true, key = mappedKey.Key, code = mappedKey.Code, frame = normalizedFrame });
    }

    private async Task<string> FocusForInputAsync(string selector, string? frame)
    {
        return await EvaluateScriptAsync($$"""
            (() => {
              const selector = {{JsonSerializer.Serialize(selector)}};
              {{FrameScopeScript(frame)}}
              const scope = resolveAutomationScope();
              if (!scope.ok) {
                return scope;
              }

              const element = scope.document.querySelector(selector);
              if (!element) {
                return { ok: false, reason: "selector not found", selector, frame: scope.frame };
              }

              const isEditable = node =>
                !!node && (node.matches?.("input:not([type='button']):not([type='submit']):not([type='reset']):not([type='checkbox']):not([type='radio']), textarea") || node.isContentEditable);
              const target = isEditable(element)
                ? element
                : element.querySelector?.("input:not([type='button']):not([type='submit']):not([type='reset']):not([type='checkbox']):not([type='radio']), textarea, [contenteditable=''], [contenteditable='true'], [contenteditable='plaintext-only']");

              if (!target || typeof target.focus !== "function") {
                return { ok: false, reason: "selector is not text-editable", selector, frame: scope.frame };
              }

              target.focus({ preventScroll: true });
              const active = scope.document.activeElement;
              if (active !== target && !target.contains(active)) {
                return { ok: false, reason: "selector did not receive focus", selector, frame: scope.frame };
              }

              if (!isEditable(active)) {
                return { ok: false, reason: "focused element is not text-editable", selector, frame: scope.frame };
              }

              return { ok: true, selector, frame: scope.frame };
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

    public async Task<string> GetNetworkLogAsync(int? limit = null)
    {
        await EnsureReadyAsync().ConfigureAwait(true);
        var cappedLimit = limit is > 0
            ? Math.Min(limit.Value, MaxNetworkEventCount)
            : MaxNetworkEventCount;

        BrowserNetworkEvent[] events;
        int count;
        lock (_networkEvents)
        {
            count = _networkEvents.Count;
            events = _networkEvents
                .Skip(Math.Max(0, _networkEvents.Count - cappedLimit))
                .ToArray();
        }

        return JsonSerializer.Serialize(new
        {
            ok = true,
            count,
            maxEvents = MaxNetworkEventCount,
            events
        }, AgentMuxJson.Options);
    }

    public async Task<string> ClearNetworkLogAsync()
    {
        await EnsureReadyAsync().ConfigureAwait(true);
        int cleared;
        lock (_networkEvents)
        {
            cleared = _networkEvents.Count;
            _networkEvents.Clear();
        }

        return JsonSerializer.Serialize(new { ok = true, cleared }, AgentMuxJson.Options);
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
            await EnableNetworkEventLoggingAsync().ConfigureAwait(true);
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

    private async Task EnableNetworkEventLoggingAsync()
    {
        if (_networkEventsEnabled || _webView.CoreWebView2 is null)
        {
            return;
        }

        var core = _webView.CoreWebView2;
        _networkRequestWillBeSentReceiver = core.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");
        _networkResponseReceivedReceiver = core.GetDevToolsProtocolEventReceiver("Network.responseReceived");
        _networkLoadingFailedReceiver = core.GetDevToolsProtocolEventReceiver("Network.loadingFailed");
        _networkLoadingFinishedReceiver = core.GetDevToolsProtocolEventReceiver("Network.loadingFinished");

        _networkRequestWillBeSentReceiver.DevToolsProtocolEventReceived += NetworkRequestWillBeSent;
        _networkResponseReceivedReceiver.DevToolsProtocolEventReceived += NetworkResponseReceived;
        _networkLoadingFailedReceiver.DevToolsProtocolEventReceived += NetworkLoadingFailed;
        _networkLoadingFinishedReceiver.DevToolsProtocolEventReceived += NetworkLoadingFinished;

        try
        {
            await core.CallDevToolsProtocolMethodAsync("Network.enable", "{}").ConfigureAwait(true);
            _networkEventsEnabled = true;
        }
        catch
        {
            UnwireNetworkEventLogging();
            throw;
        }
    }

    private void UnwireNetworkEventLogging()
    {
        if (_networkRequestWillBeSentReceiver is not null)
        {
            _networkRequestWillBeSentReceiver.DevToolsProtocolEventReceived -= NetworkRequestWillBeSent;
            _networkRequestWillBeSentReceiver = null;
        }

        if (_networkResponseReceivedReceiver is not null)
        {
            _networkResponseReceivedReceiver.DevToolsProtocolEventReceived -= NetworkResponseReceived;
            _networkResponseReceivedReceiver = null;
        }

        if (_networkLoadingFailedReceiver is not null)
        {
            _networkLoadingFailedReceiver.DevToolsProtocolEventReceived -= NetworkLoadingFailed;
            _networkLoadingFailedReceiver = null;
        }

        if (_networkLoadingFinishedReceiver is not null)
        {
            _networkLoadingFinishedReceiver.DevToolsProtocolEventReceived -= NetworkLoadingFinished;
            _networkLoadingFinishedReceiver = null;
        }
    }

    private void NetworkRequestWillBeSent(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs args) =>
        RecordNetworkEvent("requestWillBeSent", args);

    private void NetworkResponseReceived(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs args) =>
        RecordNetworkEvent("responseReceived", args);

    private void NetworkLoadingFailed(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs args) =>
        RecordNetworkEvent("loadingFailed", args);

    private void NetworkLoadingFinished(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs args) =>
        RecordNetworkEvent("loadingFinished", args);

    private void RecordNetworkEvent(string eventName, CoreWebView2DevToolsProtocolEventReceivedEventArgs args)
    {
        lock (_networkEvents)
        {
            _networkEvents.Add(CreateNetworkEvent(++_networkEventSequence, eventName, args.SessionId, args.ParameterObjectAsJson));
            if (_networkEvents.Count > MaxNetworkEventCount)
            {
                _networkEvents.RemoveRange(0, _networkEvents.Count - MaxNetworkEventCount);
            }
        }
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

    private static BrowserNetworkEvent CreateNetworkEvent(long sequence, string eventName, string? sessionId, string parametersJson)
    {
        string? requestId = null;
        string? url = null;
        string? method = null;
        int? status = null;
        string? resourceType = null;
        string? mimeType = null;
        string? errorText = null;
        bool? canceled = null;
        double? encodedDataLength = null;

        try
        {
            using var document = JsonDocument.Parse(parametersJson);
            var root = document.RootElement;
            requestId = JsonString(root, "requestId");
            resourceType = JsonString(root, "type");

            if (root.TryGetProperty("request", out var request) && request.ValueKind == JsonValueKind.Object)
            {
                url = JsonString(request, "url");
                method = JsonString(request, "method");
            }

            if (root.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Object)
            {
                url ??= JsonString(response, "url");
                status = JsonInt(response, "status");
                mimeType = JsonString(response, "mimeType");
            }

            url ??= JsonString(root, "documentURL");
            errorText = JsonString(root, "errorText");
            canceled = JsonBool(root, "canceled");
            encodedDataLength = JsonDouble(root, "encodedDataLength");
        }
        catch (JsonException)
        {
            errorText = "network event parse failed";
        }

        return new BrowserNetworkEvent(
            sequence,
            eventName,
            requestId,
            url,
            method,
            status,
            resourceType,
            mimeType,
            errorText,
            canceled,
            encodedDataLength,
            string.IsNullOrWhiteSpace(sessionId) ? null : sessionId,
            DateTimeOffset.UtcNow);
    }

    private static string? JsonString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int? JsonInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : null;

    private static double? JsonDouble(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var value)
            ? value
            : null;

    private static bool? JsonBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
            ? property.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            }
            : null;

    private static string? NormalizeFrame(string? frame) =>
        string.IsNullOrWhiteSpace(frame) ? null : frame.Trim();

    private static string FrameScopeScript(string? frame)
    {
        var normalizedFrame = NormalizeFrame(frame);
        return $$"""
              const frameName = {{JsonSerializer.Serialize(normalizedFrame)}};
              const resolveAutomationScope = () => {
                if (!frameName) {
                  return { ok: true, document, window, offsetElement: null, frame: null };
                }

                const frameElement = Array.from(document.querySelectorAll("iframe, frame")).find(candidate =>
                  candidate.name === frameName ||
                  candidate.id === frameName ||
                  candidate.getAttribute("name") === frameName ||
                  candidate.getAttribute("id") === frameName);
                if (!frameElement) {
                  return { ok: false, reason: "frame not found", frame: frameName };
                }

                frameElement.scrollIntoView({ block: "center", inline: "center" });

                let frameWindow;
                let frameDocument;
                try {
                  frameWindow = frameElement.contentWindow;
                  frameDocument = frameElement.contentDocument || frameWindow?.document;
                } catch {
                  return { ok: false, reason: "frame is not same-origin accessible", frame: frameName };
                }

                if (!frameWindow || !frameDocument) {
                  return { ok: false, reason: "frame is not same-origin accessible", frame: frameName };
                }

                return { ok: true, document: frameDocument, window: frameWindow, offsetElement: frameElement, frame: frameName };
              };

              const automationFrameOffset = scope => {
                if (!scope.offsetElement) {
                  return { x: 0, y: 0 };
                }

                const frameRect = scope.offsetElement.getBoundingClientRect();
                return {
                  x: frameRect.left + (scope.offsetElement.clientLeft || 0),
                  y: frameRect.top + (scope.offsetElement.clientTop || 0)
                };
              };
            """;
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

    private sealed record BrowserNetworkEvent(
        long Sequence,
        string Event,
        string? RequestId,
        string? Url,
        string? Method,
        int? Status,
        string? ResourceType,
        string? MimeType,
        string? ErrorText,
        bool? Canceled,
        double? EncodedDataLength,
        string? SessionId,
        DateTimeOffset CapturedAtUtc);
}
