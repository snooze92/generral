using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Generral.Core.Messaging;

/// <summary>
/// In-memory command queue: commands are lost on restart, and only the last <see cref="Retained"/> are kept
/// (see docs/specs/02-architecture.md § Command queue).
/// </summary>
public sealed class CommandQueue(IEnumerable<ICommandHandler> handlers, TimeProvider time)
{
    public const int Retained = 100;

    private readonly Dictionary<string, ICommandHandler> _handlers = handlers.ToDictionary(h => h.Name, StringComparer.OrdinalIgnoreCase);
    private readonly Channel<Command> _pending = Channel.CreateUnbounded<Command>();
    private readonly List<Command> _commands = [];
    private readonly Dictionary<string, (DateTime Started, DateTime Ended)> _lastRuns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();
    private int _lastId;

    internal ChannelReader<Command> Pending => _pending.Reader;

    /// <summary>Queues a command, or returns the identical one (same name and body) that is still queued.</summary>
    /// <exception cref="InvalidCommandException">Unknown name or invalid body.</exception>
    public CommandSnapshot Enqueue(string name, JsonObject? body = null, CommandTrigger trigger = CommandTrigger.Manual)
    {
        var handler = Handler(name);
        body ??= [];
        if (handler.Validate(body) is { } error)
        {
            throw new InvalidCommandException(error);
        }

        lock (_lock)
        {
            var duplicate = _commands.FirstOrDefault(c => c.Status == CommandStatus.Queued && c.Name == handler.Name && JsonNode.DeepEquals(c.Body, body));
            if (duplicate is not null)
            {
                return duplicate.Snapshot();
            }

            var command = new Command
            {
                Id = ++_lastId,
                Name = handler.Name,
                Body = (JsonObject)body.DeepClone(),
                Trigger = trigger,
                Queued = time.GetUtcNow().UtcDateTime,
            };
            _commands.Add(command);
            Prune();
            _pending.Writer.TryWrite(command);
            return command.Snapshot();
        }
    }

    public IReadOnlyList<CommandSnapshot> All()
    {
        lock (_lock)
        {
            return _commands.Select(c => c.Snapshot()).Reverse().ToList();
        }
    }

    public CommandSnapshot? Find(int id)
    {
        lock (_lock)
        {
            return _commands.FirstOrDefault(c => c.Id == id)?.Snapshot();
        }
    }

    /// <summary>When a command with this name last finished (since the start), for System → Tasks.</summary>
    public (DateTime Started, DateTime Ended)? LastRun(string name)
    {
        lock (_lock)
        {
            return _lastRuns.TryGetValue(name, out var run) ? run : null;
        }
    }

    internal async Task ExecuteAsync(Command command, ILogger logger, CancellationToken cancellationToken)
    {
        command.Start(time.GetUtcNow().UtcDateTime);
        try
        {
            var message = await Handler(command.Name).ExecuteAsync(command.Body, new CommandContext(command, this), cancellationToken);
            command.Finish(CommandStatus.Completed, message ?? "Completed", time.GetUtcNow().UtcDateTime);
        }
        catch (CommandFailedException e)
        {
            command.Finish(CommandStatus.Failed, e.Message, time.GetUtcNow().UtcDateTime);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(e, "Command {Name} failed", command.Name);
            command.Finish(CommandStatus.Failed, e.Message, time.GetUtcNow().UtcDateTime);
        }

        lock (_lock)
        {
            _lastRuns[command.Name] = (command.Started!.Value, command.Ended!.Value);
            Prune();
        }
    }

    private ICommandHandler Handler(string name) =>
        _handlers.GetValueOrDefault(name) ?? throw new InvalidCommandException($"Unknown command '{name}'");

    /// <summary>Drops the oldest finished commands beyond <see cref="Retained"/>; queued and running ones are kept.</summary>
    private void Prune()
    {
        var excess = _commands.Count - Retained;
        for (var i = 0; i < _commands.Count && excess > 0;)
        {
            if (_commands[i].IsFinished)
            {
                _commands.RemoveAt(i);
                excess--;
            }
            else
            {
                i++;
            }
        }
    }
}

/// <summary>Executes queued commands, at most <see cref="Concurrency"/> at a time.</summary>
public sealed class CommandWorker(CommandQueue queue, ILogger<CommandWorker> logger) : BackgroundService
{
    public const int Concurrency = 3;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, Concurrency).Select(_ => Task.Run(() => RunAsync(stoppingToken), stoppingToken)));

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        await foreach (var command in queue.Pending.ReadAllAsync(stoppingToken))
        {
            await queue.ExecuteAsync(command, logger, stoppingToken);
        }
    }
}
