using Generral.Core;
using Generral.Core.Authentication;
using Generral.Core.Configuration;
using Generral.Core.Datastore;
using Generral.Core.Instrumentation;
using Generral.Core.Lifecycle;
using Generral.Http;
using Serilog;

namespace Generral.Host;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

        try
        {
            // System → Restart stops the web host; loop to start a fresh one with the new host settings.
            while (true)
            {
                var app = Build(args);
                var restart = app.Services.GetRequiredService<IRestartService>();

                await app.RunAsync();

                if (!restart.IsRestartRequested)
                {
                    return 0;
                }

                Log.Information("Restarting Generral");
            }
        }
        catch (InvalidConfigurationException e)
        {
            Log.Fatal("{Message}", e.Message);

            return 2;
        }
        catch (Exception e) when (e is not HostAbortedException)
        {
            Log.Fatal(e, "Generral failed to start");

            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        configure?.Invoke(builder);

        var folders = new AppFolders(builder.Configuration[AppFolders.ConfigDirKey], builder.Configuration[AppFolders.UiDirKey]);
        var config = new HostConfigProvider(folders, HostConfigProvider.ReadOverrides(builder.Configuration));
        var settings = config.Current;

        // Each (re)started host gets its own logger; the static one stays the bootstrap console logger.
        builder.Host.UseSerilog((_, logger) => Logging.Configure(logger, folders, settings), preserveStaticLogger: true);

        builder.WebHost.UseUrls(ListenUrl(settings));
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);

        builder.Services.AddGenerralCore(folders, config);
        builder.Services.AddGenerralHttp(folders, config);

        var app = builder.Build();

        app.Logger.LogInformation("Starting Generral {Version} (config: {ConfigFolder})", BuildInfo.Version, folders.ConfigFolder);

        if (config.GeneratedApiKey)
        {
            app.Logger.LogInformation("Generated a new API key in {ConfigFile}", folders.ConfigFile);
        }

        DatabaseInitializer.Migrate(app.Services);

        using (var scope = app.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<InitialUserService>().EnsureInitialUser();
        }

        app.UseGenerralHttp();

        return app;
    }

    private static string ListenUrl(HostSettings settings)
    {
        var host = settings.BindAddress is "*" or "" ? "*" : settings.BindAddress;

        if (System.Net.IPAddress.TryParse(host, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            host = $"[{host}]";
        }

        return $"http://{host}:{settings.Port}";
    }
}
