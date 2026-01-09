using MediaRPC.Models;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace MediaRPC.Services;

/// <summary>
/// Service that monitors system media sessions via Windows SMTC API.
/// </summary>
public class MediaSessionService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private System.Timers.Timer? _pollTimer;
    private bool _disposed;

    public event EventHandler<MediaInfo?>? MediaInfoChanged;

    public MediaInfo? CurrentMedia { get; private set; }

    public async Task InitializeAsync()
    {
        _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;
        
        // Set up polling timer for updates (every 2 seconds)
        _pollTimer = new System.Timers.Timer(2000);
        _pollTimer.Elapsed += async (s, e) => await UpdateMediaInfoAsync();
        _pollTimer.Start();
        
        // Get initial session
        await UpdateCurrentSessionAsync();
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        _ = UpdateCurrentSessionAsync();
    }

    private async Task UpdateCurrentSessionAsync()
    {
        // Unsubscribe from old session
        if (_currentSession != null)
        {
            _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }

        _currentSession = _sessionManager?.GetCurrentSession();

        if (_currentSession != null)
        {
            _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
        }

        await UpdateMediaInfoAsync();
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        _ = UpdateMediaInfoAsync();
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        _ = UpdateMediaInfoAsync();
    }

    private async Task UpdateMediaInfoAsync()
    {
        if (_currentSession == null)
        {
            if (CurrentMedia != null)
            {
                CurrentMedia = null;
                MediaInfoChanged?.Invoke(this, null);
            }
            return;
        }

        try
        {
            var mediaProperties = await _currentSession.TryGetMediaPropertiesAsync();
            var playbackInfo = _currentSession.GetPlaybackInfo();

            if (mediaProperties == null)
            {
                CurrentMedia = null;
                MediaInfoChanged?.Invoke(this, null);
                return;
            }

            var isPlaying = playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            // Fetch thumbnail
            byte[]? thumbnail = await GetThumbnailAsync(mediaProperties.Thumbnail);

            var newMedia = new MediaInfo(
                Title: mediaProperties.Title ?? "Unknown",
                Artist: mediaProperties.Artist ?? "Unknown Artist",
                IsPlaying: isPlaying,
                Thumbnail: thumbnail
            );

            // Always fire event to keep UI and RPC in sync
            CurrentMedia = newMedia;
            MediaInfoChanged?.Invoke(this, newMedia);
        }
        catch
        {
            // Session may have been disposed
            CurrentMedia = null;
            MediaInfoChanged?.Invoke(this, null);
        }
    }

    private static async Task<byte[]?> GetThumbnailAsync(IRandomAccessStreamReference? thumbnailRef)
    {
        if (thumbnailRef == null) return null;

        try
        {
            using var stream = await thumbnailRef.OpenReadAsync();
            using var reader = new DataReader(stream);
            
            var bytes = new byte[stream.Size];
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);
            
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _pollTimer?.Stop();
        _pollTimer?.Dispose();

        if (_currentSession != null)
        {
            _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }

        if (_sessionManager != null)
        {
            _sessionManager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }

        GC.SuppressFinalize(this);
    }
}
