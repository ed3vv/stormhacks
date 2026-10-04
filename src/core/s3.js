// Math on the unit 3-sphere S^3 in R^4, in the conventions of the Unity build this game is a
// port of.
//
//   * The ground is the great 2-sphere y = 0. "Up" off the ground is the +e_y side.
//   * A frame is an SO(4) matrix whose columns are (right, up, forward, position).
//     The identity frame sits at e_w looking along e_z with e_y up.
//   * Local (Euclidean, metre) coordinates around a frame are mapped onto S^3 with the
//     exponential map, so a prop authored in metres is placed by its frame alone.

// Radius of the world in metres: walking 2*pi*R in a straight line brings you back.
export const WORLD_RADIUS = 12;

export const EX = [1, 0, 0, 0], EY = [0, 1, 0, 0], EZ = [0, 0, 1, 0], EW = [0, 0, 0, 1];
export const ZERO = [0, 0, 0, 0];

export const clamp = (x, a, b) => Math.max(a, Math.min(b, x));
export const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2] + a[3] * b[3];
export const scale = (a, s) => [a[0] * s, a[1] * s, a[2] * s, a[3] * s];
export const add = (a, b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2], a[3] + b[3]];
export const subtract = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2], a[3] - b[3]];
export const length = a => Math.hypot(a[0], a[1], a[2], a[3]);
export const mix = (a, b, t) => [0, 1, 2, 3].map(i => a[i] + (b[i] - a[i]) * t);
export const isZero = a => !a[0] && !a[1] && !a[2] && !a[3];
export const normalize = a => { const l = length(a); return l > 1e-9 ? scale(a, 1 / l) : ZERO; };

// Matrices are column-major, m[column * 4 + row], like the arrays WebGL expects.
export const identity = () => new Float64Array([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);
export const fromColumns = (a, b, c, d) => new Float64Array([...a, ...b, ...c, ...d]);
export const column = (m, i) => [m[i * 4], m[i * 4 + 1], m[i * 4 + 2], m[i * 4 + 3]];

export function multiply(a, b) {
  const out = new Float64Array(16);
  for (let c = 0; c < 4; c++) for (let r = 0; r < 4; r++) {
    let sum = 0;
    for (let k = 0; k < 4; k++) sum += a[k * 4 + r] * b[c * 4 + k];
    out[c * 4 + r] = sum;
  }
  return out;
}

export function transform(m, v) {
  const out = [0, 0, 0, 0];
  for (let r = 0; r < 4; r++) out[r] = m[r] * v[0] + m[4 + r] * v[1] + m[8 + r] * v[2] + m[12 + r] * v[3];
  return out;
}

export function determinant(m) {
  const at = (r, c) => m[c * 4 + r];
  let total = 0;
  for (let c = 0; c < 4; c++) {
    const [p, q, s] = [0, 1, 2, 3].filter(j => j !== c);
    total += (c % 2 ? -1 : 1) * at(0, c) * (
      at(1, p) * (at(2, q) * at(3, s) - at(2, s) * at(3, q)) -
      at(1, q) * (at(2, p) * at(3, s) - at(2, s) * at(3, p)) +
      at(1, s) * (at(2, p) * at(3, q) - at(2, q) * at(3, p)));
  }
  return total;
}

/**
 * Rotation in the plane of orthonormal a, b by angle t:
 * a -> a cos t + b sin t, b -> b cos t - a sin t, identity on the complement.
 */
export function planeRotation(a, b, t) {
  const c = Math.cos(t) - 1, s = Math.sin(t), m = identity();
  for (let i = 0; i < 4; i++) for (let j = 0; j < 4; j++)
    m[j * 4 + i] += c * (a[i] * a[j] + b[i] * b[j]) + s * (b[i] * a[j] - a[i] * b[j]);
  return m;
}

/**
 * Exponential map at e_w: a local tangent vector in metres becomes a point of S^3, and its
 * normal is parallel-transported along the same geodesic.
 */
export function exponentialWithNormal(v, n, radius) {
  const u = [v[0] / radius, v[1] / radius, v[2] / radius], r = Math.hypot(...u);
  if (r < 1e-7) return { point: EW, normal: [n[0], n[1], n[2], 0] };
  const d = [u[0] / r, u[1] / r, u[2] / r], s = Math.sin(r), c = Math.cos(r);
  const along = n[0] * d[0] + n[1] * d[1] + n[2] * d[2];
  return {
    point: [d[0] * s, d[1] * s, d[2] * s, c],
    normal: [0, 1, 2].map(i => n[i] - along * d[i] * (1 - c)).concat(-along * s),
  };
}

/** Geodesic distance (radians) between two points of S^3. */
export const distance = (a, b) => 2 * Math.asin(clamp(length(subtract(a, b)) * 0.5, 0, 1));
/** Component of v tangent to S^3 at p. */
export const tangent = (p, v) => subtract(v, scale(p, dot(p, v)));
/** Unit tangent at p along the shorter geodesic to q (zero if degenerate). */
export const directionTo = (p, q) => normalize(tangent(p, q));
/** Point at distance t (radians) from p along unit tangent d. */
export const walk = (p, d, t) => add(scale(p, Math.cos(t)), scale(d, Math.sin(t)));
/** Closest ground point below p. */
export const foot = p => normalize([p[0], 0, p[2], p[3]]);
/** The point at height h (radians) straight above a ground point. */
export const lift = (ground, h) => add(scale(ground, Math.cos(h)), scale(EY, Math.sin(h)));
/** Unit tangent at p pointing away from the ground. */
export const upAt = p => normalize(subtract(EY, scale(p, p[1])));
/** Height of p above the ground, in metres. */
export const height = (p, radius) => Math.asin(clamp(p[1], -1, 1)) * radius;

/** Gram-Schmidt v against an orthonormal set; zero if v lies in their span. */
export function ortho(v, ...basis) {
  let out = v;
  for (const b of basis) out = subtract(out, scale(b, dot(out, b)));
  return length(out) > 1e-6 ? normalize(out) : ZERO;
}

function anyOrtho(...basis) {
  let best = ZERO, bestLength = -1;
  for (const e of [EX, EY, EZ, EW]) {
    let v = e;
    for (const b of basis) v = subtract(v, scale(b, dot(v, b)));
    if (length(v) > bestLength) { bestLength = length(v); best = v; }
  }
  return normalize(best);
}

/** Right-handed frame at pos with the given up and (approximate) forward. */
export function frameAt(position, up, forwardHint) {
  const pos = normalize(position);
  let u = ortho(up, pos);
  if (isZero(u)) u = anyOrtho(pos);
  let f = ortho(forwardHint, pos, u);
  if (isZero(f)) f = anyOrtho(pos, u);
  const r = anyOrtho(pos, u, f);
  const frame = fromColumns(r, u, f, pos);
  if (determinant(frame) < 0) frame.set(scale(r, -1), 0);
  return frame;
}

/**
 * Frame standing on the ground at local ground coordinates (x, z) metres around e_w,
 * heading yaw radians from +z towards +x.
 */
export function groundFrame(x, z, yaw, radius) {
  const d = Math.hypot(x, z);
  const spin = planeRotation(EZ, EX, yaw);
  if (d < 1e-6) return spin;
  return multiply(planeRotation(EW, [x / d, 0, z / d, 0], d / radius), spin);
}

export const groundPoint = (x, z, radius) => column(groundFrame(x, z, 0, radius), 3);
