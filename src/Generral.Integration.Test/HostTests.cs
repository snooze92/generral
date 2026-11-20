using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Generral.Integration.Test;

public class HostTests
{
    [Test]
    public async Task Ping_is_anonymous_and_reports_ok()
    {
        await using var host = await GenerralTestHost.StartAsync();
        var client = host.CreateClient();

        var response = await client.GetAsync("/ping");

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("""{"status":"OK"}"""));
            Assert.That(response.Headers.Contains("X-Application-Version"), Is.True);
        });
    }

    [Test]
    public async Task Api_requires_authentication()
    {
        await using var host = await GenerralTestHost.StartAsync();
        var client = host.CreateClient();

        var response = await client.GetAsync("/api/v1/system/status");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase("header")]
    [TestCase("query")]
    [TestCase("bearer")]
    public async Task Api_accepts_the_api_key(string where)
    {
        await using var host = await GenerralTestHost.StartAsync();
        var client = host.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, where == "query" ? $"/api/v1/system/status?apikey={host.ApiKey}" : "/api/v1/system/status");

        if (where == "header")
        {
            request.Headers.Add("X-Api-Key", host.ApiKey);
        }
        else if (where == "bearer")
        {
            request.Headers.Authorization = new("Bearer", host.ApiKey);
        }

        var response = await client.SendAsync(request);
        var status = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(status.GetProperty("appName").GetString(), Is.EqualTo("Generral"));
            Assert.That(status.GetProperty("authentication").GetString(), Is.EqualTo("forms"));
            Assert.That(status.GetProperty("databaseType").GetString(), Is.EqualTo("sqlite"));
            Assert.That(status.GetProperty("migrationVersion").GetString(), Does.EndWith("InitialCreate"));
        });
    }

    [Test]
    public async Task Wrong_api_key_is_rejected()
    {
        await using var host = await GenerralTestHost.StartAsync();
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "not-the-key-0000000000000000000");

        var response = await client.GetAsync("/api/v1/system/status");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Ui_routes_redirect_to_login_when_signed_out()
    {
        await using var host = await GenerralTestHost.StartAsync();
        var client = host.CreateClient();

        var response = await client.GetAsync("/series");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/login?returnUrl=%2Fseries"));
        });
    }

    [Test]
    public async Task Login_page_and_assets_are_served()
    {
        await using var host = await GenerralTestHost.StartAsync();
        var client = host.CreateClient();

        var login = await client.GetStringAsync("/login");
        var css = await client.GetAsync("/assets/app.css");

        Assert.Multiple(() =>
        {
            Assert.That(login, Does.Contain("login Generral"));
            Assert.That(css.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    [Test]
    public async Task Signing_in_gives_access_to_ui_and_api_until_sign_out()
    {
        await using var host = await GenerralTestHost.StartAsync();
        var client = await host.CreateSignedInClientAsync();

        var index = await client.GetAsync("/series");
        var init = await client.GetFromJsonAsync<JsonElement>("/initialize.json");
        var status = await client.GetAsync("/api/v1/system/status");

        Assert.Multiple(async () =>
        {
            Assert.That(index.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await index.Content.ReadAsStringAsync(), Does.Contain("index"));
            Assert.That(init.GetProperty("apiKey").GetString(), Is.EqualTo(host.ApiKey));
            Assert.That(init.GetProperty("apiRoot").GetString(), Is.EqualTo("/api/v1"));
            Assert.That(status.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });

        await client.GetAsync("/logout");

        Assert.That((await client.GetAsync("/api/v1/system/status")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Wrong_password_redirects_back_with_an_error_and_locks_out_after_repeated_failures()
    {
        await using var host = await GenerralTestHost.StartAsync();
        var client = host.CreateClient();
        var form = new Dictionary<string, string> { ["username"] = GenerralTestHost.Username, ["password"] = "nope" };

        HttpResponseMessage response = null!;

        for (var i = 0; i < 5; i++)
        {
            response = await client.PostAsync("/login", new FormUrlEncodedContent(form));
        }

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(response.Headers.Location?.ToString(), Does.Contain("loginFailed=true"));
            Assert.That(response.Headers.Location?.ToString(), Does.Contain("lockoutUntil="));
        });

        // Even the right password is refused while locked out.
        form["password"] = GenerralTestHost.Password;
        response = await client.PostAsync("/login", new FormUrlEncodedContent(form));

        Assert.That(response.Headers.Location?.ToString(), Does.Contain("loginFailed=true"));
    }

    [TestCase("https://evil.example/", "/")]
    [TestCase("//evil.example/", "/")]
    [TestCase("/calendar", "/calendar")]
    public async Task Login_only_redirects_to_local_urls(string returnUrl, string expected)
    {
        await using var host = await GenerralTestHost.StartAsync();
        var client = host.CreateClient();

        var response = await client.PostAsync($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = GenerralTestHost.Username,
            ["password"] = GenerralTestHost.Password,
        }));

        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo(expected));
    }

    [Test]
    public async Task Unknown_api_routes_are_404_not_the_spa()
    {
        await using var host = await GenerralTestHost.StartAsync();
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", host.ApiKey);

        Assert.That((await client.GetAsync("/api/v1/nope")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task OpenApi_document_is_available_with_the_api_key()
    {
        await using var host = await GenerralTestHost.StartAsync();
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", host.ApiKey);

        var document = await client.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");

        Assert.That(document.GetProperty("paths").TryGetProperty("/api/v1/system/status", out _), Is.True);
    }
}
