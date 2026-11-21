using System.Text.Json.Nodes;
using Generral.Core.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Generral.Core.Test.Messaging;

public class CommandQueueTests
{
    private readonly FakeHandler _handler = new();

    [Test]
    public async Task Commands_run_to_completion_with_their_message()
    {
        var queue = new CommandQueue([_handler], TimeProvider.System);
        var queued = queue.Enqueue("Fake", new JsonObject { ["value"] = "ok" });

        Assert.That(queued.Status, Is.EqualTo(CommandStatus.Queued));
        await RunPendingAsync(queue);

        var command = queue.Find(queued.Id)!;
        Assert.Multiple(() =>
        {
            Assert.That(command.Status, Is.EqualTo(CommandStatus.Completed));
            Assert.That(command.Message, Is.EqualTo("done ok"));
            Assert.That(command.Started, Is.Not.Null);
            Assert.That(command.Ended, Is.GreaterThanOrEqualTo(command.Started!.Value));
        });
    }

    [Test]
    public async Task Failures_are_recorded_on_the_command()
    {
        var queue = new CommandQueue([_handler], TimeProvider.System);
        var queued = queue.Enqueue("Fake", new JsonObject { ["value"] = "fail" });

        await RunPendingAsync(queue);

        Assert.Multiple(() =>
        {
            Assert.That(queue.Find(queued.Id)!.Status, Is.EqualTo(CommandStatus.Failed));
            Assert.That(queue.Find(queued.Id)!.Message, Is.EqualTo("nope"));
        });
    }

    [Test]
    public void Unknown_names_and_invalid_bodies_are_rejected()
    {
        var queue = new CommandQueue([_handler], TimeProvider.System);

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidCommandException>(() => queue.Enqueue("Nope"));
            Assert.Throws<InvalidCommandException>(() => queue.Enqueue("Fake", new JsonObject { ["value"] = "invalid" }));
        });
    }

    [Test]
    public void An_identical_queued_command_is_not_queued_twice()
    {
        var queue = new CommandQueue([_handler], TimeProvider.System);

        var first = queue.Enqueue("fake", new JsonObject { ["value"] = "a" });
        var second = queue.Enqueue("Fake", new JsonObject { ["value"] = "a" });
        var other = queue.Enqueue("Fake", new JsonObject { ["value"] = "b" });

        Assert.Multiple(() =>
        {
            Assert.That(second.Id, Is.EqualTo(first.Id));
            Assert.That(first.Name, Is.EqualTo("Fake"));
            Assert.That(other.Id, Is.Not.EqualTo(first.Id));
        });
    }

    [Test]
    public async Task Only_the_last_100_finished_commands_are_kept()
    {
        var queue = new CommandQueue([_handler], TimeProvider.System);
        for (var i = 0; i < 120; i++)
        {
            queue.Enqueue("Fake", new JsonObject { ["value"] = i.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            await RunPendingAsync(queue);
        }

        var all = queue.All();
        Assert.Multiple(() =>
        {
            Assert.That(all, Has.Count.EqualTo(CommandQueue.Retained));
            Assert.That(all[0].Id, Is.EqualTo(120));
            Assert.That(all[^1].Id, Is.EqualTo(21));
        });
    }

    private static async Task RunPendingAsync(CommandQueue queue)
    {
        while (queue.Pending.TryRead(out var command))
        {
            await queue.ExecuteAsync(command, NullLogger.Instance, CancellationToken.None);
        }
    }

    private sealed class FakeHandler : ICommandHandler
    {
        public string Name => "Fake";

        public string? Validate(JsonObject body) => body["value"]?.GetValue<string>() == "invalid" ? "invalid value" : null;

        public Task<string?> ExecuteAsync(JsonObject body, CommandContext context, CancellationToken cancellationToken)
        {
            var value = body["value"]?.GetValue<string>();
            return value == "fail" ? throw new CommandFailedException("nope") : Task.FromResult<string?>($"done {value}");
        }
    }
}
