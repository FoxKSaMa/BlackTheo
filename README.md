# BlackTheo

A Celeste (Everest / FNA) mod that replaces Theo's crystal with a compact,
color-configurable, gravitationally lensed black hole.

Version **1.3.1** adds clearer option names, MoreDasheline-style custom color
input, a 0-3000 speed curve for the Madeline black hole, and stronger but
balanced Doppler shading.

## The disk is traced, not pasted on

The accretion disk is **not** a screen-space ellipse. It is a thin disk in the
plane `y = 0`. For every pixel near the hole the shader integrates a
Schwarzschild-like null geodesic (220 kick-drift steps) around the mass and
records where the ray crosses the disk plane, up to three times. That is what
produces the correct side-view structure:

- the **near side** crosses in front of the shadow as a thin band;
- the **far side** is bent over and under the shadow into bright arcs;
- a thin **photon rim** hugs the shadow;
- the same escaped-ray direction is used for the screen-space lens, so the
  background bends around the hole instead of being covered by an ellipse.

The disk is deliberately compact: the shadow is ~16 px across in Celeste's
320x180 gameplay buffer and the lensed disk is roughly 35-45 px across. The
camera is tilted ~10 degrees above the disk and rolled ~18 degrees so the disk
is an angled band, not a flat horizontal line.

The disk composite uses coverage blending instead of pure addition: coverage,
a subtle darkening under the disk, soft 10-level quantization and a
bright-background boost keep it readable on light maps while reducing the
airbrushed "paint" look.

## Options (1.3.1)

Open **Mod Options → BlackTheo**. All options apply live while a level is
loaded (the component keeps the original Theo visuals in sync).

| Option | Values | Default |
|---|---|---|
| Toggle | On / Off | On |
| Black hole lens strength | 0-10 (0 = Off, 2 = Low, 10 = High) | 2 |
| Accretion disk color | Blue / Ice blue / Red-orange / Purple / Ghost green / Dust yellow / White / Pink / Brown / Dark red / Gray / Custom | Blue |
| Custom disk color (main menu) | `RRGGBB` or `RRGGBBAA`; `#` optional | `FF7A45` |
| Doppler effect | On / Off | On |
| Stardust | On / Off | On |
| Madeline black hole mode | Off / Custom / Speed | Off |
| Madeline lens strength | 0-10 | 2 |

The custom color is a plain string setting, the same pattern as MoreDasheline:
open **main menu → Mod Options → BlackTheo**, select **Custom disk color**, and
type with the game keyboard. Only `RRGGBB` / `RRGGBBAA` are accepted; `#` is
optional. Everest disables string entries in-game, so the entry is hidden there
rather than shown greyed out; use the built-in palette presets while playing.
The invalid-input fallback is the **Ice blue** preset, `#50B4FF`.

Lens levels 2-10 interpolate from Low (2) to High (10):

| Level | Scale | Cap | Radius | Falloff | Edge fade |
|---|---:|---:|---:|---:|---:|
| 0 (Off) | 0 | 0 | 50 px | 2.2 | 72% |
| 1 | 1.0 | 5 px | 46 px | 2.3 | 76% |
| 2 (Low) | 2.0 | 10 px | 50 px | 2.2 | 72% |
| 10 (High) | 4.8 | 24 px | 80 px | 1.55 | 52% |

Doppler shading is balanced around 1.0: the approaching side is 30% brighter and
the receding side 30% dimmer, so enabling it does not darken the whole disk. The
disk animation is reversed relative to v1.1.0, so the brighter left side is the
approaching side for the counter-clockwise presentation.

A few subtle colored pixels drift inside the event horizon (where the disk does
not cover it). Their color follows the selected palette and their positions are
stable per Theo. **Stardust** turns them off completely.

### Madeline black hole mode

- **Off**: disabled.
- **Custom**: a lens-only warp follows Madeline at `Madeline lens strength` (0-10).
- **Speed**:
  - 0-200 px/s: no lens.
  - 200-800 px/s: level 0 → 4.
  - 800-1300 px/s: level 4 → 6.
  - 1300-3000 px/s: level 6 → 10.
  - Above 3000 px/s: level 10.
- The effect envelope grows to 96 px at level 10; `pow(amount, 0.7)`
  strength and a flatter center falloff make the high-strength area large.
- A black core covers the radial fold at the center. Its radius grows smoothly
  with the lens amount, from a small dot to 16 px at maximum (no mid-range cap).
- The displacement is **purely radial**, so the lens is symmetric; there is no
  tangential swirl.
- Speed is smoothed with a fast attack and slow release, and freeze frames hold
  the previous amount. Temporary 0-speed states (dash freeze, jelly/Theo grab,
  wall impact with retained velocity) therefore no longer blink the lens off.

This pass bends the already-composited screen around Madeline and does not draw
a black hole or disk.

## Component API

Every `TheoCrystal` receives a `BlackHoleComponent` in its constructor hook.
Maps or other mods can override settings per Theo:

```csharp
var bh = theo.Get<BlackHoleComponent>();

bh.LensStrengthOverride = LensStrengthMode.High;
bh.DiskColorOverride = DiskColorMode.Custom;
bh.CustomColorOverride = "#00FF80";
bh.DopplerOverride = false;
bh.SpecksOverride = false;
bh.Brightness = 1.2f;          // 0..2
bh.Enabled = false;            // return to the normal Theo crystal
```

The component owns the original sprite/light state. Disabling the component,
turning off the global option, or a shader failure restores the normal Theo
visuals instead of leaving him invisible.

## What it does

- Hides the `theo_crystal` sprite and its white vertex light via the component.
- Lifts the black hole's screen centre by 5 px while held (1.5 px otherwise) so
  it does not sit directly on Madeline's head.
- Hooks `Glitch.Apply`, the last full-screen world composite, so the whole
  gameplay image is affected but the HUD is not.
- Runs one ray-traced black-hole pass per visible `TheoCrystal` (up to four).
- Reads component overrides, otherwise global mod settings, per frame.
- Queries `Level.Tracker.GetEntities<TheoCrystal>()` every frame and recomputes
  `Theo.Center - level.Camera.Position`, so held/thrown/dead/respawn/room
  transition cases update or disappear automatically.
- Skips dead, inactive and off-screen Theos, and disables itself cleanly if the
  shader or a render pass fails.

## Visual references

- [sayed1145/glsl-black-hole](https://github.com/sayed1145/glsl-black-hole)
  (MIT) for the warm/cool disk radiance, domain-warped filaments and the
  folded-disk composition.
- [Gitee black-hole recycling station](https://gitee.com/the-journey-ahead-is-long/black-hole-recycling-station)
  for the screen-space deflection lens and compact presentation.
- Luminet (1979) / James et al. (2015) for thin-disk lensing geometry.

This is a screen-space compositor, not a full multi-pass HDR renderer: it fits
the look into one FX9 ps_3_0 shader, drawn once per visible hole (and once for
the Madeline lens).

## Files

- `src/BlackHole.fx` — geodesic integrator, palette uniforms, lens and specks.
- `src/BlackHole.cso` — compiled FX9 effect bytecode (embedded in the DLL).
- `src/BlackTheo/BlackHoleConfiguration.cs` — enums, settings, palettes.
- `src/BlackTheo/HexColor.cs` — hex-to-RGBA parser.
- `src/BlackTheo/BlackHoleComponent.cs` — per-Theo config and visual ownership.
- `src/BlackTheo/BlackHoleRenderer.cs` — capture target, parameter setup.
- `src/BlackTheo/BlackTheoModule.cs` — Everest hooks and lifetime.
- `src/BlackTheo/Dialog/` — localized option labels.
- `dist/BlackTheo/` — ready-to-install mod.
- `tools/EffectCompiler/` — a small fxc replacement using Windows'
  `d3dx9_43.dll`, so the shader can be rebuilt without the Windows SDK.

## Install

Copy the `BlackTheo` folder from the GitHub Release into
`<Celeste>/Mods/BlackTheo` and restart Celeste.

## Build

Point the build at your Celeste install either with an environment variable or
with `-CelestePath`:

```powershell
$env:CELESTE_DIR = 'C:\Program Files (x86)\Steam\steamapps\common\Celeste'
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Deploy
```

or:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 `
  -CelestePath 'C:\Program Files (x86)\Steam\steamapps\common\Celeste' -Deploy
```

The script auto-detects common Steam locations if neither is provided. The
generated package is `dist/BlackTheo`; `Effects/` is intentionally not packaged
because the compiled shader is embedded in the DLL.

Changing C# requires a full Celeste restart. Changing only the `.cso` may be
hot-reloaded by FrostHelper, but the DLL must be reloaded for code changes.

## License

MIT. See [LICENSE](LICENSE).

## Lifecycle

The effect is not attached to any entity and does not keep cached positions.
Every rendered frame it queries the current level's TheoCrystal list, filters
dead/inactive/offscreen entities and recomputes the screen centre from the live
camera. Therefore:

- held / thrown / falling Theo: follows automatically;
- player death and respawn: dead entities are skipped and the new scene's
  entities become the new source of truth;
- room transition: the new level's entity list is used next frame;
- entity removal / level exit: the hole disappears immediately;
- pause: positions are stable and the gameplay target is not corrupted.

The sprite is hidden and the vertex light alpha is zeroed by the component.
`RestoreVisualState()` puts them back when the component is disabled/removed or
the effect is globally disabled.

## Tuning

`BlackHoleRenderer.cs`:

- `EventHorizonRadius` (default 8.0 px)
- `MaxLensRadius` (80 px, used for culling; each mode has its own radius)
- `MadelineMinLensRadius` / `MadelineMaxLensRadius` (20 / 96 px)
- `MadelineMaxSpeed` (3000 px/s)
- held / grounded screen lift (5 / 1.5 px, in `CollectHoles`)

`BlackHole.fx`:

- camera inclination (`ro = (0, 11.0, -59.8)`) and 18-degree roll (`roll = 0.32`);
- disk radius range (`2.0 .. 10.5` Schwarzschild units);
- palette uniforms for inner/mid/outer/photon/halo;
- per-mode lens `lensRadius`, `LensFalloff` and `LensEdge` uniforms;
- lens-only branch for the Madeline lens (`LensOnly`);
- coverage blend / 10-level quantization for the disk;
- Doppler brightness range `0.85 .. 1.15`;
- three small horizon specks in `horizonSpecks`, gated by `SpecksEnabled`.

Rebuild the `.cso` with `tools/EffectCompiler` after editing the shader.

## Documentation

A full Chinese implementation guide for programmers is included:

- [Markdown source](docs/BlackTheo-实现原理.md)
- [PDF version](docs/BlackTheo-实现原理.pdf)
- [Standalone HTML version](docs/BlackTheo-实现原理.html)

It covers the render hook, render-target ping-pong, geodesic integration, disk crossing, fBm
shading, screen-space lens falloff, entity lifecycle and common pitfalls.
