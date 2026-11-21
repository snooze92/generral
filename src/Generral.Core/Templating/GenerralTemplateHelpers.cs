using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Generral.Core.Extensions;
using Scriban;
using Scriban.Runtime;

namespace Generral.Core.Templating;

/// <summary>Generral helpers available in every template (see docs/specs/05-mapping.md).</summary>
public sealed partial class GenerralTemplateHelpers : ITemplateHelpers
{
    public void Register(ScriptObject builtins)
    {
        TemplateRenderer.Import(builtins, "regex", typeof(RegexHelpers));
        TemplateRenderer.Import(builtins, "string", typeof(StringHelpers));
        TemplateRenderer.Import(builtins, "date", typeof(DateHelpers));
    }

    /// <summary>Parses <c>-2h</c>, <c>3d</c>, <c>1h30m</c> (units <c>w</c>, <c>d</c>, <c>h</c>, <c>m</c>, <c>s</c>).</summary>
    public static bool TryParseDuration(string text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var match = DurationPattern().Match(text.Trim());
        if (!match.Success)
        {
            return false;
        }

        foreach (Capture part in match.Groups["part"].Captures)
        {
            var amount = double.Parse(part.Value[..^1], CultureInfo.InvariantCulture);
            duration += part.Value[^1] switch
            {
                'w' => TimeSpan.FromDays(amount * 7),
                'd' => TimeSpan.FromDays(amount),
                'h' => TimeSpan.FromHours(amount),
                'm' => TimeSpan.FromMinutes(amount),
                _ => TimeSpan.FromSeconds(amount),
            };
        }

        if (match.Groups["sign"].Value == "-")
        {
            duration = -duration;
        }

        return true;
    }

    [GeneratedRegex(@"^(?<sign>[+-]?)(?<part>[0-9]+(?:\.[0-9]+)?[wdhms])+$")]
    private static partial Regex DurationPattern();

    private static class RegexHelpers
    {
        /// <summary>Groups of the first match (index 0 = whole match), or null when there is no match.</summary>
        public static ScriptArray? Match(TemplateContext context, string? text, string pattern, string? options = null)
        {
            var match = Regex.Match(text ?? "", pattern, ParseOptions(options), context.RegexTimeOut);
            return match.Success ? new ScriptArray(match.Groups.Cast<Group>().Select(g => g.Success ? g.Value : null)) : null;
        }

        private static RegexOptions ParseOptions(string? options)
        {
            var result = RegexOptions.None;
            foreach (var option in options ?? "")
            {
                result |= option switch
                {
                    'i' => RegexOptions.IgnoreCase,
                    'm' => RegexOptions.Multiline,
                    's' => RegexOptions.Singleline,
                    'x' => RegexOptions.IgnorePatternWhitespace,
                    _ => throw new ArgumentException($"Unknown regex option '{option}'", nameof(options)),
                };
            }

            return result;
        }
    }

    private static class StringHelpers
    {
        private static readonly (string Numeral, int Value)[] Numerals =
            [("M", 1000), ("CM", 900), ("D", 500), ("CD", 400), ("C", 100), ("XC", 90), ("L", 50), ("XL", 40), ("X", 10), ("IX", 9), ("V", 5), ("IV", 4), ("I", 1)];

        public static string Slugify(string? text) => (text ?? "").Slugify();

        /// <summary>Roman numeral to int (<c>XXV</c> → 25), or null when the text is not a valid numeral.</summary>
        public static int? Romanize(string? text)
        {
            var rest = (text ?? "").Trim().ToUpperInvariant();
            if (rest.Length == 0)
            {
                return null;
            }

            var input = rest;
            var value = 0;
            foreach (var (numeral, amount) in Numerals)
            {
                for (var repeat = 0; rest.StartsWith(numeral, StringComparison.Ordinal) && repeat < 3; repeat++)
                {
                    value += amount;
                    rest = rest[numeral.Length..];
                }
            }

            return rest.Length == 0 && ToRoman(value) == input ? value : null;
        }

        /// <summary>Team names compared loosely: <c>&amp;</c> → <c>and</c>, punctuation removed (dashes and slashes become spaces), spaces collapsed.</summary>
        public static string NormalizeTeam(string? text)
        {
            var builder = new StringBuilder();
            foreach (var c in (text ?? "").Replace("&", " and ", StringComparison.Ordinal))
            {
                if (c is '-' or '/' or '_' || char.IsWhiteSpace(c))
                {
                    builder.Append(' ');
                }
                else if (!char.IsPunctuation(c) && !char.IsSymbol(c))
                {
                    builder.Append(c);
                }
            }

            return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private static string ToRoman(int value)
        {
            var builder = new StringBuilder();
            foreach (var (numeral, amount) in Numerals)
            {
                for (; value >= amount; value -= amount)
                {
                    builder.Append(numeral);
                }
            }

            return builder.ToString();
        }
    }

    private static class DateHelpers
    {
        /// <summary>Adds a duration such as <c>-2h</c> or <c>3d</c>. Null date → null; null or empty duration → the date unchanged.</summary>
        public static DateTime? Add(DateTime? date, object? duration)
        {
            if (date is null)
            {
                return null;
            }

            return duration switch
            {
                null => date,
                TimeSpan span => date + span,
                string text when string.IsNullOrWhiteSpace(text) => date,
                string text when TryParseDuration(text, out var span) => date + span,
                _ => throw new ArgumentException($"Invalid duration '{duration}' (expected e.g. -2h, 3d, 1h30m)", nameof(duration)),
            };
        }

        public static int? Year(DateTime? date) => date?.Year;
    }
}
