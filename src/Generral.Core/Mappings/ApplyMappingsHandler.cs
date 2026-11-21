using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Generral.Core.Mappings;

/// <summary><c>ApplyMappings { mappingId? }</c>: one mapping, or every enabled mapping.</summary>
public sealed class ApplyMappingsHandler(ApplyMappingsService apply, IServiceScopeFactory scopes) : ICommandHandler
{
    public const string CommandName = "ApplyMappings";

    public string Name => CommandName;

    public static JsonObject Body(int mappingId) => new() { ["mappingId"] = mappingId };

    /// <summary>Queues one <c>ApplyMappings</c> per enabled mapping of the given (successfully refreshed) sources.</summary>
    public static async Task EnqueueForSourcesAsync(IServiceScopeFactory scopes, CommandContext context, IReadOnlyCollection<int> sourceIds, CancellationToken cancellationToken)
    {
        if (sourceIds.Count == 0)
        {
            return;
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
        var mappingIds = await db.Mappings
            .Where(m => m.Enabled && sourceIds.Contains(m.SourceId))
            .OrderBy(m => m.Priority).ThenBy(m => m.Id)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in mappingIds)
        {
            context.Enqueue(CommandName, Body(id));
        }
    }

    public string? Validate(JsonObject body) =>
        body["mappingId"] is null || MappingId(body) is not null ? null : "mappingId must be an integer";

    public async Task<string?> ExecuteAsync(JsonObject body, CommandContext context, CancellationToken cancellationToken)
    {
        List<int> ids;
        if (MappingId(body) is { } id)
        {
            ids = [id];
        }
        else
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
            ids = await db.Mappings.Where(m => m.Enabled).OrderBy(m => m.Priority).ThenBy(m => m.Id).Select(m => m.Id).ToListAsync(cancellationToken);
        }

        var results = new List<ApplyMappingResult>();
        foreach (var mappingId in ids)
        {
            try
            {
                results.Add(await apply.ApplyAsync(mappingId, cancellationToken));
            }
            catch (KeyNotFoundException e) when (ids.Count == 1)
            {
                throw new CommandFailedException(e.Message);
            }
            catch (KeyNotFoundException)
            {
                // Deleted while the command ran.
            }
        }

        var summary = string.Join("; ", results.Select(r => r.Summary));
        if (results.All(r => r.Success))
        {
            return results.Count == 1 ? summary : $"Applied {results.Count} mapping(s)" + (results.Count > 0 ? $": {summary}" : "");
        }

        throw new CommandFailedException(summary);
    }

    private static int? MappingId(JsonObject body) =>
        body["mappingId"] is JsonValue value && value.TryGetValue<int>(out var id) ? id : null;
}
