# thinking-orbs for Avalonia

Dotted thought-orb loading indicators for AI & agent UIs — an Avalonia port of
[thinking-orbs](https://github.com/Jakubantalik/thinking-orbs) by Jakub Antalik.
Nine hand-tuned animated states, each shipped at two purpose-tuned sizes, drawn
as plain circle fills on an Avalonia `Control`.

The geometry is a one-for-one transcription of the web engine, verified against
the library's own `spec/orbs-golden.json` — 72 cases, 70,855 values, matching to
1e-4 with two documented exceptions (below).

## Use

```xml
<Window xmlns:orbs="clr-namespace:ThinkingOrbs;assembly=ThinkingOrbs.Avalonia">
  <orbs:ThinkingOrb State="Searching" Size="Px64" />
</Window>
```

```csharp
var orb = new ThinkingOrb { State = OrbState.Connecting, Size = OrbSize.Px20 };
```

## States

Nine verbs an agent can be doing, each a distinct animation:

| `State` | What it draws |
|---|---|
| `Working` | particles on tilted orbits |
| `Searching` | a scan meridian sweeps a dotted globe |
| `Solving` | bands scramble, then click back solved |
| `Listening` | a waveform rolls through the rings |
| `Connecting` | a constellation wires itself |
| `Weaving` | three strands plait around the sphere |
| `Composing` | an undulating multi-band sash |
| `Breathing` | a ring slowly morphing |
| `Shaping` | a dotted outline: circle → triangle → square |

## Properties

| Property | Type | Default | Notes |
|---|---|---|---|
| `State` | `OrbState` | `Working` | |
| `Size` | `OrbSize` | `Px64` | `Px64` (chat-avatar) or `Px20` (inline text). Separate designs, not a scale factor: each carries its own dot count, dot size and speed tuning. |
| `Theme` | `OrbTheme` | `Auto` | `Auto` follows `ActualThemeVariant`; `Dark`/`Light` pin it. |
| `Speed` | `double` | `1` | Multiplier on the preset's baked speed. |
| `IsPaused` | `bool` | `false` | Freeze on the current frame. |

Strictly monochrome: light ink for dark backgrounds, dark ink for light ones.
`Auto` resolves through Avalonia's own theme variant, so an app-level or
control-level `RequestedThemeVariant` — including the platform default — flows
straight through and the orb repaints on a live switch.

## Accessibility & behaviour

- `AutomationProperties.Name` is set to a per-state label (`"Searching…"`).
- Reduced motion (Windows `SPI_GETCLIENTAREAANIMATION`) renders one static
  representative frame and stops the timer; the theme still follows live.
- One shared clock drives every instance, so several orbs on screen stay in
  phase, and a single timer invalidates them all. Orbs that are not
  `IsEffectivelyVisible` are skipped.
- `ThinkingOrb.FrozenTime` pins the clock to a fixed instant for reproducible
  renders.

## Projects

- `src/ThinkingOrbs.Avalonia` — the control and the engine. One package
  reference (`Avalonia`), no renderer-specific dependency.
- `demo/ThinkingOrbs.Demo` — a gallery of all nine states at both sizes, with a
  theme toggle, a speed slider and a pause button.
  `dotnet run --project demo/ThinkingOrbs.Demo`
- `golden/ThinkingOrbs.Golden` — the fidelity check.
  `dotnet run --project golden/ThinkingOrbs.Golden`

Contact sheets, at a pinned instant, without opening a window:

```
dotnet run --project demo/ThinkingOrbs.Demo -- --snapshot out 0.6
```

## Fidelity

`spec/orbs-golden.json` and `spec/orbs-spec.json` are the upstream spec files
(spec 1.0.0, thinking-orbs 0.3.1). The checker compares the resolved presets and
then every dot and line, in draw order, at four timestamps per (state × size).

Two divergences are known, pinned and expected — the run fails if either one
changes:

- `solving-20` at t=3.3 (4 values) and t=5.1 (65 values).

The cause is in the source algorithm, not in the transcription. `rubik` decides
band membership with a hard test (`coord < lo || coord >= hi`), and at the 20px
lattice the quarter-turned coordinates land 3.7e-33 and 2.2e-17 away from the
boundary at zero. One ulp of difference in `cos`/`sin` therefore decides which
side of a turning band a dot sits on. An independent third implementation
(CPython on the Windows libm) reproduces this port's numbers exactly, which is
what rules out a transcription error; matching V8 bit-for-bit would mean
shipping V8's libm. In practice: at t=3.3 two coincident dots swap draw order,
and at t=5.1 three dots out of thirty sit in a different band of a 20×20 px
spinner mid-scramble.

Two notes on the raster, inherited from the upstream port work: dots below a
pixel across are rasterised slightly differently by Skia than by a browser
canvas, so the faintest far-depth dots read a touch stronger here; and alpha is
quantised to 8 bits when the ink brush is built, as it is on a canvas.

## Deliberate differences from the web component

- No device-pixel-ratio handling: Avalonia renders in device-independent pixels
  and scales itself, so the web's DPR cap of 2 has no equivalent to port.
- Offscreen pausing is `IsEffectivelyVisible`, not an intersection observer — a
  control scrolled out of a `ScrollViewer` still ticks. Bind `IsPaused` if that
  matters for a long list.

## License

MIT. Original work © Jakub Antalik; see `LICENSE`.
