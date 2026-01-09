using DiscordRPC;
using DiscordRPC.Logging;
using MediaRPC.Models;

namespace MediaRPC.Services;

/// <summary>
/// Service that manages Discord Rich Presence connection and updates.
/// </summary>
public class DiscordRpcService : IDisposable
{
    private const string ClientId = "1458887355166490765";
    
    private DiscordRpcClient? _client;
    private bool _disposed;

    public bool IsConnected => _client?.IsInitialized == true;

    public event EventHandler<bool>? ConnectionStateChanged;

    public void Connect()
    {
        if (_client != null) return;

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

        _client.Initialize();
    }

    public void Disconnect()
    {
        if (_client == null) return;

        _client.ClearPresence();
        _client.Dispose();
        _client = null;
        ConnectionStateChanged?.Invoke(this, false);
    }

    public void UpdatePresence(MediaInfo? mediaInfo)
    {
        if (_client == null || !_client.IsInitialized) return;

        if (mediaInfo == null || !mediaInfo.IsPlaying)
        {
            _client.ClearPresence();
            return;
        }

        // Truncate strings if too long (Discord limits)
        var details = Truncate(mediaInfo.Title, 128);
        var state = Truncate($"by {mediaInfo.Artist}", 128);

        var presence = new RichPresence
        {
            Type = ActivityType.Listening,
            Details = details,
            State = state
        };

        _client.SetPresence(presence);
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

        Disconnect();
        GC.SuppressFinalize(this);
    }
}
