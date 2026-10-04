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
import {
  BALL_RADIUS, CLUB, CUP_RADIUS, GREEN_RADIUS, HOLES, SCENERY_SEED, TOTAL_PAR, groundDistance,
} from './course.js';
import {
  EY, EZ, WORLD_RADIUS, clamp, column, directionTo, distance, dot, foot, frameAt, height, isZero,
  lift, mix, multiply, ortho, planeRotation, scale, upAt, walk,
} from '../../core/s3.js';

const R = WORLD_RADIUS;
const STEP = 1 / 240;
const FAIRWAY_FRICTION = 1.7;      // rolling deceleration, m/s^2
const GREEN_FRICTION = 0.85;
const SINK_SPEED = 3.5;            // faster than this and the ball skips over the cup
const REACH = 2.6;                 // how close you must be to swing
const MIN_ANGLE = 2, MAX_ANGLE = 90, START_ANGLE = 20, AIM_DEGREES_PER_PIXEL = 0.12;
const LEASH = 4.5;                 // the flight camera trails the ball at most this far
const FOLLOW_MIN_HEIGHT = 1.2;
const FOLLOW_TRIGGER_HEIGHT = 2.5; // only shots that climb this high take the camera
const POLE_HEIGHT = 0.5 * Math.PI * R;

const lerp = (a, b, t) => a + (b - a) * clamp(t, 0, 1);
const inverseLerp = (a, b, v) => clamp((v - a) / (b - a), 0, 1);
const moveTowards = (from, to, step) => Math.abs(to - from) <= step ? to : from + Math.sign(to - from) * step;
const pingPong = t => { const l = t - Math.floor(t / 2) * 2; return 1 - Math.abs(l - 1); };
const smoothStep = t => t * t * (3 - 2 * t);

export function createGolfGame(canvas) {
  const { mesh, obstacles } = buildScenery(SCENERY_SEED);
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

  const hole = () => HOLES[holeIndex];
  const nearBall = () => groundDistance(player.foot, ball.foot) < REACH;
  const onGreen = point => distance(point, hole().cup) * R < GREEN_RADIUS;
  const shotDirection = () => ortho(player.forward, ball.position, upAt(ball.position));
  const shotSpeed = p => lerp(0.04, 1, p) * CLUB.maxSpeed;
  /** Camera pitch for the current launch height: tilts up a little, keeping the ball in view. */
  const aimPitch = () => lerp(-0.45, -0.32, inverseLerp(MIN_ANGLE, MAX_ANGLE, aimAngle));
  const say = (text, seconds) => { message = text; messageUntil = clock + seconds; };

  /**
   * Meter position that just carries a vertical shot over the pole: energy for the climb of
   * pi/2 * R, plus about 3.6 % for air drag.
   */
  function poleCrossingPower() {
    const v = Math.sqrt(2 * GRAVITY * (POLE_HEIGHT - BALL_RADIUS)) * 1.036;
    return inverseLerp(0.04, 1, v / CLUB.maxSpeed) + (v > CLUB.maxSpeed ? 1 : 0);
  }

  function startHole(index) {
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
    ball.launch(shotDirection(), shotSpeed(p), aimAngle * Math.PI / 180);
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
      ball.step(STEP, onGreen(ball.foot) ? GREEN_FRICTION : FAIRWAY_FRICTION, obstacles);
      shotMaxHeight = Math.max(shotMaxHeight, ball.heightM - BALL_RADIUS);
      groundedTime = ball.grounded ? groundedTime + STEP : 0;

      const toCup = distance(ball.foot, hole().cup) * R;
      if (toCup < CUP_RADIUS && ball.heightM < BALL_RADIUS * 1.6 && ball.speed < SINK_SPEED) {
        state = 'sunk';
        sunkTimer = 3;
        const over = strokes - hole().par;
        const name = strokes === 1 ? 'HOLE IN ONE!'
          : over <= -2 ? 'Eagle!' : over === -1 ? 'Birdie!' : over === 0 ? 'Par' : over === 1 ? 'Bogey' : `+${over}`;
        say(`${name}\nSunk in ${strokes}`, 3);
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

    const toBall = directionTo(camPosition, ball.position);
    camForward = isZero(toBall) ? ortho(camForward, camPosition) : toBall;

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
      if (sunkTimer <= 0) {
        if (holeIndex + 1 < HOLES.length) startHole(holeIndex + 1);
        else {
          state = 'finished';
          say(`Course complete!  ${total} strokes (par ${TOTAL_PAR}).\nPress N to play again.`, 1e6);
        }
      }
    }
    updateFollow(dt);
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
    renderer.draw(scene());
    input.endFrame();
    frames++;

    hud.update({
      hole: holeIndex + 1, holes: HOLES.length, par: hole().par, strokes, total,
      club: CLUB.name, pin: groundDistance(ball.foot, hole().cup), pole: POLE_HEIGHT,
      hint: hint(), message: clock < messageUntil ? message : '',
      aiming, aimAngle, charging, power, shotSpeed: shotSpeed(power), poleMark: poleCrossingPower(),
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
