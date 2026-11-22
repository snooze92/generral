// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportService.cs (Execute) and ManualImportCommand.cs
using System.Text.Json;
using System.Text.Json.Nodes;
using Generral.Core.Configuration;
using Generral.Core.Messaging;
using Generral.Core.Qualities;
using Microsoft.Extensions.DependencyInjection;

namespace Generral.Core.MediaFiles;

public enum ImportMode
{
    Auto,
    Move,
    Copy,
}

public sealed record ManualImportFile
{
    public string Path { get; init; } = "";

    public string? FolderName { get; init; }

    public int SeriesId { get; init; }

    public List<int> EpisodeIds { get; init; } = [];

    public QualityModel? Quality { get; init; }

    public string? ReleaseGroup { get; init; }

    public string? DownloadId { get; init; }
}

/// <summary><c>ManualImport { files: [{ path, seriesId, episodeIds, quality, releaseGroup, downloadId }], importMode: auto|move|copy }</c>.</summary>
public sealed class ManualImportHandler(IServiceScopeFactory scopes) : ICommandHandler
{
    public const string CommandName = "ManualImport";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public string Name => CommandName;

    public string? Validate(JsonObject body) =>
        Files(body) is { Count: > 0 } files && files.All(f => f.SeriesId > 0 && f.EpisodeIds.Count > 0 && System.IO.Path.IsPathFullyQualified(f.Path))
            ? null
            : "files must list absolute paths with a seriesId and episodeIds";

    public async Task<string?> ExecuteAsync(JsonObject body, CommandContext context, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var importer = scope.ServiceProvider.GetRequiredService<EpisodeImportService>();
        var config = await scope.ServiceProvider.GetRequiredService<MediaManagementConfigService>().GetAsync(cancellationToken);
        var mode = Enum.TryParse<ImportMode>(body["importMode"]?.ToString(), ignoreCase: true, out var m) ? m : ImportMode.Auto;
        var transfer = mode switch
        {
            ImportMode.Move => TransferMode.Move,
            _ => config.CopyUsingHardlinks ? TransferMode.HardLinkOrCopy : TransferMode.Copy,
        };

        var errors = new List<string>();
        var imported = 0;
        foreach (var file in Files(body)!)
        {
            if (!File.Exists(file.Path))
            {
                errors.Add($"{System.IO.Path.GetFileName(file.Path)}: file not found");
                continue;
            }

            var result = await importer.ImportAsync(new ImportItem
            {
                Path = file.Path,
                SeriesId = file.SeriesId,
                EpisodeIds = file.EpisodeIds,
                Quality = file.Quality ?? QualityParser.ParseQuality(System.IO.Path.GetFileName(file.Path)),
                ReleaseGroup = file.ReleaseGroup,
                DownloadId = file.DownloadId,
                SceneName = file.FolderName,
                SourceFolder = System.IO.Path.GetDirectoryName(file.Path),
            }, transfer, cancellationToken);
            if (result.Imported)
            {
                imported++;
            }
            else
            {
                errors.AddRange(result.Errors.Select(e => $"{System.IO.Path.GetFileName(file.Path)}: {e}"));
            }
        }

        if (imported == 0 && errors.Count > 0)
        {
            throw new CommandFailedException(string.Join("; ", errors));
        }

        return errors.Count == 0 ? $"{imported} file(s) imported" : $"{imported} file(s) imported; {string.Join("; ", errors)}";
    }

    private static List<ManualImportFile>? Files(JsonObject body)
    {
        try
        {
            return body["files"]?.Deserialize<List<ManualImportFile>>(JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
