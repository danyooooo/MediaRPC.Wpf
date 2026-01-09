namespace MediaRPC.Models;

/// <summary>
/// Represents media information from an active media session.
/// </summary>
public record MediaInfo(
    string Title,
    string Artist,
    bool IsPlaying,
    byte[]? Thumbnail
);
