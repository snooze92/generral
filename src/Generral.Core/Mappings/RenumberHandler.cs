using System.Text.Json.Nodes;
using Generral.Core.Messaging;

namespace Generral.Core.Mappings;

/// <summary><c>Renumber { seriesId }</c>: unlocked episodes of the series take their ideal numbers (docs/specs/05-mapping.md § Renumber).</summary>
public sealed class RenumberHandler(ApplyMappingsService apply) : ICommandHandler
{
    public const string CommandName = "Renumber";

    public string Name => CommandName;

    public string? Validate(JsonObject body) => SeriesId(body) is null ? "seriesId must be an integer" : null;

    public async Task<string?> ExecuteAsync(JsonObject body, CommandContext context, CancellationToken cancellationToken)
    {
        RenumberResult result;
        try
        {
            result = await apply.RenumberAsync(SeriesId(body)!.Value, false, cancellationToken);
        }
        catch (KeyNotFoundException e)
        {
            throw new CommandFailedException(e.Message);
        }

        var failed = result.Mappings.Where(m => !m.Success).Select(m => m.Summary).ToList();
        return failed.Count == 0
            ? $"Renumbered {result.Moves.Count} episode(s)"
            : throw new CommandFailedException($"Renumbered {result.Moves.Count} episode(s); {string.Join("; ", failed)}");
    }

    private static int? SeriesId(JsonObject body) =>
        body["seriesId"] is JsonValue value && value.TryGetValue<int>(out var id) ? id : null;
}
