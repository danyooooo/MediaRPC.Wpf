namespace MediaRPC.Models;

/// <summary>
/// Represents media information from an active media session.
/// </summary>
public record MediaInfo(
    string Title,
    string Artist,
    bool IsPlaying,
    byte[]? Thumbnail,
    string? Url = null,
    string? ArtworkUrl = null,
    TimeSpan? Duration = null,
    TimeSpan? Position = null
);
