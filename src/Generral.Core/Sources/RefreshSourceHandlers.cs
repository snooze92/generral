using System.Text.Json;
using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.Mappings;
using Generral.Core.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Generral.Core.Sources;

/// <summary>
/// <c>RefreshSources</c>: every enabled source that is due, or with <c>{ force: true }</c> every enabled source.
/// Then queues <c>ApplyMappings</c> for the mappings of the sources that refreshed successfully.
/// </summary>
public sealed class RefreshSourcesHandler(SourceRefreshService refresh, IServiceScopeFactory scopes, TimeProvider time) : ICommandHandler
{
    public const string CommandName = "RefreshSources";

    public string Name => CommandName;

    public async Task<string?> ExecuteAsync(JsonObject body, CommandContext context, CancellationToken cancellationToken)
    {
        var force = body["force"] is JsonValue value && value.GetValueKind() == JsonValueKind.True;
        List<int> ids;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
            var now = time.GetUtcNow().UtcDateTime;
            var sources = await db.Sources.AsNoTracking().Where(s => s.Enabled).OrderBy(s => s.Id).ToListAsync(cancellationToken);
            ids = sources.Where(s => force || SourceRefreshService.IsDue(s, now)).Select(s => s.Id).ToList();
        }

        var results = await Task.WhenAll(ids.Select(async id =>
        {
            try
            {
                return await refresh.RefreshAsync(id, onlyIfDue: !force, cancellationToken);
            }
            catch (KeyNotFoundException)
            {
                return null;
            }
        }));

        var done = results.OfType<SourceRefreshResult>().ToList();
        await ApplyMappingsHandler.EnqueueForSourcesAsync(scopes, context, done.Where(r => r.Success).Select(r => r.SourceId).ToList(), cancellationToken);
        var failed = done.Count(r => !r.Success);
        return failed == 0
            ? $"Refreshed {done.Count} source(s)"
            : $"Refreshed {done.Count} source(s), {failed} failed: " + string.Join("; ", done.Where(r => !r.Success).Select(r => r.Summary));
    }
}

/// <summary><c>RefreshSource { sourceId }</c>: one source, due or not, then <c>ApplyMappings</c> for its mappings.</summary>
public sealed class RefreshSourceHandler(SourceRefreshService refresh, IServiceScopeFactory scopes) : ICommandHandler
{
    public const string CommandName = "RefreshSource";

    public string Name => CommandName;

    public static int? SourceId(JsonObject body) =>
        body["sourceId"] is JsonValue value && value.TryGetValue<int>(out var id) ? id : null;

    public string? Validate(JsonObject body) => SourceId(body) is null ? "sourceId is required" : null;

    public async Task<string?> ExecuteAsync(JsonObject body, CommandContext context, CancellationToken cancellationToken)
    {
        SourceRefreshResult result;
        try
        {
            result = (await refresh.RefreshAsync(SourceId(body)!.Value, cancellationToken: cancellationToken))!;
        }
        catch (KeyNotFoundException e)
        {
            throw new CommandFailedException(e.Message);
        }

        if (!result.Success)
        {
            throw new CommandFailedException(result.Summary);
        }

        await ApplyMappingsHandler.EnqueueForSourcesAsync(scopes, context, [result.SourceId], cancellationToken);
        return result.Summary;
    }
}
