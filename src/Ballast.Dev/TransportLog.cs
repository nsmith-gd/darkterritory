using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Threading.Channels;
using Ballast.Net;

namespace Ballast.Dev;

/// <summary>
/// A host's night as it heard it (ARCHITECTURE §8 note 515): everything its transport handed it, poll by poll, and a digest
/// of what it sent back after each, with notes between. A host whose step is a function of what it polled (a deterministic
/// sim that's told everything through its transport) can be run again from this exactly, and the digests say on which
/// step a rerun first parts from the night (<see cref="ReplayTransport"/>).
/// <para>The file is gzip over records of <c>[tag u8][length VarU][body]</c>: one <see cref="Header"/> (UTF-8 JSON) first,
/// then for each poll a <see cref="Poll"/> (its events) and a <see cref="Sent"/> (the digest of the sends that followed it),
/// <see cref="Note"/>s (UTF-8 JSON, the game's) wherever they fell, and an <see cref="End"/> (UTF-8 JSON) last.</para>
/// </summary>
public static class TransportLog
{
    public const byte Header = (byte)'H', Poll = (byte)'P', Sent = (byte)'S', Note = (byte)'N', End = (byte)'E';

    /// <summary>The files' extension: a gzipped transport log.</summary>
    public const string Extension = ".dtrec";

    /// <summary>
    /// The digest of one step's sends: 64-bit, over each send's peer, delivery, length and bytes in order. Plain arithmetic,
    /// the same on every machine (a <see cref="HashCode"/> is seeded per process).
    /// </summary>
    public struct Digest
    {
        const ulong Prime = 0x100000001B3, Mix = 0x9E3779B97F4A7C15;
        ulong _h;
        public int Count { get; private set; }
        public long Bytes { get; private set; }
        public readonly ulong Value => _h;

        public static Digest Start() => new() { _h = 0xCBF29CE484222325 };

        public void Add(PeerId to, Delivery delivery, ReadOnlySpan<byte> payload)
        {
            Word(to.Value);
            Word(((ulong)delivery << 32) | (uint)payload.Length);
            int i = 0;
            for (; i + 8 <= payload.Length; i += 8)
                Word(BinaryPrimitives.ReadUInt64LittleEndian(payload[i..]));
            ulong tail = 0;
            for (int s = 0; i < payload.Length; i++, s += 8)
                tail |= (ulong)payload[i] << s;
            Word(tail);
            Count++;
            Bytes += payload.Length;
        }

        void Word(ulong w)
        {
            _h ^= w * Mix;
            _h = ulong.RotateLeft(_h, 27) * Prime;
        }
    }
}

/// <summary>One record read back from a log.</summary>
public abstract record LogRecord;
public sealed record LogHeader(string Json) : LogRecord;
public sealed record LogNote(string Json) : LogRecord;
public sealed record LogEnd(string Json) : LogRecord;
public sealed record LogSent(ulong Digest, int Count, long Bytes) : LogRecord;
public sealed record LogPoll(IReadOnlyList<TransportEvent> Events) : LogRecord;

/// <summary>
/// Writes a <see cref="TransportLog"/> without holding up the frame (note 515's cost): the caller's thread only copies a
/// poll's bytes into a buffer; each second's worth is handed to a background writer that compresses it to the file.
/// </summary>
public sealed class TransportLogWriter : IDisposable
{
    readonly Stream _file;
    readonly GZipStream _zip;
    readonly Channel<(byte[] Buffer, int Length)> _queue = Channel.CreateUnbounded<(byte[], int)>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    readonly Task _writer;
    readonly NetWriter _body = new();
    byte[] _buffer = new byte[64 * 1024];
    int _length;
    bool _closed;

    /// <summary>Hand the buffer to the writer once it holds this much.</summary>
    const int FlushBytes = 48 * 1024;

    public TransportLogWriter(Stream file, string headerJson)
    {
        _file = file;
        _zip = new GZipStream(file, CompressionLevel.Fastest, leaveOpen: true);
        _writer = Task.Run(Drain);
        Text(TransportLog.Header, headerJson);
    }

    /// <summary>Polls written so far.</summary>
    public long Polls { get; private set; }
    /// <summary>Bytes before compression.</summary>
    public long RawBytes { get; private set; }

    async Task Drain()
    {
        // Each chunk flushed through to the file as it's written, so a night cut off by a crash keeps all but its last seconds.
        await foreach (var (buffer, length) in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            _zip.Write(buffer, 0, length);
            _zip.Flush();
        }
    }

    void Record(byte tag, ReadOnlySpan<byte> body)
    {
        if (_closed)
            return;
        int need = 1 + 10 + body.Length;
        if (_length + need > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + need));
        _buffer[_length++] = tag;
        ulong n = (ulong)body.Length;
        while (n >= 0x80)
        {
            _buffer[_length++] = (byte)(n | 0x80);
            n >>= 7;
        }
        _buffer[_length++] = (byte)n;
        body.CopyTo(_buffer.AsSpan(_length));
        _length += body.Length;
        RawBytes += need;
        if (_length >= FlushBytes)
            Flush();
    }

    void Text(byte tag, string json) => Record(tag, Encoding.UTF8.GetBytes(json));

    /// <summary>A poll's events (from <paramref name="from"/> on in the list), each payload through <paramref name="scrub"/>.</summary>
    public void Polled(List<TransportEvent> events, int from, Func<byte[], ReadOnlySpan<byte>>? scrub = null)
    {
        _body.Reset();
        _body.VarU((ulong)(events.Count - from));
        for (int i = from; i < events.Count; i++)
        {
            var e = events[i];
            _body.U8((byte)e.Kind);
            _body.VarU(e.Peer.Value);
            _body.U8((byte)e.Delivery);
            ReadOnlySpan<byte> payload = e.Payload is null ? default : scrub is null ? e.Payload : scrub(e.Payload);
            _body.VarU((ulong)payload.Length);
            _body.Bytes(payload);
        }
        Record(TransportLog.Poll, _body.Written);
        Polls++;
    }

    public void Sent(in TransportLog.Digest digest) => SentRaw(digest.Value, digest.Count, digest.Bytes);

    /// <summary>A step's digest as already worked out (a log being rewritten).</summary>
    public void SentRaw(ulong digest, int count, long bytes)
    {
        _body.Reset();
        _body.U64(digest);
        _body.VarU((ulong)count);
        _body.VarU((ulong)bytes);
        Record(TransportLog.Sent, _body.Written);
    }

    public void Note(string json) => Text(TransportLog.Note, json);

    void Flush()
    {
        if (_length == 0)
            return;
        _queue.Writer.TryWrite((_buffer, _length));
        _buffer = new byte[Math.Max(64 * 1024, _buffer.Length)];
        _length = 0;
    }

    /// <summary>Writes the end record and closes the file, waiting for the writer to finish.</summary>
    public void Close(string endJson)
    {
        if (_closed)
            return;
        Text(TransportLog.End, endJson);
        Flush();
        _closed = true;
        _queue.Writer.TryComplete();
        _writer.GetAwaiter().GetResult();
        _zip.Dispose();
        _file.Dispose();
    }

    public void Dispose() => Close("{}");
}

/// <summary>Reads a <see cref="TransportLog"/> back, record by record.</summary>
public static class TransportLogReader
{
    public static IEnumerable<LogRecord> Read(string path)
    {
        using var file = File.OpenRead(path);
        foreach (var r in Read(file))
            yield return r;
    }

    public static IEnumerable<LogRecord> Read(Stream file)
    {
        using var zip = new GZipStream(file, CompressionMode.Decompress, leaveOpen: true);
        using var input = new BufferedStream(zip, 1 << 16);
        var body = Array.Empty<byte>();
        while (true)
        {
            int tag = input.ReadByte();
            if (tag < 0)
                yield break;
            ulong length = 0;
            for (int shift = 0; ; shift += 7)
            {
                int b = input.ReadByte();
                if (b < 0)
                    throw new EndOfStreamException("the log ends inside a record");
                length |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80)
                    break;
            }
            if ((ulong)body.Length < length)
                body = new byte[length];
            input.ReadExactly(body, 0, (int)length);
            var span = body.AsSpan(0, (int)length);
            yield return (byte)tag switch
            {
                TransportLog.Header => new LogHeader(Encoding.UTF8.GetString(span)),
                TransportLog.Note => new LogNote(Encoding.UTF8.GetString(span)),
                TransportLog.End => new LogEnd(Encoding.UTF8.GetString(span)),
                TransportLog.Sent => ReadSent(span.ToArray()),
                TransportLog.Poll => ReadPoll(span.ToArray()),
                _ => throw new InvalidDataException($"not a transport log: record '{(char)tag}'"),
            };
        }
    }

    static LogSent ReadSent(byte[] body)
    {
        var r = new Cursor(body);
        return new LogSent(r.U64(), (int)r.VarU(), (long)r.VarU());
    }

    static LogPoll ReadPoll(byte[] body)
    {
        var r = new Cursor(body);
        int n = (int)r.VarU();
        var events = new List<TransportEvent>(n);
        for (int i = 0; i < n; i++)
        {
            var kind = (TransportEventKind)r.U8();
            var peer = new PeerId(r.VarU());
            var delivery = (Delivery)r.U8();
            var payload = r.Bytes((int)r.VarU());
            events.Add(new TransportEvent(kind, peer, kind == TransportEventKind.Data ? payload : null, delivery));
        }
        return new LogPoll(events);
    }

    /// <summary>Reads a record's body.</summary>
    struct Cursor(byte[] data)
    {
        int _at;

        public byte U8() => data[_at++];

        public ulong U64()
        {
            ulong v = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(_at, 8));
            _at += 8;
            return v;
        }

        public ulong VarU()
        {
            ulong v = 0;
            for (int shift = 0; ; shift += 7)
            {
                byte b = data[_at++];
                v |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80)
                    return v;
            }
        }

        public byte[] Bytes(int n)
        {
            var b = data.AsSpan(_at, n).ToArray();
            _at += n;
            return b;
        }
    }
}
