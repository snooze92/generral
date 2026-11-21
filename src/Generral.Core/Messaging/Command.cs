using System.Text.Json.Nodes;

namespace Generral.Core.Messaging;

public enum CommandStatus
{
    Queued,
    Started,
    Completed,
    Failed,
}

public enum CommandTrigger
{
    Manual,
    Scheduled,
}

/// <summary>A queued or finished command (docs/specs/02-architecture.md § Command queue).</summary>
public sealed class Command
{
    private readonly Lock _lock = new();

    public required int Id { get; init; }

    public required string Name { get; init; }

    public required JsonObject Body { get; init; }

    public required CommandTrigger Trigger { get; init; }

    public required DateTime Queued { get; init; }

    public CommandStatus Status { get; private set; } = CommandStatus.Queued;

    public string? Message { get; private set; }

    public DateTime? Started { get; private set; }

    public DateTime? Ended { get; private set; }

    public bool IsFinished => Status is CommandStatus.Completed or CommandStatus.Failed;

    public CommandSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new CommandSnapshot(Id, Name, (JsonObject)Body.DeepClone(), Status, Message, Trigger, Queued, Started, Ended);
        }
    }

    internal void Start(DateTime now) => Update(CommandStatus.Started, null, started: now);

    internal void SetMessage(string message) => Update(null, message);

    internal void Finish(CommandStatus status, string? message, DateTime now) => Update(status, message, ended: now);

    private void Update(CommandStatus? status, string? message, DateTime? started = null, DateTime? ended = null)
    {
        lock (_lock)
        {
            Status = status ?? Status;
            Message = message ?? Message;
            Started = started ?? Started;
            Ended = ended ?? Ended;
        }
    }
}

public sealed record CommandSnapshot(
    int Id,
    string Name,
    JsonObject Body,
    CommandStatus Status,
    string? Message,
    CommandTrigger Trigger,
    DateTime Queued,
    DateTime? Started,
    DateTime? Ended);

/// <summary>Runs one kind of command, by <see cref="Name"/>.</summary>
public interface ICommandHandler
{
    string Name { get; }

    /// <summary>Checks the body before the command is queued: returns an error message, or null.</summary>
    string? Validate(JsonObject body) => null;

    /// <returns>The completion message.</returns>
    /// <exception cref="CommandFailedException">The command failed with a message for the user.</exception>
    Task<string?> ExecuteAsync(JsonObject body, CommandContext context, CancellationToken cancellationToken);
}

public sealed class CommandContext(Command command, CommandQueue queue)
{
    public CommandTrigger Trigger => command.Trigger;

    /// <summary>Queues a follow-up command with the same trigger.</summary>
    /// <exception cref="InvalidCommandException">Unknown name or invalid body.</exception>
    public CommandSnapshot Enqueue(string name, JsonObject? body = null) => queue.Enqueue(name, body, command.Trigger);

    public void Progress(string message) => command.SetMessage(message);
}

public sealed class CommandFailedException(string message) : Exception(message);

/// <summary>Unknown command name or invalid body: the command is not queued.</summary>
public sealed class InvalidCommandException(string message) : Exception(message);
