using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Generral.Integration.Test;

public class SystemApiTests
{
    private GenerralTestHost _host = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _host = await GenerralTestHost.StartAsync();
        _client = _host.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Api-Key", _host.ApiKey);
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _host.DisposeAsync();
    }

    [Test]
    public async Task Backup_create_list_download_delete_and_restore()
    {
        await RunCommandAsync("Backup");

        var backups = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/system/backup"))!;
        Assert.That(backups, Has.Count.EqualTo(1));
        var backup = backups[0]!;
        var id = (int)backup["id"]!;

        var download = await _client.GetAsync($"/api/v1/system/backup/{id}/download");
        using var zip = new ZipArchive(await download.Content.ReadAsStreamAsync());
        var entries = zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ToList();

        var restore = await _client.PostAsync($"/api/v1/system/backup/restore/{id}", null);
        var staged = File.Exists(Path.Combine(_host.ConfigFolder, "generral.restore"));
        var delete = await _client.DeleteAsync($"/api/v1/system/backup/{id}");
        var after = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/system/backup"))!;
        var missing = await _client.DeleteAsync($"/api/v1/system/backup/{id}");

        Assert.Multiple(() =>
        {
            Assert.That((string)backup["type"]!, Is.EqualTo("manual"));
            Assert.That((string)backup["name"]!, Does.Match(@"^generral_backup_v.+\.zip$"));
            Assert.That(entries, Is.EqualTo(new[] { "INFO", "config.yaml", "generral.db" }));
            Assert.That(restore.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(staged, Is.True);
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(after, Is.Empty);
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task Restore_upload_rejects_a_non_backup()
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent("not a zip"u8.ToArray()), "file", "backup.zip" } };

        var response = await _client.PostAsync("/api/v1/system/backup/restore/upload", form);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Tasks_health_and_logs()
    {
        await RunCommandAsync("CheckHealth");

        var tasks = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/system/task"))!;
        var health = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/health"))!;
        var shimHealth = await _client.GetAsync("/api/v3/health");
        var files = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/log/file"))!;
        var escape = await _client.GetAsync("/api/v1/log/file/..%2Fconfig.yaml");
        var checkHealth = tasks.Single(t => (string)t!["taskName"]! == "CheckHealth")!;

        Assert.Multiple(() =>
        {
            Assert.That(tasks.Select(t => (string)t!["taskName"]!), Is.SupersetOf(new[] { "RefreshSources", "RssSync", "Backup", "CheckHealth" }));
            Assert.That((int)checkHealth["interval"]!, Is.EqualTo(360));
            Assert.That(checkHealth["lastExecution"], Is.Not.Null);
            Assert.That(health.Select(h => (string)h!["source"]!), Is.SupersetOf(new[] { "IndexerRssCheck", "DownloadClientCheck" }), "nothing is configured");
            Assert.That(shimHealth.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(files.Select(f => (string)f!["filename"]!), Does.Contain("generral.txt"));
            Assert.That(escape.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });

        var contents = await _client.GetStringAsync((string)files[0]!["contentsUrl"]!);
        Assert.That(contents, Does.Contain("Starting Generral"));
    }

    private async Task RunCommandAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/command", new JsonObject { ["name"] = name });
        var id = (int)(await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var command = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/command/{id}"))!;
            if ((string)command["status"]! is "completed" or "failed")
            {
                Assert.That((string)command["status"]!, Is.EqualTo("completed"), (string?)command["message"]);
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"{name} did not finish");
    }
}
