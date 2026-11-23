using System.Net;
using System.Text.RegularExpressions;
using Generral.Core.Configuration;
using Generral.Core.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Generral.Integration.Test;

/// <summary>Runs the real host (Program.Build) in memory against a temporary config folder.</summary>
public sealed partial class GenerralTestHost : IAsyncDisposable
{
    public const string Username = "admin";
    public const string Password = "correct horse battery staple";

    private readonly WebApplication _app;

    private GenerralTestHost(WebApplication app, string configFolder, string uiFolder)
    {
        _app = app;
        ConfigFolder = configFolder;
        UiFolder = uiFolder;
    }

    public string ConfigFolder { get; }

    public string UiFolder { get; }

    public IServiceProvider Services => _app.Services;

    public string ApiKey => _app.Services.GetRequiredService<IHostConfigProvider>().Current.ApiKey;

    public static async Task<GenerralTestHost> StartAsync(Dictionary<string, string?>? settings = null, IPAddress? remoteAddress = null, Action<IServiceCollection>? services = null)
    {
        var configFolder = Path.Combine(Path.GetTempPath(), "generral-it-" + Guid.NewGuid().ToString("N"));
        var uiFolder = Path.Combine(configFolder, "UI");
        Directory.CreateDirectory(uiFolder);
        File.WriteAllText(Path.Combine(uiFolder, "index.html"), "<html><head><link rel=\"icon\" href=\"/favicon.svg\"><link href=\"/assets/app.css\"></head><body data-base=\"__URL_BASE__\">index</body></html>");
        File.WriteAllText(Path.Combine(uiFolder, "login.html"), "<html><body>login __INSTANCE_NAME__</body></html>");
        Directory.CreateDirectory(Path.Combine(uiFolder, "assets"));
        File.WriteAllText(Path.Combine(uiFolder, "assets", "app.css"), "body{}");

        var configuration = new Dictionary<string, string?>
        {
            [AppFolders.ConfigDirKey] = configFolder,
            [AppFolders.UiDirKey] = uiFolder,
            ["GENERRAL:AUTH:USERNAME"] = Username,
            ["GENERRAL:AUTH:PASSWORD"] = Password,
            [ScheduledTaskService.EnabledKey] = "false",
        };

        foreach (var (key, value) in settings ?? [])
        {
            configuration[key] = value;
        }

        var app = Host.Program.Build([], builder =>
        {
            builder.Configuration.AddInMemoryCollection(configuration);
            builder.WebHost.UseTestServer();
            services?.Invoke(builder.Services);

            if (remoteAddress != null)
            {
                builder.Services.AddSingleton<IStartupFilter>(new RemoteAddressStartupFilter(remoteAddress));
            }
        });

        await app.StartAsync();

        return new GenerralTestHost(app, configFolder, uiFolder);
    }

    public HttpClient CreateClient(bool allowAutoRedirect = false)
    {
        var server = _app.GetTestServer();
        var handler = new CookieContainerHandler(server.CreateHandler());

        return new HttpClient(new RedirectHandler(handler, allowAutoRedirect)) { BaseAddress = server.BaseAddress };
    }

    /// <summary>Signs in through the login form and returns a client holding the auth cookie.</summary>
    public async Task<HttpClient> CreateSignedInClientAsync()
    {
        var client = CreateClient();
        var response = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = Username,
            ["password"] = Password,
        }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.ToString(), Does.Not.Contain("loginFailed"));

        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();

        try
        {
            Directory.Delete(ConfigFolder, recursive: true);
        }
        catch (IOException)
        {
            // SQLite may still hold the file briefly on some platforms.
        }
    }

    private sealed class CookieContainerHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private readonly CookieContainer _cookies = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var header = _cookies.GetCookieHeader(uri);

            if (header.Length > 0)
            {
                request.Headers.Add("Cookie", header);
            }

            var response = await base.SendAsync(request, cancellationToken);

            if (response.Headers.TryGetValues("Set-Cookie", out var values))
            {
                foreach (var value in values)
                {
                    // CookieContainer rejects SameSite; it doesn't matter for an in-memory server.
                    _cookies.SetCookies(uri, SameSiteRegex().Replace(value, string.Empty));
                }
            }

            return response;
        }
    }

    private sealed class RedirectHandler(HttpMessageHandler inner, bool follow) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);

            for (var i = 0; follow && i < 5 && response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found && response.Headers.Location != null; i++)
            {
                var next = new Uri(request.RequestUri!, response.Headers.Location);
                request = new HttpRequestMessage(HttpMethod.Get, next);
                response = await base.SendAsync(request, cancellationToken);
            }

            return response;
        }
    }

    /// <summary>TestServer has no real peer; pretend requests come from a given address (runs before auth).</summary>
    private sealed class RemoteAddressStartupFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use((context, nextMiddleware) =>
                {
                    context.Connection.RemoteIpAddress = address;

                    return nextMiddleware(context);
                });

                next(app);
            };
        }
    }

    [GeneratedRegex(@";\s*samesite=\w+", RegexOptions.IgnoreCase)]
    private static partial Regex SameSiteRegex();
}
