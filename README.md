# Spherical Plane World

The primary version is a modular WebGL2 browser game. The native macOS Metal
reference build remains available in `Sources/SphericalWorld`.

Install [Node.js](https://nodejs.org/), open a terminal in this folder, and run:

```sh
npm run dev
```

Then open `http://127.0.0.1:8000/`. No `npm install` step is needed because the
server has no third-party dependencies.

You can also use the launcher for your operating system:

- macOS: double-click `run.command`
- Windows: double-click `run.bat`
- Linux: run `./run.sh` (after `chmod +x run.sh` if needed)

The native Swift reference build is macOS-only; double-click
`run-native.command` to launch it.

Move the cursor horizontally to steer the center of the automatic aiming sweep.
Press Space once to lock its direction; the power meter then oscillates. Press
Space again to lock power and shoot.
Press R to restart the hole and Escape to release the mouse.

Shots launch into the air. Gravity continuously points toward the nearest part
of the spherical ground. Ground and platform impacts bounce until their
vertical energy is low enough to settle into rolling, while box-side impacts
ricochet with stronger restitution.
