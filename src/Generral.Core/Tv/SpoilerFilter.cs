namespace Generral.Core.Tv;

/// <summary>
/// Spoiler-free mode for what leaves Generral (NFO files, artwork, notifications; spec 05 § Spoilers): once an episode's event has
/// taken place, its overview and thumbnail may show results, so they are left out.
/// </summary>
public static class SpoilerFilter
{
    public static bool IsRedacted(Episode episode, bool spoilerFree, DateTime now) =>
        spoilerFree && (episode.AirDateUtc is null || episode.AirDateUtc <= now);

    /// <summary>A detached copy without the spoiler-prone fields when <see cref="IsRedacted"/>, else the episode itself.</summary>
    public static Episode Apply(Episode episode, bool spoilerFree, DateTime now) => !IsRedacted(episode, spoilerFree, now)
        ? episode
        : new Episode
        {
            Id = episode.Id,
            SeriesId = episode.SeriesId,
            SeasonNumber = episode.SeasonNumber,
            EpisodeNumber = episode.EpisodeNumber,
            AbsoluteEpisodeNumber = episode.AbsoluteEpisodeNumber,
            Title = episode.Title,
            AirDateUtc = episode.AirDateUtc,
            AirDate = episode.AirDate,
            Runtime = episode.Runtime,
            Monitored = episode.Monitored,
            EpisodeFileId = episode.EpisodeFileId,
            Images = episode.Images with { Thumb = null },
        };
}
