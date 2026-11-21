using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Generral.Core.Datastore;
using Generral.Core.Extensions;
using Generral.Core.Mappings;
using Generral.Core.Tags;
using Generral.Core.Tv;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Generral.Http.Api;

/// <summary>Sonarr's SeriesResource, without the metadata-provider ids. <c>year: 0</c> means no year.</summary>
public sealed record SeriesResource
{
    public int Id { get; init; }

    public string? Title { get; init; }

    public string? SortTitle { get; init; }

    public SeriesStatus Status { get; init; }

    public bool Ended => Status == SeriesStatus.Ended;

    public string? Overview { get; init; }

    public string? Network { get; init; }

    public DateTime? NextAiring { get; init; }

    public DateTime? PreviousAiring { get; init; }

    /// <summary><c>null</c> on update keeps the stored images.</summary>
    public List<MediaCoverResource>? Images { get; init; }

    public List<SeasonResource> Seasons { get; init; } = [];

    public int Year { get; init; }

    public string? Path { get; init; }

    public int QualityProfileId { get; init; }

    public bool SeasonFolder { get; init; } = true;

    public bool Monitored { get; init; } = true;

    public NewItemMonitorType MonitorNewItems { get; init; } = NewItemMonitorType.All;

    public SeriesType SeriesType { get; init; } = SeriesType.Standard;

    public string? CleanTitle { get; init; }

    /// <summary>Generated from the title when empty.</summary>
    public string? TitleSlug { get; init; }

    public string? RootFolderPath { get; init; }

    public List<string> Genres { get; init; } = [];

    public List<int> Tags { get; init; } = [];

    public DateTime Added { get; init; }

    public SeriesStatisticsResource? Statistics { get; init; }

    public string? Guid { get; init; }

    /// <summary><c>null</c> on update keeps the stored settings.</summary>
    public JsonObject? SearchSettings { get; init; }
}

public sealed record SeasonResource
{
    public int SeasonNumber { get; init; }

    public bool Monitored { get; init; } = true;

    public string? Title { get; init; }

    public SeasonStatisticsResource? Statistics { get; init; }

    public List<MediaCoverResource>? Images { get; init; }
}

public sealed record SeasonStatisticsResource
{
    public DateTime? NextAiring { get; init; }

    public DateTime? PreviousAiring { get; init; }

    public int EpisodeFileCount { get; init; }

    public int EpisodeCount { get; init; }

    public int TotalEpisodeCount { get; init; }

    public long SizeOnDisk { get; init; }

    public List<string> ReleaseGroups { get; init; } = [];

    public decimal PercentOfEpisodes => EpisodeCount == 0 ? 0 : (decimal)EpisodeFileCount / EpisodeCount * 100;
}

public sealed record SeriesStatisticsResource
{
    /// <summary>Seasons other than specials.</summary>
    public int SeasonCount { get; init; }

    public int EpisodeFileCount { get; init; }

    public int EpisodeCount { get; init; }

    public int TotalEpisodeCount { get; init; }

    public long SizeOnDisk { get; init; }

    public List<string> ReleaseGroups { get; init; } = [];

    public decimal PercentOfEpisodes => EpisodeCount == 0 ? 0 : (decimal)EpisodeFileCount / EpisodeCount * 100;
}

public sealed record RenumberPreviewEpisodeResource(
    int EpisodeId, int MappingId, string EventKey, string? PartKey, string Title, int SeasonNumber, int EpisodeNumber, int NewSeasonNumber, int NewEpisodeNumber);

/// <param name="Errors">Mappings that could not be evaluated.</param>
public sealed record RenumberPreviewResource(IReadOnlyList<RenumberPreviewEpisodeResource> Episodes, IReadOnlyList<string> Errors);

public static partial class SeriesEndpoints
{
    public static void MapSeriesEndpoints(this IEndpointRouteBuilder api)
    {
        var series = api.MapGroup("/series");

        series.MapGet("/", GetAll);
        series.MapGet("/{id:int}", Get);
        series.MapPost("/", Create);
        series.MapPut("/{id:int}", Update);
        series.MapDelete("/{id:int}", Delete);
        series.MapGet("/{id:int}/renumber-preview", RenumberPreview);
    }

    /// <summary>What <c>Renumber { seriesId }</c> would change (docs/specs/05-mapping.md § Renumber). Nothing is stored.</summary>
    private static async Task<IResult> RenumberPreview(int id, ApplyMappingsService apply, CancellationToken cancellationToken)
    {
        try
        {
            var result = await apply.RenumberAsync(id, true, cancellationToken);
            return Results.Ok(new RenumberPreviewResource(
                result.Moves.Select(m => new RenumberPreviewEpisodeResource(
                    m.EpisodeId, m.MappingId, m.EventKey, m.PartKey, m.Title, m.SeasonNumber, m.EpisodeNumber, m.NewSeasonNumber, m.NewEpisodeNumber)).ToList(),
                result.Mappings.Where(m => !m.Success).Select(m => m.Summary).ToList()));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<List<SeriesResource>> GetAll(GenerralDbContext db, TimeProvider time) =>
        await ToResourcesAsync(db, await db.Series.AsNoTracking().OrderBy(s => s.SortTitle).ToListAsync(), time);

    internal static async Task<List<SeriesResource>> ToResourcesAsync(GenerralDbContext db, List<Series> series, TimeProvider time)
    {
        var ids = series.Select(s => s.Id).ToList();
        var seasons = (await db.Seasons.AsNoTracking().Where(s => ids.Contains(s.SeriesId)).ToListAsync()).ToLookup(s => s.SeriesId);
        var episodes = (await EpisodeStatsAsync(db, ids)).ToLookup(e => e.SeriesId);
        var now = time.GetUtcNow().UtcDateTime;

        return series.Select(s => ToResource(s, seasons[s.Id], episodes[s.Id], now)).ToList();
    }

    private static async Task<IResult> Get(int id, GenerralDbContext db, TimeProvider time)
    {
        var series = await db.Series.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id);
        return series is null ? Results.NotFound() : Results.Ok(await ToResourceAsync(db, series, time));
    }

    private static async Task<IResult> Create(SeriesResource resource, GenerralDbContext db, TagService tags, TimeProvider time)
    {
        var failures = await ValidateAsync(resource, null, db, tags);
        if (failures.Count > 0)
        {
            return ValidationFailure.BadRequest([.. failures]);
        }

        var title = resource.Title!.Trim();
        var rootFolderPath = Blank(resource.RootFolderPath) ? System.IO.Path.GetDirectoryName(TrimPath(resource.Path!))! : TrimPath(resource.RootFolderPath!);
        var series = new Series
        {
            Title = title,
            SortTitle = SeriesTitles.Sort(title),
            CleanTitle = SeriesTitles.Clean(title),
            TitleSlug = Blank(resource.TitleSlug) ? await UniqueSlugAsync(db, title) : resource.TitleSlug!,
            RootFolderPath = rootFolderPath,
            Path = Blank(resource.Path) ? System.IO.Path.Combine(rootFolderPath, SeriesTitles.FolderName(title, Year(resource))) : TrimPath(resource.Path!),
            Added = time.GetUtcNow().UtcDateTime,
        };
        Apply(series, resource);
        series.Images = MediaCoverResource.ToImageSet(resource.Images);
        series.SearchSettings = resource.SearchSettings?.DeepClone().AsObject() ?? [];
        db.Series.Add(series);
        await db.SaveChangesAsync();

        foreach (var season in resource.Seasons ?? [])
        {
            db.Seasons.Add(new Season
            {
                SeriesId = series.Id,
                SeasonNumber = season.SeasonNumber,
                Title = Blank(season.Title) ? null : season.Title!.Trim(),
                Monitored = season.Monitored,
                Images = MediaCoverResource.ToImageSet(season.Images),
            });
        }

        await db.SaveChangesAsync();
        return Results.Created($"series/{series.Id}", await ToResourceAsync(db, series, time));
    }

    /// <summary>Like Sonarr, changing a season's <c>monitored</c> sets it on all of the season's episodes. Seasons are not added or removed here.</summary>
    private static async Task<IResult> Update(int id, SeriesResource resource, GenerralDbContext db, TagService tags, TimeProvider time)
    {
        var series = await db.Series.SingleOrDefaultAsync(s => s.Id == id);
        if (series is null)
        {
            return Results.NotFound();
        }

        var failures = await ValidateAsync(resource, id, db, tags);
        if (failures.Count > 0)
        {
            return ValidationFailure.BadRequest([.. failures]);
        }

        var title = resource.Title!.Trim();
        series.Title = title;
        series.SortTitle = SeriesTitles.Sort(title);
        series.CleanTitle = SeriesTitles.Clean(title);
        series.TitleSlug = Blank(resource.TitleSlug) ? series.TitleSlug : resource.TitleSlug!;
        series.Path = TrimPath(resource.Path!);
        series.RootFolderPath = Blank(resource.RootFolderPath) ? System.IO.Path.GetDirectoryName(series.Path)! : TrimPath(resource.RootFolderPath!);
        Apply(series, resource);
        if (resource.Images is not null)
        {
            series.Images = MediaCoverResource.ToImageSet(resource.Images);
        }

        if (resource.SearchSettings is not null)
        {
            series.SearchSettings = resource.SearchSettings.DeepClone().AsObject();
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        var seasons = await db.Seasons.Where(s => s.SeriesId == id).ToDictionaryAsync(s => s.SeasonNumber);
        foreach (var update in resource.Seasons ?? [])
        {
            if (seasons.TryGetValue(update.SeasonNumber, out var season) && season.Monitored != update.Monitored)
            {
                season.Monitored = update.Monitored;
                await db.Episodes.Where(e => e.SeriesId == id && e.SeasonNumber == update.SeasonNumber)
                    .ExecuteUpdateAsync(e => e.SetProperty(x => x.Monitored, update.Monitored));
            }
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return Results.Accepted($"series/{series.Id}", await ToResourceAsync(db, series, time));
    }

    /// <summary>Seasons, episodes, mappings (with their versions) and episode links cascade in the database.</summary>
    private static async Task<IResult> Delete(int id, GenerralDbContext db) =>
        await db.Series.Where(s => s.Id == id).ExecuteDeleteAsync() > 0 ? Results.NoContent() : Results.NotFound();

    private static async Task<List<ValidationFailure>> ValidateAsync(SeriesResource resource, int? id, GenerralDbContext db, TagService tags)
    {
        var failures = new List<ValidationFailure>();
        var others = await db.Series.AsNoTracking().Where(s => s.Id != id).Select(s => new { s.Title, s.TitleSlug, s.Path }).ToListAsync();

        var title = resource.Title?.Trim() ?? "";
        if (title.Length == 0)
        {
            failures.Add(new ValidationFailure("title", "'Title' must not be empty."));
        }
        else if (others.Any(s => string.Equals(s.Title, title, StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add(new ValidationFailure("title", "A series with this title already exists"));
        }

        if (!Blank(resource.TitleSlug))
        {
            if (!SlugPattern().IsMatch(resource.TitleSlug!))
            {
                failures.Add(new ValidationFailure("titleSlug", "Allowed characters a-z, 0-9 and -"));
            }
            else if (others.Any(s => s.TitleSlug == resource.TitleSlug))
            {
                failures.Add(new ValidationFailure("titleSlug", "Should be unique"));
            }
        }

        // As in Sonarr, a new series needs a path or a root folder; an update needs the path.
        if (Blank(resource.Path) && (id is not null || Blank(resource.RootFolderPath)))
        {
            failures.Add(new ValidationFailure("path", "'Path' must not be empty."));
        }
        else if (!Blank(resource.Path))
        {
            var path = TrimPath(resource.Path!);
            if (!System.IO.Path.IsPathFullyQualified(path) || System.IO.Path.GetDirectoryName(path) is null)
            {
                failures.Add(new ValidationFailure("path", $"Invalid Path: '{resource.Path}'"));
            }
            else if (others.Any(s => s.Path == path))
            {
                failures.Add(new ValidationFailure("path", $"Path '{path}' is already configured for another series"));
            }
        }

        if (!Blank(resource.RootFolderPath) && !System.IO.Path.IsPathFullyQualified(resource.RootFolderPath!.Trim()))
        {
            failures.Add(new ValidationFailure("rootFolderPath", $"Invalid Path: '{resource.RootFolderPath}'"));
        }

        if (resource.Year < 0)
        {
            failures.Add(new ValidationFailure("year", "'Year' must be greater than or equal to '0'."));
        }

        var seasons = resource.Seasons ?? [];
        if (seasons.Any(s => s.SeasonNumber < 0))
        {
            failures.Add(new ValidationFailure("seasons", "Season numbers must not be negative"));
        }
        else if (seasons.GroupBy(s => s.SeasonNumber).Any(g => g.Count() > 1))
        {
            failures.Add(new ValidationFailure("seasons", "Season numbers must be unique"));
        }

        if (await TagEndpoints.ValidateTagIdsAsync(tags, resource.Tags) is { } tagFailure)
        {
            failures.Add(tagFailure);
        }

        return failures;
    }

    private static void Apply(Series series, SeriesResource resource)
    {
        series.Year = Year(resource);
        series.Overview = Blank(resource.Overview) ? null : resource.Overview;
        series.Network = Blank(resource.Network) ? null : resource.Network!.Trim();
        series.Genres = (resource.Genres ?? []).Where(g => !Blank(g)).Select(g => g.Trim()).Distinct().ToList();
        series.Status = resource.Status;
        series.SeriesType = resource.SeriesType;
        series.QualityProfileId = resource.QualityProfileId;
        series.Monitored = resource.Monitored;
        series.MonitorNewItems = resource.MonitorNewItems;
        series.SeasonFolder = resource.SeasonFolder;
        series.Tags = (resource.Tags ?? []).Distinct().ToList();
    }

    private static async Task<string> UniqueSlugAsync(GenerralDbContext db, string title)
    {
        var slug = title.Slugify() is { Length: > 0 } s ? s : "series";
        var taken = (await db.Series.Where(x => x.TitleSlug.StartsWith(slug)).Select(x => x.TitleSlug).ToListAsync()).ToHashSet();
        var candidate = slug;
        for (var i = 2; taken.Contains(candidate); i++)
        {
            candidate = $"{slug}-{i}";
        }

        return candidate;
    }

    private static async Task<SeriesResource> ToResourceAsync(GenerralDbContext db, Series series, TimeProvider time)
    {
        var seasons = await db.Seasons.AsNoTracking().Where(s => s.SeriesId == series.Id).ToListAsync();
        return ToResource(series, seasons, await EpisodeStatsAsync(db, [series.Id]), time.GetUtcNow().UtcDateTime);
    }

    private static SeriesResource ToResource(Series series, IEnumerable<Season> seasons, IEnumerable<EpisodeStat> episodes, DateTime now)
    {
        var bySeason = episodes.ToLookup(e => e.SeasonNumber);
        var seasonResources = seasons.OrderBy(s => s.SeasonNumber).Select(s => new SeasonResource
        {
            SeasonNumber = s.SeasonNumber,
            Monitored = s.Monitored,
            Title = s.Title,
            Statistics = SeasonStatistics(bySeason[s.SeasonNumber].ToList(), now),
            Images = MediaCoverResource.From(s.Images),
        }).ToList();
        var stats = seasonResources.Select(s => s.Statistics!).ToList();

        return new SeriesResource
        {
            Id = series.Id,
            Title = series.Title,
            SortTitle = series.SortTitle,
            Status = series.Status,
            Overview = series.Overview,
            Network = series.Network,
            NextAiring = stats.Min(s => s.NextAiring),
            PreviousAiring = stats.Max(s => s.PreviousAiring),
            Images = MediaCoverResource.From(series.Images),
            Seasons = seasonResources,
            Year = series.Year ?? 0,
            Path = series.Path,
            QualityProfileId = series.QualityProfileId,
            SeasonFolder = series.SeasonFolder,
            Monitored = series.Monitored,
            MonitorNewItems = series.MonitorNewItems,
            SeriesType = series.SeriesType,
            CleanTitle = series.CleanTitle,
            TitleSlug = series.TitleSlug,
            RootFolderPath = series.RootFolderPath,
            Genres = series.Genres,
            Tags = series.Tags,
            Added = series.Added,
            Statistics = new SeriesStatisticsResource
            {
                SeasonCount = seasonResources.Count(s => s.SeasonNumber > 0),
                EpisodeFileCount = stats.Sum(s => s.EpisodeFileCount),
                EpisodeCount = stats.Sum(s => s.EpisodeCount),
                TotalEpisodeCount = stats.Sum(s => s.TotalEpisodeCount),
            },
            Guid = series.Guid,
            SearchSettings = series.SearchSettings,
        };
    }

    /// <summary>Sonarr's counting: <c>episodeCount</c> is monitored aired episodes plus those with a file.</summary>
    private static SeasonStatisticsResource SeasonStatistics(List<EpisodeStat> episodes, DateTime now) => new()
    {
        NextAiring = episodes.Where(e => e.Monitored && e.AirDateUtc >= now).Min(e => e.AirDateUtc),
        PreviousAiring = episodes.Where(e => e.Monitored && e.AirDateUtc < now).Max(e => e.AirDateUtc),
        EpisodeFileCount = episodes.Count(e => e.HasFile),
        EpisodeCount = episodes.Count(e => (e.Monitored && e.AirDateUtc <= now) || e.HasFile),
        TotalEpisodeCount = episodes.Count,
    };

    private sealed record EpisodeStat(int SeriesId, int SeasonNumber, bool Monitored, DateTime? AirDateUtc, bool HasFile);

    /// <summary>Orphaned episodes without a file are hidden, so they are not counted.</summary>
    private static Task<List<EpisodeStat>> EpisodeStatsAsync(GenerralDbContext db, List<int> seriesIds) =>
        db.Episodes.AsNoTracking()
            .Where(e => seriesIds.Contains(e.SeriesId))
            .Where(e => e.Status == EpisodeStatus.Active || e.EpisodeFileId != null)
            .Select(e => new EpisodeStat(e.SeriesId, e.SeasonNumber, e.Monitored, e.AirDateUtc, e.EpisodeFileId != null))
            .ToListAsync();

    private static int? Year(SeriesResource resource) => resource.Year > 0 ? resource.Year : null;

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

    private static string TrimPath(string path)
    {
        path = path.Trim();
        return path.Length > 1 ? path.TrimEnd('/', '\\') : path;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
