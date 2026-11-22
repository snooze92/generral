using Generral.Core.DecisionEngine;
using Generral.Core.Languages;

namespace Generral.Core.Test.Languages;

public class LanguageParserTests
{
    [TestCase("UFC.324.PPV.1080p.WEB.h264-GRP", "Unknown")]
    [TestCase("UFC 324 Main Card 720p HDTV x264", "Unknown")]
    [TestCase("UFC.324.English.1080p.WEB.h264", "English")]
    [TestCase("UFC.324.TRUEFRENCH.1080p.WEB.h264-GRP", "French")]
    [TestCase("UFC 324 FRENCH 720p HDTV x264", "French")]
    [TestCase("UFC.324.VOSTFR.1080p.WEB.x264", "Unknown")]
    [TestCase("UFC.324.RUS.1080p.WEB-DL", "Russian")]
    [TestCase("UFC 324 [RUS] 1080p", "Russian")]
    [TestCase("UFC.324.German.1080p.WEB.h264", "German")]
    [TestCase("UFC.324.German.DL.1080p.WEB.h264", "German, Original")]
    [TestCase("UFC.324.MULTi.1080p.WEB.h264-GRP", "Original")]
    [TestCase("UFC.324.MULTi.TRUEFRENCH.1080p.WEB.h264-GRP", "French, Original")]
    [TestCase("UFC.324.iTA.ENG.1080p.WEB.h264", "English, Italian")]
    public void Parses_release_languages(string title, string expected)
    {
        var languages = DownloadDecisionMaker.ParseLanguages(title);

        Assert.That(string.Join(", ", languages.Select(l => l.Name).Order(StringComparer.Ordinal)), Is.EqualTo(expected));
    }
}
