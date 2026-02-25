using MediaRPC.Models;
using System;

namespace MediaRPC.Services;

/// <summary>
/// Interface for a service providing media playback information.
/// </summary>
public interface IMediaProvider : IDisposable
{
    event EventHandler<MediaInfo?>? MediaInfoChanged;
    event EventHandler? AllMediaChanged;
    
    MediaInfo? CurrentMedia { get; }
    System.Collections.Generic.IReadOnlyList<MediaInfo> AllMedia { get; }
    string ProviderName { get; }
}
