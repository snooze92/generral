// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Notifications/Discord/Discord.cs, DiscordColors.cs, DiscordProxy.cs,
// DiscordSettings.cs and Payloads/*.cs
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Generral.Core.Configuration;
using Generral.Core.Extensions;
using Generral.Core.Indexers;
using Generral.Core.Indexers.Torznab;
using Generral.Core.MediaFiles;

namespace Generral.Core.Notifications.Discord;

public enum DiscordColors
{
    Danger = 15749200,
    Success = 2605644,
    Warning = 16753920,
    Standard = 16761392,
    Upgrade = 4089856,
}

public sealed record DiscordPayload(
    string? Content,
    string? Username,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl,
    IReadOnlyList<DiscordEmbed> Embeds);

public sealed record DiscordEmbed
{
    public string? Description { get; init; }

    public string? Title { get; init; }

    public int Color { get; init; }

    public DiscordAuthor? Author { get; init; }

    public DiscordImage? Thumbnail { get; init; }

    public DiscordImage? Image { get; init; }

    public string? Timestamp { get; init; }

    public List<DiscordField> Fields { get; init; } = [];
}

public sealed record DiscordAuthor(string Name, [property: JsonPropertyName("icon_url")] string? IconUrl);

public sealed record DiscordImage(string Url);

public sealed record DiscordField(string Name, string Value, bool Inline = false);

/// <summary>
/// Sonarr's Discord embeds with a fixed field set. Overviews are only sent when spoiler-free mode allows (the message's episodes are
/// already filtered), and the episode thumbnail is never used, since it may show the result.
/// </summary>
public sealed class DiscordNotification(IHttpClientFactory clients, IHostConfigProvider hostConfig, TimeProvider time) : INotification
{
    public const string Name = "Discord";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public string Implementation => Name;

    string INotification.Name => Name;

    public string Link => "https://support.discordapp.com/hc/en-us/articles/228383668-Intro-to-Webhooks";

    public IReadOnlySet<NotificationTrigger> SupportedTriggers { get; } = Enum.GetValues<NotificationTrigger>().ToHashSet();

    public IReadOnlyList<IndexerField> Fields { get; } =
    [
        new() { Order = 0, Name = "webHookUrl", Label = "Webhook URL", Privacy = "apiKey", HelpText = "Discord channel webhook URL" },
        new() { Order = 1, Name = "username", Label = "Username", HelpText = "The username to post as, defaults to the Discord webhook default" },
        new() { Order = 2, Name = "avatar", Label = "Avatar", HelpText = "Change the avatar that is used for messages from this integration" },
        new() { Order = 3, Name = "author", Label = "Author", HelpText = "Override the embed author that shows for this notification. Blank is instance name" },
    ];

    public IReadOnlyList<IndexerValidationFailure> Validate(JsonObject settings) =>
        Uri.TryCreate(settings.Text("webHookUrl"), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? []
            : [new IndexerValidationFailure("webHookUrl", "Must be a valid http(s) URL")];

    public Task SendAsync(NotificationDefinition definition, NotificationMessage message, CancellationToken cancellationToken = default) =>
        PostAsync(definition, Build(definition, message), cancellationToken);

    public Task SendHealthAsync(NotificationDefinition definition, HealthCheck.HealthCheck check, bool restored, CancellationToken cancellationToken = default) =>
        PostAsync(definition, Payload(definition, new DiscordEmbed
        {
            Author = Author(definition),
            Title = restored ? "Health Issue Resolved: " + check.Source : check.Source,
            Description = restored ? $"The following issue is now resolved: {check.Message}" : check.Message,
            Timestamp = time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            Color = (int)(restored ? DiscordColors.Success : check.Type == HealthCheck.HealthCheckResult.Warning ? DiscordColors.Warning : DiscordColors.Danger),
        }), cancellationToken);

    public async Task<IReadOnlyList<IndexerValidationFailure>> TestAsync(NotificationDefinition definition, CancellationToken cancellationToken = default)
    {
        try
        {
            await PostAsync(definition, Payload(definition, new DiscordEmbed { Title = "Test message", Color = (int)DiscordColors.Standard, Author = Author(definition) }), cancellationToken);
            return [];
        }
        catch (Exception e) when (e is NotificationException or HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return [new IndexerValidationFailure("webHookUrl", $"Unable to send test message: {e.Message}")];
        }
    }

    public DiscordPayload Build(NotificationDefinition definition, NotificationMessage message)
    {
        var series = message.Series;
        var (description, color) = message switch
        {
            GrabMessage => ("Episode Grabbed", DiscordColors.Standard),
            DownloadMessage { IsUpgrade: true } => ("Episode Upgraded", DiscordColors.Upgrade),
            DownloadMessage => ("Episode Imported", DiscordColors.Success),
            EpisodeFileDeleteMessage => ("Episode Deleted", DiscordColors.Danger),
            SeriesAddMessage => ("Series Added", DiscordColors.Success),
            SeriesDeleteMessage => ("Series Deleted", DiscordColors.Danger),
            _ => (message.GetType().Name, DiscordColors.Standard),
        };

        var fields = new List<DiscordField>();
        if (message.Episodes.FirstOrDefault()?.Overview is { Length: > 0 } overview)
        {
            fields.Add(new DiscordField("Overview", overview.Length <= 300 ? overview : $"{overview.AsSpan(0, 300)}..."));
        }

        switch (message)
        {
            case GrabMessage grab:
                fields.Add(new DiscordField("Quality", grab.Remote.Quality.Quality.Name, true));
                fields.Add(new DiscordField("Size", BytesToString(grab.Remote.Release.Size), true));
                fields.Add(new DiscordField("Release", $"```{grab.Remote.Release.Title}```"));
                fields.Add(new DiscordField("Indexer", grab.Remote.Release.Indexer, true));
                break;
            case DownloadMessage download:
                fields.Add(new DiscordField("Quality", download.File.Quality.Quality.Name, true));
                fields.Add(new DiscordField("Size", BytesToString(download.File.Size), true));
                fields.Add(new DiscordField("Release", $"```{download.File.SceneName ?? Path.GetFileNameWithoutExtension(download.File.Path)}```"));
                break;
            case EpisodeFileDeleteMessage delete:
                fields.Add(new DiscordField("Reason", delete.Reason, true));
                fields.Add(new DiscordField("Path", delete.File.Path));
                break;
        }

        if (series.Genres.Count > 0)
        {
            fields.Add(new DiscordField("Genres", string.Join(", ", series.Genres.Take(5))));
        }

        var title = NotificationFormat.Title(series, message.Episodes).Replace("`", "\\`", StringComparison.Ordinal);
        return Payload(definition, new DiscordEmbed
        {
            Author = Author(definition),
            Description = description,
            Title = title.Length > 256 ? $"{title.AsSpan(0, 253).TrimEnd('\\')}..." : title,
            Color = (int)color,
            Thumbnail = series.Images.Poster is { Length: > 0 } poster ? new DiscordImage(poster) : null,
            Fields = fields.Where(f => f.Value.Length > 0).ToList(),
            Timestamp = time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        });
    }

    private DiscordAuthor Author(NotificationDefinition definition) =>
        new(definition.Settings.Text("author") ?? hostConfig.Current.InstanceName, null);

    private static DiscordPayload Payload(NotificationDefinition definition, DiscordEmbed embed) =>
        new(null, definition.Settings.Text("username"), definition.Settings.Text("avatar"), [embed]);

    private async Task PostAsync(NotificationDefinition definition, DiscordPayload payload, CancellationToken cancellationToken)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await clients.CreateClient(NotificationService.ClientName).PostAsync(definition.Settings.Text("webHookUrl"), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new NotificationException($"Discord returned HTTP {(int)response.StatusCode}");
        }
    }

    private static string BytesToString(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        if (bytes <= 0)
        {
            return "0 B";
        }

        var place = Math.Min((int)Math.Floor(Math.Log(bytes, 1024)), units.Length - 1);
        return $"{Math.Round(bytes / Math.Pow(1024, place), 1).ToString(CultureInfo.InvariantCulture)} {units[place]}";
    }
}
