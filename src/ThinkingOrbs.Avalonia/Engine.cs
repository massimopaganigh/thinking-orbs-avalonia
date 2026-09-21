// Shared primitives for the dotted 3D thought-orbs. Direct port of
// thinking-orbs `src/engine/core.ts` (MIT, Jakub Antalik).
//
// Every formula is transcribed verbatim; the only deliberate divergences
// are the two places where JavaScript and .NET disagree by default:
//   - Math.round() rounds half toward +infinity in JS, banker's in .NET
//     -> JsRound().
//   - Array.prototype.sort is stable since ES2019, List.Sort is not
//     -> OrderBy in FinalizeFrame (morph puts every dot at z = 0, so
//        draw order depends on that stability).

namespace ThinkingOrbs;

/// <summary>One projected dot. <see cref="White"/> is paper-theme ink in [0,1].</summary>
public struct Dot
{
    public double X, Y, Z, R, White;
    /// <summary>Alpha; 1 when the mode does not set one.</summary>
    public double A;
}

/// <summary>A stroked edge between two projected points (the `connecting` web).</summary>
public struct Line
{
    public double X1, Y1, X2, Y2, White, A, W;
}

/// <summary>
/// One rendered instant: a complete, final set of draw instructions.
/// <see cref="Dots"/> is already z-sorted into draw order and radius-clamped;
/// <see cref="Lines"/> are drawn first.
/// </summary>
public sealed class OrbFrame
{
    public required IReadOnlyList<Dot> Dots { get; init; }
    public required IReadOnlyList<Line> Lines { get; init; }
}

/// <summary>Shared spin + tilt + orthographic projection.</summary>
public delegate void Projector(double x, double y, double z, out double px, out double py, out double pz);

public static class Engine
{
    /// <summary>JavaScript `Math.round`: halves go toward +infinity.</summary>
    public static double JsRound(double x) => Math.Floor(x + 0.5);

    public static double Lerp(double a, double b, double f) => a + (b - a) * f;

    public static double Frac(double x) => x - Math.Floor(x);

    /// <summary>Deterministic hash in [0, 1).</summary>
    public static double HashD(double a, double b)
    {
        var h = Math.Sin(a * 12.9898 + b * 78.233) * 43758.5453;
        return h - Math.Floor(h);
    }

    /// <summary>Value noise on a 2D lattice — smooth, deterministic, cheap.</summary>
    public static double VNoise(double x, double y)
    {
        var xi = Math.Floor(x);
        var yi = Math.Floor(y);
        var fx = x - xi;
        var fy = y - yi;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        var a = HashD(xi, yi);
        var b = HashD(xi + 1, yi);
        var c = HashD(xi, yi + 1);
        var d = HashD(xi + 1, yi + 1);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    /// <summary>Stable directions on a unit sphere (Fibonacci lattice).</summary>
    public static (double X, double Y, double Z) FibDir(int i, int n)
    {
        var golden = Math.PI * (3 - Math.Sqrt(5));
        var y = 1 - 2 * (i + 0.5) / n;
        var rad = Math.Sqrt(1 - y * y);
        var a = i * golden;
        return (rad * Math.Cos(a), y, rad * Math.Sin(a));
    }

    /// <summary>Shortest signed angular distance, wrapped to (-pi, pi].</summary>
    public static double AngleDelta(double a, double b) => Math.Atan2(Math.Sin(a - b), Math.Cos(a - b));

    public static Projector MakeProj(double yaw, double tilt, double cx, double cy, double scale)
    {
        var st = Math.Sin(tilt);
        var ct = Math.Cos(tilt);
        var sy = Math.Sin(yaw);
        var cyw = Math.Cos(yaw);
        return (double x, double y, double z, out double px, out double py, out double pz) =>
        {
            var x1 = x * cyw + z * sy;
            var z1 = -x * sy + z * cyw;
            var y1 = y * ct - z1 * st;
            var z2 = y * st + z1 * ct;
            px = cx + x1 * scale;
            py = cy - y1 * scale;
            pz = z2;
        };
    }

    /// <summary>
    /// Turn raw mode output into a finished frame: drop invisible marks, clamp
    /// radii to the mode's floor, and z-sort far-to-near into draw order.
    /// </summary>
    public static OrbFrame FinalizeFrame(List<Dot> dots, List<Line> lines, double rMin = 0.3)
    {
        var visible = new List<Dot>(dots.Count);
        foreach (var d in dots)
        {
            if (d.A < 0.02) continue;
            var c = d;
            c.R = Math.Max(rMin, c.R);
            visible.Add(c);
        }
        // OrderBy is a stable sort, matching JS; List.Sort is not.
        // ponytail: allocates a fresh list per frame (~600 structs). Sort an
        // index array in place if profiling ever cares.
        return new OrbFrame
        {
            Dots = visible.OrderBy(d => d.Z).ToList(),
            Lines = lines.FindAll(l => l.A >= 0.02)
        };
    }

    /// <summary>
    /// Dot radii were tuned for a 300pt frame; sub-linear scaling keeps small
    /// spinners legible. Lower pow = radii shrink less with size.
    /// </summary>
    public static double RadiusScale(double size, double pow) => Math.Pow(size / 300, pow);
}

/// <summary>Per-mode draw options — the TS `ModeOpts` bag, key for key.</summary>
public sealed class ModeOpts : Dictionary<string, double>
{
    public ModeOpts() { }
    public ModeOpts(IDictionary<string, double> src) : base(src) { }

    /// <summary>`o.key ?? fallback`.</summary>
    public double Get(string key, double fallback) => TryGetValue(key, out var v) ? v : fallback;

    /// <summary>JS truthiness for a numeric opt: present and non-zero.</summary>
    public bool Flag(string key) => TryGetValue(key, out var v) && v != 0;
}
