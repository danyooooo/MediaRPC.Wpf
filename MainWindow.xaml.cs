using MediaRPC.Models;
using MediaRPC.Services;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace MediaRPC;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MediaSessionService _mediaService;
    private readonly DiscordRpcService _discordService;
    private readonly SettingsService _settingsService;
    private readonly StartupService _startupService;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();
        
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

        // Subscribe to events
        _mediaService.MediaInfoChanged += OnMediaInfoChanged;
        _discordService.ConnectionStateChanged += OnConnectionStateChanged;
        _discordService.DiscordRunningStateChanged += OnDiscordRunningStateChanged;

        // Initialize media session monitoring
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

    private void OnMediaInfoChanged(object? sender, MediaInfo? mediaInfo)
    {
        Dispatcher.Invoke(() =>
        {
            if (mediaInfo == null)
            {
                MediaInfoPanel.Visibility = Visibility.Collapsed;
                NoMediaPanel.Visibility = Visibility.Visible;
            }
            else
            {
                MediaInfoPanel.Visibility = Visibility.Visible;
                NoMediaPanel.Visibility = Visibility.Collapsed;

                TitleText.Text = mediaInfo.Title;
                ArtistText.Text = mediaInfo.Artist;

                // Update thumbnail
                UpdateThumbnail(mediaInfo.Thumbnail);
            }

            // Update Discord presence
            _discordService.UpdatePresence(mediaInfo);
        });
    }

    private void UpdateThumbnail(byte[]? thumbnailBytes)
    {
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
