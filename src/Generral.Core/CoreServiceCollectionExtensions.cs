using Generral.Core.Authentication;
using Generral.Core.Configuration;
using Generral.Core.Datastore;
using Generral.Core.Lifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

        return services;
    }
}
