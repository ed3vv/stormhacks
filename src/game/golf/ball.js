// A golf ball moving in S^3 under gravity, ported from the Unity build's S3Ball.
//
// State is a point on S^3 and a tangent velocity in m/s. Between forces the ball follows a great
// circle exactly: each step is a rotation in the plane (pos, vel). Gravity pulls towards the
// nearest ground. Ground contact keeps the centre exactly one radius up; bounces, rolling
// friction and obstacle collisions are applied there.
import { BALL_RADIUS } from './course.js';
import {
  EW, EX, EY, EZ, WORLD_RADIUS, add, directionTo, distance, dot, foot, height, isZero, length,
  lift, ortho, planeRotation, scale, subtract, tangent, transform, upAt, walk, ZERO,
} from '../../core/s3.js';

const R = WORLD_RADIUS;
export const GRAVITY = 9.81;
const RESTITUTION = 0.42;
const BOUNCE_GRIP = 0.8;      // fraction of tangential speed kept through a bounce
const BOUNCE_SPEED = 0.9;     // below this normal speed the ball rolls instead of bouncing
const AIR_DRAG = 0.06;        // per second

export class Ball {
  constructor() { this.radius = BALL_RADIUS; this.velocity = ZERO; this.lastUp = EY; this.place(EW); }

  get foot() { return foot(this.position); }
  get heightM() { return height(this.position, R); }
  get speed() { return length(this.velocity); }

  place(groundPoint) {
    this.position = lift(foot(groundPoint), this.radius / R);
    this.velocity = ZERO;
    this.lastUp = upAt(this.position);
    this.grounded = true;
  }

  /**
   * Unit tangent pointing away from the nearest ground. Past the pole of the hemisphere (a
   * quarter lap straight up) the nearest ground is on the far side, so this flips by itself.
   * Exactly at the pole every direction is "down": the ball keeps falling the way it is heading,
   * or, if it is at rest there, the way gravity last pointed.
   */
  gravityUp() {
    const u = subtract(EY, scale(this.position, this.position[1]));
    const m = length(u);
    if (m > 1e-5) return this.lastUp = scale(u, 1 / m);
    const s = this.speed;
    if (s > 1e-4) return this.lastUp = scale(this.velocity, -1 / s);
    let t = ortho(this.lastUp, this.position);
    if (isZero(t)) t = ortho(EZ, this.position);
    if (isZero(t)) t = ortho(EX, this.position);
    return this.lastUp = t;
  }

  step(dt, rollFriction, obstacles) {
    this.velocity = subtract(this.velocity, scale(this.gravityUp(), GRAVITY * dt));
    this.velocity = scale(this.velocity, Math.max(0, 1 - AIR_DRAG * dt));
    this.velocity = tangent(this.position, this.velocity);

    // free flight along the great circle
    const speed = this.speed;
    if (speed > 1e-7) {
      const rotation = planeRotation(this.position, scale(this.velocity, 1 / speed), speed * dt / R);
      this.position = transform(rotation, this.position);
      this.velocity = transform(rotation, this.velocity);
      this.lastUp = transform(rotation, this.lastUp);   // parallel transport along the step
    }

    // ground contact
    this.grounded = false;
    if (this.heightM <= this.radius) {
      this.position = lift(this.foot, this.radius / R);
      const n = upAt(this.position);
      this.velocity = tangent(this.position, this.velocity);
      const vn = dot(this.velocity, n);
      if (vn < -BOUNCE_SPEED) {
        this.velocity = subtract(this.velocity, scale(n, (1 + RESTITUTION) * vn));
        const vt = subtract(this.velocity, scale(n, dot(this.velocity, n)));
        this.velocity = subtract(this.velocity, scale(vt, 1 - BOUNCE_GRIP));
      } else {
        this.velocity = subtract(this.velocity, scale(n, vn));
        this.grounded = true;
        const rolling = this.speed;
        if (rolling > 1e-6) this.velocity = scale(this.velocity, Math.max(0, rolling - rollFriction * dt) / rolling);
      }
    }

    // trunks, rocks and houses as vertical cylinders
    for (const o of obstacles) {
      const h = this.heightM;
      if (h - this.radius > o.height) continue;
      const base = this.foot;
      const d = distance(base, o.foot) * R, min = o.radius + this.radius;
      if (d >= min || d < 1e-5) continue;
      const away = scale(directionTo(base, o.foot), -1);
      this.position = lift(walk(base, away, (min - d) / R), h / R);
      const a = ortho(away, this.position);
      const va = dot(this.velocity, a);
      if (va < 0) this.velocity = subtract(this.velocity, scale(a, 1.55 * va));
      this.velocity = tangent(this.position, this.velocity);
    }
  }

  /** Launch at the given speed along horizontal direction dir with loft (radians). */
  launch(direction, speed, loft) {
    const up = upAt(this.position);
    const d = ortho(direction, this.position, up);
    this.velocity = scale(add(scale(d, Math.cos(loft)), scale(up, Math.sin(loft))), speed);
    this.grounded = false;
  }
}
