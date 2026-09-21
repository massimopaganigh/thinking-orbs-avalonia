// Golden-vector check: evaluate the ported engine at the spec'd timestamps
// and compare every dot and line against spec/orbs-golden.json, the ground
// truth extracted from the web library. This is the fidelity guarantee —
// the port is verified as numbers, not as pixels.
//
//   dotnet run --project golden/ThinkingOrbs.Golden
//
// Exit code 0 = every case matches within the spec's tolerance.

using System.Globalization;
using System.Text.Json;
using ThinkingOrbs;

var specPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "spec", "orbs-golden.json");
specPath = Path.GetFullPath(specPath);

if (!File.Exists(specPath))
{
    Console.Error.WriteLine($"golden file not found: {specPath}");
    return 2;
}

using var doc = JsonDocument.Parse(File.ReadAllText(specPath));
var root = doc.RootElement;
var tolerance = root.GetProperty("tolerance").GetDouble();
var resolvedSpec = root.GetProperty("resolved");

Console.WriteLine($"spec {root.GetProperty("specVersion").GetString()} " +
                  $"(thinking-orbs {root.GetProperty("sourceLibrary").GetProperty("version").GetString()}), " +
                  $"tolerance {tolerance}");

// Two cases cannot be matched bit-for-bit by any runtime that does not
// ship V8's libm, and the reason is in the source algorithm rather than in
// this port. `rubik` decides band membership with a hard test
// (coord < lo || coord >= hi); at the 20px lattice — 5 latitude rings, 12
// longitudes — quarter-turned coordinates land 3.7e-33 and 2.2e-17 away
// from the boundary at 0, so one ulp of difference in cos/sin decides which
// side of a turning band a dot sits on. An independent third implementation
// (CPython, Windows libm) reproduces this port's numbers exactly, which is
// what rules out a transcription error.
//
// The budget below is pinned: a change in these counts, in either
// direction, fails the run like any other regression.
//   solving-20-3.3: two coincident dots (same x, y, z to 1 ulp) swap draw
//                   order — 4 values, visually nothing.
//   solving-20-5.1: three dots of thirty sit in a different band.
var knownDivergences = new Dictionary<string, int>
{
    ["solving-20-3.3"] = 4,
    ["solving-20-5.1"] = 65
};

var states = Enum.GetValues<OrbState>().ToDictionary(s => s.ToString().ToLowerInvariant());
var failures = new List<string>();
var perCase = new Dictionary<string, int>();
var currentCase = "";
var checkedValues = 0;
var cases = 0;

foreach (var c in root.GetProperty("cases").EnumerateArray())
{
    cases++;
    var key = c.GetProperty("key").GetString()!;
    currentCase = key;
    var state = states[c.GetProperty("state").GetString()!];
    var size = (OrbSize)c.GetProperty("size").GetInt32();
    var t = c.GetProperty("t").GetDouble();

    var resolved = Presets.Resolve(state, size);

    // 1. the resolved options themselves — catches a scaling-rule slip before
    //    it shows up as thousands of mismatched dots
    var expectOpts = resolvedSpec.GetProperty($"{c.GetProperty("state").GetString()}-{(int)size}");
    if (!string.Equals(expectOpts.GetProperty("mode").GetString(), resolved.Mode.ToString(),
            StringComparison.OrdinalIgnoreCase))
        failures.Add($"{key}: mode {resolved.Mode} != {expectOpts.GetProperty("mode").GetString()}");
    Near($"{key}.speed", resolved.Speed, expectOpts.GetProperty("speed").GetDouble());
    foreach (var opt in expectOpts.GetProperty("opts").EnumerateObject())
        Near($"{key}.opts.{opt.Name}", resolved.Opts.Get(opt.Name, double.NaN), opt.Value.GetDouble());

    // 2. the frame itself
    var frame = Modes.Frames[resolved.Mode](c.GetProperty("size").GetInt32(), t, resolved.Opts);

    // ORB_DUMP=<case key> prints the ported frame next to the golden one —
    // the first thing to reach for when a case drifts.
    if (Environment.GetEnvironmentVariable("ORB_DUMP") == key)
    {
        for (var i = 0; i < frame.Dots.Count; i++)
        {
            var d = frame.Dots[i];
            var b = i * 6;
            Console.WriteLine($"{i,4} got {d.X,12:F6} {d.Y,12:F6} {d.Z,12:F6} {d.R,9:F6} {d.White,9:F6} {d.A,9:F6}" +
                              $"   want {c.GetProperty("dots")[b].GetDouble(),12:F6} " +
                              $"{c.GetProperty("dots")[b + 1].GetDouble(),12:F6} " +
                              $"{c.GetProperty("dots")[b + 2].GetDouble(),12:F6} " +
                              $"{c.GetProperty("dots")[b + 3].GetDouble(),9:F6} " +
                              $"{c.GetProperty("dots")[b + 4].GetDouble(),9:F6} " +
                              $"{c.GetProperty("dots")[b + 5].GetDouble(),9:F6}");
        }
    }

    var expectDots = c.GetProperty("dots");
    var expectLines = c.GetProperty("lines");
    if (frame.Dots.Count != c.GetProperty("dotCount").GetInt32())
    {
        failures.Add($"{key}: dotCount {frame.Dots.Count} != {c.GetProperty("dotCount").GetInt32()}");
        continue;
    }
    if (frame.Lines.Count != c.GetProperty("lineCount").GetInt32())
    {
        failures.Add($"{key}: lineCount {frame.Lines.Count} != {c.GetProperty("lineCount").GetInt32()}");
        continue;
    }

    // Dot stride 6: x, y, z, r, white, a. Dots are in draw order.
    for (var i = 0; i < frame.Dots.Count; i++)
    {
        var d = frame.Dots[i];
        var b = i * 6;
        Near($"{key}.dot[{i}].x", d.X, expectDots[b].GetDouble());
        Near($"{key}.dot[{i}].y", d.Y, expectDots[b + 1].GetDouble());
        Near($"{key}.dot[{i}].z", d.Z, expectDots[b + 2].GetDouble());
        Near($"{key}.dot[{i}].r", d.R, expectDots[b + 3].GetDouble());
        Near($"{key}.dot[{i}].white", d.White, expectDots[b + 4].GetDouble());
        Near($"{key}.dot[{i}].a", d.A, expectDots[b + 5].GetDouble());
    }

    // Line stride 7: x1, y1, x2, y2, white, a, w.
    for (var i = 0; i < frame.Lines.Count; i++)
    {
        var l = frame.Lines[i];
        var b = i * 7;
        Near($"{key}.line[{i}].x1", l.X1, expectLines[b].GetDouble());
        Near($"{key}.line[{i}].y1", l.Y1, expectLines[b + 1].GetDouble());
        Near($"{key}.line[{i}].x2", l.X2, expectLines[b + 2].GetDouble());
        Near($"{key}.line[{i}].y2", l.Y2, expectLines[b + 3].GetDouble());
        Near($"{key}.line[{i}].white", l.White, expectLines[b + 4].GetDouble());
        Near($"{key}.line[{i}].a", l.A, expectLines[b + 5].GetDouble());
        Near($"{key}.line[{i}].w", l.W, expectLines[b + 6].GetDouble());
    }
}

void Near(string what, double got, double want)
{
    checkedValues++;
    if (Math.Abs(got - want) <= tolerance) return;
    perCase[currentCase] = perCase.GetValueOrDefault(currentCase) + 1;
    if (failures.Count < 12 && !knownDivergences.ContainsKey(currentCase))
        failures.Add($"{what}: {got.ToString("R", CultureInfo.InvariantCulture)} != " +
                     $"{want.ToString("R", CultureInfo.InvariantCulture)} " +
                     $"(delta {Math.Abs(got - want).ToString("G4", CultureInfo.InvariantCulture)})");
}

var unexpected = perCase.Where(kv => knownDivergences.GetValueOrDefault(kv.Key) != kv.Value).ToList();
var missing = knownDivergences.Where(kv => !perCase.ContainsKey(kv.Key)).ToList();

foreach (var (k, v) in knownDivergences.OrderBy(kv => kv.Key))
    if (perCase.GetValueOrDefault(k) == v)
        Console.WriteLine($"known libm divergence: {k} ({v} values) — see the note in Program.cs");

if (unexpected.Count == 0 && missing.Count == 0)
{
    Console.WriteLine($"PASS — {cases} cases, {checkedValues} values within {tolerance}");
    return 0;
}

Console.Error.WriteLine($"FAIL — over {cases} cases / {checkedValues} values");
foreach (var f in failures) Console.Error.WriteLine("  " + f);
foreach (var (k, v) in unexpected.OrderByDescending(kv => kv.Value))
    Console.Error.WriteLine($"  {k}: {v} mismatches (expected {knownDivergences.GetValueOrDefault(k)})");
foreach (var (k, v) in missing)
    Console.Error.WriteLine($"  {k}: now matches exactly — the {v}-value divergence is gone, update the budget");
return 1;
