# Spherical Plane World

The primary version is a modular WebGL2 browser game. The native macOS Metal
reference build remains available in `Sources/SphericalWorld`.

Double-click `run.command`, or run a local web server from Terminal:

```sh
python3 -m http.server 8000
```

Then open `http://127.0.0.1:8000/`. To run the native reference build instead,
double-click `run-native.command`.

Move the cursor horizontally to steer the center of the automatic aiming sweep.
Press Space once to lock its direction; the power meter then oscillates. Press
Space again to lock power and shoot.
Press R to restart the hole and Escape to release the mouse.

Shots launch into the air. Gravity continuously points toward the nearest part
of the spherical ground. Ground and platform impacts bounce until their
vertical energy is low enough to settle into rolling, while box-side impacts
ricochet with stronger restitution.
