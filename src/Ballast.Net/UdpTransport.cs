using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace Ballast.Net;

/// <summary>
/// <see cref="ITransport"/> over plain UDP: direct IP and LAN play, playtests, and the host's own player on
/// localhost. The protocol is <see cref="DatagramTransport{TAddress}"/>'s; this is one non-blocking socket.
/// </summary>
public sealed class UdpTransport : DatagramTransport<EndPoint>
{
    readonly Socket _socket;

    UdpTransport(Socket socket, DatagramOptions? options) : base(new Carrier(socket), options) => _socket = socket;
    UdpTransport(Socket socket, DatagramOptions? options, EndPoint host) : base(new Carrier(socket), options, host) => _socket = socket;

    /// <summary>Listens on <paramref name="port"/> (0 = any free port; see <see cref="Port"/>).</summary>
    public static UdpTransport Host(int port, DatagramOptions? options = null, IPAddress? bind = null)
    {
        var socket = MakeSocket();
        socket.Bind(new IPEndPoint(bind ?? IPAddress.Any, port));
        return new UdpTransport(socket, options);
    }

    /// <summary>Starts connecting to a host. <see cref="TransportEventKind.Connected"/> arrives once it accepts.</summary>
    public static UdpTransport Connect(IPEndPoint host, DatagramOptions? options = null)
    {
        if (host.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("IPv4 only for now", nameof(host));
        var socket = MakeSocket();
        socket.Bind(new IPEndPoint(IPAddress.Any, 0));
        return new UdpTransport(socket, options, host);
    }

    public int Port => ((IPEndPoint)_socket.LocalEndPoint!).Port;

    static Socket MakeSocket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
        if (OperatingSystem.IsWindows())
        {
            // Otherwise an ICMP "port unreachable" from a departed peer surfaces as a ConnectionReset on
            // the next receive, for everyone on the socket.
            const int SioUdpConnReset = -1744830452;
            socket.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);
        }
        return socket;
    }

    sealed class Carrier(Socket socket) : IDatagramCarrier<EndPoint>
    {
        public void Send(EndPoint to, ReadOnlySpan<byte> datagram)
        {
            try
            {
                socket.SendTo(datagram, SocketFlags.None, to);
            }
            catch (SocketException)
            {
                // Unreachable or buffer full: indistinguishable from loss, and handled the same way.
            }
        }

        public bool TryReceive(Span<byte> buffer, out int length, [MaybeNullWhen(false)] out EndPoint from)
        {
            while (true)
            {
                EndPoint any = new IPEndPoint(IPAddress.Any, 0);
                try
                {
                    // Not Available: an empty datagram reads as 0 bytes available and would wedge the queue.
                    if (!socket.Poll(0, SelectMode.SelectRead))
                        break;
                    length = socket.ReceiveFrom(buffer, SocketFlags.None, ref any);
                    from = any;
                    return true;
                }
                catch (SocketException e) when (e.SocketErrorCode is SocketError.WouldBlock or SocketError.ConnectionReset or SocketError.MessageSize)
                {
                    if (e.SocketErrorCode == SocketError.WouldBlock)
                        break;
                }
            }
            length = 0;
            from = null;
            return false;
        }

        public void Dispose() => socket.Dispose();
    }
}
