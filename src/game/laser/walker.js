import { dot, add, sub, mul, norm, cross, clamp } from '../../core/vector.js';
import { lift } from './geometry.js';

export const EYE_HEIGHT = .07;

/** The player walking on the ground sphere, with a first-person camera. */
export class Walker {
  constructor(isBlocked) {
    this.isBlocked = isBlocked;
    this.reset([1, 0, 0], [0, 1, 0]);
  }

  reset(position, facing) {
    this.position = [...position];
    this.forward = [...facing];
    this.right = cross(this.position, this.forward);
    this.pitch = 0;
  }

  look(dx, dy) {
    const angle = dx * .0025, f = this.forward, r = this.right;
    this.forward = add(mul(f, Math.cos(angle)), mul(r, Math.sin(angle)));
    this.right = add(mul(f, -Math.sin(angle)), mul(r, Math.cos(angle)));
    this.pitch = clamp(this.pitch - dy * .0025, -1.56, 1.56);
  }

  update(forwardAmount, strafeAmount, dt, running) {
    const speed = running ? 2.2 : .7;
    if (forwardAmount) this.walk('forward', forwardAmount * speed * dt);
    if (strafeAmount) this.walk('right', strafeAmount * speed * dt);
    this.position = norm(this.position);
    this.forward = norm(sub(this.forward, mul(this.position, dot(this.forward, this.position))));
    this.right = cross(this.position, this.forward);
  }

  /** The eye sits EYE_HEIGHT above the ground; forward includes the pitch. Same frame layout as the golf camera. */
  get camera() {
    const h = EYE_HEIGHT, p = this.position, cp = Math.cos(this.pitch), sp = Math.sin(this.pitch);
    const groundForward = [...this.forward, 0], groundUp = [...mul(p, -Math.sin(h)), Math.cos(h)];
    return {
      eye: lift(p, h),
      forward: add(mul(groundForward, cp), mul(groundUp, sp)),
      up: add(mul(groundForward, -sp), mul(groundUp, cp)),
      right: [...this.right, 0],
      position: p,
    };
  }

  walk(axis, amount) {
    const tangent = this[axis];
    const next = add(mul(this.position, Math.cos(amount)), mul(tangent, Math.sin(amount)));
    // Refuse to step into an obstacle, but always let the player out of one.
    if (this.isBlocked(next) && !this.isBlocked(this.position)) return;
    this[axis] = add(mul(this.position, -Math.sin(amount)), mul(tangent, Math.cos(amount)));
    this.position = next;
  }
}
