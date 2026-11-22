// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using System.Net.Sockets;
using System.Text;
using Generral.Core.Download;
using Generral.Core.Indexers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Generral.Core.Test.Download;

public class TorrentReleaseFetcherTests
{
    private const string Hash = "6AEB9888840AC1407E6F48B5D32824431921057B";

    [Test]
    public async Task A_redirect_to_a_magnet_is_passed_on_as_the_magnet()
    {
        // A real handler and socket: Prowlarr answers /download with "301 Location: magnet:…", which an
        // auto-redirecting handler can't follow.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var magnet = $"magnet:?xt=urn:btih:{Hash}&dn=&tr=udp%3A%2F%2Ftracker.example%3A451%2Fannounce";
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync();
            var stream = socket.GetStream();
            var buffer = new byte[4096];
            _ = await stream.ReadAsync(buffer);
            var response = $"HTTP/1.1 301 Moved Permanently\r\nLocation: {magnet}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response));
        });

        var services = new ServiceCollection();
        services.AddHttpClient(TorrentReleaseFetcher.ClientName).ConfigurePrimaryHttpMessageHandler(TorrentReleaseFetcher.CreateHandler);
        using var provider = services.BuildServiceProvider();
        var fetcher = new TorrentReleaseFetcher(provider.GetRequiredService<IHttpClientFactory>(), NullLogger<TorrentReleaseFetcher>.Instance);

        var payload = await fetcher.ResolveAsync(new ReleaseInfo { Guid = "g", Title = "UFC 324", DownloadUrl = $"http://127.0.0.1:{port}/12/download?file=x" });
        await server;

        Assert.Multiple(() =>
        {
            Assert.That(payload.MagnetUrl, Is.EqualTo(magnet));
            Assert.That(payload.Hash, Is.EqualTo(Hash));
            Assert.That(payload.File, Is.Null);
        });
    }
}
