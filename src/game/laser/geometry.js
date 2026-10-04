import { dot, add, sub, mul, len, norm, cross, clamp } from '../../core/vector.js';

// The world is the unit 3-sphere S³ ⊂ R⁴ and the ground is the great 2-sphere w = 0,
// so a ground point is a unit 3-vector g, lifted to R⁴ as (g, 0). Geodesics are great
// circles x(t) = x cos t + d sin t. A laser sent along the ground stays on it, and the
// antipode of a ground point is on the ground too.

export const TAU = 2 * Math.PI;
export const UP = [0, 0, 0, 1];

/** The point `height` above ground point g. */
export const lift = (g, height) => [...mul(g, Math.cos(height)), Math.sin(height)];
export const pointAt = (x, d, t) => add(mul(x, Math.cos(t)), mul(d, Math.sin(t)));
export const tangentAt = (x, d, t) => sub(mul(d, Math.cos(t)), mul(x, Math.sin(t)));
export const reflect = (d, n) => sub(d, mul(n, 2 * dot(d, n)));

/** Geodesic distance along x(t) until it first passes below the ground, or null if it lies in the ground. */
export function groundHit(x, d) {
  const a = x[3], b = d[3];
  if (Math.abs(a) < 1e-9 && Math.abs(b) < 1e-9) return null;
  if (a <= 1e-9 && b < 0) return 0;
  // w(t) = R cos(t - φ) crosses zero going downward at φ + π/2.
  let t = Math.atan2(b, a) + Math.PI / 2;
  while (t <= 1e-7) t += TAU;
  while (t > TAU + 1e-7) t -= TAU;
  return t;
}

/** A unit vector orthogonal to three orthonormal vectors in R⁴. */
export function complement(a, b, c) {
  let best = null, bestLength = -1;
  for (let axis = 0; axis < 4; axis++) {
    let v = [0, 0, 0, 0]; v[axis] = 1;
    v = sub(v, add(add(mul(a, dot(v, a)), mul(b, dot(v, b))), mul(c, dot(v, c))));
    const length = len(v);
    if (length > bestLength) { best = mul(v, 1 / length); bestLength = length; }
  }
  return best;
}

// Ground (S²) helpers.
export const distance = (a, b) => Math.acos(clamp(dot(a, b), -1, 1));
/** Walk from `base` along tangent vector `v` for |v| radians. */
export function expMap(base, v) {
  const length = len(v);
  return length < 1e-12 ? [...base] : norm(add(mul(base, Math.cos(length)), mul(v, Math.sin(length) / length)));
}
/** Unit tangent at a pointing along the shortest geodesic toward b. */
export const direction = (a, b) => norm(sub(b, mul(a, dot(a, b))));
/** Parallel transport of tangent w from a to b along their geodesic. */
export const transport = (w, a, b) => sub(w, mul(add(a, b), dot(w, b) / (1 + dot(a, b))));
export const rotateAbout = (facing, position, angle) =>
  norm(add(mul(facing, Math.cos(angle)), mul(cross(position, facing), Math.sin(angle))));

export const Kind = { WALL: 0, EMITTER: 1, GOAL: 2, MIRROR: 3, DIFFRACTOR: 4, SCENERY: 5 };

/** How far blocks reach below the ground, so beams in the ground sphere strike them cleanly. */
export const SINK = .02;
export const MIRROR_EXTENT = [.005, .04, .05];
export const DIFFRACTOR_EXTENT = [.026, .026, .045];

/**
 * A box standing on the ground, defined in the gnomonic chart centred on its ground
 * point, where great circles are straight lines. Every face is part of a great
 * 2-sphere and the sides stand exactly perpendicular to the ground. `lo` and `hi` are
 * chart bounds along facing, side and up.
 */
export class Block {
  constructor(kind, position, facing, lo, hi, movable = false) {
    this.kind = kind; this.position = position; this.facing = facing;
    this.lo = lo; this.hi = hi; this.movable = movable;
    this.side = cross(position, facing);
    const corner = [0, 1, 2].map(i => Math.max(hi[i], -lo[i]));
    // Cosine of the angular radius of a ball enclosing the block.
    this.boundingCos = Math.cos(Math.atan(len(corner))) * .999;
  }

  /** Extents are angles: half length along facing, half width, height. */
  static withExtent(kind, position, facing, [x, y, z], movable = false) {
    const tx = Math.tan(x), ty = Math.tan(y);
    return new Block(kind, position, facing, [-tx, -ty, -Math.tan(SINK)], [tx, ty, Math.tan(z)], movable);
  }
  static emitter(p, f) { return Block.withExtent(Kind.EMITTER, p, f, [.028, .024, .032]); }
  static goal(p, f) { return Block.withExtent(Kind.GOAL, p, f, [.036, .036, .062]); }
  static mirror(p, f, movable = true) { return Block.withExtent(Kind.MIRROR, p, f, MIRROR_EXTENT, movable); }
  static diffractor(p, f, movable = true) { return Block.withExtent(Kind.DIFFRACTOR, p, f, DIFFRACTOR_EXTENT, movable); }
  /** A straight wall along the geodesic from a to b. */
  static wall(a, b, thickness = .012, height = .045) {
    const middle = norm(add(a, b));
    return Block.withExtent(Kind.WALL, middle, direction(middle, b), [distance(a, b) / 2 + thickness / 2, thickness / 2, height]);
  }

  rotated(angle) {
    return new Block(this.kind, this.position, rotateAbout(this.facing, this.position, angle), this.lo, this.hi, this.movable);
  }

  /** First entry of the geodesic x(t) = x cos t + d sin t into the box: { t, axis, normal } or null. */
  intersect(x, d) {
    const e1 = this.facing, e2 = this.side, c = this.position;
    const dot3 = (v, e) => v[0] * e[0] + v[1] * e[1] + v[2] * e[2];
    const pl = [dot3(x, e1), dot3(x, e2), x[3], dot3(x, c)];
    const dl = [dot3(d, e1), dot3(d, e2), d[3], dot3(d, c)];
    if (Math.hypot(pl[3], dl[3]) < this.boundingCos) return null;
    // Re-parametrise from the point q nearest the centre. On the hemisphere around the
    // centre the chart point moves along a straight line, linear in tan(t - tc), so the
    // ordinary slab test applies.
    const tc = Math.atan2(dl[3], pl[3]), ct = Math.cos(tc), st = Math.sin(tc);
    const qw = pl[3] * ct + dl[3] * st;
    let enter = -Infinity, exit = Infinity, axis = 0;
    const dir = [0, 0, 0];
    for (let i = 0; i < 3; i++) {
      const o = (pl[i] * ct + dl[i] * st) / qw;
      dir[i] = (dl[i] * ct - pl[i] * st) / qw;
      if (Math.abs(dir[i]) < 1e-14) {
        if (o < this.lo[i] || o > this.hi[i]) return null;
        continue;
      }
      const a = (this.lo[i] - o) / dir[i], b = (this.hi[i] - o) / dir[i];
      if (Math.min(a, b) > enter) { enter = Math.min(a, b); axis = i; }
      exit = Math.min(exit, Math.max(a, b));
    }
    if (!(enter <= exit) || enter === -Infinity) return null;
    let t = tc + Math.atan(enter);
    if (t <= 1e-7) t += TAU;
    const positive = dir[axis] < 0, bound = positive ? this.hi[axis] : this.lo[axis];
    const basis = axis === 0 ? [...e1, 0] : axis === 1 ? [...e2, 0] : UP;
    const normal = norm(mul(sub(basis, mul([...c, 0], bound)), positive ? 1 : -1));
    return { t, axis, normal };
  }

  footprintContains(p, margin) {
    const cp = dot(p, this.position);
    if (cp <= 0) return false;
    return Math.abs(dot(p, this.facing) / cp) <= this.hi[0] + margin
      && Math.abs(dot(p, this.side) / cp) <= this.hi[1] + margin;
  }

  footprintCorners() {
    const a = this.hi[0], b = this.hi[1];
    return [[a, b], [a, -b], [-a, -b], [-a, b]].map(([x, y]) =>
      norm(add(add(this.position, mul(this.facing, x)), mul(this.side, y))));
  }

  /** Separating-axis test between the two footprints, in this block's chart. */
  footprintOverlaps(other, margin) {
    const reach = Math.atan(Math.hypot(this.hi[0], this.hi[1])) + Math.atan(Math.hypot(other.hi[0], other.hi[1]));
    if (distance(this.position, other.position) > reach + margin) return false;
    const a = this.hi[0] + margin, b = this.hi[1] + margin;
    const mine = [[a, b], [a, -b], [-a, -b], [-a, b]];
    const theirs = other.footprintCorners().map(g => {
      const cp = dot(g, this.position);
      return [dot(g, this.facing) / cp, dot(g, this.side) / cp];
    });
    const axes = [[1, 0], [0, 1]];
    for (let i = 0; i < 2; i++) {
      const edge = [theirs[i + 1][0] - theirs[i][0], theirs[i + 1][1] - theirs[i][1]];
      axes.push([-edge[1], edge[0]]);
    }
    for (const [ax, ay] of axes) {
      const pa = mine.map(([x, y]) => x * ax + y * ay), pb = theirs.map(([x, y]) => x * ax + y * ay);
      if (Math.max(...pa) < Math.min(...pb) || Math.max(...pb) < Math.min(...pa)) return false;
    }
    return true;
  }
}
