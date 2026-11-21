using Generral.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Generral.Core.Presets;

public sealed record StoredPreset(PresetDocument Document, bool Shipped);

/// <summary>
/// Shipped presets (read-only, <c>GENERRAL__PRESETSDIR</c>, default <c>&lt;app&gt;/presets</c>) and imported ones
/// (<c>&lt;config&gt;/presets</c>). Files are read on every call: the folders are small.
/// </summary>
public sealed class PresetStore(IAppFolders folders, IConfiguration configuration, ILogger<PresetStore> logger)
{
    /// <summary>Configuration key (from <c>GENERRAL__PRESETSDIR</c>) for the shipped presets.</summary>
    public const string PresetsDirKey = "GENERRAL:PRESETSDIR";

    public const int MaxSize = 256 * 1024;

    public string ShippedFolder => Path.GetFullPath(configuration[PresetsDirKey] is { Length: > 0 } dir ? dir : Path.Combine(AppContext.BaseDirectory, "presets"));

    public string UserFolder => Path.Combine(folders.ConfigFolder, "presets");

    /// <summary>Shipped first; an imported preset never replaces a shipped one.</summary>
    public IReadOnlyList<StoredPreset> All()
    {
        var shipped = Read(ShippedFolder, shipped: true);
        var ids = shipped.Select(p => p.Document.Id).ToHashSet();
        return [.. shipped, .. Read(UserFolder, shipped: false).Where(p => !ids.Contains(p.Document.Id))];
    }

    public StoredPreset? Find(string id) => All().FirstOrDefault(p => p.Document.Id == id);

    /// <summary>Validates and stores an imported preset, replacing an imported one with the same id.</summary>
    /// <exception cref="PresetException">Invalid, or the id of a shipped preset.</exception>
    public PresetDocument Import(string text)
    {
        if (text.Length > MaxSize)
        {
            throw new PresetException($"preset: larger than {MaxSize / 1024} KB");
        }

        var document = PresetDocument.Parse(text);
        document.Resolve(document.SampleValues().ToDictionary(p => p.Key, p => (string?)p.Value));
        if (Read(ShippedFolder, shipped: true).Any(p => p.Document.Id == document.Id))
        {
            throw new PresetException($"id: '{document.Id}' is a shipped preset, use another id");
        }

        Directory.CreateDirectory(UserFolder);
        File.WriteAllText(Path.Combine(UserFolder, document.Id + ".yaml"), text);
        return document;
    }

    private List<StoredPreset> Read(string folder, bool shipped)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var presets = new List<StoredPreset>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.y*ml").Order(StringComparer.Ordinal))
        {
            try
            {
                presets.Add(new StoredPreset(PresetDocument.Parse(File.ReadAllText(file)), shipped));
            }
            catch (Exception e) when (e is PresetException or IOException)
            {
                logger.LogWarning("Skipping preset {File}: {Error}", file, e.Message);
            }
        }

        return presets.DistinctBy(p => p.Document.Id).ToList();
    }
}
