namespace Generral.Core.Datastore;

/// <summary>A key/value pair for settings edited in the UI (as opposed to host-level settings in config.yaml).</summary>
public class ConfigEntry
{
    public int Id { get; set; }

    public required string Key { get; set; }

    public required string Value { get; set; }
}
