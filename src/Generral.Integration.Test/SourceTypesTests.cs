using Generral.Core.Lifecycle;
using Generral.Core.Sources.Http;
using Generral.Core.Sources.Types;
using Microsoft.Extensions.DependencyInjection;

namespace Generral.Integration.Test;

public class SourceTypesTests
{
    [Test]
    public async Task Source_types_and_http_client_are_registered()
    {
        await using var host = await GenerralTestHost.StartAsync();

        var types = host.Services.GetRequiredService<SourceTypeRegistry>();
        var client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(SourceFetcherFactory.ClientName);

        Assert.Multiple(() =>
        {
            Assert.That(types.All.Select(t => t.Id), Is.EquivalentTo(new[] { "thesportsdb", "http-json", "ical", "static" }));
            Assert.That(client.Timeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(client.DefaultRequestHeaders.UserAgent.ToString(), Is.EqualTo(BuildInfo.UserAgent));
        });
    }

    [Test]
    public async Task Source_client_does_not_connect_to_link_local_addresses()
    {
        await using var host = await GenerralTestHost.StartAsync();
        var client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(SourceFetcherFactory.ClientName);

        var e = Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("http://169.254.169.254/latest/meta-data/"));

        Assert.That(e!.ToString(), Does.Contain("link-local"));
    }
}
