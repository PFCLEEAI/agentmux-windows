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

        return await EvaluateScriptAsync($$"""
            (() => {
              const selector = {{JsonSerializer.Serialize(selector)}};
              const element = document.querySelector(selector);
              if (!element) {
                return { ok: false, reason: "selector not found", selector };
              }

              element.click();
              return { ok: true, selector };
            })()
            """).ConfigureAwait(true);
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
}
