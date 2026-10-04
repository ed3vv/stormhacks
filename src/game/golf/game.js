// Golf on the ground great-sphere of S^3: walk up to the ball, aim left/right by turning, set the
// launch height with the mouse, hold the left button to charge and release to swing.
// Ported from the Unity build's S3Golf.
//
// Straight up from any spot, a quarter lap (pi/2 * R) away, is the pole of the hemisphere: every
// direction there leads back down. A drive hard enough to cross the pole lands on the far side of
// the world, at the antipode for a vertical shot. Putts roll along great circles, so a hard putt
// goes all the way round and comes back from behind you.
import { createInput } from '../../app/input.js';
import { createHud } from '../../app/ui.js';
import { Renderer } from '../../graphics/renderer.js';
import { buildScenery } from '../../graphics/scenery.js';
import { Ball, GRAVITY } from './ball.js';
import { Player } from './player.js';
import { LAYOUT } from './layout.js';
import { BALL_RADIUS, CUP_RADIUS, HOLES, TOTAL_PAR, groundDistance, launchSpeed } from './course.js';
import {
  EY, EZ, WORLD_RADIUS, clamp, column, directionTo, distance, dot, foot, frameAt, height, isZero,
  lift, mix, multiply, ortho, planeRotation, scale, upAt, walk,
} from '../../core/s3.js';

const R = WORLD_RADIUS;
const STEP = 1 / 240;
const ROLL_FRICTION = 0.54 * R;    // rolling deceleration, m/s^2
const REACH = 2.6;                 // how close you must be to swing
const MIN_ANGLE = 2, MAX_ANGLE = 90, START_ANGLE = 20, AIM_DEGREES_PER_PIXEL = 0.12;
const AIM_PITCH_LOW = -0.45, AIM_PITCH_HIGH = 1.05;   // camera pitch at the flattest and steepest loft
const LEASH = 4.5;                 // the flight camera trails the ball at most this far
const FOLLOW_MIN_HEIGHT = 1.2;
const FOLLOW_TRIGGER_HEIGHT = 2.5; // only shots that climb this high take the camera
const POLE_HEIGHT = 0.5 * Math.PI * R;
const REPLAY_MIN_DISTANCE = 10;    // metres: shorter putts are not worth replaying
const REPLAY_SPEED = 0.5;          // half speed
const REPLAY_SAMPLE = 1 / 60;      // seconds of flight between recorded samples
const REPLAY_TAIL = 1;             // seconds held on the last frame before it runs again
const REPLAY_EYE_DISTANCE = 3.1;   // the world has radius 1 in the replay's own coordinates
const REPLAY_EYE_TILT = 22;         // degrees the replay camera starts above level with the cup's horizon
const REPLAY_LOOK = 0.25;          // degrees the replay camera orbits per pixel of mouse
const REPLAY_MAX_TILT = 85;        // short of straight above or below, where its up would vanish

/**
 * The replay's view of a point of S^3. The ground is the great 2-sphere y = 0, so dropping y
 * leaves a plain unit 2-sphere, and a point h radians up sits over the same ground point, pulled
 * in towards the centre. The playable hemisphere therefore becomes a solid unit ball - ground on
 * the outside, the pole at the very centre - which is what makes the whole flight visible from
 * outside at once.
 *
 * How far in is depth(h), and it must match the replay vertex shader. Merely dropping y would put
 * the point cos(h) out, which is flat at the ground: a house would be as wide as ever but paper
 * thin. This depth instead falls one for one with height at the ground, so things keep their
 * true proportions there, and still reaches the centre at the pole, a quarter lap up.
 */
const REPLAY_BEND = (Math.PI / 2 - 1) / (Math.PI / 2) ** 2;
const toBall = p => {
  const out = Math.hypot(p[0], p[2], p[3]);
  if (out < 1e-9) return [0, 0, 0];
  const h = Math.atan2(p[1], out), k = (1 - h + REPLAY_BEND * h * h) / out;
  return [p[0] * k, p[2] * k, p[3] * k];
};

const cross3 = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const unit3 = v => { const l = Math.hypot(...v); return l > 1e-9 ? v.map(c => c / l) : null; };

const lerp = (a, b, t) => a + (b - a) * clamp(t, 0, 1);
const inverseLerp = (a, b, v) => clamp((v - a) / (b - a), 0, 1);
const moveTowards = (from, to, step) => Math.abs(to - from) <= step ? to : from + Math.sign(to - from) * step;
const pingPong = t => { const l = t - Math.floor(t / 2) * 2; return 1 - Math.abs(l - 1); };
const smoothStep = t => t * t * (3 - 2 * t);

export function createGolfGame(canvas) {
  const { mesh, obstacles } = buildScenery(LAYOUT);
  const renderer = new Renderer(canvas, mesh);
  const hud = createHud();
  const input = createInput(canvas);
  const player = new Player();
  const ball = new Ball();

  let state = 'ready';               // ready | moving | sunk | finished
  let holeIndex = 0, strokes = 0, total = 0;
  let charging = false, aiming = false;
  let charge = 0, power = 0, accumulator = 0, moveTime = 0, sunkTimer = 0, aimAngle = START_ANGLE;
  let shotMaxHeight = 0, groundedTime = 0, lastShot = HOLES[0].tee;
  let followWeight = 0, camPosition = null, camUp = null, camForward = null;
  let flagFrame = null;
  let clock = 0, message = '', messageUntil = 0;
  let track = [], sinceSample = 0;                 // the flight being recorded, for the replay
  let replay = null;

  const hole = () => HOLES[holeIndex];
  const nearBall = () => groundDistance(player.foot, ball.foot) < REACH;
  const shotDirection = () => ortho(player.forward, ball.position, upAt(ball.position));
  /**
   * Camera pitch for the current launch height. It stays low and keeps the ball in view for the
   * flat shots you play most of the time, then swings back hard towards the top of the dial so a
   * near-vertical shot is actually aimed at something you can see: at 90 degrees the pole of the
   * hemisphere, straight up from the ball, sits inside the top of the frame.
   */
  const aimPitch = () => {
    const t = inverseLerp(MIN_ANGLE, MAX_ANGLE, aimAngle);
    return lerp(AIM_PITCH_LOW, AIM_PITCH_HIGH, t * t);
  };
  const say = (text, seconds) => { message = text; messageUntil = clock + seconds; };

/**
   * Meter position whose shot just carries straight up over the pole: the climb of pi/2 * R plus
   * a little for air drag, solved against the launch curve. Over 1 when the meter cannot reach it.
   */
  function poleCrossingPower() {
    const need = Math.sqrt(2 * GRAVITY * (POLE_HEIGHT - BALL_RADIUS)) * 1.02;
    if (launchSpeed(1) < need) return 2;
    let low = 0, high = 1;
    for (let i = 0; i < 30; i++) {
      const mid = (low + high) / 2;
      if (launchSpeed(mid) < need) low = mid; else high = mid;
    }
    return high;
  }

  function startHole(index) {
    replay = null;
    holeIndex = index;
    strokes = 0; aimAngle = START_ANGLE; followWeight = 0;
    ball.place(hole().tee);
    lastShot = ball.foot;
    state = 'ready';
    const toTee = directionTo(hole().cup, hole().tee);
    flagFrame = frameAt(hole().cup, EY, isZero(toTee) ? EZ : toTee);
    standAtBall();
    const span = groundDistance(hole().tee, hole().cup);
    say(`Hole ${index + 1}  ·  Par ${hole().par}  ·  ${Math.round(span)} m` +
      (span > 0.97 * Math.PI * R ? '\nThe cup is at your antipode. Every direction leads there,\nincluding straight up.' : ''), 5);
  }

  /**
   * Puts the player behind and a little left of the ball, facing along the line to the cup.
   * The side offset keeps the ball on screen and lets you read the launch height from the guide.
   */
  function standAtBall() {
    const base = ball.foot;
    let toCup = directionTo(base, hole().cup);
    if (isZero(toCup)) toCup = ortho(player.forward, base, EY);
    if (isZero(toCup)) toCup = ortho(EZ, base, EY);
    const right = column(frameAt(base, EY, toCup), 0);
    const offset = [0, 1, 2, 3].map(i => -1.5 * toCup[i] - 0.75 * right[i]);
    const span = Math.hypot(...offset);
    const stand = walk(base, scale(offset, 1 / span), span / R);
    player.teleport(stand, ortho(toCup, stand, EY), aimPitch());
  }

  // ------------------------------------------------------------------ aiming and swinging

  function updateAim(dt) {
    aiming = state === 'ready' && input.locked && nearBall();
    player.externalPitch = aiming;
    if (!aiming) return;
    aimAngle = clamp(aimAngle + input.mouseY * AIM_DEGREES_PER_PIXEL, MIN_ANGLE, MAX_ANGLE);
    player.pitch = moveTowards(player.pitch, aimPitch(), dt * 2);
  }

  function handleSwing(dt) {
    if (!input.locked || !nearBall()) { charging = false; return; }
    if (input.leftPressed) { charging = true; charge = 0; }
    if (!charging) return;
    if (input.rightPressed) { charging = false; return; }   // cancel
    charge += dt;
    power = pingPong(charge * 0.75);
    if (input.leftReleased) { charging = false; shoot(power); }
  }

  function shoot(p) {
    lastShot = ball.foot;
    track = [[...ball.position]];
    sinceSample = 0;
    ball.launch(shotDirection(), launchSpeed(p), aimAngle * Math.PI / 180);
    strokes++; total++;
    state = 'moving';
    moveTime = accumulator = 0;
    shotMaxHeight = groundedTime = 0;
  }

  function simulate(dt) {
    accumulator += Math.min(dt, 0.1);
    moveTime += dt;
    while (accumulator >= STEP) {
      accumulator -= STEP;
      ball.step(STEP, ROLL_FRICTION, obstacles);
      shotMaxHeight = Math.max(shotMaxHeight, ball.heightM - BALL_RADIUS);
      groundedTime = ball.grounded ? groundedTime + STEP : 0;
      sinceSample += STEP;
      if (sinceSample >= REPLAY_SAMPLE) { sinceSample -= REPLAY_SAMPLE; track.push([...ball.position]); }

      // the cup takes the ball whenever it rolls or lands in it; there is no lip-out
      const toCup = distance(ball.foot, hole().cup) * R;
      if (toCup < CUP_RADIUS && ball.heightM < BALL_RADIUS * 1.3) {
        state = 'sunk';
        sunkTimer = 3;
        const over = strokes - hole().par;
        const name = strokes === 1 ? 'HOLE IN ONE!'
          : over <= -2 ? 'Eagle!' : over === -1 ? 'Birdie!' : over === 0 ? 'Par' : over === 1 ? 'Bogey' : `+${over}`;
        say(`${name}\nSunk in ${strokes}`, 3);
        const length = groundDistance(lastShot, ball.foot);
        if (length > REPLAY_MIN_DISTANCE && track.length > 1) startReplay(length);
        return;
      }
      if (ball.grounded && ball.speed < 0.05) {
        ball.velocity = [0, 0, 0, 0];
        state = 'ready';
        if (shotMaxHeight > POLE_HEIGHT - 1 && groundDistance(lastShot, ball.foot) > POLE_HEIGHT)
          say('Over the pole!\nIt came down on the far side of the world.', 3.5);
        return;
      }
    }
    if (moveTime > 40) { ball.velocity = [0, 0, 0, 0]; state = 'ready'; }
  }

  // ------------------------------------------------------------------ instant replay

  /**
   * Watch the shot again from outside the world. In the replay's coordinates the whole of S^3
   * that you can stand in is a solid unit ball: the ground is its surface and height above the
   * ground is depth towards the centre. The camera sits off the ball, far enough to take all of
   * it in, and starts with the cup at the bottom of the frame - the mouse orbits it from there; the shell is drawn
   * as glass so the flight inside stays visible - a drive over the pole is seen passing clean
   * through the middle of the world.
   */
  function startReplay(length) {
    const from = toBall(track[0]), cup = unit3(toBall(hole().cup));
    // the cup hangs straight down, so every replay is framed the same way whatever the hole
    const up = cup.map(c => -c);
    // and the camera looks along the normal of the shot's plane, which shows the arc face on
    // rather than edge on and is square to that "down" by construction
    const normal = cross3(from, cup);
    // a shot straight to the antipode leaves the plane undefined: any direction level with the
    // cup will do, so take one from whichever world axis is least aligned with it
    const axis = Math.hypot(...normal) > 0.05
      ? unit3(normal)
      : unit3(cross3(cup, Math.abs(cup[1]) < 0.9 ? [0, 1, 0] : [1, 0, 0]));
    // it starts lifted a little above that level, looking slightly down on the world with the
    // cup still low, and the mouse orbits it around the world from there
    replay = {
      axis, side: cross3(up, axis), up, length,
      turn: 0, tilt: REPLAY_EYE_TILT,
      elapsed: 0, duration: (track.length - 1) * REPLAY_SAMPLE,
    };
    renderer.uploadTrail(track.map(toBall));
    state = 'replay';
  }

  function advanceReplay(dt) {
    // it runs again and again, a beat after the ball drops, until a click moves on
    if (input.clicked) { replay = null; advanceHole(); return; }
    replay.elapsed += dt * REPLAY_SPEED;
    if (replay.elapsed > replay.duration + REPLAY_TAIL * REPLAY_SPEED) replay.elapsed = 0;
    if (input.locked) {
      replay.turn -= input.mouseX * REPLAY_LOOK;
      replay.tilt = clamp(replay.tilt - input.mouseY * REPLAY_LOOK, -REPLAY_MAX_TILT, REPLAY_MAX_TILT);
    }
  }

  function replayEye() {
    const turn = replay.turn * Math.PI / 180, tilt = replay.tilt * Math.PI / 180;
    const level = replay.axis.map((c, i) => c * Math.cos(turn) + replay.side[i] * Math.sin(turn));
    return level.map((c, i) => (c * Math.cos(tilt) + replay.up[i] * Math.sin(tilt)) * REPLAY_EYE_DISTANCE);
  }

  function replayScene() {
    const progress = clamp(replay.elapsed / Math.max(replay.duration, 1e-6), 0, 1);
    const position = progress * (track.length - 1);
    const first = Math.floor(position), second = Math.min(first + 1, track.length - 1);
    const blend = position - first;
    const here = mix(track[first], track[second], blend);
    return {
      eye: replayEye(),
      up: replay.up,
      ball: toBall(here),
      cup: toBall(hole().cup),
      flag: flagFrame,
      drawnSamples: second + 1,
      progress,
    };
  }

  // ------------------------------------------------------------------ flight camera

  function updateFollow(dt) {
    const want = state === 'moving' && shotMaxHeight > FOLLOW_TRIGGER_HEIGHT && groundedTime < 0.35;
    if (want && followWeight <= 0) {
      const c = player.cameraFrame;
      camPosition = column(c, 3); camUp = column(c, 1); camForward = column(c, 2);
    }
    followWeight = moveTowards(followWeight, want ? 1 : 0, dt / (want ? 0.5 : 0.7));
    if (followWeight > 0) trackBall(dt);
  }

  /**
   * Trail the ball on a leash, look at it, and keep "up" pointing away from the nearest ground.
   * When the camera crosses the pole, up flips and the view rolls over smoothly.
   */
  function trackBall(dt) {
    const d = distance(camPosition, ball.position) * R;
    if (d > LEASH) camPosition = walk(camPosition, directionTo(camPosition, ball.position), (d - LEASH) / R);
    if (height(camPosition, R) < FOLLOW_MIN_HEIGHT) camPosition = lift(foot(camPosition), FOLLOW_MIN_HEIGHT / R);

    for (const o of obstacles) {                        // stay out of trees and houses
      if (o.height < 2) continue;
      const clearance = o.radius + 1.6, base = foot(camPosition), h = height(camPosition, R);
      const gap = distance(base, o.foot) * R;
      if (gap >= clearance || gap < 1e-4 || h > o.height + 1.5) continue;
      camPosition = lift(walk(base, scale(directionTo(base, o.foot), -1), (clearance - gap) / R), h / R);
    }

    const toward = directionTo(camPosition, ball.position);
    camForward = isZero(toward) ? ortho(camForward, camPosition) : toward;

    let previousUp = ortho(camUp, camPosition, camForward);
    if (isZero(previousUp)) previousUp = column(frameAt(camPosition, camUp, camForward), 1);
    const away = [0, 1, 2, 3].map(i => EY[i] - camPosition[1] * camPosition[i]);
    let target = Math.hypot(...away) > 1e-4 ? ortho(away, camPosition, camForward) : previousUp;
    if (isZero(target)) target = previousUp;

    // rotate previousUp towards target about the view axis (handles a full 180 degree roll)
    const angle = Math.acos(clamp(dot(previousUp, target), -1, 1));
    let side = ortho(target, camPosition, camForward, previousUp);
    if (isZero(side)) side = column(frameAt(camPosition, previousUp, camForward), 0);
    const turn = angle * (1 - Math.exp(-5 * dt));
    camUp = [0, 1, 2, 3].map(i => Math.cos(turn) * previousUp[i] + Math.sin(turn) * side[i]);
  }

  function viewFrame() {
    const playerCamera = player.cameraFrame;
    if (followWeight <= 0) return playerCamera;
    const follow = frameAt(camPosition, camUp, camForward);
    const s = smoothStep(followWeight);
    if (s >= 1) return follow;
    const a = column(playerCamera, 3);
    const position = walk(a, directionTo(a, camPosition), distance(a, camPosition) * s);
    return frameAt(position, mix(column(playerCamera, 1), camUp, s), mix(column(playerCamera, 2), camForward, s));
  }

  // ------------------------------------------------------------------ frame

  function hint() {
    if (!input.locked) return 'Click to capture the mouse';
    if (state === 'ready' && nearBall())
      return charging ? 'Release to swing  (right-click cancels)'
        : 'Turn to aim · mouse up/down sets height · hold left mouse to charge';
    if (state === 'ready') return `Walk to the ball (${Math.round(groundDistance(player.foot, ball.foot))} m away), or press R to jump to it`;
    if (state === 'moving') return `Ball height ${Math.max(0, ball.heightM - BALL_RADIUS).toFixed(1)} m`;
    return '';
  }

  function step(dt) {
    // the replay takes the whole screen; nothing in the world moves while it runs
    if (state === 'replay') { advanceReplay(dt); return; }

    if (input.pressed('KeyR') && state !== 'sunk') standAtBall();
    if (input.pressed('KeyB') && (state === 'ready' || state === 'moving') && strokes > 0) {
      ball.place(lastShot);
      state = 'ready';
      followWeight = 0;
      strokes++; total++;
      say('Ball returned to its last spot (+1 stroke)', 2.5);
    }
    if (input.pressed('KeyN')) { total = 0; startHole(0); }
    if (input.pressed('Space')) player.jump();

    updateAim(dt);
    if (input.locked) player.look(input.mouseX, input.mouseY);
    player.update(dt, input, obstacles);

    if (state === 'ready') handleSwing(dt);
    else if (state === 'moving') simulate(dt);
    else if (state === 'sunk') {
      sunkTimer -= dt;
      if (sunkTimer <= 0) advanceHole();
    }
    updateFollow(dt);
  }

  function advanceHole() {
    if (holeIndex + 1 < HOLES.length) startHole(holeIndex + 1);
    else {
      state = 'finished';
      say(`Course complete!  ${total} strokes (par ${TOTAL_PAR}).\nPress N to play again.`, 1e6);
    }
  }

  function scene() {
    const ready = state === 'ready' && nearBall();
    const direction = ready ? shotDirection() : null;
    const show = direction && !isZero(direction);
    return {
      view: viewFrame(),
      cup: hole().cup,
      flag: flagFrame,
      ball: state === 'sunk' || state === 'finished' ? null : frameAt(ball.position, upAt(ball.position), EZ),
      arrow: show ? frameAt(ball.foot, EY, direction) : null,
      guide: show && aiming
        ? multiply(frameAt(ball.position, upAt(ball.position), direction), planeRotation(EZ, EY, aimAngle * Math.PI / 180))
        : null,
    };
  }

  let animationFrame = 0, last = performance.now(), statsTime = last, frames = 0;
  const frame = now => {
    const dt = Math.min((now - last) / 1000, 0.05);
    last = now;
    clock += dt;
    step(dt);
    if (state === 'replay') renderer.drawReplay(replayScene()); else renderer.draw(scene());
    input.endFrame();
    frames++;

    hud.update({
      hole: holeIndex + 1, holes: HOLES.length, par: hole().par, strokes, total,
      pin: groundDistance(ball.foot, hole().cup), pole: POLE_HEIGHT,
      hint: hint(), message: clock < messageUntil ? message : '',
      aiming, aimAngle, charging, power, shotSpeed: launchSpeed(power), poleMark: poleCrossingPower(),
      replay: replay && { length: replay.length },
    });
    if (now - statsTime >= 250) { hud.setFps(frames, now - statsTime); statsTime = now; frames = 0; }
    animationFrame = requestAnimationFrame(frame);
  };

  startHole(0);
  animationFrame = requestAnimationFrame(frame);
  return {
    stop() { cancelAnimationFrame(animationFrame); input.dispose(); },
  };
}
