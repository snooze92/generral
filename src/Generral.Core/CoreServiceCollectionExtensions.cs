using Generral.Core.Authentication;
using Generral.Core.Backup;
using Generral.Core.Blocklisting;
using Generral.Core.Configuration;
using Generral.Core.Datastore;
using Generral.Core.DecisionEngine;
using Generral.Core.Download;
using Generral.Core.Download.Clients.QBittorrent;
using Generral.Core.Download.TrackedDownloads;
using Generral.Core.Extras.Metadata;
using Generral.Core.Extras.Metadata.Consumers.PlexNfo;
using Generral.Core.HealthCheck;
using Generral.Core.History;
using Generral.Core.Indexers;
using Generral.Core.Indexers.Torznab;
using Generral.Core.IndexerSearch;
using Generral.Core.Lifecycle;
using Generral.Core.Mappings;
using Generral.Core.MediaFiles;
using Generral.Core.Messaging;
using Generral.Core.Notifications;
using Generral.Core.Notifications.Discord;
using Generral.Core.Notifications.Plex;
using Generral.Core.Notifications.Webhook;
using Generral.Core.Organizer;
using Generral.Core.Presets;
using Generral.Core.Profiles;
using Generral.Core.Qualities;
using Generral.Core.RemotePathMappings;
using Generral.Core.RootFolders;
using Generral.Core.Sources;
using Generral.Core.Sources.Extraction;
using Generral.Core.Sources.Http;
using Generral.Core.Sources.Types;
using Generral.Core.Tags;
using Generral.Core.Templating;
using Generral.Core.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Generral.Core;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddGenerralCore(this IServiceCollection services, IAppFolders folders, IHostConfigProvider config)
    {
        services.AddSingleton(folders);
        services.AddSingleton(config);
        services.AddSingleton<IRuntimeInfo, RuntimeInfo>();

        var connectionString = DatabaseConnection.GetConnectionString(config.Current, folders);
        services.AddDbContext<GenerralDbContext>(options => options.UseSqlite(connectionString));

        services.AddScoped<IUserService, UserService>();
        services.AddScoped<InitialUserService>();
        services.AddScoped<IUiSettingsService, UiSettingsService>();
        services.AddScoped<TagService>();
        services.AddScoped<QualityDefinitionService>();
        services.AddScoped<QualityProfileService>();
        services.AddScoped<ReleaseProfileService>();
        services.AddScoped<CalendarService>();
        services.AddSingleton<PresetStore>();

        services.AddSourceTypes();
        services.AddSingleton<SourceRefreshService>();

        services.AddHttpClient(TorznabClient.ClientName, TorznabClient.ConfigureClient)
            .ConfigurePrimaryHttpMessageHandler(SourceFetcherFactory.CreateHandler);
        services.AddSingleton<TorznabClient>();
        services.AddScoped<IndexerConfigService>();
        services.AddScoped<NamingConfigService>();
        services.AddScoped<MediaManagementConfigService>();
        services.AddScoped<RootFolderService>();
        services.AddScoped<RemotePathMappingService>();
        services.AddSingleton<DownloadDecisionMaker>();
        services.AddHttpClient(QBittorrentClient.ClientName, QBittorrentClient.ConfigureClient)
            .ConfigurePrimaryHttpMessageHandler(QBittorrentClient.CreateHandler);
        services.AddSingleton<IDownloadClient, QBittorrentClient>();
        services.AddSingleton<DownloadClientProvider>();
        services.AddHttpClient(TorrentReleaseFetcher.ClientName, TorznabClient.ConfigureClient)
            .ConfigurePrimaryHttpMessageHandler(TorrentReleaseFetcher.CreateHandler);
        services.AddSingleton<TorrentReleaseFetcher>();
        services.AddScoped<DownloadClientConfigService>();
        services.AddScoped<HistoryService>();
        services.AddScoped<BlocklistService>();
        services.AddSingleton<TrackedDownloadService>();
        services.AddScoped<CompletedDownloadService>();
        services.AddScoped<EpisodeImportService>();
        services.AddSingleton<DiskTransferService>();
        services.AddSingleton<RecycleBinProvider>();
        services.AddSingleton<ICommandHandler, CleanUpRecycleBinHandler>();
        services.AddSingleton<ICommandHandler, ManualImportHandler>();
        services.TryAddSingleton<IDownloadService, DownloadService>();
        services.AddSingleton<IndexerStatusService>();
        services.AddSingleton<ReleaseSearchService>();
        services.AddSingleton<IMetadataConsumer, PlexNfoMetadata>();
        services.AddScoped<MetadataService>();
        services.AddHttpClient(NotificationService.ClientName, NotificationService.ConfigureClient)
            .ConfigurePrimaryHttpMessageHandler(SourceFetcherFactory.CreateHandler);
        services.AddSingleton<INotification, PlexServerNotification>();
        services.AddSingleton<INotification, WebhookNotification>();
        services.AddSingleton<INotification, DiscordNotification>();
        services.AddSingleton<NotificationProvider>();
        services.AddScoped<NotificationService>();

        services.AddSingleton<ProviderStatusTracker>();
        services.AddSingleton<HealthCheckService>();
        services.AddScoped<IProvideHealthCheck, IndexerRssCheck>();
        services.AddScoped<IProvideHealthCheck, IndexerSearchCheck>();
        services.AddScoped<IProvideHealthCheck, IndexerStatusCheck>();
        services.AddScoped<IProvideHealthCheck, DownloadClientCheck>();
        services.AddScoped<IProvideHealthCheck, DownloadClientStatusCheck>();
        services.AddScoped<IProvideHealthCheck, RootFolderCheck>();
        services.AddScoped<IProvideHealthCheck, RemotePathMappingCheck>();
        services.AddScoped<IProvideHealthCheck, SourceRefreshCheck>();
        services.AddScoped<IProvideHealthCheck, MappingCheck>();
        services.AddSingleton<ICommandHandler, CheckHealthHandler>();
        services.AddSingleton<BackupService>();
        services.AddSingleton<ICommandHandler, BackupHandler>();
        services.AddSingleton<CommandQueue>();
        services.AddSingleton<ICommandHandler, RefreshSourcesHandler>();
        services.AddSingleton<ICommandHandler, RefreshSourceHandler>();
        services.AddSingleton<MappingEngine>();
        services.AddSingleton<ApplyMappingsService>();
        services.AddSingleton<ICommandHandler, ApplyMappingsHandler>();
        services.AddSingleton<ICommandHandler, RenumberHandler>();
        services.AddSingleton<ICommandHandler, EpisodeSearchHandler>();
        services.AddSingleton<ICommandHandler, SeasonSearchHandler>();
        services.AddSingleton<ICommandHandler, SeriesSearchHandler>();
        services.AddSingleton<ICommandHandler, MissingEpisodeSearchHandler>();
        services.AddSingleton<ICommandHandler, RssSyncHandler>();
        services.AddSingleton<ICommandHandler, OnAirSearchHandler>();
        services.AddSingleton<ICommandHandler, CutoffUnmetEpisodeSearchHandler>();
        services.AddSingleton<ICommandHandler, RefreshMonitoredDownloadsHandler>();
        services.AddHostedService<CommandWorker>();
        services.AddSingleton<ScheduledTaskService>();
        services.AddHostedService(sp => sp.GetRequiredService<ScheduledTaskService>());

        return services;
    }

    public static IServiceCollection AddSourceTypes(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<TemplateRenderer>(_ => new TemplateRenderer());
        services.TryAddSingleton(sp => new ExtractionEngine(sp.GetRequiredService<TemplateRenderer>(), sp.GetRequiredService<TimeProvider>()));

        services.AddHttpClient(SourceFetcherFactory.ClientName, SourceFetcherFactory.ConfigureClient)
            .ConfigurePrimaryHttpMessageHandler(SourceFetcherFactory.CreateHandler);
        services.AddSingleton<SourceResponseCache>();
        services.AddSingleton<SourceFetcherFactory>();

        services.AddSingleton<ISourceType, TheSportsDbSourceType>();
        services.AddSingleton<ISourceType, HttpJsonSourceType>();
        services.AddSingleton<ISourceType, HttpHtmlSourceType>();
        services.AddSingleton<ISourceType, IcalSourceType>();
        services.AddSingleton<ISourceType, StaticSourceType>();
        services.AddSingleton<SourceTypeRegistry>();

        return services;
    }
}
