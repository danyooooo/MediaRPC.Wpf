using MediaRPC.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MediaRPC.Services;

public class ExtensionMediaProvider : IMediaProvider
{
    private readonly ExtensionBridgeService _bridge;
    private readonly Dictionary<int, MediaInfo> _sessions = new();
    private int _activeTabId = -1;

    public event EventHandler<MediaInfo?>? MediaInfoChanged;
    public event EventHandler? AllMediaChanged;

    public ExtensionMediaProvider(ExtensionBridgeService bridge)
    {
        _bridge = bridge;
        _bridge.MessageReceived += OnMessageReceived;
        _bridge.ClientsCountChanged += OnClientsCountChanged;
    }

    private void OnClientsCountChanged(object? sender, int count)
    {
        if (count == 0)
        {
            _sessions.Clear();
            _activeTabId = -1;
            NotifyChanges();
        }
    }

    public string ProviderName => "Browser Extension";

    public MediaInfo? CurrentMedia
    {
        get
        {
            if (_sessions.TryGetValue(_activeTabId, out var activeMedia))
                return activeMedia;
            
            // Fallback to any active session if we don't know the exact active tab
            return _sessions.Values.FirstOrDefault(m => m.IsPlaying) ?? _sessions.Values.FirstOrDefault();
        }
    }

    public IReadOnlyList<MediaInfo> AllMedia => _sessions.Values.ToList().AsReadOnly();

    private void OnMessageReceived(object? sender, ExtensionMessage msg)
    {
        try
        {
            switch (msg.type)
            {
                case "SESSION_UPDATE":
                    HandleSessionUpdate(msg.data);
                    break;
                case "ACTIVE_TAB_CHANGED":
                    HandleActiveTabChanged(msg.data);
                    break;
                case "TAB_CLOSED":
                    HandleTabClosed(msg.data);
                    break;
            }
        }
        catch
        {
            // Ignore malformed messages
        }
    }

    private void HandleSessionUpdate(JsonElement data)
    {
        int tabId = data.GetProperty("tabId").GetInt32();
        bool active = data.GetProperty("active").GetBoolean();
        var info = data.GetProperty("mediaInfo");

        string title = info.GetProperty("title").GetString() ?? "";
        string artist = info.GetProperty("artist").GetString() ?? "";
        bool isPlaying = info.GetProperty("isPlaying").GetBoolean();
        string url = info.GetProperty("url").GetString() ?? "";
        
        double posSec = info.TryGetProperty("position", out var p) ? p.GetDouble() : 0;
        double durSec = info.TryGetProperty("duration", out var d) ? d.GetDouble() : 0;
        string sessionMode = data.TryGetProperty("sessionMode", out var sm) ? sm.GetString() ?? "normal" : "normal";

        string? artworkUrl = info.GetProperty("artwork").ValueKind == JsonValueKind.String 
            ? info.GetProperty("artwork").GetString() 
            : null;

        var mediaInfo = new MediaInfo(
            Title: title,
            Artist: artist,
            IsPlaying: isPlaying,
            Thumbnail: null, // We'd need to download it, maybe later or UI handles it
            Url: url,
            ArtworkUrl: artworkUrl,
            Duration: durSec > 0 ? TimeSpan.FromSeconds(durSec) : null,
            Position: posSec > 0 ? TimeSpan.FromSeconds(posSec) : null,
            SessionMode: sessionMode
        );

        // Store session
        _sessions[tabId] = mediaInfo;
        
        if (active)
        {
            _activeTabId = tabId;
        }

        NotifyChanges();
    }

    private void HandleActiveTabChanged(JsonElement data)
    {
        if (data.TryGetProperty("tabId", out var idProp))
        {
            _activeTabId = idProp.GetInt32();
            NotifyChanges();
        }
    }

    private void HandleTabClosed(JsonElement data)
    {
        if (data.TryGetProperty("tabId", out var idProp))
        {
            int tabId = idProp.GetInt32();
            if (_sessions.Remove(tabId))
            {
                if (_activeTabId == tabId)
                {
                    _activeTabId = -1;
                }
                NotifyChanges();
            }
        }
    }

    private void NotifyChanges()
    {
        MediaInfoChanged?.Invoke(this, CurrentMedia);
        AllMediaChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _bridge.MessageReceived -= OnMessageReceived;
        _bridge.ClientsCountChanged -= OnClientsCountChanged;
    }
}
