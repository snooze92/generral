using Generral.Core.Tv;

namespace Generral.Core.Test.Tv;

public class SeriesTitlesTests
{
    [TestCase("The Cup: Finals", "thecupfinals")]
    [TestCase("Formula 1 and the 3% Club", "formula13percentclub")]
    [TestCase("Ligue des Champions – Éte", "liguedeschampionsete")]
    [TestCase("1923", "1923")]
    public void Clean_title_drops_punctuation_and_inner_articles(string title, string expected) =>
        Assert.That(SeriesTitles.Clean(title), Is.EqualTo(expected));

    [TestCase("The Cup: Finals", "cup finals")]
    [TestCase("A  Season", "season")]
    [TestCase("UFC", "ufc")]
    public void Sort_title_drops_punctuation_and_a_leading_article(string title, string expected) =>
        Assert.That(SeriesTitles.Sort(title), Is.EqualTo(expected));

    [TestCase("UFC: Fight Night", null, "UFC - Fight Night")]
    [TestCase("What? A/B \"Cup\"", 2026, "What! A+B Cup (2026)")]
    [TestCase(" Cup.. ", null, "Cup")]
    public void Folder_name_replaces_illegal_characters(string title, int? year, string expected) =>
        Assert.That(SeriesTitles.FolderName(title, year), Is.EqualTo(expected));
}
