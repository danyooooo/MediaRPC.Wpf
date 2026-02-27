using MediaRPC.Models;
using MediaRPC.Services;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Linq;

namespace MediaRPC;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly ExtensionBridgeService _bridgeService;
    private readonly ExtensionMediaProvider _extensionProvider;
    private readonly MediaSessionService _mediaService;
    private readonly DiscordRpcService _discordService;
    private readonly SettingsService _settingsService;
    private readonly StartupService _startupService;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();
        
        _bridgeService = new ExtensionBridgeService();
        _extensionProvider = new ExtensionMediaProvider(_bridgeService);
        _mediaService = new MediaSessionService();
        _discordService = new DiscordRpcService();
        _settingsService = new SettingsService();
        _startupService = new StartupService();

        Initialize();
    }

    private async void Initialize()
    {
        // Load settings
        StartupCheckBox.IsChecked = _settingsService.RunAtStartup;

        if (_settingsService.UseDynamicDomainLayout)
            DynamicLayoutRadio.IsChecked = true;
        else
            ClassicLayoutRadio.IsChecked = true;

        _discordService.UseDynamicDomainLayout = _settingsService.UseDynamicDomainLayout;

        // Subscribe to events
        _extensionProvider.AllMediaChanged += OnAnyMediaChanged;
        _mediaService.AllMediaChanged += OnAnyMediaChanged;
        _discordService.ConnectionStateChanged += OnConnectionStateChanged;
        _discordService.DiscordRunningStateChanged += OnDiscordRunningStateChanged;

        // Initialize media session monitoring
        _bridgeService.StartListening();
        await _mediaService.InitializeAsync();

        // Start Discord monitoring with auto-connect if startup mode or setting enabled
        var autoConnect = App.IsStartupMode || _settingsService.RunAtStartup;
        _discordService.StartDiscordMonitoring(autoConnect);
        
        // Update button state based on Discord availability
        UpdateConnectButtonState();

        // Handle startup mode
        if (App.IsStartupMode)
        {
            Hide();
        }
    }

    private void OnAnyMediaChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(UpdateUIAndDiscord);
    }

    private void UpdateUIAndDiscord()
    {
        // Decide active media: prioritize whatever is currently playing
        var extMedia = _extensionProvider.CurrentMedia;
        var smtcMedia = _mediaService.CurrentMedia;

        MediaInfo? activeMedia = null;

        if (extMedia != null && extMedia.IsPlaying)
            activeMedia = extMedia; // Extension is playing, priority #1
        else if (smtcMedia != null && smtcMedia.IsPlaying)
            activeMedia = smtcMedia; // SMTC is playing, priority #2
        else if (extMedia != null)
            activeMedia = extMedia; // Extension is paused, priority #3
        else
            activeMedia = smtcMedia; // SMTC is paused or null, priority #4

        if (activeMedia == null)
        {
            MediaInfoPanel.Visibility = Visibility.Collapsed;
            ConcurrentSessionsPanel.Visibility = Visibility.Collapsed;
            NoMediaPanel.Visibility = Visibility.Visible;
            _discordService.UpdatePresence(null);
            return;
        }

        MediaInfoPanel.Visibility = Visibility.Visible;
        NoMediaPanel.Visibility = Visibility.Collapsed;

        TitleText.Text = activeMedia.Title;
        ArtistText.Text = activeMedia.Artist;
        SourceText.Text = string.IsNullOrEmpty(activeMedia.Url) ? "Local Device" : activeMedia.Url;
        SourceText.Visibility = string.IsNullOrEmpty(activeMedia.Url) ? Visibility.Collapsed : Visibility.Visible;

        if (!string.IsNullOrEmpty(activeMedia.Url) && System.Uri.TryCreate(activeMedia.Url, System.UriKind.Absolute, out var uri))
        {
            DomainText.Text = uri.Host.StartsWith("www.") ? uri.Host.Substring(4) : uri.Host;
            DomainText.Visibility = Visibility.Visible;
        }
        else
        {
            DomainText.Visibility = Visibility.Collapsed;
        }

        if (activeMedia.Duration.HasValue && activeMedia.Duration.Value.TotalSeconds > 0)
        {
            TimePanel.Visibility = Visibility.Visible;
            if (activeMedia.Position.HasValue)
            {
                TimeText.Text = $"{(int)activeMedia.Position.Value.TotalMinutes}:{activeMedia.Position.Value.Seconds:D2} / {(int)activeMedia.Duration.Value.TotalMinutes}:{activeMedia.Duration.Value.Seconds:D2}";
                TimeBar.Maximum = activeMedia.Duration.Value.TotalSeconds;
                TimeBar.Value = activeMedia.Position.Value.TotalSeconds;
            }
            else
            {
                TimeText.Text = $"0:00 / {(int)activeMedia.Duration.Value.TotalMinutes}:{activeMedia.Duration.Value.Seconds:D2}";
                TimeBar.Maximum = activeMedia.Duration.Value.TotalSeconds;
                TimeBar.Value = 0;
            }
        }
        else
        {
            TimePanel.Visibility = Visibility.Collapsed;
        }

        // Update thumbnail
        UpdateThumbnail(activeMedia.ArtworkUrl, activeMedia.Thumbnail);

        // Build list of other concurrent sessions
        var allSessions = new System.Collections.Generic.List<MediaInfo>();
        allSessions.AddRange(_extensionProvider.AllMedia);
        allSessions.AddRange(_mediaService.AllMedia);

        // Exclude the active one
        var others = allSessions.Where(m => m != activeMedia).ToList();
        if (others.Count > 0)
        {
            ConcurrentSessionsPanel.Visibility = Visibility.Visible;
            ConcurrentSessionsList.ItemsSource = others;
        }
        else
        {
            ConcurrentSessionsPanel.Visibility = Visibility.Collapsed;
        }

        // Update Discord presence with the ACTIVE session ONLY
        _discordService.UpdatePresence(activeMedia);
    }

    private void UpdateThumbnail(string? artworkUrl, byte[]? thumbnailBytes)
    {
        if (!string.IsNullOrEmpty(artworkUrl))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(artworkUrl);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                
                ThumbnailImage.Source = bitmap;
                NoThumbnailIcon.Visibility = Visibility.Collapsed;
                return;
            }
            catch
            {
                // Fall through to memory stream or placeholder
            }
        }

        if (thumbnailBytes != null && thumbnailBytes.Length > 0)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = new MemoryStream(thumbnailBytes);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                
                ThumbnailImage.Source = bitmap;
                NoThumbnailIcon.Visibility = Visibility.Collapsed;
                return;
            }
            catch
            {
                // Fall through to show placeholder
            }
        }
        
        ThumbnailImage.Source = null;
        NoThumbnailIcon.Visibility = Visibility.Visible;
    }

    private void OnConnectionStateChanged(object? sender, bool isConnected)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateConnectButtonState();
            
            // If just connected, immediately update presence with current media
            if (isConnected && _mediaService.CurrentMedia != null)
            {
                _discordService.UpdatePresence(_mediaService.CurrentMedia);
            }
        });
    }

    private void OnDiscordRunningStateChanged(object? sender, bool isRunning)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateConnectButtonState();
        });
    }

    private void UpdateConnectButtonState()
    {
        if (!_discordService.IsDiscordRunning)
        {
            ConnectButton.Content = "Discord Not Running";
            ConnectButton.IsEnabled = false;
        }
        else if (_discordService.IsConnected)
        {
            ConnectButton.Content = "Disconnect";
            ConnectButton.IsEnabled = true;
        }
        else
        {
            ConnectButton.Content = "Connect";
            ConnectButton.IsEnabled = true;
        }
    }

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_discordService.IsConnected)
        {
            _discordService.Disconnect();
        }
        else
        {
            _discordService.Connect();
        }
    }

    private void StartupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        var isChecked = StartupCheckBox.IsChecked == true;
        _settingsService.RunAtStartup = isChecked;
        _startupService.IsEnabled = isChecked;
        
        // Update Discord monitoring auto-connect based on new setting
        _discordService.StopDiscordMonitoring();
        _discordService.StartDiscordMonitoring(isChecked);
    }

    private void LayoutRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (ClassicLayoutRadio == null || DynamicLayoutRadio == null) return;

        bool useDynamic = DynamicLayoutRadio.IsChecked == true;
        _settingsService.UseDynamicDomainLayout = useDynamic;
        _discordService.UseDynamicDomainLayout = useDynamic;

        // Push update immediately
        Dispatcher.Invoke(UpdateUIAndDiscord);
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExiting)
        {
            // Minimize to tray instead of closing
            e.Cancel = true;
            Hide();
        }
        else
        {
            // Actually closing - clean up
            _extensionProvider.Dispose();
            _bridgeService.Dispose();
            _mediaService.Dispose();
            _discordService.Dispose();
            TrayIcon.Dispose();
        }
    }

    private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
    {
        ShowWindow();
    }

    private void ShowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowWindow();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _isExiting = true;
        Application.Current.Shutdown();
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
}
