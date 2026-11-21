namespace Generral.Core.Datastore;

/// <summary>Image URLs of an event, series, season or episode. Each owner only uses the kinds it supports.</summary>
public sealed record ImageSet
{
    public string? Poster { get; init; }

    public string? Fanart { get; init; }

    public string? Thumb { get; init; }

    public string? Banner { get; init; }

    public string? Logo { get; init; }
}
