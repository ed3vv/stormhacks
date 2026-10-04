# S3Golf HUD rebuild — plan

Style concept: `Concepts/hud_style.html` (serve the S3Golf folder and open it; it overlays the kit on real captures).

## Why the current HUD looks low-res
- IMGUI scaled with `GUI.matrix` (S3Hud.Begin): glyphs are rasterised at 12–46 px and then bilinear-stretched; the scale is clamped to 0.6–2.5, so small windows get an oversized blurry HUD and 4K gets a stretched one.
- Panels, key caps and icons are tiny runtime textures (24–64 px) 9-sliced and stretched.
- "Shadows" are the same text redrawn 1–2 px offset; panels are 58 % translucent black, which goes grey and muddy on snow and sand.
- Generic Roboto + translucent rounded rectangles + pills = the "default template" look, with no tie to the game's art.
- The editor Game view is 880×406, which makes all of the above worse while testing.

## Art direction: "Faceted golf"
Borrow from what's on screen: flat-shaded low-poly facets, the red pennant flag, the chunky cream-and-red ball.

- **Shapes:** chamfered polygons (no round corners), each split into a lighter top facet and a darker body, like a flat-shaded rock. Hard 6 px "extrusion" drop under every block instead of soft glows.
- **Colour:** solid deep ink `#1B2033` panels (opaque, so they read on grass, sand and snow), cream `#FFF3D6` text, gold `#FFC23D` highlight. Each map's accent colours the pennant: Meadow flag red, Desert terracotta, Eternal Poles ice/aurora.
- **Type:** Lilita One (chunky display, for numbers and titles) + Rubik 500/700/800 (labels, letter-spaced caps). Both SIL OFL from Google Fonts.
- **Signature pieces:**
  - Scorecard = a pennant on a pole, with diamond progress pips (done / current / to go) and stat chips underneath.
  - Power meter = 20 skewed segments green→gold→red, an ice-blue "over the pole" notch, a ghost tick at last shot's power.
  - Key caps = cream 3D keys with a bottom bevel.
  - Centre messages = a gold ribbon banner that drops in with an overshoot, smaller and higher than today.
  - Markers = faceted icons (flag pennant, crystal gem, pole ring) with slanted ink tags.
  - Puzzle inventory = square tool slots with drawn icons and counts; an empty slot greys out instead of highlighting.
  - Crosshair = small diamond.

## Tech: UI Toolkit (Unity 6.5)
- One `UIDocument` + `PanelSettings` (Scale With Screen Size, reference 1920×1080, match 0.5). SDF text is sharp at every resolution.
- `Theme.uss` holds every token (colours, spacing 4/8/16/24/32, type scale, the extrusion depth). The map accent is swapped with a root class (`.meadow`, `.desert`, `.basin`).
- Faceted shapes come from small custom elements drawn with `generateVisualContent` / `Painter2D`, which are vector and antialiased: `FacetPanel`, `Pennant`, `SegmentBar`, `MarkerIcon`. Everything else is plain USS (borders, key caps, layout).
- The HUD root uses `pickingMode = Ignore` so it never eats the click that locks the mouse; only the menus receive pointer events.
- Motion uses USS transitions and `experimental.animation` (banner drop, pip fill, slot select).

## Phases
0. **Foundation:** import Lilita One + Rubik (needs your OK to download from Google Fonts), create the font assets, then add `Assets/S3/UI/` with PanelSettings, `Theme.uss`, `Hud.uxml`, `Hud.uss`, and add a UIDocument to `S3World.unity`. Set the Game view to 1920×1080.
1. **Separate logic from drawing:** add a `HudState` struct that S3Golf fills each frame, and a new `S3HudView.cs` that binds it to the UI. Move marker projection out of `DrawMarker` into a helper both can use.
2. **Gameplay HUD:** pennant scorecard, stat badges, power meter, hint bar, banner, markers (pooled), crosshair, inventory, puzzle hint card, "Click to play".
3. **Screens:** level select (L) as a full-screen board with one column per map, faceted cards, best score and completion; pause/controls (Esc) as its own screen; an end-of-map golf scorecard table (par, strokes, ± coloured).
4. **Remove the old HUD:** delete `S3Hud.cs` and the `OnGUI` block in S3Golf.cs (lines ~1053–1363).
5. **Verify:** Play Mode screen captures at 1280×720, 1920×1080 and 3840×2160 for each map and state (aim, power, flight, puzzle, sealed cup, banner, menus, scorecard), with before/after comparisons.

## Optional follow-up
- 3D anti-aliasing: `S3Setup` forces MSAA off (`S3Setup.cs:109`). Check whether the S³ projection shaders handle MSAA 4× or FXAA/SMAA; that would also make the whole game look sharper.
