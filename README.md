# Antipolf

A golf puzzle game played inside a 3-sphere (S³). The world has no edge: hit the ball far enough
and it comes back from behind you. Lasers and mirrors use the same geometry, so a beam fired
round the world returns to its start, and the point opposite you (your *antipode*) is where
everything refocuses.

Built for StormHacks 2026, "Beyond Euclid" challenge. Play it at **antipolf.vercel.app**.

## The campaign

| Map | What it teaches |
|---|---|
| **Meadow** | Plain golf on a closed world. Learn to aim, loft and power a shot, and watch the ball come back round. |
| **Desert** | Laser puzzles only. Place mirrors and diffractors so the beam reaches the goal, using the way light wraps around S³. |
| **Aurora Basin** | Both together. Each cup is sealed until a laser puzzle lights its goal, then you play the hole. |

Mirrors are solid to the golf ball. Puzzles don't affect your score; only strokes count.

## Controls

**Golf**

| Input | Action |
|---|---|
| A / D or mouse left-right | Turn the shot |
| W / S or Up / Down | Raise / lower the launch angle |
| Mouse up-down | Tilt the camera only |
| Shift | Fine adjustment |
| Space | Start the power meter, press again to hit |
| Right-click | Cancel the power meter |
| Tab | Walk around freely (WASD, Shift to run, Space to jump) |
| F (E on the Meadow) | Glide back to the ball |
| B | Return the ball to the last shot (+1 stroke) |
| C | Reset camera pitch |
| R / N | Restart the hole / restart the course |
| M | Next map |
| G | Toggle graphics |
| L | Level select |
| Esc | Pause |
| Enter | Skip the hole result |

**Laser puzzles**

| Input | Action |
|---|---|
| 1 / 2 | Pick mirror / diffractor |
| Mouse wheel | Switch tool |
| Left-click | Place a piece, or pick one up |
| Q / E | Rotate the held piece (Shift for fine) |
| H | Show / hide a hint |
| R / N | Restart the puzzle / go to the first one |

## How it works

The world is the unit 3-sphere in R⁴. The ground is the great 2-sphere y = 0, and a full lap of the
world is 2πR metres.

- **Frames.** A position and heading is an SO(4) matrix with columns (right, up, forward, position).
  Moving the player or the ball is a rotation in a plane of R⁴ ([S3.cs](Assets/S3/Scripts/S3.cs)).
- **Props.** Trees, houses, rocks and fences are built from low-poly meshes authored in metres around
  a point. The exponential map at that point puts them onto S³, with normals parallel-transported along
  the same geodesic ([S3Scenery.cs](Assets/S3/Scripts/S3Scenery.cs), [MeshBuilder.cs](Assets/S3/Scripts/MeshBuilder.cs)).
- **Rendering.** Vertex shaders do the same mapping on the GPU every frame, so the whole world bends
  correctly around the camera ([Assets/S3/Shaders](Assets/S3/Shaders)).
- **Physics.** The ball is simulated directly on S³ along geodesics, with bounces off the ground,
  fences, mirrors and scenery ([S3Golf.cs](Assets/S3/Scripts/S3Golf.cs), [S3Ball.cs](Assets/S3/Scripts/S3Ball.cs)).
- **Lasers.** Beams travel along great circles, reflect off mirrors, and split at diffractors
  ([S3Laser.cs](Assets/S3/Scripts/S3Laser.cs), [S3Puzzle.cs](Assets/S3/Scripts/S3Puzzle.cs)).
  Levels are templates (`Reflection`, `LongWay`, `Antipode`, `Zigzag`, `FullCircle`, and the Aurora Basin
  locks) placed by frame ([S3Levels.cs](Assets/S3/Scripts/S3Levels.cs)).
- **UI.** A UI Toolkit HUD with a faceted low-poly look, one stylesheet of tokens, and a per-map accent
  colour.

## Running it

Requires **Unity 6000.5.2f1** (Unity 6, URP, the Input System package).

1. Clone the repo and open the `S3Golf` folder in Unity Hub.
2. Open `Assets/Scenes/S3World.unity` and press Play.
3. For a WebGL build, use the `Web - Desktop - Release` build profile under `Assets/Settings/Build Profiles`.

## Project layout

```
Assets/S3/Scripts   world math, golf, lasers, puzzles, scenery, audio, HUD logic
Assets/S3/Shaders   S³ vertex mapping, object / beam / glow shaders
Assets/S3/UI        UI Toolkit HUD, theme, fonts
Assets/S3/Post      per-map post-processing volumes
Assets/Audio        music and sound effects
Concepts/           HUD style concept and plan
Captures/           development screenshots and puzzle notes
```

## Credits

Team: Ryan Liu, Ethan Fu, Sean Huang, Eden Liang

Fonts: Lilita One (Juan Montoreano) and Rubik (Hubert & Fischer), both under the SIL Open Font
License 1.1, from Google Fonts.

Inspire by https://github.com/HackerPoet/HyperEngine

## Where it's going

Replacing the placeholder scenery with hand-made models, more levels, a daily level, and possibly a
Steam release.
