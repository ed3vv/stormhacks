# Inverse Golf

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
  game/golf/               Golf state, physics, and course data
  graphics/                Camera, WebGL renderer, and shaders
  styles/                  Web interface styles
tools/                     Development server and validation scripts
native/                    Swift/Metal reference application
legacy/                    Archived standalone prototype
```

Run `npm run check` to syntax-check every JavaScript module.

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
