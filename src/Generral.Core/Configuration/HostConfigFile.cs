using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Generral.Core.Configuration;

/// <summary>The on-disk shape of <c>config.yaml</c>. Every value is optional.</summary>
public sealed class HostConfigFile
{
    public int? Port { get; set; }

    public string? UrlBase { get; set; }

    public string? BindAddress { get; set; }

    public string? ApiKey { get; set; }

    public string? AuthenticationMethod { get; set; }

    public string? AuthenticationRequired { get; set; }

    public string? LogLevel { get; set; }

    public DatabaseSection? Database { get; set; }

    public bool? AllowScriptSources { get; set; }

    public string? InstanceName { get; set; }

    public sealed class DatabaseSection
    {
        public string? Type { get; set; }

        public string? ConnectionString { get; set; }
    }

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public static HostConfigFile Parse(string yaml)
    {
        return Deserializer.Deserialize<HostConfigFile?>(yaml) ?? new HostConfigFile();
    }

    public string Serialize()
    {
        const string header =
            "# Generral host settings. Environment variables (GENERRAL__*) take precedence over this file.\n" +
            "# Everything else is stored in the database and edited from the UI.\n";

        return header + Serializer.Serialize(this);
    }
}
