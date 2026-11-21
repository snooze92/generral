using System.Text.Json.Nodes;

namespace Generral.Core.Sources;

public class Source
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Source type plugin id, e.g. <c>thesportsdb</c> or <c>http-json</c>.</summary>
    public required string Type { get; set; }

    public JsonObject Settings { get; set; } = [];

    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromHours(12);

    public bool Enabled { get; set; } = true;

    public DateTime? LastRefresh { get; set; }

    public DateTime? LastSuccess { get; set; }

    public string? LastError { get; set; }

    public List<int> Tags { get; set; } = [];
}
