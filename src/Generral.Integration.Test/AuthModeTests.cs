using System.Net;

namespace Generral.Integration.Test;

public class AuthModeTests
{
    [Test]
    public async Task External_auth_trusts_the_reverse_proxy_for_the_ui()
    {
        await using var host = await GenerralTestHost.StartAsync(new() { ["GENERRAL:AUTHMETHOD"] = "external" });
        var client = host.CreateClient();

        Assert.Multiple(async () =>
        {
            Assert.That((await client.GetAsync("/series")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That((await client.GetAsync("/api/v1/system/status")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    [Test]
    public async Task Disabled_for_local_addresses_lets_lan_clients_in()
    {
        await using var host = await GenerralTestHost.StartAsync(
            new() { ["GENERRAL:AUTHREQUIRED"] = "disabledForLocalAddresses" },
            IPAddress.Parse("192.168.1.20"));
        var client = host.CreateClient();

        Assert.That((await client.GetAsync("/series")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Disabled_for_local_addresses_still_requires_auth_from_the_internet()
    {
        await using var host = await GenerralTestHost.StartAsync(
            new() { ["GENERRAL:AUTHREQUIRED"] = "disabledForLocalAddresses" },
            IPAddress.Parse("81.2.3.4"));
        var client = host.CreateClient();

        Assert.That((await client.GetAsync("/series")).StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
    }

    [Test]
    public async Task Disabled_for_local_addresses_is_ignored_behind_a_proxy()
    {
        await using var host = await GenerralTestHost.StartAsync(
            new() { ["GENERRAL:AUTHREQUIRED"] = "disabledForLocalAddresses" },
            IPAddress.Parse("172.18.0.5"));
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "81.2.3.4");

        Assert.That((await client.GetAsync("/series")).StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
    }

    [Test]
    public async Task Url_base_prefixes_every_route_except_ping()
    {
        await using var host = await GenerralTestHost.StartAsync(new() { ["GENERRAL:URLBASE"] = "/generral" });
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", host.ApiKey);

        var signedOut = host.CreateClient();
        var redirect = await signedOut.GetAsync("/generral/series");

        Assert.Multiple(async () =>
        {
            Assert.That((await client.GetAsync("/generral/api/v1/system/status")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That((await client.GetAsync("/api/v1/system/status")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That((await client.GetAsync("/ping")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(redirect.Headers.Location?.ToString(), Is.EqualTo("/generral/login?returnUrl=%2Fgenerral%2Fseries"));
            Assert.That(await client.GetStringAsync("/generral/series"), Does.Contain("href=\"/generral/assets/app.css\"").And.Contain("data-base=\"/generral\""));
        });
    }
}
