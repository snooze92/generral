using Generral.Core.Configuration;

namespace Generral.Core.Test.Configuration;

public class HostConfigProviderTests
{
    private TestFolders _folders = null!;

    [SetUp]
    public void SetUp() => _folders = new TestFolders();

    [TearDown]
    public void TearDown() => _folders.Dispose();

    private HostConfigProvider Create(Dictionary<string, string>? env = null)
    {
        return new HostConfigProvider(_folders, env ?? []);
    }

    [Test]
    public void First_start_creates_config_file_with_generated_api_key()
    {
        var provider = Create();

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(_folders.ConfigFile), Is.True);
            Assert.That(provider.GeneratedApiKey, Is.True);
            Assert.That(provider.Current.ApiKey, Has.Length.EqualTo(32));
            Assert.That(File.ReadAllText(_folders.ConfigFile), Does.Contain(provider.Current.ApiKey));
        });
    }

    [Test]
    public void Api_key_is_stable_across_restarts()
    {
        var first = Create().Current.ApiKey;
        var second = Create();

        Assert.Multiple(() =>
        {
            Assert.That(second.Current.ApiKey, Is.EqualTo(first));
            Assert.That(second.GeneratedApiKey, Is.False);
        });
    }

    [Test]
    public void Defaults_match_spec()
    {
        var settings = Create().Current;

        Assert.Multiple(() =>
        {
            Assert.That(settings.Port, Is.EqualTo(8990));
            Assert.That(settings.UrlBase, Is.Empty);
            Assert.That(settings.BindAddress, Is.EqualTo("*"));
            Assert.That(settings.AuthenticationMethod, Is.EqualTo(AuthenticationType.Forms));
            Assert.That(settings.AuthenticationRequired, Is.EqualTo(AuthenticationRequiredType.Enabled));
            Assert.That(settings.LogLevel, Is.EqualTo("info"));
            Assert.That(settings.DatabaseType, Is.EqualTo(DatabaseType.Sqlite));
            Assert.That(settings.AllowScriptSources, Is.False);
            Assert.That(settings.InstanceName, Is.EqualTo("Generral"));
        });
    }

    [Test]
    public void Environment_overrides_file_which_overrides_defaults()
    {
        File.WriteAllText(_folders.ConfigFile, """
            port: 9000
            urlBase: generral/
            logLevel: debug
            database:
              connectionString: /data/g.db
            """);

        var provider = Create(new() { ["GENERRAL__PORT"] = "9100", ["GENERRAL__AUTHMETHOD"] = "external" });
        var settings = provider.Current;

        Assert.Multiple(() =>
        {
            Assert.That(settings.Port, Is.EqualTo(9100));
            Assert.That(settings.UrlBase, Is.EqualTo("/generral"));
            Assert.That(settings.LogLevel, Is.EqualTo("debug"));
            Assert.That(settings.DatabaseConnectionString, Is.EqualTo("/data/g.db"));
            Assert.That(settings.AuthenticationMethod, Is.EqualTo(AuthenticationType.External));
            Assert.That(provider.IsOverriddenByEnvironment(HostConfigKey.Port), Is.True);
            Assert.That(provider.IsOverriddenByEnvironment(HostConfigKey.UrlBase), Is.False);
        });
    }

    [Test]
    public void Env_api_key_is_not_written_to_file()
    {
        var provider = Create(new() { ["GENERRAL__APIKEY"] = "fromenvironment0000000000000000ab" });

        Assert.Multiple(() =>
        {
            Assert.That(provider.Current.ApiKey, Is.EqualTo("fromenvironment0000000000000000ab"));
            Assert.That(File.ReadAllText(_folders.ConfigFile), Does.Not.Contain("apiKey"));
            Assert.That(() => provider.ResetApiKey(), Throws.InvalidOperationException);
        });
    }

    [Test]
    public void Update_persists_and_raises_changed()
    {
        var provider = Create();
        HostSettings? changed = null;
        provider.Changed += (_, s) => changed = s;

        provider.Update(new HostSettingsUpdate { Port = 9200, UrlBase = "/x/", AuthenticationRequired = AuthenticationRequiredType.DisabledForLocalAddresses });

        var reloaded = Create().Current;

        Assert.Multiple(() =>
        {
            Assert.That(changed?.Port, Is.EqualTo(9200));
            Assert.That(reloaded.Port, Is.EqualTo(9200));
            Assert.That(reloaded.UrlBase, Is.EqualTo("/x"));
            Assert.That(reloaded.AuthenticationRequired, Is.EqualTo(AuthenticationRequiredType.DisabledForLocalAddresses));
        });
    }

    [Test]
    public void Reset_api_key_changes_and_persists_it()
    {
        var provider = Create();
        var before = provider.Current.ApiKey;

        provider.ResetApiKey();

        Assert.Multiple(() =>
        {
            Assert.That(provider.Current.ApiKey, Is.Not.EqualTo(before));
            Assert.That(Create().Current.ApiKey, Is.EqualTo(provider.Current.ApiKey));
        });
    }

    [TestCase("GENERRAL__PORT", "abc")]
    [TestCase("GENERRAL__AUTHMETHOD", "basic")]
    [TestCase("GENERRAL__AUTHREQUIRED", "sometimes")]
    [TestCase("GENERRAL__LOGLEVEL", "verbose")]
    [TestCase("GENERRAL__ALLOWSCRIPTSOURCES", "yes")]
    public void Invalid_values_fail_fast_with_a_helpful_message(string variable, string value)
    {
        Assert.That(
            () => Create(new() { [variable] = value }),
            Throws.TypeOf<InvalidConfigurationException>().With.Message.Contains(variable));
    }

    [Test]
    public void Invalid_yaml_fails_fast()
    {
        File.WriteAllText(_folders.ConfigFile, "port: [unclosed");

        Assert.That(() => Create(), Throws.TypeOf<InvalidConfigurationException>());
    }

    [TestCase("", "")]
    [TestCase("/", "")]
    [TestCase("generral", "/generral")]
    [TestCase("/generral/", "/generral")]
    [TestCase(" /a/b/ ", "/a/b")]
    public void Url_base_is_normalized(string input, string expected)
    {
        Assert.That(HostConfigProvider.NormalizeUrlBase(input), Is.EqualTo(expected));
    }
}
