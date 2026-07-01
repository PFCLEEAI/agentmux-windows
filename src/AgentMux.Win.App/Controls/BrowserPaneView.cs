using System.IO;
using System.Runtime.InteropServices;
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
    private const int MaxResponseBodyChars = 1_000_000;
    private const int MaxConsoleEventCount = 200;
    private const int MaxConsoleMessageChars = 4_096;
    private const int DefaultWaitForSelectorTimeoutMs = 5_000;
    private const int MaxWaitForSelectorTimeoutMs = 30_000;
    private const int DefaultWaitForLoadTimeoutMs = 5_000;
    private const int MaxWaitForLoadTimeoutMs = 30_000;
    private const int NetworkIdleQuietWindowMs = 500;
    private const int MaxDownloadEventCount = 100;
    private const int MaxDownloadFileNameLength = 120;

    private readonly List<BrowserNetworkEvent> _networkEvents = [];
    private readonly List<BrowserConsoleEvent> _consoleEvents = [];
    private readonly List<BrowserDownloadEvent> _downloadEvents = [];
    private readonly List<TrackedDownload> _activeDownloads = [];
    private readonly TextBox _addressBox;
    private readonly Button _goButton;
    private readonly TextBlock _fallback;
    private readonly WebView2 _webView;
    private CoreWebView2DevToolsProtocolEventReceiver? _networkRequestWillBeSentReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? _networkResponseReceivedReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? _networkLoadingFailedReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? _networkLoadingFinishedReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? _consoleApiCalledReceiver;
    private Task? _readyTask;
    private TaskCompletionSource? _navigationCompletion;
    private ulong? _pendingNavigationId;
    private string _url = DefaultUrl;
    private DateTimeOffset _lastNavigationStartedAtUtc = DateTimeOffset.UtcNow;
    private long _networkEventSequence;
    private long _consoleEventSequence;
    private long _downloadEventSequence;
    private bool _webViewEventsWired;
    private bool _downloadEventsWired;
    private bool _networkEventsEnabled;
    private bool _consoleEventsEnabled;
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
        UnwireDownloadEventLogging();
        UnwireConsoleEventLogging();
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
            _lastNavigationStartedAtUtc = DateTimeOffset.UtcNow;
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
        var navigationSettled = await TryWaitForInputNavigationAsync().ConfigureAwait(true);
        return JsonSerializer.Serialize(new { ok = true, selector, frame = normalizedFrame, x = target.X, y = target.Y, navigationSettled });
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

    public async Task<string> WaitForSelectorAsync(
        string selector,
        string? state = null,
        int? timeoutMs = null,
        string? frame = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return """{"ok":false,"reason":"selector is required"}""";
        }

        var normalizedState = string.IsNullOrWhiteSpace(state) ? "visible" : state.Trim().ToLowerInvariant();
        if (normalizedState is not ("visible" or "attached" or "hidden"))
        {
            return JsonSerializer.Serialize(new { ok = false, reason = "unsupported state", state });
        }

        if (timeoutMs is <= 0)
        {
            return JsonSerializer.Serialize(new { ok = false, reason = "timeoutMs must be positive", timeoutMs }, AgentMuxJson.Options);
        }

        var cappedTimeoutMs = timeoutMs is > 0
            ? Math.Min(timeoutMs.Value, MaxWaitForSelectorTimeoutMs)
            : DefaultWaitForSelectorTimeoutMs;
        var startedAt = DateTimeOffset.UtcNow;
        var deadline = startedAt.AddMilliseconds(cappedTimeoutMs);
        var normalizedFrame = NormalizeFrame(frame);
        string lastResult = "";

        await EnsureReadyAsync().ConfigureAwait(true);
        while (DateTimeOffset.UtcNow <= deadline)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return JsonSerializer.Serialize(new { ok = false, reason = "cancelled", selector, state = normalizedState }, AgentMuxJson.Options);
            }

            lastResult = await EvaluateSelectorStateAsync(selector, normalizedState, normalizedFrame).ConfigureAwait(true);
            if (SelectorStateMatched(lastResult))
            {
                using var document = JsonDocument.Parse(lastResult);
                var root = document.RootElement;
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    selector,
                    state = normalizedState,
                    frame = JsonString(root, "frame"),
                    attached = JsonBool(root, "attached") ?? false,
                    visible = JsonBool(root, "visible") ?? false,
                    elapsedMs = Math.Max(0, (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds),
                    timeoutMs = cappedTimeoutMs,
                    maxTimeoutMs = MaxWaitForSelectorTimeoutMs
                }, AgentMuxJson.Options);
            }

            if (SelectorStateUnavailable(lastResult, out var unavailableReason))
            {
                return JsonSerializer.Serialize(new
                {
                    ok = false,
                    reason = unavailableReason,
                    selector,
                    state = normalizedState,
                    frame = normalizedFrame,
                    timeoutMs = cappedTimeoutMs,
                    maxTimeoutMs = MaxWaitForSelectorTimeoutMs
                }, AgentMuxJson.Options);
            }

            try
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return JsonSerializer.Serialize(new { ok = false, reason = "cancelled", selector, state = normalizedState }, AgentMuxJson.Options);
            }
        }

        return JsonSerializer.Serialize(new
        {
            ok = false,
            reason = "timeout",
            selector,
            state = normalizedState,
            frame = normalizedFrame,
            timeoutMs = cappedTimeoutMs,
            maxTimeoutMs = MaxWaitForSelectorTimeoutMs
        }, AgentMuxJson.Options);
    }

    public async Task<string> WaitForLoadAsync(
        string? state = null,
        int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedState = string.IsNullOrWhiteSpace(state) ? "load" : state.Trim().ToLowerInvariant();
        if (normalizedState is not ("domcontentloaded" or "load" or "network-idle"))
        {
            return JsonSerializer.Serialize(new { ok = false, reason = "unsupported state", state }, AgentMuxJson.Options);
        }

        if (timeoutMs is <= 0)
        {
            return JsonSerializer.Serialize(new { ok = false, reason = "timeoutMs must be positive", timeoutMs }, AgentMuxJson.Options);
        }

        var cappedTimeoutMs = timeoutMs is > 0
            ? Math.Min(timeoutMs.Value, MaxWaitForLoadTimeoutMs)
            : DefaultWaitForLoadTimeoutMs;
        var startedAt = DateTimeOffset.UtcNow;
        var deadline = startedAt.AddMilliseconds(cappedTimeoutMs);
        var readyState = "";
        var networkActivity = new NetworkActivitySnapshot(0, _lastNavigationStartedAtUtc, 0);

        await EnsureRuntimeReadyAsync().ConfigureAwait(true);
        while (DateTimeOffset.UtcNow <= deadline)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return JsonSerializer.Serialize(new { ok = false, reason = "cancelled", state = normalizedState }, AgentMuxJson.Options);
            }

            readyState = await ReadDocumentReadyStateAsync().ConfigureAwait(true);
            networkActivity = GetNetworkActivitySnapshot(_lastNavigationStartedAtUtc);
            var idleMs = Math.Max(0, (int)(DateTimeOffset.UtcNow - networkActivity.LastActivityUtc).TotalMilliseconds);

            if (LoadStateMatched(normalizedState, readyState, networkActivity.InFlightRequests, idleMs))
            {
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    state = normalizedState,
                    readyState,
                    elapsedMs = Math.Max(0, (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds),
                    timeoutMs = cappedTimeoutMs,
                    maxTimeoutMs = MaxWaitForLoadTimeoutMs,
                    inFlightRequests = networkActivity.InFlightRequests,
                    networkIdleMs = idleMs,
                    networkEventCount = networkActivity.EventCount
                }, AgentMuxJson.Options);
            }

            try
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return JsonSerializer.Serialize(new { ok = false, reason = "cancelled", state = normalizedState }, AgentMuxJson.Options);
            }
        }

        var finalIdleMs = Math.Max(0, (int)(DateTimeOffset.UtcNow - networkActivity.LastActivityUtc).TotalMilliseconds);
        return JsonSerializer.Serialize(new
        {
            ok = false,
            reason = "timeout",
            state = normalizedState,
            readyState,
            timeoutMs = cappedTimeoutMs,
            maxTimeoutMs = MaxWaitForLoadTimeoutMs,
            inFlightRequests = networkActivity.InFlightRequests,
            networkIdleMs = finalIdleMs,
            networkEventCount = networkActivity.EventCount
        }, AgentMuxJson.Options);
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

    private async Task<string> EvaluateSelectorStateAsync(string selector, string state, string? frame)
    {
        try
        {
            return await _webView.CoreWebView2!.ExecuteScriptAsync($$"""
                (() => {
                  const selector = {{JsonSerializer.Serialize(selector)}};
                  const state = {{JsonSerializer.Serialize(state)}};
                  {{FrameScopeScript(frame)}}
                  const scope = resolveAutomationScope();
                  if (!scope.ok) {
                    return scope;
                  }

                  let element = null;
                  try {
                    element = scope.document.querySelector(selector);
                  } catch (error) {
                    return { ok: false, reason: "invalid selector", selector, state, frame: scope.frame };
                  }

                  const attached = !!element;
                  let visible = false;
                  if (element) {
                    const style = scope.window.getComputedStyle(element);
                    const rect = element.getBoundingClientRect();
                    visible = style.display !== "none" && style.visibility !== "hidden" && rect.width > 0 && rect.height > 0;
                  }

                  const matched = state === "attached"
                    ? attached
                    : state === "hidden"
                      ? !visible
                      : visible;

                  return {
                    ok: matched,
                    selector,
                    state,
                    frame: scope.frame,
                    attached,
                    visible
                  };
                })()
                """).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return """{"ok":false,"reason":"document unavailable"}""";
        }
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

    public async Task<string> GetConsoleLogAsync(int? limit = null)
    {
        await EnsureReadyAsync().ConfigureAwait(true);
        var cappedLimit = limit is > 0
            ? Math.Min(limit.Value, MaxConsoleEventCount)
            : MaxConsoleEventCount;

        BrowserConsoleEvent[] events;
        int count;
        lock (_consoleEvents)
        {
            count = _consoleEvents.Count;
            events = _consoleEvents
                .Skip(Math.Max(0, _consoleEvents.Count - cappedLimit))
                .ToArray();
        }

        return JsonSerializer.Serialize(new
        {
            ok = true,
            count,
            maxEvents = MaxConsoleEventCount,
            maxMessageChars = MaxConsoleMessageChars,
            events
        }, AgentMuxJson.Options);
    }

    public async Task<string> ClearConsoleLogAsync()
    {
        await EnsureReadyAsync().ConfigureAwait(true);
        int cleared;
        lock (_consoleEvents)
        {
            cleared = _consoleEvents.Count;
            _consoleEvents.Clear();
        }

        return JsonSerializer.Serialize(new { ok = true, cleared }, AgentMuxJson.Options);
    }

    public async Task<string> GetResponseBodyAsync(string requestId)
    {
        await EnsureReadyAsync().ConfigureAwait(true);
        var normalizedRequestId = requestId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedRequestId))
        {
            return JsonSerializer.Serialize(new { ok = false, reason = "requestId is required" }, AgentMuxJson.Options);
        }

        var requestState = GetNetworkRequestState(normalizedRequestId);
        if (!requestState.SeenResponse)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                requestId = normalizedRequestId,
                reason = "requestId is not in the active browser network log"
            }, AgentMuxJson.Options);
        }

        if (!requestState.SeenLoadingFinished)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                requestId = normalizedRequestId,
                reason = "response has not finished loading"
            }, AgentMuxJson.Options);
        }

        try
        {
            var parametersJson = JsonSerializer.Serialize(new { requestId = normalizedRequestId }, AgentMuxJson.Options);
            var responseJson = await _webView.CoreWebView2!
                .CallDevToolsProtocolMethodAsync("Network.getResponseBody", parametersJson)
                .ConfigureAwait(true);

            using var document = JsonDocument.Parse(responseJson);
            var root = document.RootElement;
            var body = JsonString(root, "body");
            if (body is null)
            {
                return JsonSerializer.Serialize(new
                {
                    ok = false,
                    requestId = normalizedRequestId,
                    reason = "response body unavailable"
                }, AgentMuxJson.Options);
            }

            var base64Encoded = JsonBool(root, "base64Encoded") ?? false;
            var truncated = body.Length > MaxResponseBodyChars;
            var returnedBody = truncated ? body[..MaxResponseBodyChars] : body;
            return JsonSerializer.Serialize(new
            {
                ok = true,
                requestId = normalizedRequestId,
                body = returnedBody,
                base64Encoded,
                bodyLength = body.Length,
                truncated,
                maxBodyChars = MaxResponseBodyChars
            }, AgentMuxJson.Options);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                requestId = normalizedRequestId,
                reason = "response body unavailable"
            }, AgentMuxJson.Options);
        }
    }

    public async Task<string> ExportHarMetadataAsync(string path)
    {
        await EnsureReadyAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path))
        {
            return JsonSerializer.Serialize(new { ok = false, reason = "path is required" }, AgentMuxJson.Options);
        }

        string? fullPath = null;
        try
        {
            fullPath = Path.GetFullPath(path);
            BrowserNetworkEvent[] events;
            lock (_networkEvents)
            {
                events = _networkEvents.ToArray();
            }

            var entries = BuildHarEntries(events);
            var har = BuildHarMetadata(entries);
            var options = new JsonSerializerOptions(AgentMuxJson.Options)
            {
                WriteIndented = true
            };

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(fullPath, JsonSerializer.Serialize(har, options)).ConfigureAwait(true);

            return JsonSerializer.Serialize(new
            {
                ok = true,
                path = fullPath,
                entryCount = entries.Length,
                eventCount = events.Length,
                metadataOnly = true
            }, AgentMuxJson.Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                path = fullPath ?? path,
                reason = ex.Message
            }, AgentMuxJson.Options);
        }
    }

    public async Task<string> GetDownloadLogAsync(int? limit = null)
    {
        await EnsureReadyAsync().ConfigureAwait(true);
        var cappedLimit = limit is > 0
            ? Math.Min(limit.Value, MaxDownloadEventCount)
            : MaxDownloadEventCount;

        BrowserDownloadSnapshot[] downloads;
        int count;
        lock (_downloadEvents)
        {
            count = _downloadEvents.Count;
            downloads = _downloadEvents
                .Skip(Math.Max(0, _downloadEvents.Count - cappedLimit))
                .Select(download => download.Snapshot())
                .ToArray();
        }

        return JsonSerializer.Serialize(new
        {
            ok = true,
            count,
            maxDownloads = MaxDownloadEventCount,
            downloads
        }, AgentMuxJson.Options);
    }

    public async Task<string> ClearDownloadLogAsync()
    {
        await EnsureReadyAsync().ConfigureAwait(true);
        int cleared;
        lock (_downloadEvents)
        {
            cleared = _downloadEvents.Count;
            _downloadEvents.Clear();
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
        await EnsureRuntimeReadyAsync().ConfigureAwait(true);
        await WaitForNavigationAsync().ConfigureAwait(true);
    }

    private async Task EnsureRuntimeReadyAsync()
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
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            await _webView.EnsureCoreWebView2Async().ConfigureAwait(true);
            WireWebViewEvents();
            WireDownloadEvents();
            await EnableNetworkEventLoggingAsync().ConfigureAwait(true);
            await EnableConsoleEventLoggingAsync().ConfigureAwait(true);
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
            _lastNavigationStartedAtUtc = DateTimeOffset.UtcNow;
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

    private void WireDownloadEvents()
    {
        if (_downloadEventsWired || _webView.CoreWebView2 is null)
        {
            return;
        }

        _webView.CoreWebView2.DownloadStarting += BrowserDownloadStarting;
        _downloadEventsWired = true;
    }

    private void UnwireDownloadEventLogging()
    {
        if (_downloadEventsWired && _webView.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.DownloadStarting -= BrowserDownloadStarting;
            _downloadEventsWired = false;
        }

        TrackedDownload[] activeDownloads;
        lock (_activeDownloads)
        {
            activeDownloads = _activeDownloads.ToArray();
            _activeDownloads.Clear();
        }

        foreach (var activeDownload in activeDownloads)
        {
            activeDownload.Dispose();
        }
    }

    private void BrowserDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs args)
    {
        _pendingNavigationId = null;
        _navigationCompletion?.TrySetResult();

        var operation = args.DownloadOperation;
        var resultPath = BuildDownloadPath(args.ResultFilePath, operation.Uri);
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
        args.ResultFilePath = resultPath;
        args.Handled = true;

        var entry = CreateDownloadEvent(++_downloadEventSequence, operation, resultPath);
        AddDownloadEvent(entry);

        EventHandler<object>? bytesReceivedChanged = null;
        EventHandler<object>? stateChanged = null;
        TrackedDownload? tracked = null;

        bytesReceivedChanged = (_, _) => UpdateDownloadEvent(entry, operation);
        stateChanged = (_, _) =>
        {
            UpdateDownloadEvent(entry, operation);
            if (!ShouldStopTrackingDownload(operation))
            {
                return;
            }

            if (bytesReceivedChanged is not null)
            {
                operation.BytesReceivedChanged -= bytesReceivedChanged;
            }

            if (stateChanged is not null)
            {
                operation.StateChanged -= stateChanged;
            }

            if (tracked is not null)
            {
                lock (_activeDownloads)
                {
                    _activeDownloads.Remove(tracked);
                }
            }
        };

        tracked = new TrackedDownload(operation, bytesReceivedChanged, stateChanged);
        operation.BytesReceivedChanged += bytesReceivedChanged;
        operation.StateChanged += stateChanged;

        lock (_activeDownloads)
        {
            _activeDownloads.Add(tracked);
        }

        UpdateDownloadEvent(entry, operation);
        if (ShouldStopTrackingDownload(operation))
        {
            stateChanged(operation, EventArgs.Empty);
        }
    }

    private void AddDownloadEvent(BrowserDownloadEvent entry)
    {
        lock (_downloadEvents)
        {
            _downloadEvents.Add(entry);
            if (_downloadEvents.Count > MaxDownloadEventCount)
            {
                _downloadEvents.RemoveRange(0, _downloadEvents.Count - MaxDownloadEventCount);
            }
        }
    }

    private static BrowserDownloadEvent CreateDownloadEvent(long sequence, CoreWebView2DownloadOperation operation, string resultFilePath)
    {
        var entry = new BrowserDownloadEvent
        {
            Sequence = sequence,
            Uri = string.IsNullOrWhiteSpace(operation.Uri) ? null : operation.Uri,
            ResultFilePath = resultFilePath,
            MimeType = string.IsNullOrWhiteSpace(operation.MimeType) ? null : operation.MimeType,
            ContentDisposition = string.IsNullOrWhiteSpace(operation.ContentDisposition) ? null : operation.ContentDisposition,
            StartedAtUtc = DateTimeOffset.UtcNow
        };
        UpdateDownloadEvent(entry, operation);
        return entry;
    }

    private static void UpdateDownloadEvent(BrowserDownloadEvent entry, CoreWebView2DownloadOperation operation)
    {
        var now = DateTimeOffset.UtcNow;
        lock (entry)
        {
            entry.BytesReceived = operation.BytesReceived;
            entry.TotalBytesToReceive = operation.TotalBytesToReceive;
            entry.State = operation.State.ToString();
            entry.CanResume = operation.CanResume;
            entry.InterruptReason = operation.State == CoreWebView2DownloadState.Interrupted
                ? operation.InterruptReason.ToString()
                : null;
            entry.UpdatedAtUtc = now;
            if (entry.CompletedAtUtc is null && ShouldStopTrackingDownload(operation))
            {
                entry.CompletedAtUtc = now;
            }
        }
    }

    private static bool ShouldStopTrackingDownload(CoreWebView2DownloadOperation operation) =>
        operation.State == CoreWebView2DownloadState.Completed
        || (operation.State == CoreWebView2DownloadState.Interrupted && !operation.CanResume);

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

    private async Task EnableConsoleEventLoggingAsync()
    {
        if (_consoleEventsEnabled || _webView.CoreWebView2 is null)
        {
            return;
        }

        var core = _webView.CoreWebView2;
        _consoleApiCalledReceiver = core.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled");
        _consoleApiCalledReceiver.DevToolsProtocolEventReceived += RuntimeConsoleApiCalled;

        try
        {
            await core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}").ConfigureAwait(true);
            _consoleEventsEnabled = true;
        }
        catch
        {
            UnwireConsoleEventLogging();
            throw;
        }
    }

    private void UnwireConsoleEventLogging()
    {
        if (_consoleApiCalledReceiver is not null)
        {
            _consoleApiCalledReceiver.DevToolsProtocolEventReceived -= RuntimeConsoleApiCalled;
            _consoleApiCalledReceiver = null;
        }

        _consoleEventsEnabled = false;
    }

    private void RuntimeConsoleApiCalled(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs args)
    {
        lock (_consoleEvents)
        {
            _consoleEvents.Add(CreateConsoleEvent(++_consoleEventSequence, args.SessionId, args.ParameterObjectAsJson));
            if (_consoleEvents.Count > MaxConsoleEventCount)
            {
                _consoleEvents.RemoveRange(0, _consoleEvents.Count - MaxConsoleEventCount);
            }
        }
    }

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

    private NetworkRequestState GetNetworkRequestState(string requestId)
    {
        var seenResponse = false;
        var seenLoadingFinished = false;
        lock (_networkEvents)
        {
            foreach (var candidate in _networkEvents)
            {
                if (!string.Equals(candidate.RequestId, requestId, StringComparison.Ordinal))
                {
                    continue;
                }

                seenResponse |= string.Equals(candidate.Event, "responseReceived", StringComparison.Ordinal);
                seenLoadingFinished |= string.Equals(candidate.Event, "loadingFinished", StringComparison.Ordinal);
            }
        }

        return new NetworkRequestState(seenResponse, seenLoadingFinished);
    }

    private NetworkActivitySnapshot GetNetworkActivitySnapshot(DateTimeOffset sinceUtc)
    {
        var inFlight = new HashSet<string>(StringComparer.Ordinal);
        var lastActivityUtc = sinceUtc;
        var eventCount = 0;
        lock (_networkEvents)
        {
            foreach (var candidate in _networkEvents)
            {
                if (candidate.CapturedAtUtc < sinceUtc)
                {
                    continue;
                }

                eventCount++;
                if (candidate.CapturedAtUtc > lastActivityUtc)
                {
                    lastActivityUtc = candidate.CapturedAtUtc;
                }

                if (string.IsNullOrWhiteSpace(candidate.RequestId))
                {
                    continue;
                }

                if (candidate.Event is "requestWillBeSent" or "responseReceived")
                {
                    inFlight.Add(candidate.RequestId);
                }
                else if (candidate.Event is "loadingFinished" or "loadingFailed")
                {
                    inFlight.Remove(candidate.RequestId);
                }
            }
        }

        return new NetworkActivitySnapshot(inFlight.Count, lastActivityUtc, eventCount);
    }

    private static object[] BuildHarEntries(BrowserNetworkEvent[] events) =>
        events
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.RequestId))
            .GroupBy(candidate => candidate.RequestId!)
            .OrderBy(group => group.Min(candidate => candidate.Sequence))
            .Select(group => BuildHarEntry(group.ToArray()))
            .ToArray();

    private static object BuildHarMetadata(object[] entries)
    {
        return new
        {
            log = new
            {
                version = "1.2",
                creator = new
                {
                    name = "AgentMux Windows",
                    version = "pre-alpha"
                },
                comment = "Metadata-only HAR preview. Headers, cookies, post data, response bodies, and downloaded files are intentionally omitted.",
                entries
            }
        };
    }

    private static object BuildHarEntry(BrowserNetworkEvent[] events)
    {
        var startedAt = events.Min(candidate => candidate.CapturedAtUtc);
        var finishedAt = events.Max(candidate => candidate.CapturedAtUtc);
        var elapsedMs = Math.Max(0, (finishedAt - startedAt).TotalMilliseconds);
        var url = FirstString(events, candidate => candidate.Url) ?? "";
        var method = FirstString(events, candidate => candidate.Method) ?? "GET";
        var status = FirstInt(events, candidate => candidate.Status);
        var mimeType = FirstString(events, candidate => candidate.MimeType) ?? "";
        var resourceType = FirstString(events, candidate => candidate.ResourceType);
        var sessionId = FirstString(events, candidate => candidate.SessionId);
        var encodedDataLength = LastDouble(events, candidate => candidate.EncodedDataLength);
        var errorText = FirstString(events, candidate => candidate.ErrorText);
        var canceled = events.Any(candidate => candidate.Canceled == true);
        var eventNames = events.Select(candidate => candidate.Event).Distinct(StringComparer.Ordinal).ToArray();

        return new
        {
            startedDateTime = startedAt,
            time = elapsedMs,
            request = new
            {
                method,
                url,
                httpVersion = "unknown",
                cookies = Array.Empty<object>(),
                headers = Array.Empty<object>(),
                queryString = Array.Empty<object>(),
                headersSize = -1,
                bodySize = -1
            },
            response = new
            {
                status = status ?? 0,
                statusText = "",
                httpVersion = "unknown",
                cookies = Array.Empty<object>(),
                headers = Array.Empty<object>(),
                content = new
                {
                    size = encodedDataLength ?? -1,
                    mimeType
                },
                redirectURL = "",
                headersSize = -1,
                bodySize = encodedDataLength ?? -1
            },
            cache = new { },
            timings = new
            {
                blocked = -1,
                dns = -1,
                connect = -1,
                send = -1,
                wait = -1,
                receive = -1,
                ssl = -1
            },
            _agentMux = new
            {
                requestId = events[0].RequestId,
                resourceType,
                sessionId,
                events = eventNames,
                capturedEventCount = events.Length,
                metadataOnly = true,
                errorText,
                canceled
            }
        };
    }

    private static string? FirstString(BrowserNetworkEvent[] events, Func<BrowserNetworkEvent, string?> selector)
    {
        foreach (var candidate in events)
        {
            var value = selector(candidate);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static int? FirstInt(BrowserNetworkEvent[] events, Func<BrowserNetworkEvent, int?> selector)
    {
        foreach (var candidate in events)
        {
            var value = selector(candidate);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static double? LastDouble(BrowserNetworkEvent[] events, Func<BrowserNetworkEvent, double?> selector)
    {
        double? value = null;
        foreach (var candidate in events)
        {
            value = selector(candidate) ?? value;
        }

        return value;
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

    private async Task<bool> TryWaitForInputNavigationAsync()
    {
        try
        {
            await WaitForNavigationAsync(allowStartDelay: true).ConfigureAwait(true);
            return true;
        }
        catch (InvalidOperationException ex) when (string.Equals(ex.Message, "browser navigation is still loading", StringComparison.Ordinal))
        {
            _pendingNavigationId = null;
            _navigationCompletion?.TrySetResult();
            return false;
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

    private static bool SelectorStateMatched(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("ok", out var okElement)
                && okElement.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool SelectorStateUnavailable(string json, out string reason)
    {
        reason = "";
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("reason", out var reasonElement)
                || reasonElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var parsedReason = reasonElement.GetString();
            if (parsedReason is "frame not found" or "frame is not same-origin accessible" or "invalid selector")
            {
                reason = parsedReason;
                return true;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<string> ReadDocumentReadyStateAsync()
    {
        try
        {
            var json = await _webView.CoreWebView2!.ExecuteScriptAsync("document.readyState").ConfigureAwait(true);
            return JsonSerializer.Deserialize<string>(json, AgentMuxJson.Options) ?? "";
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or JsonException)
        {
            return "";
        }
    }

    private static bool LoadStateMatched(string state, string readyState, int inFlightRequests, int networkIdleMs) =>
        state switch
        {
            "domcontentloaded" => readyState is "interactive" or "complete",
            "load" => readyState == "complete",
            "network-idle" => readyState == "complete"
                && inFlightRequests == 0
                && networkIdleMs >= NetworkIdleQuietWindowMs,
            _ => false
        };

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

    private static BrowserConsoleEvent CreateConsoleEvent(long sequence, string? sessionId, string parametersJson)
    {
        var type = "log";
        string? source = null;
        string? uri = null;
        int? line = null;
        int? columnNumber = null;
        int? executionContextId = null;
        var message = "";

        try
        {
            using var document = JsonDocument.Parse(parametersJson);
            var root = document.RootElement;
            type = JsonString(root, "type") ?? type;
            executionContextId = JsonInt(root, "executionContextId");

            if (root.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array)
            {
                message = string.Join(" ", args.EnumerateArray().Select(ConsoleArgumentText).Where(static text => text.Length > 0));
            }

            if (root.TryGetProperty("stackTrace", out var stackTrace)
                && stackTrace.ValueKind == JsonValueKind.Object
                && stackTrace.TryGetProperty("callFrames", out var callFrames)
                && callFrames.ValueKind == JsonValueKind.Array)
            {
                var firstFrame = callFrames.EnumerateArray().FirstOrDefault();
                if (firstFrame.ValueKind == JsonValueKind.Object)
                {
                    uri = JsonString(firstFrame, "url");
                    source = JsonString(firstFrame, "functionName");
                    if (string.IsNullOrWhiteSpace(source))
                    {
                        source = uri;
                    }

                    line = JsonInt(firstFrame, "lineNumber");
                    columnNumber = JsonInt(firstFrame, "columnNumber");
                }
            }
        }
        catch (JsonException)
        {
            type = "parseError";
            message = "console event parse failed";
        }

        var originalMessageLength = message.Length;
        var truncated = originalMessageLength > MaxConsoleMessageChars;
        if (truncated)
        {
            message = message[..MaxConsoleMessageChars];
        }

        return new BrowserConsoleEvent(
            sequence,
            "consoleAPICalled",
            type,
            type,
            message,
            originalMessageLength,
            truncated,
            line,
            source,
            uri,
            columnNumber,
            executionContextId,
            string.IsNullOrWhiteSpace(sessionId) ? null : sessionId,
            DateTimeOffset.UtcNow);
    }

    private static string ConsoleArgumentText(JsonElement argument)
    {
        if (argument.TryGetProperty("value", out var value))
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "null",
                _ => JsonString(argument, "description") ?? JsonString(argument, "type") ?? ""
            };
        }

        return JsonString(argument, "unserializableValue")
            ?? JsonString(argument, "description")
            ?? JsonString(argument, "type")
            ?? "";
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

    private static string DownloadDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentMux",
            "Downloads");

    private static string BuildDownloadPath(string? suggestedResultPath, string? uri)
    {
        var fileName = Path.GetFileName(suggestedResultPath);
        if (string.IsNullOrWhiteSpace(fileName)
            && Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri))
        {
            fileName = Path.GetFileName(parsedUri.LocalPath);
        }

        fileName = SanitizeFileName(string.IsNullOrWhiteSpace(fileName) ? "download.bin" : fileName);
        return Path.Combine(
            DownloadDirectory(),
            $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}-{fileName}");
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray())
            .Trim()
            .TrimEnd('.');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "download.bin";
        }

        if (sanitized.Length <= MaxDownloadFileNameLength)
        {
            return sanitized;
        }

        var extension = Path.GetExtension(sanitized);
        if (extension.Length > 20)
        {
            extension = "";
        }

        var stem = Path.GetFileNameWithoutExtension(sanitized);
        var maxStemLength = Math.Max(1, MaxDownloadFileNameLength - extension.Length);
        return string.Concat(stem.AsSpan(0, Math.Min(stem.Length, maxStemLength)), extension);
    }

    private readonly record struct AutomationTarget(bool Ok, double X, double Y);

    private readonly record struct BrowserKey(string Key, string Code, int VirtualKeyCode);

    private readonly record struct NetworkRequestState(bool SeenResponse, bool SeenLoadingFinished);

    private readonly record struct NetworkActivitySnapshot(int InFlightRequests, DateTimeOffset LastActivityUtc, int EventCount);

    private sealed class TrackedDownload(
        CoreWebView2DownloadOperation operation,
        EventHandler<object> bytesReceivedChanged,
        EventHandler<object> stateChanged) : IDisposable
    {
        public void Dispose()
        {
            operation.BytesReceivedChanged -= bytesReceivedChanged;
            operation.StateChanged -= stateChanged;
        }
    }

    private sealed class BrowserDownloadEvent
    {
        public long Sequence { get; init; }

        public string? Uri { get; init; }

        public string? ResultFilePath { get; init; }

        public string? MimeType { get; init; }

        public string? ContentDisposition { get; init; }

        public long BytesReceived { get; set; }

        public ulong? TotalBytesToReceive { get; set; }

        public string State { get; set; } = "";

        public bool CanResume { get; set; }

        public string? InterruptReason { get; set; }

        public DateTimeOffset StartedAtUtc { get; init; }

        public DateTimeOffset UpdatedAtUtc { get; set; }

        public DateTimeOffset? CompletedAtUtc { get; set; }

        public BrowserDownloadSnapshot Snapshot()
        {
            lock (this)
            {
                return new BrowserDownloadSnapshot(
                    Sequence,
                    Uri,
                    ResultFilePath,
                    MimeType,
                    ContentDisposition,
                    BytesReceived,
                    TotalBytesToReceive,
                    State,
                    CanResume,
                    InterruptReason,
                    StartedAtUtc,
                    UpdatedAtUtc,
                    CompletedAtUtc);
            }
        }
    }

    private sealed record BrowserDownloadSnapshot(
        long Sequence,
        string? Uri,
        string? ResultFilePath,
        string? MimeType,
        string? ContentDisposition,
        long BytesReceived,
        ulong? TotalBytesToReceive,
        string State,
        bool CanResume,
        string? InterruptReason,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        DateTimeOffset? CompletedAtUtc);

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

    private sealed record BrowserConsoleEvent(
        long Sequence,
        string Event,
        string Type,
        string Level,
        string Message,
        int MessageLength,
        bool Truncated,
        int? Line,
        string? Source,
        string? Uri,
        int? ColumnNumber,
        int? ExecutionContextId,
        string? SessionId,
        DateTimeOffset CapturedAtUtc);
}
