using System.Text.RegularExpressions;
using Generral.Core.Extensions;
using Scriban;
using Scriban.Runtime;

namespace Generral.Core.Templating;

/// <summary>Generral helpers available in every template (see docs/specs/05-mapping.md).</summary>
public sealed class GenerralTemplateHelpers : ITemplateHelpers
{
    public void Register(ScriptObject builtins)
    {
        TemplateRenderer.Import(builtins, "regex", typeof(RegexHelpers));
        TemplateRenderer.Import(builtins, "string", typeof(StringHelpers));
    }

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
        public static string Slugify(string? text) => (text ?? "").Slugify();
    }
}
