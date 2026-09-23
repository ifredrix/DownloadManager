using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IfredrixDownloadManager;

/// <summary>
/// Minimal SOCKS5 client (RFC 1928, no-auth) for routing downloads through
/// the Tor network. Hostnames — including <c>.onion</c> addresses — are sent
/// to the proxy unresolved (ATYP domain), so Tor itself resolves them and
/// nothing leaks via local DNS.
/// Needs a running Tor: the Tor expert bundle (port 9050 by default)
/// or Tor Browser (port 9150).
/// </summary>
public static class TorProxy
{
    /// <summary>Configured SOCKS endpoint. Defaults match Tor's defaults.</summary>
    public static string Host { get; set; } = "127.0.0.1";

    public static int Port { get; set; } = 9050;

    public static bool IsOnionUrl(string url)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            return uri.Host.EndsWith(".onion", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// True for loopback/LAN targets a Tor exit node can never reach (it
    /// would dial its own local network, which fails with SOCKS5 0x01 or
    /// hits the wrong network entirely). Only IP literals and "localhost"
    /// count - anything else would need a DNS lookup, which is exactly what
    /// route-all exists to avoid. A LAN hostname therefore stays on Tor and
    /// fails loudly instead of silently leaking a local lookup.
    /// </summary>
    public static bool IsLocalUrl(Uri uri)
    {
        try
        {
            if (uri.IsLoopback) return true;
            if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                return true;
            if (!IPAddress.TryParse(uri.Host, out var ip)) return false;

            if (IPAddress.IsLoopback(ip)) return true;
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;
                var v6 = ip.GetAddressBytes();
                return (v6[0] & 0xFE) == 0xFC; // fc00::/7 unique-local
            }

            var b = ip.GetAddressBytes();
            return b[0] == 10                                // 10.0.0.0/8
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) // 172.16.0.0/12
                || (b[0] == 192 && b[1] == 168)              // 192.168.0.0/16
                || (b[0] == 169 && b[1] == 254);             // link-local
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Quick liveness check (TCP connect, ~1.5 s budget).</summary>
    public static async Task<bool> IsAvailableAsync(string? host = null, int? port = null)
    {
        using var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(host ?? Host, port ?? Port)
                .WaitAsync(TimeSpan.FromMilliseconds(1500))
                .ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Opens a SOCKS5 CONNECT tunnel to the destination. The returned stream
    /// is ready for plain HTTP, or for a TLS handshake on top (HTTPS).
    /// </summary>
    public static async Task<Stream> ConnectAsync(
        string destHost, int destPort, CancellationToken ct = default)
        => await ConnectAsync(Host, Port, destHost, destPort, ct).ConfigureAwait(false);

    public static async Task<Stream> ConnectAsync(
        string proxyHost, int proxyPort, string destHost, int destPort, CancellationToken ct = default)
    {
        var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(proxyHost, proxyPort, ct).ConfigureAwait(false);
            var stream = tcp.GetStream();

            // Greeting: version 5, one method, no authentication.
            await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 }, ct).ConfigureAwait(false);
            var method = await ReadExactAsync(stream, 2, ct).ConfigureAwait(false);
            if (method[0] != 0x05) throw new InvalidOperationException("Not a SOCKS5 proxy.");
            if (method[1] == 0xFF) throw new InvalidOperationException("SOCKS5 proxy needs authentication.");

            // Request: CONNECT + destination (domain stays unresolved so Tor
            // resolves it — required for .onion).
            byte[] request;
            if (IPAddress.TryParse(destHost, out var ip))
            {
                var raw = ip.GetAddressBytes();
                var atyp = (byte)(raw.Length == 4 ? 0x01 : 0x04);
                request = new byte[4 + raw.Length + 2];
                request[0] = 0x05; request[1] = 0x01; request[2] = 0x00; request[3] = atyp;
                Buffer.BlockCopy(raw, 0, request, 4, raw.Length);
                request[^2] = (byte)(destPort >> 8);
                request[^1] = (byte)(destPort & 0xFF);
            }
            else
            {
                var name = Encoding.ASCII.GetBytes(destHost);
                if (name.Length == 0 || name.Length > 255)
                {
                    throw new ArgumentException("Bad destination host.", nameof(destHost));
                }
                request = new byte[4 + 1 + name.Length + 2];
                request[0] = 0x05; request[1] = 0x01; request[2] = 0x00; request[3] = 0x03;
                request[4] = (byte)name.Length;
                Buffer.BlockCopy(name, 0, request, 5, name.Length);
                request[^2] = (byte)(destPort >> 8);
                request[^1] = (byte)(destPort & 0xFF);
            }

            await stream.WriteAsync(request, ct).ConfigureAwait(false);

            var reply = await ReadExactAsync(stream, 4, ct).ConfigureAwait(false);
            if (reply[0] != 0x05) throw new InvalidOperationException("Not a SOCKS5 proxy.");
            if (reply[1] != 0x00)
            {
                throw new InvalidOperationException(
                    $"Tor refused the connection (SOCKS5 code 0x{reply[1]:X2}).");
            }

            // Consume the bound address + port.
            var addrLen = reply[3] switch
            {
                0x01 => 4,
                0x04 => 16,
                0x03 => (await ReadExactAsync(stream, 1, ct).ConfigureAwait(false))[0],
                _ => throw new InvalidOperationException("Bad SOCKS5 reply.")
            };
            await ReadExactAsync(stream, addrLen + 2, ct).ConfigureAwait(false);

            // NetworkStream owns the socket: disposing it closes everything.
            return stream;
        }
        catch
        {
            try { tcp.Close(); } catch { }
            throw;
        }
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), ct).ConfigureAwait(false);
            if (read == 0) throw new IOException("SOCKS5 proxy closed the connection.");
            offset += read;
        }
        return buffer;
    }
}
