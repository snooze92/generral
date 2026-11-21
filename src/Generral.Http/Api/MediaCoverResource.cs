using Generral.Core.Datastore;

namespace Generral.Http.Api;

/// <summary>Sonarr's <c>MediaCover</c>. There is no local cover cache yet, so <c>url</c> is the remote URL.</summary>
public sealed record MediaCoverResource(string CoverType, string? Url, string? RemoteUrl)
{
    public static List<MediaCoverResource> From(ImageSet images)
    {
        var covers = new List<MediaCoverResource>();
        Add(covers, "poster", images.Poster);
        Add(covers, "banner", images.Banner);
        Add(covers, "fanart", images.Fanart);
        Add(covers, "screenshot", images.Thumb);
        Add(covers, "clearlogo", images.Logo);
        return covers;
    }

    /// <summary>Unknown cover types are ignored; <c>remoteUrl</c> wins over <c>url</c>.</summary>
    public static ImageSet ToImageSet(IEnumerable<MediaCoverResource?>? covers)
    {
        var urls = (covers ?? [])
            .Where(c => c?.CoverType is not null && !string.IsNullOrWhiteSpace(c.RemoteUrl ?? c.Url))
            .GroupBy(c => c!.CoverType.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => (g.First()!.RemoteUrl ?? g.First()!.Url)!.Trim());

        return new ImageSet
        {
            Poster = urls.GetValueOrDefault("poster"),
            Banner = urls.GetValueOrDefault("banner"),
            Fanart = urls.GetValueOrDefault("fanart"),
            Thumb = urls.GetValueOrDefault("screenshot"),
            Logo = urls.GetValueOrDefault("clearlogo"),
        };
    }

    private static void Add(List<MediaCoverResource> covers, string type, string? url)
    {
        if (!string.IsNullOrEmpty(url))
        {
            covers.Add(new MediaCoverResource(type, url, url));
        }
    }
}
