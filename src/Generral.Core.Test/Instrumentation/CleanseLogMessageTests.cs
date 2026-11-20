using Generral.Core.Instrumentation;

namespace Generral.Core.Test.Instrumentation;

public class CleanseLogMessageTests
{
    [TestCase("GET /api/v1/system/status?apikey=0123456789abcdef0123456789abcdef", "apikey=(removed)")]
    [TestCase("https://tracker.example/announce/0123456789abcdef0123", "announce/(removed)")]
    [TestCase("POST login password=hunter2&user=me", "password=(removed)")]
    [TestCase("Header X-Api-Key: 0123456789abcdef0123456789abcdef", "X-Api-Key: (removed)")]
    [TestCase("Config at /home/julien/.config/Generral", "/home/(removed)/")]
    [TestCase("https://discord.com/api/webhooks/123/secret-token", "webhooks/(removed)/(removed)")]
    public void Secrets_are_removed(string message, string expected)
    {
        var cleansed = CleanseLogMessage.Cleanse(message);

        Assert.That(cleansed, Does.Contain(expected));
    }

    [Test]
    public void Remote_ip_is_masked_but_local_ip_is_kept()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CleanseLogMessage.Cleanse("Auth-Success ip 81.2.3.4 username 'x'"), Does.Contain("81.*.*.4"));
            Assert.That(CleanseLogMessage.Cleanse("Auth-Success ip 192.168.1.10 username 'x'"), Does.Contain("192.168.1.10"));
        });
    }

    [Test]
    public void Plain_messages_are_untouched()
    {
        Assert.That(CleanseLogMessage.Cleanse("Applying 1 database migration(s)"), Is.EqualTo("Applying 1 database migration(s)"));
    }
}
