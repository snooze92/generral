// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Languages/Language.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Generral.Core.Languages;

/// <summary>Sonarr's language list, with Sonarr's ids. <see cref="Original"/> (-2) and <see cref="Any"/> (-1, Radarr) are profile-only values.</summary>
[JsonConverter(typeof(LanguageJsonConverter))]
public sealed record Language(int Id, string Name)
{
    public static readonly Language Unknown = new(0, "Unknown");
    public static readonly Language English = new(1, "English");
    public static readonly Language French = new(2, "French");
    public static readonly Language Spanish = new(3, "Spanish");
    public static readonly Language German = new(4, "German");
    public static readonly Language Italian = new(5, "Italian");
    public static readonly Language Danish = new(6, "Danish");
    public static readonly Language Dutch = new(7, "Dutch");
    public static readonly Language Japanese = new(8, "Japanese");
    public static readonly Language Icelandic = new(9, "Icelandic");
    public static readonly Language Chinese = new(10, "Chinese");
    public static readonly Language Russian = new(11, "Russian");
    public static readonly Language Polish = new(12, "Polish");
    public static readonly Language Vietnamese = new(13, "Vietnamese");
    public static readonly Language Swedish = new(14, "Swedish");
    public static readonly Language Norwegian = new(15, "Norwegian");
    public static readonly Language Finnish = new(16, "Finnish");
    public static readonly Language Turkish = new(17, "Turkish");
    public static readonly Language Portuguese = new(18, "Portuguese");
    public static readonly Language Flemish = new(19, "Flemish");
    public static readonly Language Greek = new(20, "Greek");
    public static readonly Language Korean = new(21, "Korean");
    public static readonly Language Hungarian = new(22, "Hungarian");
    public static readonly Language Hebrew = new(23, "Hebrew");
    public static readonly Language Lithuanian = new(24, "Lithuanian");
    public static readonly Language Czech = new(25, "Czech");
    public static readonly Language Arabic = new(26, "Arabic");
    public static readonly Language Hindi = new(27, "Hindi");
    public static readonly Language Bulgarian = new(28, "Bulgarian");
    public static readonly Language Malayalam = new(29, "Malayalam");
    public static readonly Language Ukrainian = new(30, "Ukrainian");
    public static readonly Language Slovak = new(31, "Slovak");
    public static readonly Language Thai = new(32, "Thai");
    public static readonly Language PortugueseBrazil = new(33, "Portuguese (Brazil)");
    public static readonly Language SpanishLatino = new(34, "Spanish (Latino)");
    public static readonly Language Romanian = new(35, "Romanian");
    public static readonly Language Latvian = new(36, "Latvian");
    public static readonly Language Persian = new(37, "Persian");
    public static readonly Language Catalan = new(38, "Catalan");
    public static readonly Language Croatian = new(39, "Croatian");
    public static readonly Language Serbian = new(40, "Serbian");
    public static readonly Language Bosnian = new(41, "Bosnian");
    public static readonly Language Estonian = new(42, "Estonian");
    public static readonly Language Tamil = new(43, "Tamil");
    public static readonly Language Indonesian = new(44, "Indonesian");
    public static readonly Language Macedonian = new(45, "Macedonian");
    public static readonly Language Slovenian = new(46, "Slovenian");
    public static readonly Language Azerbaijani = new(47, "Azerbaijani");
    public static readonly Language Uzbek = new(48, "Uzbek");
    public static readonly Language Malay = new(49, "Malay");
    public static readonly Language Urdu = new(50, "Urdu");
    public static readonly Language Romansh = new(51, "Romansh");
    public static readonly Language Georgian = new(52, "Georgian");
    public static readonly Language Irish = new(53, "Irish");
    public static readonly Language Original = new(-2, "Original");
    public static readonly Language Any = new(-1, "Any");

    public static readonly IReadOnlyList<Language> All =
    [
        Unknown, English, French, Spanish, German, Italian, Danish, Dutch, Japanese, Icelandic, Chinese, Russian, Polish, Vietnamese, Swedish, Norwegian, Finnish, Turkish, Portuguese, Flemish, Greek, Korean, Hungarian, Hebrew, Lithuanian, Czech, Arabic, Hindi, Bulgarian, Malayalam, Ukrainian, Slovak, Thai, PortugueseBrazil, SpanishLatino, Romanian, Latvian, Persian, Catalan, Croatian, Serbian, Bosnian, Estonian, Tamil, Indonesian, Macedonian, Slovenian, Azerbaijani, Uzbek, Malay, Urdu, Romansh, Georgian, Irish, Original,
    ];

    private static readonly Dictionary<int, Language> Lookup = All.Append(Any).ToDictionary(l => l.Id);

    public static Language? Find(int id) => Lookup.GetValueOrDefault(id);

    public override string ToString() => Name;
}

/// <summary>Writes <c>{ id, name }</c>, reads back by <c>id</c> (or a bare number).</summary>
public sealed class LanguageJsonConverter : JsonConverter<Language>
{
    public override Language Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        int? id = root.ValueKind == JsonValueKind.Number ? root.GetInt32() : null;
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name.Equals("id", StringComparison.OrdinalIgnoreCase) && property.Value.TryGetInt32(out var value))
                {
                    id = value;
                }
            }
        }

        return id is { } known ? Language.Find(known) ?? throw new JsonException($"Unknown language id {known}") : throw new JsonException("A language needs an 'id'");
    }

    public override void Write(Utf8JsonWriter writer, Language value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(options.PropertyNamingPolicy?.ConvertName("Id") ?? "Id", value.Id);
        writer.WriteString(options.PropertyNamingPolicy?.ConvertName("Name") ?? "Name", value.Name);
        writer.WriteEndObject();
    }
}
