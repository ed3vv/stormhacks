// First-person walker on the ground great-sphere of S^3, ported from the Unity build's S3Player.
//
// The player is an SO(4) frame (right, up, forward, foot). Since the ground is the great sphere
// y = 0, "up" is always e_y. Walking a distance s is a rotation in the plane (foot, direction),
// applied in the local frame, which parallel-transports heading for free. The eye is the foot
// lifted along up, then pitched.
import {
  EW, EX, EY, EZ, WORLD_RADIUS, clamp, column, directionTo, distance, dot, foot, frameAt,
  identity, multiply, planeRotation, scale,
} from '../../core/s3.js';

const R = WORLD_RADIUS;
export const EYE_HEIGHT = 1.7;
export const WALK_SPEED = 4.5;
export const RUN_MULTIPLIER = 2.2;
export const LOOK_SENSITIVITY = 0.0022;   // radians per pixel
const JUMP_SPEED = 4.5;
const GRAVITY = 12;
const BODY_RADIUS = 0.35;

export class Player {
  constructor() {
    this.ground = identity();
    this.pitch = -0.08;
    this.jumpHeight = 0;
    this.jumpVelocity = 0;
    this.externalPitch = false;   // while true the golf aim owns mouse Y
  }

  get foot() { return column(this.ground, 3); }
  get forward() { return column(this.ground, 2); }
  get right() { return column(this.ground, 0); }

  /** Eye frame: the foot lifted to eye height, then pitched. */
  get cameraFrame() {
    return multiply(multiply(this.ground, planeRotation(EW, EY, (EYE_HEIGHT + this.jumpHeight) / R)),
      planeRotation(EZ, EY, this.pitch));
  }

  /** Turn right by angle radians. */
  turn(angle) { this.ground = multiply(this.ground, planeRotation(EZ, EX, angle)); }

  look(dx, dy) {
    this.turn(dx * LOOK_SENSITIVITY);
    if (!this.externalPitch) this.pitch = clamp(this.pitch + dy * LOOK_SENSITIVITY, -1.5, 1.5);
  }

  /** Walk dx metres right and dz metres forward along a geodesic. */
  move(dx, dz) {
    const d = Math.hypot(dx, dz);
    if (d < 1e-7) return;
    this.ground = multiply(this.ground, planeRotation(EW, [dx / d, 0, dz / d, 0], d / R));
  }

  jump() { if (this.jumpHeight <= 0) this.jumpVelocity = JUMP_SPEED; }

  update(dt, input, obstacles) {
    const ahead = (input.held('KeyW') ? 1 : 0) - (input.held('KeyS') ? 1 : 0);
    const side = (input.held('KeyD') ? 1 : 0) - (input.held('KeyA') ? 1 : 0);
    if (ahead || side) {
      const speed = WALK_SPEED * (input.held('ShiftLeft') || input.held('ShiftRight') ? RUN_MULTIPLIER : 1) * dt;
      const n = Math.hypot(ahead, side);
      this.move(side / n * speed, ahead / n * speed);
    }

    if (this.jumpHeight > 0 || this.jumpVelocity > 0) {
      this.jumpVelocity -= GRAVITY * dt;
      this.jumpHeight += this.jumpVelocity * dt;
      if (this.jumpHeight <= 0) { this.jumpHeight = 0; this.jumpVelocity = 0; }
    }

    this.collide(obstacles);
    this.ground = frameAt(foot(this.foot), EY, this.forward);   // keep the frame exactly orthonormal
  }

  collide(obstacles) {
    for (const o of obstacles) {
      if (o.height < 0.45 || this.jumpHeight > o.height) continue;   // step or jump over low things
      const d = distance(this.foot, o.foot) * R, min = o.radius + BODY_RADIUS;
      if (d >= min || d < 1e-5) continue;
      const away = scale(directionTo(this.foot, o.foot), -1), push = min - d;
      this.move(dot(away, this.right) * push, dot(away, this.forward) * push);
    }
  }

  /** Stand at a ground point facing along a tangent direction. */
  teleport(groundPoint, forward, pitch) {
    this.ground = frameAt(foot(groundPoint), EY, forward);
    this.pitch = pitch;
    this.jumpHeight = this.jumpVelocity = 0;
  }
}
