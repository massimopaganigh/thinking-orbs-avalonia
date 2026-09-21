// The nine mode painters, ported one-for-one from `src/engine/`:
// orbits.ts, lattice.ts (globe / rubik / wave), web.ts, braid.ts,
// ribbon.ts (ribbon + ring via the faceOn flag) and morph.ts.
//
// Each returns a finished frame: a z-sorted, radius-clamped draw list.
// Nothing here reads a rendering surface — the same numbers drive the
// Avalonia control and the golden-vector check.

namespace ThinkingOrbs;

/// <summary>Geometry for one instant: pure math over (size, t, opts).</summary>
public delegate OrbFrame ModeFrame(double size, double t, ModeOpts o);

public static class Modes
{
    public static readonly IReadOnlyDictionary<ModeKey, ModeFrame> Frames = new Dictionary<ModeKey, ModeFrame>
    {
        [ModeKey.Orbits] = Orbits,
        [ModeKey.Globe] = Globe,
        [ModeKey.Rubik] = Rubik,
        [ModeKey.Wave] = Wave,
        [ModeKey.Web] = Web,
        [ModeKey.Braid] = Braid,
        [ModeKey.Ribbon] = Ribbon,
        // ring shares ribbon's geometry — the `faceOn` profile flag switches it
        [ModeKey.Ring] = Ribbon,
        [ModeKey.Morph] = Morph
    };

    // --- Orbits: particles on tilted orbits — "working" ------------------
    // No nucleus (the tuned preset runs coreless): just ghost paths and the
    // particles doing the work.

    public static OrbFrame Orbits(double size, double t, ModeOpts o)
    {
        var cx = size / 2;
        var cy = size / 2;
        var R = size / 2 * 0.82;
        var pt = Engine.MakeProj(t * 0.12, 0.3, cx, cy, 1);
        var rs = Engine.RadiusScale(size, o.Get("rsPow", 0.6));

        var dots = new List<Dot>();
        var orbitN = (int)o.Get("orbitN", 12);
        var ghostN = (int)o.Get("ghostN", 40);
        var particles = (int)o.Get("particles", 3);
        var ghostR = o.Get("ghostR", 0.9);
        var ghostA = o.Get("ghostA", 0.5);
        var partR = o.Get("partR", 1.2);
        var partRDepth = o.Get("partRDepth", 1.6);

        // orbits: each a tilted circle — a ghost path + running particles
        for (var orb = 0; orb < orbitN; orb++)
        {
            var h1 = Engine.HashD(orb, 1.7);
            var h2 = Engine.HashD(orb, 5.2);
            var h3 = Engine.HashD(orb, 8.9);
            var ro = R * (0.45 + 0.52 * h1);
            var th = h1 * 2 * Math.PI;
            var phi = Math.Acos(2 * h2 - 1);
            // orbit plane basis (u, v perpendicular to normal n)
            var nx = Math.Sin(phi) * Math.Cos(th);
            var ny = Math.Cos(phi);
            var nz = Math.Sin(phi) * Math.Sin(th);
            var ux = -ny;
            var uy = nx;
            const double uz = 0;
            var ul = Math.Max(1e-6, Math.Sqrt(ux * ux + uy * uy));
            ux /= ul;
            uy /= ul;
            var vx = ny * uz - nz * uy;
            var vy = nz * ux - nx * uz;
            var vz = nx * uy - ny * ux;
            var speed = (0.25 + 0.55 * h3) * (h3 > 0.5 ? 1 : -1);

            // ghost path
            for (var k = 0; k < ghostN; k++)
            {
                var a = (double)k / ghostN * 2 * Math.PI;
                pt((ux * Math.Cos(a) + vx * Math.Sin(a)) * ro,
                   (uy * Math.Cos(a) + vy * Math.Sin(a)) * ro,
                   (uz * Math.Cos(a) + vz * Math.Sin(a)) * ro,
                   out var px, out var py, out var z);
                var depth = (z / ro + 1) / 2;
                dots.Add(new Dot { X = px, Y = py, Z = z, R = ghostR * rs, White = 0.72, A = ghostA * (0.4 + 0.6 * depth) });
            }
            // the particles doing the work
            for (var m = 0; m < particles; m++)
            {
                var a = t * speed + (double)m / particles * 2 * Math.PI + h2 * 6;
                pt((ux * Math.Cos(a) + vx * Math.Sin(a)) * ro,
                   (uy * Math.Cos(a) + vy * Math.Sin(a)) * ro,
                   (uz * Math.Cos(a) + vz * Math.Sin(a)) * ro,
                   out var px, out var py, out var z);
                var depth = (z / ro + 1) / 2;
                dots.Add(new Dot
                {
                    X = px, Y = py, Z = z,
                    R = (partR + partRDepth * depth) * rs,
                    White = 0.3 - 0.22 * depth,
                    A = 1
                });
            }
        }
        return Engine.FinalizeFrame(dots, [], o.Get("rMin", 0.3));
    }

    // --- Globe: lat/long field, a scan meridian sweeps — "searching" -----

    public static OrbFrame Globe(double size, double t, ModeOpts o)
    {
        const double spin = 0.5;
        var cx = size / 2;
        var cy = size / 2;
        var radius = size / 2 * 0.82;
        var tilt = 0.4 + 0.06 * Math.Sin(t * 0.35);
        var pt = Engine.MakeProj(t * spin, tilt, cx, cy, radius);
        // scan sweeps relative to the spin; scanMul scales that relative rate
        var scan = t * (spin + (1.7 - spin) * o.Get("scanMul", 1));
        var rs = Engine.RadiusScale(size, o.Get("rsPow", 0.6));
        var dimBase = o.Get("dimBase", 1);
        var rBase = o.Get("rBase", 0.6);
        var rDepth = o.Get("rDepth", 1.7);
        var rBoost = o.Get("rBoost", 1);
        var inkFar = o.Get("inkFar", 0.62);
        var inkSpan = o.Get("inkSpan", 0.54);

        var dots = new List<Dot>();
        var latRings = (int)o.Get("latRings", 17);
        var lonDensity = o.Get("lonDensity", 44);
        for (var li = 0; li <= latRings; li++)
        {
            var lat = -Math.PI / 2 + (double)li / latRings * Math.PI;
            var cosLat = Math.Cos(lat);
            var sinLat = Math.Sin(lat);
            var lonCount = (int)Math.Max(1, Engine.JsRound(Math.Abs(cosLat) * lonDensity));
            for (var lj = 0; lj < lonCount; lj++)
            {
                var lon = (double)lj / lonCount * 2 * Math.PI;
                pt(cosLat * Math.Cos(lon), sinLat, cosLat * Math.Sin(lon), out var px, out var py, out var z);
                var depth = (z + 1) / 2;
                // the scan: a moving meridian read as a size ripple, not a shine
                var d = Engine.AngleDelta(lon + t * spin, scan);
                var boost = Math.Exp(-(d * d) / 0.18) * Math.Max(0, z);
                dots.Add(new Dot
                {
                    X = px, Y = py, Z = z,
                    R = (rBase + rDepth * depth + rBoost * boost) * rs,
                    White = inkFar - inkSpan * depth,
                    // dimBase < 1 fades un-scanned dots so the meridian reads clearly
                    A = dimBase + (1 - dimBase) * Math.Min(1, boost)
                });
            }
        }
        return Engine.FinalizeFrame(dots, [], o.Get("rMin", 0.3));
    }

    // --- Rubik: bands twist in quarter turns, scramble then solve --------

    private readonly record struct Move(int Axis, double Lo, double Hi, double Ang);

    private static Move[] MakeMoves(int count)
    {
        var moves = new Move[count];
        for (var i = 0; i < count; i++)
        {
            var axis = (int)Math.Min(2, Math.Floor(Engine.HashD(i, 2.3) * 3));
            var lo = -1.0 + 0.5 * Math.Min(3, Math.Floor(Engine.HashD(i, 5.9) * 4));
            var dir = Engine.HashD(i, 7.7) < 0.5 ? 1 : -1;
            moves[i] = new Move(axis, lo, lo + 0.5, dir * Math.PI / 2);
        }
        return moves;
    }

    // Rapid eased moves scramble, then replay in reverse (palindrome) so
    // everything clicks back to solved, rests, repeats.
    private static (double[] Amount, int Active) SolveCycle(double time, int count, double slotDur, double rest)
    {
        var cyc = 2 * count * slotDur + rest;
        var tc = time % cyc; // JS `%` is fmod — exact; t - floor(t/c)*c is not
        var amount = new double[count];
        var active = -1;
        if (tc < 2 * count * slotDur)
        {
            var slot = (int)Math.Floor(tc / slotDur);
            var p = (tc - slot * slotDur) / slotDur;
            var cl = Math.Min(1, p / 0.7);
            var ep = 1 - Math.Pow(1 - cl, 3); // machine ease-out
            if (slot < count)
            {
                for (var i = 0; i < slot; i++) amount[i] = 1;
                amount[slot] = ep;
                active = slot;
            }
            else
            {
                var u = 2 * count - 1 - slot;
                for (var i = 0; i < u; i++) amount[i] = 1;
                amount[u] = 1 - ep;
                active = u;
            }
        }
        return (amount, active);
    }

    private static (double X, double Y, double Z, bool InActive) ApplyMoves(
        double x, double y, double z, Move[] moves, double[] amount, int active)
    {
        var inActive = false;
        for (var i = 0; i < moves.Length; i++)
        {
            if (amount[i] <= 0) continue;
            var mv = moves[i];
            var coord = mv.Axis == 0 ? x : mv.Axis == 1 ? y : z;
            if (coord < mv.Lo || coord >= mv.Hi) continue;
            if (i == active) inActive = true;
            var a = mv.Ang * amount[i];
            var ca = Math.Cos(a);
            var sa = Math.Sin(a);
            if (mv.Axis == 0)
            {
                var y2 = y * ca - z * sa;
                z = y * sa + z * ca;
                y = y2;
            }
            else if (mv.Axis == 1)
            {
                var x2 = x * ca + z * sa;
                z = -x * sa + z * ca;
                x = x2;
            }
            else
            {
                var x2 = x * ca - y * sa;
                y = x * sa + y * ca;
                x = x2;
            }
        }
        return (x, y, z, inActive);
    }

    public static OrbFrame Rubik(double size, double t, ModeOpts o)
    {
        var cx = size / 2;
        var cy = size / 2;
        var R = size / 2 * 0.82;
        var pt = Engine.MakeProj(t * 0.55, 0.35 + 0.1 * Math.Sin(t * 0.9), cx, cy, R);
        var rs = Engine.RadiusScale(size, o.Get("rsPow", 0.6));
        var moveCount = (int)o.Get("moveCount", 14);
        var moves = MakeMoves(moveCount);
        var (amount, active) = SolveCycle(t, moveCount, 0.42, 1.2);
        var rBase = o.Get("rBase", 0.6);
        var rDepth = o.Get("rDepth", 1.7);
        var rActive = o.Get("rActive", 0.3);
        var inkFar = o.Get("inkFar", 0.62);
        var inkSpan = o.Get("inkSpan", 0.54);

        var dots = new List<Dot>();
        var latRings = (int)o.Get("latRings", 15);
        var lonDensity = o.Get("lonDensity", 40);
        for (var li = 0; li <= latRings; li++)
        {
            var lat = -Math.PI / 2 + (double)li / latRings * Math.PI;
            var cosLat = Math.Cos(lat);
            var sinLat = Math.Sin(lat);
            var lonCount = (int)Math.Max(1, Engine.JsRound(Math.Abs(cosLat) * lonDensity));
            for (var lj = 0; lj < lonCount; lj++)
            {
                var lon = (double)lj / lonCount * 2 * Math.PI;
                var (x, y, z, inActive) = ApplyMoves(
                    cosLat * Math.Cos(lon), sinLat, cosLat * Math.Sin(lon), moves, amount, active);
                pt(x, y, z, out var px, out var py, out var zr);
                var depth = (zr + 1) / 2;
                // the band being turned inks a touch darker — the "hand"
                dots.Add(new Dot
                {
                    X = px, Y = py, Z = zr,
                    R = (rBase + rDepth * depth + (inActive ? rActive : 0)) * rs,
                    White = inkFar - inkSpan * depth - (inActive ? 0.14 : 0),
                    A = 1
                });
            }
        }
        return Engine.FinalizeFrame(dots, [], o.Get("rMin", 0.3));
    }

    // --- Wave: a waveform rolls through the rings — "listening" ----------

    public static OrbFrame Wave(double size, double t, ModeOpts o)
    {
        var cx = size / 2;
        var cy = size / 2;
        // 0.76 base x 1.15 — the undulation pulls the sphere inward, so wave
        // read ~15% smaller than the other lattice modes; scaled up to match
        var R = size / 2 * 0.874;
        var pt = Engine.MakeProj(t * 0.18, 0.38, cx, cy, 1);
        var rs = Engine.RadiusScale(size, o.Get("rsPow", 0.6));
        var rBase = o.Get("rBase", 0.6);
        var rDepth = o.Get("rDepth", 1.7);

        var dots = new List<Dot>();
        var rings = (int)o.Get("rings", 15);
        var lonDensity = o.Get("lonDensity", 40);
        for (var ri = 0; ri <= rings; ri++)
        {
            var lat = -Math.PI / 2 + (double)ri / rings * Math.PI;
            var cosLat = Math.Cos(lat);
            var sinLat = Math.Sin(lat);
            // two waves, different tempi — organic, never quite repeating
            var w = 0.62 * Math.Sin(t * 2.1 - ri * 0.52) + 0.38 * Math.Sin(t * 1.27 + ri * 0.83);
            var rr = R * (0.88 + 0.105 * w);
            var lonCount = (int)Math.Max(1, Engine.JsRound(Math.Abs(cosLat) * lonDensity));
            for (var lj = 0; lj < lonCount; lj++)
            {
                var lon = (double)lj / lonCount * 2 * Math.PI;
                pt(cosLat * Math.Cos(lon) * rr, sinLat * rr, cosLat * Math.Sin(lon) * rr,
                   out var px, out var py, out var z);
                var depth = (z / R + 1) / 2;
                var crest = Math.Max(0, w);
                dots.Add(new Dot
                {
                    X = px, Y = py, Z = z,
                    R = (rBase + rDepth * depth) * (1 + 0.4 * crest) * rs,
                    White = 0.66 - 0.56 * depth - 0.1 * crest,
                    A = 1
                });
            }
        }
        return Engine.FinalizeFrame(dots, [], o.Get("rMin", 0.3));
    }

    // --- Web: a constellation wires itself — "connecting" ----------------
    // Nodes drift on the sphere under slow value noise; any pair closer than
    // `thr` grows an edge, and bright packets run along re-picked node pairs.

    public static OrbFrame Web(double size, double t, ModeOpts o)
    {
        var cx = size / 2;
        var cy = size / 2;
        var R = size / 2 * 0.8 * o.Get("spread", 1);
        // the projector carries the radius as its scale, so node vectors stay
        // unit-length and distances below are in unit-sphere space
        var pt = Engine.MakeProj(t * 0.12, 0.32, cx, cy, R);
        var rs = Engine.RadiusScale(size, o.Get("rsPow", 0.6));

        var nodeN = (int)o.Get("nodeN", 30);
        var thr = o.Get("thr", 0.72);
        var nodeR = o.Get("nodeR", 1.4);
        var nodeRDepth = o.Get("nodeRDepth", 1.8);

        // nodes: fib lattice + slow noise wander, renormalised to the surface
        var nodes = new (double X, double Y, double Z)[nodeN];
        for (var i = 0; i < nodeN; i++)
        {
            var d = Engine.FibDir(i, nodeN);
            var x = d.X + 0.3 * (Engine.VNoise(i * 0.31 + 9, t * 0.24) - 0.5) * 2;
            var y = d.Y + 0.3 * (Engine.VNoise(i * 0.53 + 27, t * 0.21) - 0.5) * 2;
            var z = d.Z + 0.3 * (Engine.VNoise(i * 0.77 + 55, t * 0.27) - 0.5) * 2;
            var l = Math.Sqrt(x * x + y * y + z * z);
            nodes[i] = (x / l, y / l, z / l);
        }

        var lines = new List<Line>();
        var dots = new List<Dot>();

        // edges between close neighbours, alpha by proximity + depth
        for (var i = 0; i < nodeN; i++)
        {
            for (var j = i + 1; j < nodeN; j++)
            {
                var dx = nodes[i].X - nodes[j].X;
                var dy = nodes[i].Y - nodes[j].Y;
                var dz = nodes[i].Z - nodes[j].Z;
                var dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (dist >= thr) continue;
                pt(nodes[i].X, nodes[i].Y, nodes[i].Z, out var x1, out var y1, out var z1);
                pt(nodes[j].X, nodes[j].Y, nodes[j].Z, out var x2, out var y2, out var z2);
                var depth = ((z1 + z2) / 2 + 1) / 2;
                lines.Add(new Line
                {
                    X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                    White = 0.42,
                    A = (1 - dist / thr) * (0.3 + 0.55 * depth),
                    W = Math.Max(0.6, o.Get("lineW", 0.8) * rs)
                });
            }
        }

        for (var i = 0; i < nodeN; i++)
        {
            pt(nodes[i].X, nodes[i].Y, nodes[i].Z, out var px, out var py, out var z);
            var depth = (z + 1) / 2;
            var pulse = 1 + 0.25 * Math.Sin(t * 1.4 + i * 2.7);
            dots.Add(new Dot
            {
                X = px, Y = py, Z = z,
                R = (nodeR + nodeRDepth * depth) * pulse * rs,
                White = 0.55 - 0.45 * depth,
                A = 1
            });
        }

        // signals: bright packets running between paired nodes
        var signals = (int)o.Get("signals", 5);
        for (var s = 0; s < signals; s++)
        {
            var seg = Math.Floor(t * 0.55 + s * 7.31);
            var a = (int)Math.Floor(Engine.HashD(seg, s * 3.1 + 1.7) * nodeN);
            var b = (int)Math.Floor(Engine.HashD(seg, s * 5.7 + 4.2) * nodeN);
            if (a == b) continue;
            var f = Engine.Frac(t * 0.55 + s * 7.31);
            var x = Engine.Lerp(nodes[a].X, nodes[b].X, f);
            var y = Engine.Lerp(nodes[a].Y, nodes[b].Y, f);
            var z = Engine.Lerp(nodes[a].Z, nodes[b].Z, f);
            var l = Math.Max(1e-6, Math.Sqrt(x * x + y * y + z * z));
            pt(x / l, y / l, z / l, out var px, out var py, out var zr);
            var depth = (zr + 1) / 2;
            dots.Add(new Dot
            {
                X = px, Y = py, Z = zr,
                R = (nodeR * 1.5 + nodeRDepth * depth) * rs,
                White = 0.05,
                A = 0.5 + 0.5 * depth
            });
        }

        return Engine.FinalizeFrame(dots, lines, o.Get("rMin", 0.3));
    }

    // --- Braid: three strands plait around the sphere — "weaving" --------

    public static OrbFrame Braid(double size, double t, ModeOpts o)
    {
        var cx = size / 2;
        var cy = size / 2;
        var R = size / 2 * 0.76;
        var pt = Engine.MakeProj(t * 0.4, 0.3, cx, cy, 1);
        var rs = Engine.RadiusScale(size, o.Get("rsPow", 0.6));

        var dots = new List<Dot>();
        var ghostN = (int)o.Get("ghostN", 150);
        for (var i = 0; i < ghostN; i++)
        {
            var d = Engine.FibDir(i, ghostN);
            pt(d.X * R, d.Y * R, d.Z * R, out var px, out var py, out var z);
            var depth = (z / R + 1) / 2;
            dots.Add(new Dot { X = px, Y = py, Z = z, R = 0.8 * rs, White = 0.78, A = 0.1 + 0.22 * depth });
        }

        var strandN = (int)o.Get("strandN", 52);
        var turns = o.Get("turns", 3);
        var rBase = o.Get("rBase", 1.2);
        var rDepth = o.Get("rDepth", 1.8);
        for (var s = 0; s < 3; s++)
        {
            var phase = (double)s / 3 * 2 * Math.PI;
            for (var i = 0; i < strandN; i++)
            {
                // u walks pole to pole; the Frac() drift slides the whole strand
                var u = (Engine.Frac((double)i / strandN + t * 0.045) * 2 - 1) * 0.96;
                var surf = Math.Sqrt(Math.Max(0, 1 - u * u));
                var endFade = Math.Min(1, (1 - Math.Abs(u)) / 0.1);
                var a = u * Math.PI * turns + phase;
                // radial breathing: strands trade places — the over/under of a plait
                var weave = 1 + 0.075 * Math.Sin(u * Math.PI * turns * 2 + phase * 2 + t * 0.8);
                var rr = surf * R * weave;
                pt(Math.Cos(a) * rr, u * R * weave, Math.Sin(a) * rr, out var px, out var py, out var zr);
                var depth = (zr / R + 1) / 2;
                dots.Add(new Dot
                {
                    X = px, Y = py, Z = zr,
                    R = (rBase + rDepth * depth) * rs,
                    White = 0.55 - 0.45 * depth,
                    A = endFade * (0.45 + 0.55 * depth)
                });
            }
        }
        return Engine.FinalizeFrame(dots, [], o.Get("rMin", 0.3));
    }

    // --- Ribbon: an undulating sash rides a great circle — "composing" ---
    // The same painter drives "breathing" (ring) via the `faceOn` flag: a
    // face-on circle whose radius — not its out-of-plane offset — undulates.

    public static OrbFrame Ribbon(double size, double t, ModeOpts o)
    {
        var cx = size / 2;
        var cy = size / 2;
        var R = size / 2 * 0.78;
        // spin scales the 3D tumble; spin=0 freezes the band's orientation,
        // leaving only the traveling undulation
        var spin = o.Get("spin", 1);
        const double camTilt = 0.3;
        var pt = Engine.MakeProj(t * 0.1 * spin, camTilt, cx, cy, 1);
        var rs = Engine.RadiusScale(size, o.Get("rsPow", 0.6));
        var faceOn = o.Flag("faceOn");

        var dots = new List<Dot>();
        var ghostN = (int)o.Get("ghostN", 150);
        for (var i = 0; i < ghostN; i++)
        {
            var d = Engine.FibDir(i, ghostN);
            pt(d.X * R, d.Y * R, d.Z * R, out var px, out var py, out var z);
            var depth = (z / R + 1) / 2;
            dots.Add(new Dot { X = px, Y = py, Z = z, R = 0.8 * rs, White = 0.78, A = 0.1 + 0.22 * depth });
        }

        // The band plane, precessing (frozen when spin=0). The projection
        // squashes the band's great circle vertically by cos(ta + camTilt);
        // face-on sets ta = -camTilt so that term is 1 and the band reads as
        // a true circle rather than ribbon's tilted ellipse.
        var ya = t * 0.24 * spin;
        var ta = faceOn ? -camTilt : 0.55 + 0.3 * Math.Sin(t * 0.18) * spin;
        var ux = Math.Cos(ya);
        const double uy = 0;
        var uz = Math.Sin(ya);
        var vx = -uz * Math.Sin(ta);
        var vy = Math.Cos(ta);
        var vz = ux * Math.Sin(ta);
        // plane normal n = u x v
        var nx = uy * vz - uz * vy;
        var ny = uz * vx - ux * vz;
        var nz = ux * vy - uy * vx;

        // Radial lobes swell past R, so pull the base radius in by (most of)
        // the wobble amplitude. The silhouette then stays inside the frame
        // however far the deformation is pushed.
        var wobMul = o.Get("wobMul", 1);
        var wobAmp = 0.23 * wobMul;
        var baseR = faceOn ? R / (1 + 0.85 * wobAmp) : R;

        var baseLanes = o.Get("lanes", 5);
        var segs = (int)o.Get("segs", 88);
        var lanes = (int)Math.Max(1, Engine.JsRound(baseLanes * o.Get("bandMul", 1)));
        var rBase = o.Get("rBase", 1.1);
        var rDepth = o.Get("rDepth", 1.7);
        for (var w = 0; w < lanes; w++)
        {
            var laneOff = (w - (lanes - 1) / 2.0) * 0.075;
            var edge = Math.Abs(w - (lanes - 1) / 2.0) / Math.Max(1, (lanes - 1) / 2.0);
            for (var k = 0; k < segs; k++)
            {
                var a = (double)k / segs * 2 * Math.PI;
                // the undulation: two traveling waves along the band; wobMul
                // scales the deformation — 0 is a clean band
                var wob = (0.16 * Math.Sin(a * 3 - t * 1.7 + w * 0.22) + 0.07 * Math.Sin(a * 5 + t * 1.1)) * wobMul;
                // A normal-direction wobble is cancelled by the re-normalisation
                // below: the point lands back on the sphere, so the silhouette is
                // pinned at R and the deformation can only pull dots inward.
                // Face-on instead modulates the in-plane RADIUS, so lobes
                // genuinely swell outward and pinch inward.
                var radial = faceOn ? 1 + wob : 1;
                var off = faceOn ? laneOff : laneOff + wob;
                var x = ux * Math.Cos(a) + vx * Math.Sin(a) + nx * off;
                var y = uy * Math.Cos(a) + vy * Math.Sin(a) + ny * off;
                var z = uz * Math.Cos(a) + vz * Math.Sin(a) + nz * off;
                var l = Math.Sqrt(x * x + y * y + z * z);
                var rr = baseR * radial;
                pt(x / l * rr, y / l * rr, z / l * rr, out var px, out var py, out var zr);
                var depth = (zr / R + 1) / 2;
                dots.Add(new Dot
                {
                    X = px, Y = py, Z = zr,
                    R = (rBase + rDepth * depth) * (1 - 0.25 * edge) * rs,
                    White = 0.52 - 0.44 * depth + 0.18 * edge,
                    A = 0.4 + 0.6 * depth
                });
            }
        }
        return Engine.FinalizeFrame(dots, [], o.Get("rMin", 0.3));
    }

    // --- Morph: a dotted outline circle -> triangle -> square — "shaping" -
    // Each shape is a continuous closed path parameterised by arc length
    // (top-centre start, clockwise). Every frame blends the two neighbouring
    // paths, then lays the dots EVENLY along the blended outline.

    private delegate (double X, double Y) Path(double f);

    private static double SmoothE(double x) => x * x * (3 - 2 * x);

    private static Path PolyPath((double X, double Y)[] verts)
    {
        var V = verts.Length;
        var L = new double[V];
        double total = 0;
        for (var i = 0; i < V; i++)
        {
            var a = verts[i];
            var b = verts[(i + 1) % V];
            var l = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            L[i] = l;
            total += l;
        }
        return f =>
        {
            var target = f * total;
            var i = 0;
            while (target > L[i] && i < V - 1)
            {
                target -= L[i];
                i++;
            }
            var a = verts[i];
            var b = verts[(i + 1) % V];
            var ff = L[i] != 0 ? Math.Min(1, target / L[i]) : 0;
            return (a.X + (b.X - a.X) * ff, a.Y + (b.Y - a.Y) * ff);
        };
    }

    private static readonly Path Circle = f =>
    {
        var a = -Math.PI / 2 + f * 2 * Math.PI;
        return (Math.Cos(a) * 0.24, Math.Sin(a) * 0.24);
    };

    private static readonly Path Triangle = PolyPath([(0.0, -0.26), (0.24, 0.16), (-0.24, 0.16)]);

    // 5-vertex walk so the path STARTS at top-centre like the other shapes
    private static readonly Path Square =
        PolyPath([(0, -0.2), (0.2, -0.2), (0.2, 0.2), (-0.2, 0.2), (-0.2, -0.2)]);

    private static readonly Path[] Cycle = [Circle, Triangle, Square];

    private const double Hold = 1.4;
    private const double MorphDur = 0.9;
    private const double Seg = Hold + MorphDur;

    // Tuned in inkform, which paints this through a blur + threshold "goo"
    // filter; we draw plain circles instead. The dot GEOMETRY is identical —
    // don't "correct" the softer edge by shrinking the radius.
    public static OrbFrame Morph(double size, double t, ModeOpts o)
    {
        var K = Cycle.Length;
        var cyc = Seg * K;
        var tc = t % cyc;
        var k = (int)Math.Floor(tc / Seg);
        var local = tc - k * Seg;
        var m = local > Hold ? SmoothE((local - Hold) / MorphDur) : 0;
        var sprd = o.Get("spread", 1);

        // blend the two shape PATHS at m, then measure the blended outline
        var pA = Cycle[k];
        var pB = Cycle[(k + 1) % K];
        const int M = 160;
        var pts = new (double X, double Y)[M];
        for (var i = 0; i < M; i++)
        {
            var f = (double)i / M;
            var a = pA(f);
            var b = pB(f);
            pts[i] = ((a.X + (b.X - a.X) * m) * sprd, (a.Y + (b.Y - a.Y) * m) * sprd);
        }
        var L = new double[M];
        double total = 0;
        for (var i = 0; i < M; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % M];
            var l = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            L[i] = l;
            total += l;
        }

        // dot radius depends ONLY on rDot (the size knob); the count sets the
        // gaps. Formed shapes breathe a little (uniform pulse).
        // low floor keeps sparse outlines possible while never degenerating
        var n = (int)Math.Max(6, Engine.JsRound(34 * o.Get("iconD", 1)));
        var re = o.Get("rDot", 0.021) * 1.35 * sprd;
        var pulse = 1 + 0.02 * Math.Sin(local * 3.1);

        var dots = new List<Dot>(n);
        var c2 = size / 2;
        var seg = 0;
        double acc = 0;
        for (var k2 = 0; k2 < n; k2++)
        {
            var target = (double)k2 / n * total;
            while (acc + L[seg] < target && seg < M - 1)
            {
                acc += L[seg];
                seg++;
            }
            var a = pts[seg];
            var b = pts[(seg + 1) % M];
            var f = L[seg] != 0 ? Math.Min(1, (target - acc) / L[seg]) : 0;
            var x = (a.X + (b.X - a.X) * f) * pulse;
            var y = (a.Y + (b.Y - a.Y) * f) * pulse;
            dots.Add(new Dot
            {
                X = c2 + x * size,
                Y = c2 + y * size,
                Z = 0,
                R = Math.Max(0.35, re * size),
                White = 0.1,
                A = 1
            });
        }
        return Engine.FinalizeFrame(dots, [], o.Get("rMin", 0.3));
    }
}
