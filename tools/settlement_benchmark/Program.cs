using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ProjectRealm.Domain;

// A bounded diagnostic harness, NOT the game scheduler or a full economic model.
// All simulated people, decisions, flows and prices below are synthetic.
namespace SettlementBenchmark;

internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true };

    private static void Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Usage: benchmark fixture.json result.json");
        var fixture = JsonSerializer.Deserialize<Fixture>(File.ReadAllText(args[0]), Json);
        fixture.Validate();
        var result = new BenchmarkResult
        {
            Runtime = RuntimeInformation.FrameworkDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            FixtureHash = fixture.SourceSha256,
            StartedUtc = DateTime.UtcNow,
        };
        foreach (var config in new[]
        {
            new Config("standard", 100_000, 12, 12, 6, 6, 4),
            new Config("stress", 300_000, 24, 24, 24, 12, 16),
        })
        {
            Console.WriteLine($"Starting {config.Name}; real geography, synthetic actors/economy.");
            var setup = Stopwatch.StartNew();
            var world = new World(fixture, config);
            var scenario = new ScenarioResult
            {
                Name = config.Name, Counties = world.Counties, Divisions = world.Divisions,
                Population = fixture.Divisions.Sum(d => d.Population),
                Cohorts = world.Divisions * config.CohortsPerDivision,
                Officials = world.Counties * config.OfficialsPerCounty,
                OtherPeople = config.People, ResourceSlots = world.Divisions * World.Goods,
                CandidatesPerActor = config.Candidates, RelationsPerCandidate = config.Relations,
                Transfers = world.Counties * config.TransfersPerCounty, SetupMs = setup.Elapsed.TotalMilliseconds,
            };
            var warm = new Job(world, false);
            warm.Drain(); // Same full workload for JIT warm-up; never reported as a measured sample.
            warm.Validate();
            string expected = null;
            for (var repeat = 0; repeat < 3; repeat++)
            {
                foreach (var sliced in new[] { false, true })
                {
                    var measured = Measure(world, sliced, false, false);
                    expected ??= measured.Job.Digest();
                    Require(expected == measured.Job.Digest(), "sync/sliced results differ");
                    scenario.Runs.Add(measured.Metrics);
                    Console.WriteLine($"  {measured.Metrics.Mode}: work={measured.Metrics.WorkMs:F2}ms, max slice={measured.Metrics.MaxSliceMs:F2}ms, slices={measured.Metrics.Slices}");
                }
            }
            var reversed = Measure(world, true, false, true);
            Require(reversed.Job.Digest() == expected, "independent work order changed the result");
            scenario.Checks.Add("sync_vs_sliced_and_reversed_exact_sha256");
            scenario.Checks.Add("population_resource_money_conservation_and_county_rollup");
            var paced = Measure(world, true, true, false);
            Require(paced.Job.Digest() == expected, "60Hz-paced result differs");
            scenario.Runs.Add(paced.Metrics);
            scenario.ResultSha256 = expected;
            scenario.Checks.Add("60hz_paced_loop_same_result");
            CheckRecovery(world, expected, scenario);
            CheckPublication(world, paced.Job, scenario);
            scenario.SampleCounty = CountySample.Capture(world, paced.Job, 0, 0);
            result.Scenarios.Add(scenario);
            File.WriteAllText(args[1], JsonSerializer.Serialize(result, Json));
            Console.WriteLine($"Finished {config.Name}: all {scenario.Checks.Count} checks passed.");
        }
        // Exercise the already-present multi-layer accounting probe as well.
        var probe = new LayeredSettlementYearProbe(LayeredSettlementYearScenario.CreateDroughtSettlementProbe()).Run();
        Require(probe.Months.Count == 12, "existing year probe failed");
        result.ExistingYearProbeMonths = probe.Months.Count;
        result.FinishedUtc = DateTime.UtcNow;
        File.WriteAllText(args[1], JsonSerializer.Serialize(result, Json));
    }

    private static (Job Job, RunMetrics Metrics) Measure(World world, bool sliced, bool paced, bool reverse)
    {
        var allocationStart = GC.GetTotalAllocatedBytes(true);
        var gc0 = GC.CollectionCount(0);
        var prep = Stopwatch.StartNew();
        var job = new Job(world, reverse);
        var preparationMs = prep.Elapsed.TotalMilliseconds;
        var slices = new List<double>();
        var maxItemMs = 0.0;
        var total = Stopwatch.StartNew();
        var stageMs = new double[Job.StageNames.Length];
        if (!sliced)
        {
            job.Drain();
            slices.Add(total.Elapsed.TotalMilliseconds);
        }
        else
        {
            while (!job.Complete)
            {
                var frame = Stopwatch.StartNew();
                var tick = Stopwatch.StartNew();
                do
                {
                    var stage = job.Phase;
                    var item = Stopwatch.StartNew();
                    job.Step();
                    var elapsed = item.Elapsed.TotalMilliseconds;
                    maxItemMs = Math.Max(maxItemMs, elapsed);
                    stageMs[stage] += elapsed;
                } while (!job.Complete && tick.Elapsed.TotalMilliseconds < 2.0);
                slices.Add(tick.Elapsed.TotalMilliseconds);
                if (paced && !job.Complete)
                {
                    // Headless heartbeat, NOT a rendered Unity frame. Sleeping gives CPU time back.
                    var remaining = 1000.0 / 60.0 - frame.Elapsed.TotalMilliseconds;
                    if (remaining > 0) Thread.Sleep(TimeSpan.FromMilliseconds(remaining));
                }
            }
        }
        var wallMs = total.Elapsed.TotalMilliseconds;
        var allocations = GC.GetTotalAllocatedBytes(true) - allocationStart;
        var gcCollections = GC.CollectionCount(0) - gc0;
        var audit = Stopwatch.StartNew();
        job.Validate();
        var auditMs = audit.Elapsed.TotalMilliseconds;
        var hashing = Stopwatch.StartNew();
        job.Digest();
        var hashMs = hashing.Elapsed.TotalMilliseconds;
        var ordered = slices.Order().ToArray();
        return (job, new RunMetrics
        {
            Mode = !sliced ? "synchronous" : paced ? "sliced_2ms_paced_60hz" : "sliced_2ms_unpaced",
            PreparationMs = preparationMs, WorkMs = slices.Sum(), WallMs = wallMs,
            Slices = slices.Count, P95SliceMs = ordered[(int)Math.Ceiling(ordered.Length * 0.95) - 1],
            MaxSliceMs = ordered[^1], MaxWorkItemMs = maxItemMs,
            Over4MsSlices = slices.Count(t => t > 4), Over16_67MsSlices = slices.Count(t => t > 1000.0 / 60),
            AllocatedMiB = allocations / 1048576.0, Gen0Collections = gcCollections,
            AuditMs = auditMs, HashMs = hashMs,
            StageWorkMs = Job.StageNames.Zip(stageMs).ToDictionary(p => p.First, p => p.Second),
        });
    }

    private static void CheckRecovery(World world, string expected, ScenarioResult result)
    {
        // Test a mid-production and a mid-cross-county-transfer checkpoint, including sequential deltas.
        foreach (var phase in new[] { 1, 4 })
        {
            var partial = new Job(world, true);
            while (partial.Phase < phase || partial.Cursor < 3) partial.Step();
            var io = Stopwatch.StartNew();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(partial.State, Json);
            var restored = JsonSerializer.Deserialize<JobState>(bytes, Json);
            result.CheckpointRoundtripMs.Add(io.Elapsed.TotalMilliseconds);
            result.CheckpointMiB = Math.Max(result.CheckpointMiB, bytes.Length / 1048576.0);
            var resumed = new Job(world, restored);
            resumed.Drain();
            resumed.Validate();
            Require(resumed.Digest() == expected, "checkpoint restart lost or replayed work");
        }
        result.Checks.Add("serialized_mid_production_and_mid_transfer_resume_exact");
        var invalid = new Job(world, false).State;
        invalid.Month++;
        MustReject(() => new Job(world, invalid), "wrong-month checkpoint accepted");
        result.Checks.Add("wrong_month_checkpoint_rejected");
        var orphan = new Fixture
        {
            CountyIds = world.Fixture.CountyIds,
            Divisions = new[] { new Division { Id = "bad", County = world.Counties, Population = 1 } },
        };
        MustReject(orphan.Validate, "orphan division accepted");
        result.Checks.Add("orphan_parent_reference_rejected");
    }

    private static void CheckPublication(World world, Job finished, ScenarioResult result)
    {
        var store = new PublicationStore(world);
        MustReject(() => store.Publish(new Job(world, false)), "partial month became visible");
        Require(store.TrySeptemberSpend("SEP-CMD-1", 123), "valid reserved spend rejected");
        Require(!store.TrySeptemberSpend("SEP-CMD-1", 123), "same new-month command applied twice");
        Require(!store.TrySeptemberSpend("SEP-TOO-LARGE", long.MaxValue), "overspend accepted");
        Require(store.Publish(finished), "first publication rejected");
        var after = store.AvailableCash;
        Require(after == finished.State.HouseholdCash[0] - 123, "August overwrote September command");
        Require(!store.Publish(finished) && store.AvailableCash == after, "duplicate month publication changed money");
        result.Checks.Add("partial_publication_blocked_and_month_commit_idempotent");
        result.Checks.Add("new_month_reserved_command_survives_prior_month_publication");
        result.Checks.Add("duplicate_command_and_overspend_blocked");
        // Negative control: a one-unit accounting corruption must be caught, not silently normalized.
        finished.State.CountyStocks[0]++;
        MustReject(finished.Validate, "corrupt county stock escaped audit");
        finished.State.CountyStocks[0]--;
        finished.Validate();
        result.Checks.Add("injected_one_unit_resource_corruption_detected");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static void MustReject(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException(message);
    }
}

internal sealed class Fixture
{
    public string SourceSha256 = "";
    public string[] CountyIds;
    public string[] CountyNames = Array.Empty<string>();
    public Division[] Divisions;

    public void Validate()
    {
        Program.Require(CountyIds.Length > 0 && CountyIds.Distinct().Count() == CountyIds.Length, "duplicate county IDs");
        Program.Require(Divisions.Length > 0 && Divisions.Select(d => d.Id).Distinct().Count() == Divisions.Length, "duplicate division IDs");
        foreach (var d in Divisions)
            Program.Require(d.County >= 0 && d.County < CountyIds.Length && d.Population > 0, "invalid division parent/population");
    }
}

internal sealed class Division
{
    public string Id;
    public int County;
    public long Population;
}

internal sealed record Config(string Name, int People, int OfficialsPerCounty, int CohortsPerDivision,
    int Candidates, int Relations, int TransfersPerCounty);

internal sealed class World
{
    public const int Goods = 29;
    public const int Month = 162808;
    public readonly Fixture Fixture;
    public readonly Config Config;
    public readonly int Counties, Divisions, Actors;
    public readonly int[][] CountyDivisions;
    public readonly long[] StartingStocks;
    public readonly int[] ActorMorale;
    public readonly SectorProductionActivity[] Activities;
    public readonly SimulationDriverRecord[][] Drivers;
    public readonly LedgerPeriod Period = LedgerPeriod.Monthly(1628, 8);

    public World(Fixture fixture, Config config)
    {
        Fixture = fixture; Config = config; Counties = fixture.CountyIds.Length; Divisions = fixture.Divisions.Length;
        Actors = config.People + Counties * config.OfficialsPerCounty;
        CountyDivisions = Enumerable.Range(0, Counties)
            .Select(c => Enumerable.Range(0, Divisions).Where(d => fixture.Divisions[d].County == c).ToArray()).ToArray();
        StartingStocks = new long[Divisions * Goods];
        ActorMorale = Enumerable.Range(0, Actors).Select(a => (int)(Noise(a, 0) % 1000)).ToArray();
        Activities = new SectorProductionActivity[Divisions];
        Drivers = new SimulationDriverRecord[Divisions][];
        for (var d = 0; d < Divisions; d++)
        {
            var id = new StableId(fixture.Divisions[d].Id);
            var sector = (EconomicSectorKind)(d % 7);
            var outputs = new ValuedCommodityQuantity[Goods];
            for (var g = 0; g < Goods; g++)
            {
                StartingStocks[d * Goods + g] = fixture.Divisions[d].Population * 20 + 100_000 + g;
                outputs[g] = new ValuedCommodityQuantity(new StableId($"SYNTH-GOOD-{g:D2}"),
                    "synthetic_unit", fixture.Divisions[d].Population / (g + 10) + 100, 1, 1);
            }
            Activities[d] = new SectorProductionActivity(new StableId($"SYNTH-ACT-{d}"), id, id, sector,
                outputs, Array.Empty<ValuedCommodityQuantity>(), 0, 0, 0, 1, 1, 0, 0);
            var driverCount = config.Name == "stress" ? 4 : 1;
            Drivers[d] = Enumerable.Range(0, driverCount).Select(k => new SimulationDriverRecord(
                new StableId($"SYNTH-DRV-{d}-{k}"), id, SimulationDriverOrigin.ExternalCondition,
                k == 0 ? SimulationDriverKind.Weather : SimulationDriverKind.NaturalDisaster,
                Period, Period, Period, 1,
                new[] { new SimulationDriverEffect(new StableId($"SYNTH-EFFECT-{k}"),
                    k % 2 == 0 ? SimulationEffectKind.ProductionMultiplier : SimulationEffectKind.LossRateAdditive,
                    k % 2 == 0 ? 0.85m : 0.03m, targetSector: sector) })).ToArray();
        }
    }

    // Counter-based deterministic randomness: unrelated work ordering never consumes a shared stream.
    public static uint Noise(int entity, int salt)
    {
        unchecked
        {
            uint x = (uint)entity * 747796405u + (uint)salt * 2891336453u + 1628u;
            x = ((x >> (int)((x >> 28) + 4)) ^ x) * 277803737u;
            return (x >> 22) ^ x;
        }
    }
}

internal sealed class JobState
{
    public string SourceSha256, Scenario;
    public int Month, Phase, Cursor;
    public bool Reverse;
    public long[] Population, Births, Deaths, Stocks, Produced, Consumed, Lost;
    public long[] CountyStocks, CountyDelta, HouseholdCash, TreasuryCash;
    public int[] Choices, NextMorale;
}

internal sealed class Job
{
    internal static readonly string[] StageNames = { "population_cohorts", "existing_production_and_resource_flows", "official_and_person_decisions", "county_rollup", "cross_county_transfers", "tax_and_local_audit" };
    private readonly World world;
    private readonly ProductionSettlementService production = new();
    public JobState State { get; }
    public int Phase => State.Phase;
    public int Cursor => State.Cursor;
    public bool Complete => Phase == StageNames.Length;

    public Job(World world, bool reverse)
    {
        this.world = world;
        var cohorts = world.Divisions * world.Config.CohortsPerDivision;
        var resources = world.Divisions * World.Goods;
        State = new JobState
        {
            SourceSha256 = world.Fixture.SourceSha256, Scenario = world.Config.Name, Month = World.Month, Reverse = reverse,
            Population = new long[cohorts], Births = new long[cohorts], Deaths = new long[cohorts],
            Stocks = new long[resources], Produced = new long[resources], Consumed = new long[resources], Lost = new long[resources],
            CountyStocks = new long[world.Counties * World.Goods], CountyDelta = new long[world.Counties * World.Goods],
            HouseholdCash = new long[world.Counties], TreasuryCash = new long[world.Counties],
            Choices = new int[world.Actors], NextMorale = new int[world.Actors],
        };
    }

    public Job(World world, JobState state)
    {
        this.world = world;
        Program.Require(state.Month == World.Month && state.SourceSha256 == world.Fixture.SourceSha256 && state.Scenario == world.Config.Name,
            "checkpoint month/content/scenario mismatch");
        Program.Require(state.Phase >= 0 && state.Phase < StageNames.Length && state.Cursor >= 0, "bad checkpoint cursor");
        State = state;
    }

    private int Count => Phase switch
    {
        0 => (State.Population.Length + 127) / 128,
        1 => world.Divisions,
        2 => (world.Actors + 31) / 32,
        3 => world.Counties,
        4 => world.Counties * world.Config.TransfersPerCounty,
        5 => world.Counties,
        _ => 0,
    };

    public void Drain() { while (!Complete) Step(); }

    public void Step()
    {
        Program.Require(!Complete, "completed job was executed again");
        var count = Count;
        // Transfers are intentionally canonical: no reordering dependent mutations.
        var index = State.Reverse && Phase != 4 ? count - 1 - Cursor : Cursor;
        switch (Phase)
        {
            case 0: Cohorts(index * 128, Math.Min((index + 1) * 128, State.Population.Length)); break;
            case 1: Produce(index); break;
            case 2: Actors(index * 32, Math.Min((index + 1) * 32, world.Actors)); break;
            case 3: Rollup(index); break;
            case 4: Transfer(index); break;
            case 5: TaxAndAudit(index); break;
        }
        State.Cursor++;
        if (State.Cursor == count) { State.Phase++; State.Cursor = 0; }
    }

    private void Cohorts(int start, int end)
    {
        var n = world.Config.CohortsPerDivision;
        for (var i = start; i < end; i++)
        {
            var total = world.Fixture.Divisions[i / n].Population;
            var opening = total / n + (i % n < total % n ? 1 : 0);
            State.Births[i] = opening / 600 + (World.Noise(i, 1) % 13 == 0 ? 1 : 0);
            State.Deaths[i] = Math.Min(opening, opening / (world.Config.Name == "stress" ? 250 : 800));
            State.Population[i] = opening + State.Births[i] - State.Deaths[i];
        }
    }

    private void Produce(int d)
    {
        var settled = production.Settle(world.Activities[d], world.Period, world.Drivers[d]);
        long population = 0;
        for (var c = 0; c < world.Config.CohortsPerDivision; c++) population += State.Population[d * world.Config.CohortsPerDivision + c];
        for (var g = 0; g < World.Goods; g++)
        {
            var i = d * World.Goods + g;
            var produced = (long)decimal.Floor(settled.Outputs[g].UsableQuantity);
            var available = world.StartingStocks[i] + produced;
            var consumed = Math.Min(available, population / (g + 2) + 1);
            var lost = (available - consumed) / 1000;
            State.Produced[i] = produced; State.Consumed[i] = consumed; State.Lost[i] = lost;
            State.Stocks[i] = available - consumed - lost;
        }
    }

    private void Actors(int start, int end)
    {
        for (var a = start; a < end; a++)
        {
            var d = (int)(World.Noise(a, 3) % (uint)world.Divisions);
            var risk = (int)(State.Consumed[d * World.Goods] % 1000);
            long best = long.MinValue;
            var choice = -1;
            for (var c = 0; c < world.Config.Candidates; c++)
            {
                long score = world.ActorMorale[a] * (c + 1L) - risk * (c % 3 + 1);
                for (var r = 0; r < world.Config.Relations; r++)
                {
                    var neighbor = (int)(World.Noise(a, r + 5) % (uint)world.Actors);
                    var weight = (long)(World.Noise(a, c * 41 + r + 100) % 21) - 10;
                    score += world.ActorMorale[neighbor] * weight;
                }
                if (score > best) { best = score; choice = c; }
            }
            State.Choices[a] = choice;
            State.NextMorale[a] = Math.Clamp(world.ActorMorale[a] + choice - risk / 100, 0, 1000);
        }
    }

    private void Rollup(int county)
    {
        foreach (var d in world.CountyDivisions[county])
            for (var g = 0; g < World.Goods; g++) State.CountyStocks[county * World.Goods + g] += State.Stocks[d * World.Goods + g];
    }

    private void Transfer(int id)
    {
        var from = id % world.Counties;
        var to = (from + 1 + (int)(World.Noise(id, 99) % (uint)(world.Counties - 1))) % world.Counties;
        var good = id % World.Goods;
        var debit = from * World.Goods + good;
        var credit = to * World.Goods + good;
        var amount = Math.Min(State.CountyStocks[debit], 1 + World.Noise(id, 66) % 10_000);
        // Both sides are part of one invisible staging task; publication happens only after closure.
        State.CountyStocks[debit] -= amount; State.CountyDelta[debit] -= amount;
        State.CountyStocks[credit] += amount; State.CountyDelta[credit] += amount;
    }

    private void TaxAndAudit(int county)
    {
        long population = world.CountyDivisions[county].Sum(d => world.Fixture.Divisions[d].Population);
        var openingCash = population * 100;
        var tax = openingCash / (world.Config.Name == "stress" ? 12 : 20);
        State.HouseholdCash[county] = openingCash - tax;
        State.TreasuryCash[county] = tax;
        Program.Require(State.HouseholdCash[county] + State.TreasuryCash[county] == openingCash, "money conservation failure");
        foreach (var d in world.CountyDivisions[county])
            for (var g = 0; g < World.Goods; g++)
            {
                var i = d * World.Goods + g;
                Program.Require(State.Stocks[i] >= 0 && State.Stocks[i] == world.StartingStocks[i] + State.Produced[i] - State.Consumed[i] - State.Lost[i], "resource equation failure");
            }
    }

    public void Validate()
    {
        Program.Require(Complete, "incomplete month cannot be audited/published");
        Program.Require(State.Population.Sum() == world.Fixture.Divisions.Sum(d => d.Population) + State.Births.Sum() - State.Deaths.Sum(), "population not conserved");
        for (var c = 0; c < world.Counties; c++)
        {
            for (var g = 0; g < World.Goods; g++)
            {
                var sum = world.CountyDivisions[c].Sum(d => State.Stocks[d * World.Goods + g]);
                var i = c * World.Goods + g;
                Program.Require(State.CountyStocks[i] >= 0 && State.CountyStocks[i] == sum + State.CountyDelta[i], "county rollup mismatch");
            }
            var cash = world.CountyDivisions[c].Sum(d => world.Fixture.Divisions[d].Population) * 100;
            Program.Require(cash == State.HouseholdCash[c] + State.TreasuryCash[c], "tax created money");
        }
        for (var g = 0; g < World.Goods; g++)
        {
            long delta = 0;
            for (var c = 0; c < world.Counties; c++) delta += State.CountyDelta[c * World.Goods + g];
            Program.Require(delta == 0, "cross-county transfer not balanced");
        }
        Program.Require(State.Choices.All(c => c >= 0 && c < world.Config.Candidates), "unsettled actor");
    }

    public string Digest()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var values in new[] { State.Population, State.Births, State.Deaths, State.Stocks, State.Produced,
            State.Consumed, State.Lost, State.CountyStocks, State.CountyDelta, State.HouseholdCash, State.TreasuryCash })
            hash.AppendData(MemoryMarshal.AsBytes(values.AsSpan()));
        hash.AppendData(MemoryMarshal.AsBytes(State.Choices.AsSpan()));
        hash.AppendData(MemoryMarshal.AsBytes(State.NextMorale.AsSpan()));
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

internal sealed class PublicationStore
{
    private readonly World world;
    private readonly HashSet<string> commands = new();
    private long reserved;
    private Job committed;
    public PublicationStore(World world) { this.world = world; }
    // This synthetic ledger has a known 10% reservation-safe lower bound after this month's tax.
    // A real game must reserve its actual obligations; this is not a generic balance rule.
    private long ReservedSafeCash => world.CountyDivisions[0].Sum(d => world.Fixture.Divisions[d].Population) * 10;
    public long AvailableCash => committed == null ? ReservedSafeCash - reserved : committed.State.HouseholdCash[0] - reserved;
    public bool TrySeptemberSpend(string id, long amount)
    {
        if (commands.Contains(id) || amount < 0 || amount > AvailableCash) return false;
        commands.Add(id); reserved += amount; return true;
    }
    public bool Publish(Job result)
    {
        Program.Require(result.Complete, "cannot publish unfinished month");
        if (committed != null) return false;
        Program.Require(result.State.Month == World.Month && result.State.HouseholdCash[0] >= reserved, "publication/reservation conflict");
        committed = result; // Atomic publication of the already-closed in-memory generation, no database I/O.
        return true;
    }
}

internal sealed class BenchmarkResult
{
    public string Runtime, Architecture, FixtureHash;
    public DateTime StartedUtc, FinishedUtc;
    public int ExistingYearProbeMonths;
    public List<ScenarioResult> Scenarios = new();
}

internal sealed class ScenarioResult
{
    public string Name, ResultSha256;
    public int Counties, Divisions, Cohorts, Officials, OtherPeople, ResourceSlots, CandidatesPerActor, RelationsPerCandidate, Transfers;
    public long Population;
    public double SetupMs, CheckpointMiB;
    public CountySample SampleCounty;
    public List<double> CheckpointRoundtripMs = new();
    public List<RunMetrics> Runs = new();
    public List<string> Checks = new();
}

internal sealed class CountySample
{
    public string CountyId, CountyName, Resource;
    public long Opening, Produced, Consumed, Lost, NetTransfer, Closing;

    public static CountySample Capture(World world, Job job, int county, int good)
    {
        return new CountySample
        {
            CountyId = world.Fixture.CountyIds[county],
            CountyName = world.Fixture.CountyNames[county],
            Resource = $"SYNTH-GOOD-{good:D2}",
            Opening = world.CountyDivisions[county].Sum(d => world.StartingStocks[d * World.Goods + good]),
            Produced = world.CountyDivisions[county].Sum(d => job.State.Produced[d * World.Goods + good]),
            Consumed = world.CountyDivisions[county].Sum(d => job.State.Consumed[d * World.Goods + good]),
            Lost = world.CountyDivisions[county].Sum(d => job.State.Lost[d * World.Goods + good]),
            NetTransfer = job.State.CountyDelta[county * World.Goods + good],
            Closing = job.State.CountyStocks[county * World.Goods + good],
        };
    }
}

internal sealed class RunMetrics
{
    public string Mode;
    public double PreparationMs, WorkMs, WallMs, P95SliceMs, MaxSliceMs, MaxWorkItemMs, AllocatedMiB, AuditMs, HashMs;
    public int Slices, Over4MsSlices, Over16_67MsSlices, Gen0Collections;
    public Dictionary<string, double> StageWorkMs;
}
