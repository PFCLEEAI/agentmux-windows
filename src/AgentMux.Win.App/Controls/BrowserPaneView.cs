using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;

namespace AgentMux.Win.App.Controls;

internal sealed class BrowserPaneView : Grid
{
    private const string DefaultUrl = "about:blank";

    private readonly TextBox _addressBox;
    private readonly Button _goButton;
    private readonly TextBlock _fallback;
    private readonly WebView2 _webView;
    private string _url = DefaultUrl;
    private bool _webViewInitializing;
    private bool _webViewReady;
    private bool _webViewFailed;

    public event EventHandler<string>? NavigateRequested;

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
        if (_webViewReady || _webViewFailed || _webViewInitializing)
        {
            return;
        }

        _webViewInitializing = true;
        try
        {
            await _webView.EnsureCoreWebView2Async().ConfigureAwait(true);
            _webViewReady = true;
            _webView.Visibility = Visibility.Visible;
            _fallback.Visibility = Visibility.Collapsed;
            NavigateWebView();
        }
        catch
        {
            _webViewFailed = true;
            _webView.Visibility = Visibility.Collapsed;
            _fallback.Visibility = Visibility.Visible;
        }
        finally
        {
            _webViewInitializing = false;
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
            _webView.CoreWebView2.Navigate(uri.AbsoluteUri);
        }
        catch
        {
            UseFallback();
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

    private void UseFallback()
    {
        _webViewReady = false;
        _webViewFailed = true;
        _webView.Visibility = Visibility.Collapsed;
        _fallback.Visibility = Visibility.Visible;
    }

    private static string FallbackText(string url) => $"Browser pane: {url}";
}
