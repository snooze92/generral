using System.Security.Cryptography;
using System.Text;

namespace Generral.Core.Download;

/// <summary>Infohash (v1, upper-case hex) of a magnet link or a <c>.torrent</c> file.</summary>
public static class TorrentInfoHash
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <returns>The hash from the first <c>xt=urn:btih:</c> (hex or base32), or null.</returns>
    public static string? FromMagnet(string magnet)
    {
        var query = magnet.IndexOf('?', StringComparison.Ordinal) is var q and >= 0 ? magnet[(q + 1)..] : "";
        foreach (var part in query.Split('&'))
        {
            var value = Uri.UnescapeDataString(part);
            if (!value.StartsWith("xt=urn:btih:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var hash = value["xt=urn:btih:".Length..];
            if (hash.Length == 40 && hash.All(Uri.IsHexDigit))
            {
                return hash.ToUpperInvariant();
            }

            if (hash.Length == 32 && FromBase32(hash.ToUpperInvariant()) is { } bytes)
            {
                return Convert.ToHexString(bytes);
            }
        }

        return null;
    }

    /// <exception cref="FormatException">Not a bencoded torrent with an <c>info</c> dictionary.</exception>
    public static string FromTorrentFile(byte[] torrent)
    {
        if (torrent.Length == 0 || torrent[0] != 'd')
        {
            throw new FormatException("Not a torrent file");
        }

        var position = 1;
        while (position < torrent.Length && torrent[position] != 'e')
        {
            var key = ReadString(torrent, ref position);
            var start = position;
            Skip(torrent, ref position);
            if (key == "info")
            {
                return Convert.ToHexString(SHA1.HashData(torrent.AsSpan(start, position - start)));
            }
        }

        throw new FormatException("The torrent has no info dictionary");
    }

    private static string ReadString(byte[] data, ref int position)
    {
        var start = position;
        Skip(data, ref position);
        var colon = Array.IndexOf(data, (byte)':', start);
        return Encoding.UTF8.GetString(data, colon + 1, position - colon - 1);
    }

    private static void Skip(byte[] data, ref int position)
    {
        if (position >= data.Length)
        {
            throw new FormatException("Truncated torrent file");
        }

        switch ((char)data[position])
        {
            case 'i':
                position = Array.IndexOf(data, (byte)'e', position) + 1;
                if (position == 0)
                {
                    throw new FormatException("Truncated torrent file");
                }

                break;
            case 'l' or 'd':
                position++;
                while (position < data.Length && data[position] != 'e')
                {
                    Skip(data, ref position);
                }

                position++;
                break;
            case >= '0' and <= '9':
                var colon = Array.IndexOf(data, (byte)':', position);
                if (colon < 0 || !int.TryParse(data.AsSpan(position, colon - position), out var length) || colon + 1 + length > data.Length)
                {
                    throw new FormatException("Invalid string in torrent file");
                }

                position = colon + 1 + length;
                break;
            default:
                throw new FormatException("Invalid torrent file");
        }

        if (position > data.Length)
        {
            throw new FormatException("Truncated torrent file");
        }
    }

    private static byte[]? FromBase32(string value)
    {
        var bytes = new byte[value.Length * 5 / 8];
        int buffer = 0, bits = 0, index = 0;
        foreach (var c in value)
        {
            var digit = Base32Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (digit < 0)
            {
                return null;
            }

            buffer = (buffer << 5) | digit;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes[index++] = (byte)(buffer >> bits);
            }
        }

        return bytes;
    }
}
