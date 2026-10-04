# Spherical Plane World

The primary version is a modular WebGL2 browser game. The native macOS Metal
reference build remains available under `native/`.

Install [Node.js](https://nodejs.org/), open a terminal in this folder, and run:

```sh
npm run dev
```

Open the URL printed in the terminal (normally `http://127.0.0.1:8000/`). If
that port is already occupied, the server automatically tries the next one. No
`npm install` step is needed because the server has no third-party dependencies.

You can also use the launcher for your operating system:

- macOS: double-click `run.command`
- Windows: double-click `run.bat`
- Linux: run `./run.sh` (after `chmod +x run.sh` if needed)

The native Swift reference build is macOS-only; double-click
`run-native.command` to launch it.

## Project layout

```text
index.html                 Web entry page
src/
  app/                     Bootstrap, menu/HUD, and input handling
  core/                    Reusable math utilities
  game/world/              Scenery shared by every minigame (course boxes, hole)
  game/golf/               Golf state and physics
  game/laser/              Laser maze puzzles, beam tracing, and walking
  graphics/                Cameras, WebGL renderers, and shaders; world-shading.js
                           holds the GLSL for the shared world
  styles/                  Web interface styles
tools/                     Development server and validation scripts
native/                    Swift/Metal reference application
legacy/                    Archived standalone prototype
```

Run `npm run check` to syntax-check every JavaScript module, and
`npm run check:laser` to prove every laser level starts unsolved and is solved by
its stored solution.

Move the cursor horizontally to steer the center of the automatic aiming sweep,
and vertically to set the launch height, from nearly flat up to straight up.
Press Space once to lock its direction; the power meter then oscillates. Press
Space again to lock power and shoot.
Press V to toggle free-view mode. In free view, the cursor can look in any
direction, golf controls pause, and only the FPS display remains visible.
Press R to restart the hole and Escape to release the mouse.

Shots launch into the air. Gravity continuously points toward the nearest part
of the spherical ground, so a strong enough shot straight up passes through the
center of the world and comes down on the opposite side. Ground and platform impacts bounce until their
vertical energy is low enough to settle into rolling, while box-side impacts
ricochet with stronger restitution.

## Laser maze

The laser puzzle from the native *Antipode* prototype, set in the same world as the
golf course: the same ground, sky and course scenery, with the puzzles laid out
beside the tee. Laser light follows true geodesics of S³, so a beam sent along the
ground goes all the way round the world, and light leaving a point meets again at its
antipode. The golf boxes are solid: they stop light and you cannot walk through them.

Click to capture the mouse, then walk with WASD or the arrows (Shift runs). Click to
put down the hologram under the crosshair, or to pick up a placed block; right-click
or X also picks up. Q / E or the scroll wheel turn the block (Shift: fine). 1 / 2 / Tab
choose mirror or diffractor, R restarts, N / P change level, H hides the help.
