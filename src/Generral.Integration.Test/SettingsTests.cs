using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Generral.Integration.Test;

public class SettingsTests
{
    [Test]
    public async Task General_settings_round_trip_to_config_yaml()
    {
        await using var host = await GenerralTestHost.StartAsync();
        var client = await host.CreateSignedInClientAsync();

        var settings = (await client.GetFromJsonAsync<JsonObject>("/api/v1/settings/general"))!;

        Assert.Multiple(() =>
        {
            Assert.That(settings["port"]!.GetValue<int>(), Is.EqualTo(8990));
            Assert.That(settings["username"]!.GetValue<string>(), Is.EqualTo(GenerralTestHost.Username));
            Assert.That(settings["password"]!.GetValue<string>(), Is.Empty);
        });

        settings["instanceName"] = "Sport";
        settings["logLevel"] = "debug";

        var response = await client.PutAsJsonAsync("/api/v1/settings/general", settings);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(File.ReadAllText(Path.Combine(host.ConfigFolder, "config.yaml")), Does.Contain("instanceName: Sport").And.Contain("logLevel: debug"));
        });
    }

    [Test]
    public async Task Invalid_general_settings_return_sonarr_style_validation_errors()
    {
        await using var host = await GenerralTestHost.StartAsync();
        var client = await host.CreateSignedInClientAsync();
        var settings = (await client.GetFromJsonAsync<JsonObject>("/api/v1/settings/general"))!;

        settings["port"] = 0;
        settings["password"] = "a";
        settings["passwordConfirmation"] = "b";

        var response = await client.PutAsJsonAsync("/api/v1/settings/general", settings);
        var failures = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(failures.EnumerateArray().Select(f => f.GetProperty("propertyName").GetString()), Is.EquivalentTo(new[] { "port", "passwordConfirmation" }));
        });
    }

    [Test]
    public async Task Changing_the_password_signs_out_existing_sessions()
    {
        await using var host = await GenerralTestHost.StartAsync();
        var client = await host.CreateSignedInClientAsync();
        var settings = (await client.GetFromJsonAsync<JsonObject>("/api/v1/settings/general"))!;

        settings["password"] = "a new password";
        settings["passwordConfirmation"] = "a new password";
        (await client.PutAsJsonAsync("/api/v1/settings/general", settings)).EnsureSuccessStatusCode();

        Assert.That((await client.GetAsync("/api/v1/system/status")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Env_overridden_fields_are_reported_and_not_changed()
    {
        await using var host = await GenerralTestHost.StartAsync(new() { ["GENERRAL:PORT"] = "9999" });
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", host.ApiKey);

        var settings = (await client.GetFromJsonAsync<JsonObject>("/api/v1/settings/general"))!;
        settings["port"] = 1234;
        (await client.PutAsJsonAsync("/api/v1/settings/general", settings)).EnsureSuccessStatusCode();
        var after = (await client.GetFromJsonAsync<JsonObject>("/api/v1/settings/general"))!;

        Assert.Multiple(() =>
        {
            Assert.That(settings["overriddenByEnvironment"]!.AsArray().Select(n => n!.GetValue<string>()), Does.Contain("port"));
            Assert.That(after["port"]!.GetValue<int>(), Is.EqualTo(9999));
        });
    }

    [Test]
    public async Task Resetting_the_api_key_invalidates_the_old_one()
    {
        await using var host = await GenerralTestHost.StartAsync();
        var client = host.CreateClient();
        var oldKey = host.ApiKey;
        client.DefaultRequestHeaders.Add("X-Api-Key", oldKey);

        (await client.PostAsync("/api/v1/settings/general/resetapikey", null)).EnsureSuccessStatusCode();

        Assert.Multiple(async () =>
        {
            Assert.That(host.ApiKey, Is.Not.EqualTo(oldKey));
            Assert.That((await client.GetAsync("/api/v1/system/status")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        });
    }

    [Test]
    public async Task Ui_settings_round_trip()
    {
        await using var host = await GenerralTestHost.StartAsync();
        var client = await host.CreateSignedInClientAsync();

        var ui = (await client.GetFromJsonAsync<JsonObject>("/api/v1/settings/ui"))!;
        Assert.That(ui["shortDateFormat"]!.GetValue<string>(), Is.EqualTo("MMM D YYYY"));

        ui["theme"] = "dark";
        (await client.PutAsJsonAsync("/api/v1/settings/ui", ui)).EnsureSuccessStatusCode();

        var after = (await client.GetFromJsonAsync<JsonObject>("/api/v1/settings/ui"))!;
        Assert.That(after["theme"]!.GetValue<string>(), Is.EqualTo("dark"));

        ui["theme"] = "purple";
        Assert.That((await client.PutAsJsonAsync("/api/v1/settings/ui", ui)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
