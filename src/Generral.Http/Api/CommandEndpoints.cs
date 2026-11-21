using System.Text.Json.Nodes;
using Generral.Core.Messaging;
using Microsoft.AspNetCore.Routing;

namespace Generral.Http.Api;

/// <summary>Sonarr's CommandResource, without priority and result.</summary>
public sealed record CommandResource
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required string CommandName { get; init; }

    public string? Message { get; init; }

    public required JsonObject Body { get; init; }

    public required CommandStatus Status { get; init; }

    public required DateTime Queued { get; init; }

    public DateTime? Started { get; init; }

    public DateTime? Ended { get; init; }

    public TimeSpan? Duration { get; init; }

    public required CommandTrigger Trigger { get; init; }

    public static CommandResource From(CommandSnapshot command) => new()
    {
        Id = command.Id,
        Name = command.Name,
        CommandName = command.Name,
        Message = command.Message,
        Body = command.Body,
        Status = command.Status,
        Queued = command.Queued,
        Started = command.Started,
        Ended = command.Ended,
        Duration = command.Ended - command.Started,
        Trigger = command.Trigger,
    };
}

public static class CommandEndpoints
{
    public static void MapCommandEndpoints(this IEndpointRouteBuilder api)
    {
        var commands = api.MapGroup("/command");

        commands.MapGet("/", (CommandQueue queue) => queue.All().Select(CommandResource.From));
        commands.MapGet("/{id:int}", (int id, CommandQueue queue) =>
            queue.Find(id) is { } command ? Results.Ok(CommandResource.From(command)) : Results.NotFound());
        commands.MapPost("/", Enqueue);
    }

    /// <summary>Like Sonarr, the body is the command's properties next to its <c>name</c>.</summary>
    public static IResult Enqueue(JsonObject request, CommandQueue queue)
    {
        var body = (JsonObject)request.DeepClone();
        var name = body["name"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        body.Remove("name");

        if (string.IsNullOrWhiteSpace(name))
        {
            return ValidationFailure.BadRequest(new ValidationFailure("name", "Name is required"));
        }

        try
        {
            return Created(queue.Enqueue(name, body));
        }
        catch (InvalidCommandException e)
        {
            return ValidationFailure.BadRequest(new ValidationFailure("name", e.Message));
        }
    }

    public static IResult Created(CommandSnapshot command) =>
        Results.Created($"command/{command.Id}", CommandResource.From(command));
}
