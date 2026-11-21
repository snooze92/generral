using System.Text.Json.Nodes;
using Generral.Core.Sources.Extraction;
using Generral.Core.Sources.Http;

namespace Generral.Core.Sources.Types;

/// <summary>
/// Base of the built-in types: reads the settings into <typeparamref name="TConfig"/> (collecting validation
/// failures), and turns request and document errors into <see cref="SourceFetchException"/>s.
/// </summary>
public abstract class SourceType<TConfig>(SourceFetcherFactory fetchers) : ISourceType
{
    public abstract string Id { get; }

    public abstract string Name { get; }

    public IReadOnlyList<SourceField> Fields => field ??= [.. TypeFields, .. CommonFields()];

    protected abstract IReadOnlyList<SourceField> TypeFields { get; }

    protected virtual TimeSpan DefaultRequestDelay => TimeSpan.FromSeconds(1);

    /// <summary>Types without requests don't have the <c>requestDelay</c> and <c>headers</c> settings.</summary>
    protected virtual bool UsesHttp => true;

    /// <summary>Types whose dates always carry an offset don't have the <c>timezone</c> setting.</summary>
    protected virtual bool UsesTimezone => true;

    public IReadOnlyList<SourceValidationFailure> Validate(JsonObject settings)
    {
        var reader = new SourceSettings(settings);
        Read(reader);
        ReadCommon(reader, null);
        return reader.Failures;
    }

    public async Task<SourceFetchResult> FetchAsync(JsonObject settings, SourceFetchOptions? options = null, CancellationToken cancellationToken = default)
    {
        var reader = new SourceSettings(settings);
        var config = Read(reader);
        var (requestSettings, timezone) = ReadCommon(reader, options);
        reader.ThrowIfInvalid();

        var fetcher = fetchers.Create(requestSettings);
        try
        {
            return await FetchAsync(config, new SourceFetchContext(fetcher, timezone), cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or TimeoutException or ExtractionException or ExtractionDefinitionException)
        {
            throw new SourceFetchException(e.Message, fetcher.Responses, e);
        }
        catch (SourceFetchException e) when (e.Responses.Count == 0)
        {
            throw new SourceFetchException(e.Message, fetcher.Responses, e.InnerException);
        }
    }

    /// <summary>Reads (and validates) the type's own settings. Failures are added to <paramref name="settings"/>.</summary>
    protected abstract TConfig Read(SourceSettings settings);

    protected abstract Task<SourceFetchResult> FetchAsync(TConfig config, SourceFetchContext context, CancellationToken cancellationToken);

    protected static SourceFetchResult Result(SourceFetchContext context, ExtractionResult extraction, JsonObject? meta = null, IEnumerable<string>? warnings = null) =>
        Result(context, extraction.Items, meta, [.. extraction.Warnings, .. warnings ?? []]);

    protected static SourceFetchResult Result(SourceFetchContext context, IEnumerable<ExtractedItem> items, JsonObject? meta = null, IEnumerable<string>? warnings = null)
    {
        var list = items.ToList();
        return new SourceFetchResult
        {
            Events = list.Where(i => i.Event is not null).Select(i => i.Event!).ToList(),
            ItemErrors = list.Where(i => i.Event is null).Select(i => new SourceItemError(i.Index, i.Url, i.Fields, i.Errors)).ToList(),
            Responses = context.Fetcher.Responses.ToList(),
            Meta = meta ?? [],
            Warnings = warnings?.ToList() ?? [],
        };
    }

    private IEnumerable<SourceField> CommonFields() => SourceSettings.CommonFields(DefaultRequestDelay).Where(f => f.Name switch
    {
        SourceSettings.TimezoneField => UsesTimezone,
        _ => UsesHttp,
    });

    private (SourceRequestSettings Requests, TimeZoneInfo Timezone) ReadCommon(SourceSettings settings, SourceFetchOptions? options) =>
        (UsesHttp ? settings.RequestSettings(DefaultRequestDelay, options) : new SourceRequestSettings(),
         UsesTimezone ? settings.Timezone() : TimeZoneInfo.Utc);
}

public sealed record SourceFetchContext(SourceDocumentFetcher Fetcher, TimeZoneInfo Timezone)
{
    public ExtractionOptions ExtractionOptions => new() { Timezone = Timezone };
}

/// <summary>The registered source types, by id.</summary>
public sealed class SourceTypeRegistry(IEnumerable<ISourceType> types)
{
    private readonly Dictionary<string, ISourceType> _types = types.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ISourceType> All => _types.Values;

    public ISourceType? Find(string id) => _types.GetValueOrDefault(id);

    /// <exception cref="KeyNotFoundException">Unknown type.</exception>
    public ISourceType Get(string id) => Find(id) ?? throw new KeyNotFoundException($"Unknown source type '{id}'");
}
