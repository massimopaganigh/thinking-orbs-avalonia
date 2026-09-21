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

// One case cannot be matched by any runtime that does not ship V8's libm,
// and the reason is in the source algorithm rather than in this port.
// `rubik` decides band membership with a hard test
// (coord < lo || coord >= hi); at the 20px lattice — 5 latitude rings, 12
// longitudes — quarter-turned coordinates land 3.7e-33 and 2.2e-17 away
// from the boundary at 0, so one ulp of difference in cos/sin decides which
// side of a turning band a dot sits on. An independent third implementation
// (CPython, Windows libm) reproduces this port's numbers exactly, which is
// what rules out a transcription error.
//
// Only these cases may differ at all. Anywhere else, a single unmatched dot
// fails the run on every platform.
string[] driftAllowed = ["solving-20-3.3", "solving-20-5.1"];

// How many dots actually go unmatched, per platform: .NET's Math.Sin/Cos go
// to the C runtime, so glibc, Apple's libm and the UCRT need not agree with
// each other either. A recorded platform is pinned — more OR fewer fails the
// run. An unrecorded one passes and prints the line to paste in, so its
// first CI run reports the numbers instead of this file guessing them.
var divergenceBudgets = new Dictionary<string, Dictionary<string, int>>
{
    // three dots of thirty sit in a different band, mid-scramble
    ["windows"] = new() { ["solving-20-5.1"] = 3 }
};

var platform = OperatingSystem.IsWindows() ? "windows"
    : OperatingSystem.IsMacOS() ? "macos"
    : OperatingSystem.IsLinux() ? "linux"
    : "other";
var recorded = divergenceBudgets.TryGetValue(platform, out var budget) ? budget : null;

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

    // Dot stride 6: x, y, z, r, white, a.
    //
    // The golden file stores one particular draw order, but only part of it
    // is specified behaviour. What the painter promises is "far to near";
    // between dots at the same depth — `ring` is face-on, so its whole band
    // sits at one z — the order is whatever the sort happened to leave, and
    // one ulp of libm difference reshuffles it without moving a pixel.
    // Pinning the exact sequence would therefore fail on a platform whose
    // libm rounds differently while drawing an identical picture. So check
    // the two things that are real: the frame is z-sorted, and it holds the
    // same dots.
    var gotDots = new List<double[]>(frame.Dots.Count);
    var wantDots = new List<double[]>(frame.Dots.Count);
    for (var i = 0; i < frame.Dots.Count; i++)
    {
        var d = frame.Dots[i];
        var b = i * 6;
        gotDots.Add([d.X, d.Y, d.Z, d.R, d.White, d.A]);
        wantDots.Add([
            expectDots[b].GetDouble(), expectDots[b + 1].GetDouble(), expectDots[b + 2].GetDouble(),
            expectDots[b + 3].GetDouble(), expectDots[b + 4].GetDouble(), expectDots[b + 5].GetDouble()
        ]);
    }

    for (var i = 1; i < gotDots.Count; i++)
        if (gotDots[i][2] < gotDots[i - 1][2])
            failures.Add($"{key}: not z-sorted at dot[{i}] ({gotDots[i][2]} after {gotDots[i - 1][2]})");

    // Matched by value, not by index or sort key: the golden stores 6
    // decimals and the port has full precision, so any comparator ordering on
    // a raw coordinate pairs them up wrong the moment two dots agree to 6
    // decimals. ponytail: O(n^2) over at most 600 dots, ~13M compares for the
    // whole run. Bucket by z if it ever matters.
    var pool = new List<int>(gotDots.Count);
    for (var i = 0; i < gotDots.Count; i++) pool.Add(i);

    foreach (var w in wantDots)
    {
        checkedValues += 6;
        var hit = pool.FindIndex(j => Fits(gotDots[j], w));
        if (hit >= 0)
        {
            pool.RemoveAt(hit);
            continue;
        }
        perCase[key] = perCase.GetValueOrDefault(key) + 1;
        if (failures.Count < 12 && !driftAllowed.Contains(key))
            failures.Add($"{key}: no dot matches x={w[0]:F6} y={w[1]:F6} z={w[2]:F6} " +
                         $"r={w[3]:F6} white={w[4]:F6} a={w[5]:F6}");
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

/// All six values within tolerance: the same dot, painted the same way.
bool Fits(double[] a, double[] b)
{
    for (var k = 0; k < 6; k++)
        if (Math.Abs(a[k] - b[k]) > tolerance) return false;
    return true;
}

void Near(string what, double got, double want)
{
    checkedValues++;
    if (Math.Abs(got - want) <= tolerance) return;
    perCase[currentCase] = perCase.GetValueOrDefault(currentCase) + 1;
    if (failures.Count < 12 && !driftAllowed.Contains(currentCase))
        failures.Add($"{what}: {got.ToString("R", CultureInfo.InvariantCulture)} != " +
                     $"{want.ToString("R", CultureInfo.InvariantCulture)} " +
                     $"(delta {Math.Abs(got - want).ToString("G4", CultureInfo.InvariantCulture)})");
}

// Outside the drift-allowed set, any mismatch is a plain failure. Inside
// it, a recorded platform is pinned to its exact count.
var unexpected = perCase
    .Where(kv => !driftAllowed.Contains(kv.Key) ||
                 (recorded != null && recorded.GetValueOrDefault(kv.Key) != kv.Value))
    .ToList();
var missing = recorded?.Where(kv => perCase.GetValueOrDefault(kv.Key) != kv.Value).ToList() ?? [];

foreach (var k in driftAllowed.Order())
{
    var seen = perCase.GetValueOrDefault(k);
    if (seen != 0)
        Console.WriteLine($"known libm divergence on {platform}: {k} " +
                          $"({seen} unmatched dots) — see the note in Program.cs");
}

if (recorded == null)
    Console.WriteLine($"no recorded budget for {platform}; to pin it, add to divergenceBudgets:" +
                      Environment.NewLine +
                      $"    [\"{platform}\"] = new() {{ " +
                      string.Join(", ", driftAllowed.Order()
                          .Where(k => perCase.GetValueOrDefault(k) != 0)
                          .Select(k => $"[\"{k}\"] = {perCase[k]}")) + " },");

if (unexpected.Count == 0 && missing.Count == 0)
{
    Console.WriteLine($"PASS — {cases} cases, {checkedValues} values within {tolerance}");
    return 0;
}

Console.Error.WriteLine($"FAIL — over {cases} cases / {checkedValues} values");
foreach (var f in failures) Console.Error.WriteLine("  " + f);
foreach (var (k, v) in unexpected.OrderByDescending(kv => kv.Value))
    Console.Error.WriteLine($"  {k}: {v} unmatched dots (expected {recorded?.GetValueOrDefault(k) ?? 0})");
foreach (var (k, v) in missing.Where(kv => !unexpected.Any(u => u.Key == kv.Key)))
    Console.Error.WriteLine($"  {k}: {perCase.GetValueOrDefault(k)} unmatched dots, budget says {v} — update it");
return 1;
