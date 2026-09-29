using Ballast.Net;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Net;

public sealed record HarnessOptions
{
    public int Bots { get; init; } = 8;
    public int Cars { get; init; } = 10;
    public double Seconds { get; init; } = 120;
    public int Seed { get; init; } = 1;
    public LinkConditions Link { get; init; } = LinkConditions.Rough;
    public double StartDistance { get; init; } = 600;
}

public sealed record ClientReport(byte Id, string Bot, double MaxCorrectionM, int Corrections, int Snapshots, int HostMissedInputs,
    bool Alive, string Surface, string Where, long BytesUp, long BytesDown);

public sealed record HarnessReport(int Ticks, double Seconds, string Link, double TrainDistance, double TrainSpeed, double BoilerPressure, double Tender,
    int SnapshotBytes, double DownKbpsPerClient, double UpKbpsPerClient, double MaxCorrectionM, int Deaths,
    IReadOnlyList<ClientReport> Clients);

/// <summary>
/// Host plus N bot clients in one process over a <see cref="LoopbackNetwork"/> with simulated lag and loss,
/// faster than real time (GDD §34 "Stability: desync and crash detection across 2–8 clients").
/// </summary>
public static class Harness
{
    public static HarnessReport Run(RailLine line, TrainTuning trainTuning, PlayerTuning playerTuning, HarnessOptions o, BoilerTuning? boiler = null)
    {
        var net = new LoopbackNetwork(o.Seed, o.Link);
        var hostTransport = new CountingTransport(net.CreateHost());
        var host = new HostSession(hostTransport, NewTrain(line, trainTuning, o, boiler), trainTuning, playerTuning);

        var clients = new List<(ClientSession Session, IBot Bot, CountingTransport Transport)>();
        for (int i = 0; i < o.Bots; i++)
        {
            var transport = new CountingTransport(net.CreateClient());
            IBot bot = i == 0 ? new ConductorBot() : new RoofWalkerBot(o.Seed * 1000 + i);
            clients.Add((new ClientSession(transport, NewTrain(line, trainTuning, o, boiler), trainTuning, playerTuning), bot, transport));
        }

        int ticks = (int)(o.Seconds * SimConstants.TickRate);
        for (uint t = 0; t < ticks; t++)
        {
            net.Advance(SimConstants.TickSeconds);
            host.Step();
            foreach (var (session, bot, _) in clients)
            {
                var intent = session.Connected ? bot.Decide(session.Predicted, session.Train, t) : default;
                session.Step(intent);
            }
        }

        var hostPlayers = host.Players.ToDictionary(p => p.Id, p => p.State);
        var reports = clients.Select(c =>
        {
            byte id = c.Session.PlayerId ?? 0;
            var s = hostPlayers.GetValueOrDefault(id);
            string where = s.Parent == PlayerState.World ? "ground" : s.Parent == 0 ? "engine" : $"car {s.Parent}";
            return new ClientReport(id, c.Bot.Name, Math.Round(c.Session.MaxCorrection, 4), c.Session.Corrections, c.Session.SnapshotsReceived,
                host.MissedInputs(id), s.Alive, s.Surface.ToString(), where, c.Transport.BytesSent, c.Transport.BytesReceived);
        }).ToList();

        double seconds = ticks * SimConstants.TickSeconds;
        return new HarnessReport(ticks, seconds, $"{o.Link.LatencySeconds * 1000:0}ms ±{o.Link.JitterSeconds * 1000:0} loss {o.Link.LossRate:P0}",
            Math.Round(host.Train.Dynamics.Distance, 1), Math.Round(host.Train.Dynamics.Speed, 2),
            Math.Round(host.Train.Boiler.Pressure, 1), Math.Round(host.Train.Boiler.Tender), host.LastSnapshotBytes,
            Math.Round(reports.Average(r => r.BytesDown) * 8 / 1000 / seconds, 1), Math.Round(reports.Average(r => r.BytesUp) * 8 / 1000 / seconds, 1),
            reports.Max(r => r.MaxCorrectionM), reports.Count(r => !r.Alive), reports);
    }

    static TrainOnLine NewTrain(RailLine line, TrainTuning t, HarnessOptions o, BoilerTuning? boiler) =>
        new(new TrainDynamics(Consist.Uniform(t, o.Cars, 1)), line, o.StartDistance, boiler);

    /// <summary>Counts payload bytes both ways for bandwidth reporting.</summary>
    sealed class CountingTransport(ITransport inner) : ITransport
    {
        public long BytesSent, BytesReceived;
        public PeerId LocalId => inner.LocalId;

        public void Send(PeerId to, ReadOnlySpan<byte> payload, Delivery delivery)
        {
            BytesSent += payload.Length;
            inner.Send(to, payload, delivery);
        }

        public void Poll(List<TransportEvent> into)
        {
            int start = into.Count;
            inner.Poll(into);
            for (int i = start; i < into.Count; i++)
                BytesReceived += into[i].Payload?.Length ?? 0;
        }

        public void Disconnect(PeerId peer) => inner.Disconnect(peer);
        public void Dispose() => inner.Dispose();
    }
}
