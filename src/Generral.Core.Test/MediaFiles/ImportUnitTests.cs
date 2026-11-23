using Generral.Core.Configuration;
using Generral.Core.MediaFiles;
using Generral.Core.RemotePathMappings;
using Generral.Core.Test.Sources;
using Microsoft.Extensions.Logging.Abstractions;

namespace Generral.Core.Test.MediaFiles;

public class ImportUnitTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp() => _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "generral-test-" + Guid.NewGuid().ToString("N"))).FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    [Test]
    public void Remote_path_mapping_longest_match_and_host()
    {
        RemotePathMapping[] mappings =
        [
            new() { Host = "qbit", RemotePath = "/downloads/", LocalPath = "/mnt/dl/" },
            new() { Host = "qbit", RemotePath = "/downloads/tv/", LocalPath = "/mnt/tv/" },
            new() { Host = "other", RemotePath = "/", LocalPath = "/x/" },
        ];

        Assert.Multiple(() =>
        {
            Assert.That(RemotePathMappingService.RemapRemoteToLocal(mappings, "QBIT", "/downloads/tv/UFC 314"), Is.EqualTo("/mnt/tv/UFC 314"));
            Assert.That(RemotePathMappingService.RemapRemoteToLocal(mappings, "qbit", "/downloads/movie.mkv"), Is.EqualTo("/mnt/dl/movie.mkv"));
            Assert.That(RemotePathMappingService.RemapRemoteToLocal(mappings, "qbit", "/downloads"), Is.EqualTo("/mnt/dl"));
            Assert.That(RemotePathMappingService.RemapRemoteToLocal(mappings, "nas", "/downloads/a"), Is.EqualTo("/downloads/a"), "other host");
        });
    }

    [Test]
    public void Video_files_skip_samples_extras_and_hidden_folders()
    {
        Write("UFC 314/main.mkv", 1000);
        Write("UFC 314/prelims.mp4", 900);
        Write("UFC 314/tiny.mkv", 10);
        Write("UFC 314/main-sample.mkv", 900);
        Write("UFC 314/Featurettes/bonus.mkv", 900);
        Write("UFC 314/.hidden/x.mkv", 900);
        Write("UFC 314/main.nfo", 900);

        var files = EpisodeImportService.VideoFiles(Path.Combine(_root, "UFC 314")).Select(f => f.Name);

        Assert.That(files, Is.EqualTo(new[] { "main.mkv", "prelims.mp4" }));
    }

    [TestCase(new[] { "Main Card.mkv:600", "Prelims.mkv:500" }, "", "Main Card.mkv", TestName = "Main card by name")]
    [TestCase(new[] { "ufc.334.mkv:600", "ufc.334.prelims.mkv:500" }, @"\bprelims?\b", "ufc.334.mkv", TestName = "Only one not excluded by mustNot")]
    [TestCase(new[] { "a.mkv:1000", "b.mkv:400" }, "", "a.mkv", TestName = "Clearly largest")]
    [TestCase(new[] { "a.mkv:1000", "b.mkv:600" }, "", null, TestName = "Ambiguous")]
    [TestCase(new[] { "Gane vs Hokit.mkv:500", "Countdown.mkv:600" }, "", "Gane vs Hokit.mkv", TestName = "Named like the episode")]
    public void Single_episode_picks_one_file_of_a_multi_file_release(string[] files, string mustNot, string? expected)
    {
        var infos = files.Select(f => f.Split(':')).Select(f => new FileInfo(Write("UFC 334/" + f[0], int.Parse(f[1], System.Globalization.CultureInfo.InvariantCulture)))).ToList();
        var episode = new Generral.Core.Tv.Episode { Title = "UFC 334: Gane vs. Hokit" };
        var terms = new Generral.Core.DecisionEngine.SearchTerms { MustNot = mustNot.Length > 0 ? [mustNot] : [] };

        var picked = EpisodeImportService.PickFile(infos, new Generral.Core.DecisionEngine.SearchCandidate(episode, terms));

        Assert.That(picked?.Name, Is.EqualTo(expected));
    }

    [Test]
    public void Hardlink_falls_back_to_copy_across_filesystems()
    {
        const string shm = "/dev/shm";
        if (!Directory.Exists(shm))
        {
            Assert.Ignore("No tmpfs to cross filesystems");
        }

        var source = Write("a.mkv", 100);
        var destination = Path.Combine(shm, "generral-" + Guid.NewGuid().ToString("N"), "b.mkv");
        var transfer = new DiskTransferService(NullLogger<DiskTransferService>.Instance);
        try
        {
            var mode = transfer.TransferFile(source, destination, TransferMode.HardLinkOrCopy);
            var sameDevice = transfer.TransferFile(source, Path.Combine(_root, "c.mkv"), TransferMode.HardLinkOrCopy);

            Assert.Multiple(() =>
            {
                Assert.That(mode, Is.EqualTo(TransferMode.Copy));
                Assert.That(new FileInfo(destination).Length, Is.EqualTo(100));
                Assert.That(sameDevice, Is.EqualTo(TransferMode.HardLinkOrCopy));
                Assert.That(() => transfer.TransferFile(source, destination, TransferMode.Copy), Throws.InstanceOf<IOException>(), "never overwrites");
            });
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(destination)!, recursive: true);
        }
    }

    [Test]
    public void Recycle_bin_keeps_both_files_and_cleans_up_old_ones()
    {
        var time = new FakeTime(new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));
        var bin = new RecycleBinProvider(new DiskTransferService(NullLogger<DiskTransferService>.Instance), time, NullLogger<RecycleBinProvider>.Instance);
        var config = new MediaManagementConfig { RecycleBin = Path.Combine(_root, "bin"), RecycleBinCleanupDays = 7 };

        var first = bin.DeleteFile(config, Write("tv/UFC/a.mkv", 1), "UFC");
        var second = bin.DeleteFile(config, Write("tv/UFC/a.mkv", 1), "UFC");
        time.Advance(TimeSpan.FromDays(8));
        var removed = bin.Cleanup(config);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(Path.Combine(_root, "bin", "UFC", "a.mkv")));
            Assert.That(second, Is.EqualTo(Path.Combine(_root, "bin", "UFC", "a_2.mkv")));
            Assert.That(removed, Is.EqualTo(2));
            Assert.That(Directory.Exists(Path.Combine(_root, "bin", "UFC")), Is.False);
        });
    }

    private string Write(string relative, int size)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
        return path;
    }
}
