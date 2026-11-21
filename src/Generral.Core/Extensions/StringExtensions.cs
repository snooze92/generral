using System.Globalization;
using System.Text;

namespace Generral.Core.Extensions;

public static class StringExtensions
{
    public static string RemoveDiacritics(this string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Lower-case ASCII slug: diacritics removed, runs of other characters collapsed to <c>-</c>.</summary>
    public static string Slugify(this string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingDash = false;
        foreach (var c in value.RemoveDiacritics().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                if (pendingDash && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(c);
                pendingDash = false;
            }
            else
            {
                pendingDash = true;
            }
        }

        return builder.ToString();
    }
}
