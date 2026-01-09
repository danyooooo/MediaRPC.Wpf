using DiscordRPC;
using DiscordRPC.Logging;
using MediaRPC.Models;
using System.Diagnostics;
using System.IO;

namespace MediaRPC.Services;

/// <summary>
/// Service that manages Discord Rich Presence connection and updates.
/// Includes Discord process detection for auto-connect/disconnect.
/// </summary>
public class DiscordRpcService : IDisposable
{
    private const string ClientId = "1458887355166490765";
    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MediaRPC",
        "cache.png"
    );
    
    private DiscordRpcClient? _client;
    private bool _disposed;
    private System.Timers.Timer? _discordCheckTimer;
    private bool _wasDiscordRunning;
    private bool _autoConnectEnabled;
    private MediaInfo? _lastMediaInfo;

    public bool IsConnected => _client?.IsInitialized == true && _client?.IsDisposed == false;
    public bool IsDiscordRunning => CheckDiscordRunning();

    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<bool>? DiscordRunningStateChanged;

    public DiscordRpcService()
    {
        // Ensure cache directory exists
        var cacheDir = Path.GetDirectoryName(CachePath);
        if (!string.IsNullOrEmpty(cacheDir))
            Directory.CreateDirectory(cacheDir);
    }

    /// <summary>
    /// Starts monitoring Discord process state.
    /// </summary>
    public void StartDiscordMonitoring(bool autoConnect = false)
    {
        _autoConnectEnabled = autoConnect;
        _wasDiscordRunning = IsDiscordRunning;
        
        _discordCheckTimer?.Stop();
        _discordCheckTimer = new System.Timers.Timer(2000); // Check every 2 seconds
        _discordCheckTimer.Elapsed += OnDiscordCheckTimer;
        _discordCheckTimer.Start();
        
        // If Discord is already running and auto-connect is enabled, connect now
        if (_autoConnectEnabled && _wasDiscordRunning && !IsConnected)
        {
            Connect();
        }
    }

    public void StopDiscordMonitoring()
    {
        _discordCheckTimer?.Stop();
        _discordCheckTimer?.Dispose();
        _discordCheckTimer = null;
    }

    private void OnDiscordCheckTimer(object? sender, System.Timers.ElapsedEventArgs e)
    {
        var isRunning = IsDiscordRunning;
        
        // Check if Discord state changed
        if (isRunning != _wasDiscordRunning)
        {
            _wasDiscordRunning = isRunning;
            DiscordRunningStateChanged?.Invoke(this, isRunning);
            
            if (!isRunning)
            {
                // Discord closed - force disconnect and cleanup
                ForceDisconnect();
            }
            else if (_autoConnectEnabled)
            {
                // Discord started and auto-connect enabled - reconnect
                Connect();
            }
        }
        
        // Also check if client died unexpectedly while Discord is running
        if (isRunning && _client != null && _client.IsDisposed && _autoConnectEnabled)
        {
            // Client was disposed but Discord is running - reconnect
            _client = null;
            Connect();
        }
    }

    private static bool CheckDiscordRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("Discord");
            var hasDiscord = processes.Length > 0;
            
            // Dispose process handles
            foreach (var p in processes)
                p.Dispose();
                
            return hasDiscord;
        }
        catch
        {
            return false;
        }
    }

    public void Connect()
    {
        if (_client != null && !_client.IsDisposed) return;
        
        // Check if Discord is running before attempting connection
        if (!IsDiscordRunning)
        {
            ConnectionStateChanged?.Invoke(this, false);
            return;
        }

        _client = new DiscordRpcClient(ClientId)
        {
            Logger = new ConsoleLogger { Level = LogLevel.Warning }
        };

        _client.OnReady += (sender, e) =>
        {
            ConnectionStateChanged?.Invoke(this, true);
        };

        _client.OnClose += (sender, e) =>
        {
            ConnectionStateChanged?.Invoke(this, false);
        };

        _client.OnError += (sender, e) =>
        {
            ConnectionStateChanged?.Invoke(this, false);
        };

        _client.OnConnectionFailed += (sender, e) =>
        {
            // Connection failed - likely Discord closed
            ConnectionStateChanged?.Invoke(this, false);
        };

        _client.Initialize();
    }

    /// <summary>
    /// Force disconnect without waiting for events.
    /// </summary>
    private void ForceDisconnect()
    {
        if (_client != null)
        {
            try
            {
                if (!_client.IsDisposed)
                {
                    _client.ClearPresence();
                    _client.Dispose();
                }
            }
            catch
            {
                // Ignore errors during force disconnect
            }
            _client = null;
        }
        ClearCachedThumbnail();
        ConnectionStateChanged?.Invoke(this, false);
    }

    public void Disconnect()
    {
        if (_client == null) return;

        try
        {
            if (!_client.IsDisposed)
            {
                _client.ClearPresence();
                _client.Dispose();
            }
        }
        catch
        {
            // Ignore errors during disconnect
        }
        _client = null;
        ClearCachedThumbnail();
        ConnectionStateChanged?.Invoke(this, false);
    }

    /// <summary>
    /// Caches the thumbnail to disk and returns the file path.
    /// </summary>
    public string? CacheThumbnail(byte[]? thumbnailBytes)
    {
        if (thumbnailBytes == null || thumbnailBytes.Length == 0)
        {
            ClearCachedThumbnail();
            return null;
        }

        try
        {
            File.WriteAllBytes(CachePath, thumbnailBytes);
            return CachePath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Clears the cached thumbnail file.
    /// </summary>
    public void ClearCachedThumbnail()
    {
        try
        {
            if (File.Exists(CachePath))
                File.Delete(CachePath);
        }
        catch
        {
            // Ignore deletion errors
        }
    }

    public void UpdatePresence(MediaInfo? mediaInfo)
    {
        _lastMediaInfo = mediaInfo;
        
        if (_client == null || !_client.IsInitialized || _client.IsDisposed) return;

        if (mediaInfo == null || !mediaInfo.IsPlaying)
        {
            try
            {
                _client.ClearPresence();
            }
            catch
            {
                // Ignore errors
            }
            ClearCachedThumbnail();
            return;
        }

        // Cache thumbnail
        CacheThumbnail(mediaInfo.Thumbnail);

        // Truncate strings if too long (Discord limits)
        var details = Truncate(mediaInfo.Title, 128);
        var state = Truncate($"by {mediaInfo.Artist}", 128);

        var presence = new RichPresence
        {
            Type = ActivityType.Listening,
            Details = details,
            State = state
        };

        try
        {
            _client.SetPresence(presence);
        }
        catch
        {
            // Client may have been disposed
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopDiscordMonitoring();
        Disconnect();
        GC.SuppressFinalize(this);
    }
}
