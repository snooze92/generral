// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Qualities/Quality.cs, src/NzbDrone.Core/Qualities/QualitySource.cs
// and src/NzbDrone.Core/Qualities/QualityFinder.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Generral.Core.Qualities;

public enum QualitySource
{
    Unknown,
    Television,
    TelevisionRaw,
    Web,
    WebRip,
    DVD,
    Bluray,
    BlurayRaw,
}

/// <summary>Sonarr's quality list, with Sonarr's ids, so profiles look (and sync) the same.</summary>
[JsonConverter(typeof(QualityJsonConverter))]
public sealed record Quality(int Id, string Name, QualitySource Source, int Resolution)
{
    public static readonly Quality Unknown = new(0, "Unknown", QualitySource.Unknown, 0);
    public static readonly Quality SDTV = new(1, "SDTV", QualitySource.Television, 480);
    public static readonly Quality DVD = new(2, "DVD", QualitySource.DVD, 480);
    public static readonly Quality WEBDL1080p = new(3, "WEBDL-1080p", QualitySource.Web, 1080);
    public static readonly Quality HDTV720p = new(4, "HDTV-720p", QualitySource.Television, 720);
    public static readonly Quality WEBDL720p = new(5, "WEBDL-720p", QualitySource.Web, 720);
    public static readonly Quality Bluray720p = new(6, "Bluray-720p", QualitySource.Bluray, 720);
    public static readonly Quality Bluray1080p = new(7, "Bluray-1080p", QualitySource.Bluray, 1080);
    public static readonly Quality WEBDL480p = new(8, "WEBDL-480p", QualitySource.Web, 480);
    public static readonly Quality HDTV1080p = new(9, "HDTV-1080p", QualitySource.Television, 1080);
    public static readonly Quality RAWHD = new(10, "Raw-HD", QualitySource.TelevisionRaw, 1080);
    public static readonly Quality WEBRip480p = new(12, "WEBRip-480p", QualitySource.WebRip, 480);
    public static readonly Quality Bluray480p = new(13, "Bluray-480p", QualitySource.Bluray, 480);
    public static readonly Quality WEBRip720p = new(14, "WEBRip-720p", QualitySource.WebRip, 720);
    public static readonly Quality WEBRip1080p = new(15, "WEBRip-1080p", QualitySource.WebRip, 1080);
    public static readonly Quality HDTV2160p = new(16, "HDTV-2160p", QualitySource.Television, 2160);
    public static readonly Quality WEBRip2160p = new(17, "WEBRip-2160p", QualitySource.WebRip, 2160);
    public static readonly Quality WEBDL2160p = new(18, "WEBDL-2160p", QualitySource.Web, 2160);
    public static readonly Quality Bluray2160p = new(19, "Bluray-2160p", QualitySource.Bluray, 2160);
    public static readonly Quality Bluray1080pRemux = new(20, "Bluray-1080p Remux", QualitySource.BlurayRaw, 1080);
    public static readonly Quality Bluray2160pRemux = new(21, "Bluray-2160p Remux", QualitySource.BlurayRaw, 2160);
    public static readonly Quality Bluray576p = new(22, "Bluray-576p", QualitySource.Bluray, 576);

    public static readonly IReadOnlyList<Quality> All =
    [
        Unknown, SDTV, DVD, WEBRip480p, WEBDL480p, Bluray480p, Bluray576p, HDTV720p, WEBRip720p, WEBDL720p, Bluray720p,
        Bluray1080p, HDTV1080p, WEBRip1080p, WEBDL1080p, RAWHD, HDTV2160p, WEBRip2160p, WEBDL2160p, Bluray2160p,
        Bluray1080pRemux, Bluray2160pRemux,
    ];

    private static readonly Dictionary<int, Quality> AllLookup = All.ToDictionary(q => q.Id);

    /// <summary>
    /// Sonarr's weights and groups. Sizes are MB per minute of runtime; the 720p/1080p maxima are raised
    /// over Sonarr's TV defaults because sport events run 3–5 h while the default runtime is 120 min.
    /// </summary>
    public static readonly IReadOnlyList<QualityDefinition> DefaultQualityDefinitions =
    [
        new(Unknown) { Weight = 1, MinSize = 1, MaxSize = 199.9, PreferredSize = 95 },
        new(SDTV) { Weight = 2, MinSize = 2, MaxSize = 100, PreferredSize = 95 },
        new(WEBRip480p) { Weight = 3, MinSize = 2, MaxSize = 100, PreferredSize = 95, GroupName = "WEB 480p" },
        new(WEBDL480p) { Weight = 3, MinSize = 2, MaxSize = 100, PreferredSize = 95, GroupName = "WEB 480p" },
        new(DVD) { Weight = 4, MinSize = 2, MaxSize = 100, PreferredSize = 95 },
        new(Bluray480p) { Weight = 5, MinSize = 2, MaxSize = 100, PreferredSize = 95 },
        new(Bluray576p) { Weight = 6, MinSize = 2, MaxSize = 100, PreferredSize = 95 },
        new(HDTV720p) { Weight = 7, MinSize = 3, MaxSize = 200, PreferredSize = 95 },
        new(HDTV1080p) { Weight = 8, MinSize = 4, MaxSize = 250, PreferredSize = 95 },
        new(RAWHD) { Weight = 9, MinSize = 4, MaxSize = null, PreferredSize = 95 },
        new(WEBRip720p) { Weight = 10, MinSize = 3, MaxSize = 200, PreferredSize = 95, GroupName = "WEB 720p" },
        new(WEBDL720p) { Weight = 10, MinSize = 3, MaxSize = 200, PreferredSize = 95, GroupName = "WEB 720p" },
        new(Bluray720p) { Weight = 11, MinSize = 4, MaxSize = 200, PreferredSize = 95 },
        new(WEBRip1080p) { Weight = 12, MinSize = 4, MaxSize = 250, PreferredSize = 95, GroupName = "WEB 1080p" },
        new(WEBDL1080p) { Weight = 12, MinSize = 4, MaxSize = 250, PreferredSize = 95, GroupName = "WEB 1080p" },
        new(Bluray1080p) { Weight = 13, MinSize = 4, MaxSize = 300, PreferredSize = 95 },
        new(Bluray1080pRemux) { Weight = 14, MinSize = 35, MaxSize = null, PreferredSize = 95 },
        new(HDTV2160p) { Weight = 15, MinSize = 35, MaxSize = 199.9, PreferredSize = 95 },
        new(WEBRip2160p) { Weight = 16, MinSize = 35, MaxSize = null, PreferredSize = 95, GroupName = "WEB 2160p" },
        new(WEBDL2160p) { Weight = 16, MinSize = 35, MaxSize = null, PreferredSize = 95, GroupName = "WEB 2160p" },
        new(Bluray2160p) { Weight = 17, MinSize = 35, MaxSize = null, PreferredSize = 95 },
        new(Bluray2160pRemux) { Weight = 18, MinSize = 35, MaxSize = null, PreferredSize = 95 },
    ];

    public static Quality? Find(int id) => AllLookup.GetValueOrDefault(id);

    public static Quality FindById(int id) =>
        Find(id) ?? throw new ArgumentException("ID does not match a known quality", nameof(id));

    public static Quality FindBySourceAndResolution(QualitySource source, int resolution)
    {
        if (All.SingleOrDefault(q => q.Source == source && q.Resolution == resolution) is { } matching)
        {
            return matching;
        }

        // 576p Television/Web releases must not be rolled up to Bluray-576p.
        if (resolution < 720)
        {
            switch (source)
            {
                case QualitySource.Television:
                    return SDTV;
                case QualitySource.Web:
                    return WEBDL480p;
                case QualitySource.WebRip:
                    return WEBRip480p;
            }
        }

        return All.Where(q => q.Resolution == resolution).OrderBy(q => q.Source).FirstOrDefault(q => q.Source >= source) ?? Unknown;
    }

    public override string ToString() => Name;
}

/// <summary>Writes the whole quality, reads it back by <c>id</c> only (like Sonarr's resource mapping).</summary>
public sealed class QualityJsonConverter : JsonConverter<Quality>
{
    public override Quality Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var id = root.ValueKind == JsonValueKind.Number ? root.GetInt32()
            : root.ValueKind == JsonValueKind.Object && TryGetId(root, out var value) ? value
            : throw new JsonException("A quality needs an 'id'");

        return Quality.Find(id) ?? throw new JsonException($"Unknown quality id {id}");
    }

    public override void Write(Utf8JsonWriter writer, Quality value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(Name(options, "Id"), value.Id);
        writer.WriteString(Name(options, "Name"), value.Name);
        writer.WriteString(Name(options, "Source"), JsonNamingPolicy.CamelCase.ConvertName(value.Source.ToString()));
        writer.WriteNumber(Name(options, "Resolution"), value.Resolution);
        writer.WriteEndObject();
    }

    private static bool TryGetId(JsonElement element, out int id)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals("id", StringComparison.OrdinalIgnoreCase) && property.Value.TryGetInt32(out id))
            {
                return true;
            }
        }

        id = 0;
        return false;
    }

    private static string Name(JsonSerializerOptions options, string name) => options.PropertyNamingPolicy?.ConvertName(name) ?? name;
}
