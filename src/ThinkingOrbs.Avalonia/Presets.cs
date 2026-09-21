// Density profiles, the multiplier machinery that scales them, and the
// shipped tunings. Port of `src/engine/profiles.ts` + `src/presets.ts`.

using System.Collections.Concurrent;

namespace ThinkingOrbs;

/// <summary>The nine verbs an agent can be doing, each a distinct animation.</summary>
public enum OrbState
{
    /// <summary>Particles on tilted orbits.</summary>
    Working,
    /// <summary>A scan meridian sweeps a dotted globe.</summary>
    Searching,
    /// <summary>Bands scramble in quarter turns, then click back.</summary>
    Solving,
    /// <summary>A waveform rolls through latitude rings.</summary>
    Listening,
    /// <summary>A constellation wires itself, packets running the edges.</summary>
    Connecting,
    /// <summary>Three strands plait around the sphere.</summary>
    Weaving,
    /// <summary>An undulating multi-band sash.</summary>
    Composing,
    /// <summary>A face-on ring slowly morphing.</summary>
    Breathing,
    /// <summary>A dotted outline morphs circle to triangle to square.</summary>
    Shaping
}

/// <summary>
/// The two tuned size presets, in device-independent pixels. Separate
/// designs, not a scale factor: each carries its own dot count, dot size
/// and speed tuning.
/// </summary>
public enum OrbSize
{
    /// <summary>Chat-avatar scale.</summary>
    Px64 = 64,
    /// <summary>Inline-text scale.</summary>
    Px20 = 20
}

public enum ModeKey { Orbits, Globe, Rubik, Wave, Web, Braid, Ribbon, Ring, Morph }

public sealed record Resolved(ModeKey Mode, double Speed, ModeOpts Opts);

public static class Presets
{
    public static readonly IReadOnlyDictionary<OrbState, ModeKey> StateToMode = new Dictionary<OrbState, ModeKey>
    {
        [OrbState.Working] = ModeKey.Orbits,
        [OrbState.Searching] = ModeKey.Globe,
        [OrbState.Solving] = ModeKey.Rubik,
        [OrbState.Listening] = ModeKey.Wave,
        [OrbState.Connecting] = ModeKey.Web,
        [OrbState.Weaving] = ModeKey.Braid,
        [OrbState.Composing] = ModeKey.Ribbon,
        [OrbState.Breathing] = ModeKey.Ring,
        [OrbState.Shaping] = ModeKey.Morph
    };

    /// <summary>Per-state accessibility label, matching the web component.</summary>
    public static readonly IReadOnlyDictionary<OrbState, string> Labels = new Dictionary<OrbState, string>
    {
        [OrbState.Working] = "Working…",
        [OrbState.Searching] = "Searching…",
        [OrbState.Solving] = "Solving…",
        [OrbState.Listening] = "Listening…",
        [OrbState.Connecting] = "Connecting…",
        [OrbState.Weaving] = "Weaving…",
        [OrbState.Composing] = "Composing…",
        [OrbState.Breathing] = "Thinking…",
        [OrbState.Shaping] = "Shaping…"
    };

    // 2-D lattices (rings x dots-per-ring) come in pairs — each side takes
    // sqrt(scale) so the TOTAL dot count scales by `scale`; flat lists scale
    // linearly. `iconD` sets the morph outline's sampling density.
    private static readonly (string A, string B)[] CountPairs =
        [("latRings", "lonDensity"), ("rings", "lonDensity"), ("lanes", "segs")];

    private static readonly string[] CountKeys = ["orbitN", "ghostN", "nodeN", "strandN", "signals"];
    private static readonly string[] IconDensityKeys = ["iconD"];

    // Every key that sets a dot's rendered radius — scaling all of them keeps
    // a dot's near/far falloff intact while shrinking or growing the mark.
    private static readonly string[] RadiusKeys =
        ["rBase", "rDepth", "rActive", "rDot", "ghostR", "partR", "partRDepth", "nodeR", "nodeRDepth"];

    public static ModeOpts ScaleCounts(ModeOpts opts, double scale)
    {
        var o = new ModeOpts(opts);
        var done = new HashSet<string>();
        var rt = Math.Sqrt(scale);
        foreach (var (a, b) in CountPairs)
        {
            if (o.ContainsKey(a) && o.ContainsKey(b) && !done.Contains(a) && !done.Contains(b))
            {
                o[a] = Math.Max(2, Engine.JsRound(o[a] * rt));
                o[b] = Math.Max(2, Engine.JsRound(o[b] * rt));
                done.Add(a);
                done.Add(b);
            }
        }
        foreach (var k in CountKeys)
        {
            // 0 means the mode opted out of that layer entirely (ring has no
            // ghost sphere) — scaling must not resurrect it as a stray dot.
            if (o.TryGetValue(k, out var v) && v != 0 && !done.Contains(k))
                o[k] = Math.Max(1, Engine.JsRound(v * scale));
        }
        foreach (var k in IconDensityKeys)
            if (o.TryGetValue(k, out var v)) o[k] = Math.Max(0.02, v * scale);
        return o;
    }

    public static ModeOpts ScaleRadii(ModeOpts opts, double scale)
    {
        var o = new ModeOpts(opts);
        foreach (var k in RadiusKeys)
            if (o.TryGetValue(k, out var v)) o[k] = v * scale;
        // remember the multiplier itself — spacing-derived radii (the morph
        // outline) use it, since they aren't based on any single radius key
        o["rSizeMul"] = o.Get("rSizeMul", 1) * scale;
        return o;
    }

    /// <summary>Base (fine) profiles per mode, before preset multipliers.</summary>
    public static readonly IReadOnlyDictionary<ModeKey, ModeOpts> BaseProfiles = new Dictionary<ModeKey, ModeOpts>
    {
        [ModeKey.Globe] = new()
        {
            ["latRings"] = 17, ["lonDensity"] = 44, ["rBase"] = 0.6, ["rDepth"] = 1.7, ["rBoost"] = 1.0,
            ["inkFar"] = 0.62, ["inkSpan"] = 0.54, ["rsPow"] = 0.6, ["rMin"] = 0.3
        },
        [ModeKey.Orbits] = new()
        {
            ["orbitN"] = 12, ["ghostN"] = 40, ["ghostR"] = 0.9, ["ghostA"] = 0.5, ["particles"] = 3,
            ["partR"] = 1.2, ["partRDepth"] = 1.6, ["rsPow"] = 0.6, ["rMin"] = 0.3
        },
        [ModeKey.Rubik] = new()
        {
            ["latRings"] = 15, ["lonDensity"] = 40, ["moveCount"] = 14, ["rBase"] = 0.6, ["rDepth"] = 1.7,
            ["rActive"] = 0.3, ["inkFar"] = 0.62, ["inkSpan"] = 0.54, ["rsPow"] = 0.6, ["rMin"] = 0.3
        },
        [ModeKey.Wave] = new()
        {
            ["rings"] = 15, ["lonDensity"] = 40, ["rBase"] = 0.6, ["rDepth"] = 1.7, ["rsPow"] = 0.6, ["rMin"] = 0.3
        },
        [ModeKey.Web] = new()
        {
            ["nodeN"] = 30, ["thr"] = 0.72, ["signals"] = 5, ["nodeR"] = 1.4, ["nodeRDepth"] = 1.8,
            ["lineW"] = 0.8, ["rsPow"] = 0.6, ["rMin"] = 0.3
        },
        [ModeKey.Braid] = new()
        {
            ["strandN"] = 52, ["turns"] = 3.0, ["ghostN"] = 150, ["rBase"] = 1.2, ["rDepth"] = 1.8,
            ["rsPow"] = 0.6, ["rMin"] = 0.3
        },
        [ModeKey.Ribbon] = new()
        {
            ["lanes"] = 5, ["segs"] = 88, ["ghostN"] = 150, ["rBase"] = 1.1, ["rDepth"] = 1.7,
            ["rsPow"] = 0.6, ["rMin"] = 0.3
        },
        // ring shares ribbon's painter; faceOn cancels the camera tilt and
        // moves the undulation onto the radius, and there is no ghost sphere
        [ModeKey.Ring] = new()
        {
            ["lanes"] = 5, ["segs"] = 88, ["ghostN"] = 0, ["faceOn"] = 1, ["rBase"] = 1.1, ["rDepth"] = 1.7,
            ["rsPow"] = 0.6, ["rMin"] = 0.3
        },
        [ModeKey.Morph] = new() { ["rDot"] = 0.021, ["iconD"] = 1, ["rMin"] = 0.25 }
    };

    private sealed record Preset(double Speed, double Count, double Size, ModeOpts? Extra = null);

    private static readonly Dictionary<(ModeKey, OrbSize), Preset> Table = new()
    {
        [(ModeKey.Orbits, OrbSize.Px64)] = new(1.885, 1, 1),
        [(ModeKey.Orbits, OrbSize.Px20)] = new(3.9, 0.238, 2.4),
        [(ModeKey.Globe, OrbSize.Px64)] = new(2.015, 0.42, 1.15, new() { ["scanMul"] = 4.08, ["dimBase"] = 0.45 }),
        [(ModeKey.Globe, OrbSize.Px20)] = new(2.665, 0.105, 1.75, new() { ["scanMul"] = 4.335, ["dimBase"] = 0.45 }),
        [(ModeKey.Rubik, OrbSize.Px64)] = new(1.82, 0.35, 1.05),
        [(ModeKey.Rubik, OrbSize.Px20)] = new(1.95, 0.088, 1.9),
        [(ModeKey.Wave, OrbSize.Px64)] = new(4.388, 0.341, 1),
        [(ModeKey.Wave, OrbSize.Px20)] = new(3.998, 0.105, 1.6),
        [(ModeKey.Web, OrbSize.Px64)] = new(3.315, 1.35, 0.95),
        [(ModeKey.Web, OrbSize.Px20)] = new(6.63, 0.25, 1.52),
        [(ModeKey.Braid, OrbSize.Px64)] = new(1.625, 0.5, 1),
        [(ModeKey.Braid, OrbSize.Px20)] = new(2.75, 0.1125, 1.36),
        [(ModeKey.Ribbon, OrbSize.Px64)] = new(2.34, 0.25, 0.85, new() { ["spin"] = 0, ["bandMul"] = 3.9, ["wobMul"] = 1 }),
        [(ModeKey.Ribbon, OrbSize.Px20)] = new(3.12, 0.051, 1.073, new() { ["spin"] = 0, ["bandMul"] = 4.94, ["wobMul"] = 1 }),
        [(ModeKey.Ring, OrbSize.Px64)] = new(3.24, 0.25, 0.956, new() { ["spin"] = 0, ["bandMul"] = 3.627, ["wobMul"] = 0.368 }),
        [(ModeKey.Ring, OrbSize.Px20)] = new(3.78, 0.028, 1.622, new() { ["spin"] = 0, ["bandMul"] = 3.968, ["wobMul"] = 0.565 }),
        [(ModeKey.Morph, OrbSize.Px64)] = new(2.405, 0.702, 0.395, new() { ["spread"] = 1.45 }),
        [(ModeKey.Morph, OrbSize.Px20)] = new(2.08, 0.53, 1.011, new() { ["spread"] = 1.45 })
    };

    private static readonly ConcurrentDictionary<(OrbState, OrbSize), Resolved> Cache = new();

    /// <summary>Resolve a (state, size) pair to its mode + fully-scaled draw options.</summary>
    public static Resolved Resolve(OrbState state, OrbSize size) => Cache.GetOrAdd((state, size), k =>
    {
        var mode = StateToMode[k.Item1];
        var preset = Table[(mode, k.Item2)];
        var opts = new ModeOpts(BaseProfiles[mode]);
        if (preset.Count != 1) opts = ScaleCounts(opts, preset.Count);
        if (preset.Size != 1) opts = ScaleRadii(opts, preset.Size);
        if (preset.Extra != null)
            foreach (var (key, v) in preset.Extra) opts[key] = v;
        return new Resolved(mode, preset.Speed, opts);
    });
}
