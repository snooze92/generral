using System.Text.RegularExpressions;
using AngleSharp.Css.Dom;
using AngleSharp.Css.Parser;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Generral.Core.Sources.Extraction;

/// <summary>CSS selection over HTML documents (AngleSharp), for <see cref="DocumentFormat.Html"/> extractions.</summary>
internal static partial class HtmlExtraction
{
    private static readonly CssSelectorParser SelectorParser = new();

    /// <summary>Never part of a field's text.</summary>
    private static readonly ISelector Hidden = SelectorParser.ParseSelector("style, script")!;

    private static readonly ISelector Footnotes = SelectorParser.ParseSelector("sup")!;

    public static IDocument Parse(string html) => new HtmlParser().ParseDocument(html);

    public static ISelector CompileSelector(string selector, string location) =>
        SelectorParser.ParseSelector(selector) ?? throw new ExtractionDefinitionException($"{location}: invalid CSS selector '{selector}'");

    public static List<object?> SelectItems(ISelector selector, IDocument document) =>
        document.Descendants<IElement>().Where(e => selector.Match(e, null)).Select(e => (object?)e).ToList();

    /// <summary>The first match of the first alternative that matches (the item itself included), as text or an attribute.</summary>
    public static string? Select(CompiledField field, IElement item)
    {
        var element = First(field.Selector!, item);
        if (element is null)
        {
            return null;
        }

        return field.Definition.Attribute is { } attribute ? element.GetAttribute(attribute) : Text(element, field.Remove);
    }

    /// <summary>
    /// The item's text without <c>sup</c> footnote markers, not its markup: markup churn and footnote renumbering
    /// (e.g. on Wikipedia) would otherwise change every event's hash.
    /// </summary>
    public static string RawText(IElement item) => Text(item, Footnotes);

    /// <summary>The <c>href</c> of the first match (its text without one).</summary>
    public static string? NextUrl(ISelector selector, IDocument document)
    {
        var element = document.Descendants<IElement>().FirstOrDefault(e => selector.Match(e, null));
        return element is null ? null : element.GetAttribute("href") ?? Text(element, null);
    }

    private static IElement? First(ISelector selector, IElement item)
    {
        IEnumerable<ISelector> alternatives = selector is IMultiSelector and IEnumerable<ISelector> list ? list : [selector];
        var candidates = item.Descendants<IElement>().Prepend(item).ToList();
        return alternatives
            .Select(alternative => candidates.FirstOrDefault(e => alternative.Match(e, item)))
            .FirstOrDefault(e => e is not null);
    }

    /// <summary>Text content, trimmed and whitespace-collapsed; <c>br</c> counts as a space.</summary>
    private static string Text(IElement element, ISelector? remove)
    {
        var clone = (IElement)element.Clone();
        foreach (var hidden in clone.Descendants<IElement>().Where(e => Hidden.Match(e, clone) || remove?.Match(e, clone) == true).ToList())
        {
            hidden.Remove();
        }

        foreach (var br in clone.Descendants<IElement>().Where(e => e.LocalName == "br").ToList())
        {
            br.Replace(clone.Owner!.CreateTextNode(" "));
        }

        return Whitespace().Replace(clone.TextContent, " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
