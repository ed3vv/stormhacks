// The map: the ground great-sphere and the low-poly props that dress it - houses, trees, rocks,
// mushrooms and grass, in the style of Hyperbolica. Ported from the Unity build's S3Scenery.
//
// What stands where comes from layout.js by way of placement.js; this module only turns each
// resolved prop into triangles. Each prop is authored in plain local metres, placed on the ground
// great-sphere by a frame, and baked into one shared mesh. The solid ones also report themselves
// as vertical cylinders, which is what the player and the ball actually collide with.
import { MeshBuilder, MeshData, bakeLocal, shade } from './mesh-builder.js';
import { BALL_RADIUS } from '../game/golf/course.js';
import { houseShape, propSize, randomSource, resolveLayout } from '../game/golf/placement.js';
import { WORLD_RADIUS, column, groundFrame } from '../core/s3.js';

const R = WORLD_RADIUS;
const WALL = [0.60, 0.47, 0.38], WALL_EDGE = [0.45, 0.34, 0.27];
const ROOF = [0.86, 0.10, 0.08], ROOF_EDGE = [0.62, 0.06, 0.05];
const PANE = [0.98, 0.88, 0.36], FRAME = [0.26, 0.21, 0.18];
const DOOR = [0.62, 0.63, 0.66], CHIMNEY = [0.40, 0.38, 0.38], BARK = [0.42, 0.29, 0.19];

// ------------------------------------------------------------------ ground

/** The whole ground great-sphere y = 0, as a subdivided icosahedron. Its up is e_y everywhere. */
export function buildGround(subdivisions = 5) {
  const t = (1 + Math.sqrt(5)) / 2;
  const points = [
    [-1, t, 0], [1, t, 0], [-1, -t, 0], [1, -t, 0], [0, -1, t], [0, 1, t],
    [0, -1, -t], [0, 1, -t], [t, 0, -1], [t, 0, 1], [-t, 0, -1], [-t, 0, 1],
  ].map(p => { const l = Math.hypot(...p); return [p[0] / l, p[1] / l, p[2] / l]; });
  let faces = [
    0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
    3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
  ];
  for (let s = 0; s < subdivisions; s++) {
    const mid = new Map();
    const midpoint = (a, b) => {
      const key = a < b ? `${a}_${b}` : `${b}_${a}`;
      if (mid.has(key)) return mid.get(key);
      const m = [0, 1, 2].map(i => points[a][i] + points[b][i]), l = Math.hypot(...m);
      points.push([m[0] / l, m[1] / l, m[2] / l]);
      mid.set(key, points.length - 1);
      return points.length - 1;
    };
    const next = [];
    for (let f = 0; f < faces.length; f += 3) {
      const [a, b, c] = [faces[f], faces[f + 1], faces[f + 2]];
      const ab = midpoint(a, b), bc = midpoint(b, c), ca = midpoint(c, a);
      next.push(a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca);
    }
    faces = next;
  }
  const mesh = new MeshData();
  // the ground sphere lives in (x, z, w); e_y is normal to it everywhere
  for (const p of points) mesh.addVertex([p[0], 0, p[1], p[2]], [0, 1, 0, 0], [1, 1, 1]);
  for (let f = 0; f < faces.length; f += 3) mesh.addTriangle(faces[f], faces[f + 1], faces[f + 2]);
  return mesh.toBuffers();
}

// ------------------------------------------------------------------ props

/** Gabled house: walls width by depth, wall height, roof rise. Front faces -z. */
function house(mb, width, depth, wallHeight, rise) {
  const x = width / 2, z = depth / 2, h = wallHeight;
  mb.quad([-x, 0, -z], [x, 0, -z], [x, h, -z], [-x, h, -z], WALL);
  mb.quad([x, 0, z], [-x, 0, z], [-x, h, z], [x, h, z], WALL);
  mb.quad([-x, 0, z], [-x, 0, -z], [-x, h, -z], [-x, h, z], WALL_EDGE);
  mb.quad([x, 0, -z], [x, 0, z], [x, h, z], [x, h, -z], WALL_EDGE);
  mb.tri([-x, h, -z], [x, h, -z], [0, h + rise, -z], WALL);
  mb.tri([x, h, z], [-x, h, z], [0, h + rise, z], WALL);
  // roof: two thick slabs whose planes meet the wall tops exactly
  const o = 0.45, eave = h - rise * o / x, zo = z + o;
  mb.slab([-x - o, eave, -zo], [-x - o, eave, zo], [0, h + rise, zo], [0, h + rise, -zo], 0.2, ROOF, ROOF_EDGE);
  mb.slab([x + o, eave, zo], [x + o, eave, -zo], [0, h + rise, -zo], [0, h + rise, zo], 0.2, ROOF, ROOF_EDGE);
  mb.box([x * 0.35, h + rise * 0.4, z * 0.3], [x * 0.35 + 0.5, h + rise + 0.7, z * 0.3 + 0.5], CHIMNEY);

  // windows and door, slightly proud of the walls
  const window = (c, outward, side, w, wh) => {
    const rect = (hw, hh, off, color) => {
      const corner = (sw, sh) => [0, 1, 2].map(i => c[i] + outward[i] * off + side[i] * sw * hw + (i === 1 ? sh * hh : 0));
      mb.quad(corner(-1, -1), corner(1, -1), corner(1, 1), corner(-1, 1), color);
    };
    rect(w / 2 + 0.08, wh / 2 + 0.08, 0.02, FRAME);
    rect(w / 2, wh / 2, 0.04, PANE);
    rect(0.03, wh / 2, 0.05, FRAME);
    rect(w / 2, 0.03, 0.05, FRAME);
  };
  const wy = Math.min(1.65, h - 0.8);
  const door = [-x * 0.45, 1, -z], shift = (p, dx, dy, dz) => [p[0] + dx, p[1] + dy, p[2] + dz];
  mb.quad(shift(door, -0.5, -1, -0.02), shift(door, 0.5, -1, -0.02), shift(door, 0.5, 1.05, -0.02), shift(door, -0.5, 1.05, -0.02), FRAME);
  mb.quad(shift(door, -0.42, -1, -0.04), shift(door, 0.42, -1, -0.04), shift(door, 0.42, 0.97, -0.04), shift(door, -0.42, 0.97, -0.04), DOOR);
  window([x * 0.45, wy, -z], [0, 0, -1], [1, 0, 0], 0.9, 0.9);
  window([0, h + rise * 0.35, -z], [0, 0, -1], [1, 0, 0], 0.55, 0.55);
  window([-x * 0.45, wy, z], [0, 0, 1], [-1, 0, 0], 0.9, 0.9);
  window([x * 0.45, wy, z], [0, 0, 1], [-1, 0, 0], 0.9, 0.9);
  for (const s of [-1, 1]) {
    window([-x, wy, s * z * 0.45], [-1, 0, 0], [0, 0, -1], 0.8, 0.9);
    window([x, wy, s * z * 0.45], [1, 0, 0], [0, 0, 1], 0.8, 0.9);
  }
}

function roundTree(mb, random, s) {
  const trunk = 2 * s;
  mb.frustum([0, 0, 0], 0.24 * s, 0.15 * s, trunk + 0.4 * s, 6, BARK);
  const g = random(), leaf = [0.20 + 0.08 * g, 0.48 + 0.12 * g, 0.16];
  mb.icosphere([0, trunk + 0.9 * s, 0], [1.35 * s, 1.15 * s, 1.35 * s], 1, leaf, random, 0.12);
  mb.icosphere([0.45 * s, trunk + 1.7 * s, 0.2 * s], [0.85 * s, 0.8 * s, 0.85 * s], 1, shade(leaf, 1.08), random, 0.12);
  mb.icosphere([-0.5 * s, trunk + 0.5 * s, -0.3 * s], [0.8 * s, 0.7 * s, 0.8 * s], 1, shade(leaf, 0.92), random, 0.12);
}

function pineTree(mb, random, s) {
  mb.frustum([0, 0, 0], 0.2 * s, 0.12 * s, 1.2 * s, 6, BARK);
  const g = random(), leaf = [0.12 + 0.05 * g, 0.38 + 0.1 * g, 0.20];
  for (let k = 0; k < 3; k++)
    mb.frustum([0, (0.9 + k) * s, 0], (1.5 - 0.38 * k) * s, 0, 1.8 * s, 7, shade(leaf, 1 + 0.06 * k), k * 0.4);
}

function tuft(mb, random) {
  const blades = 3 + Math.floor(random() * 3), g = random();
  for (let i = 0; i < blades; i++) {
    const a = 2 * Math.PI * random(), dir = [Math.cos(a), 0, Math.sin(a)], side = [-dir[2], 0, dir[0]];
    const root = 0.05 * random(), b = [dir[0] * root, 0, dir[2] * root];
    const reach = 0.08 + 0.1 * random(), top = 0.2 + 0.25 * random();
    const tip = [b[0] + dir[0] * reach, top, b[2] + dir[2] * reach];
    const color = [0.25 + 0.1 * g, 0.55 + 0.12 * g, 0.18];
    mb.tri([b[0] - side[0] * 0.035, 0, b[2] - side[2] * 0.035], [b[0] + side[0] * 0.035, 0, b[2] + side[2] * 0.035], tip, color);
  }
}

// ------------------------------------------------------------------ world assembly

function propMesh(prop) {
  const random = randomSource(prop.seed);
  switch (prop.type) {
    case 'house': {
      const mb = new MeshBuilder();
      const { width, depth, wallHeight, rise } = houseShape(prop.style);
      house(mb, width, depth, wallHeight, rise);
      return mb;
    }
    case 'tree': {
      const mb = new MeshBuilder();
      if (prop.variant === 'pine') pineTree(mb, random, prop.scale); else roundTree(mb, random, prop.scale);
      return mb;
    }
    case 'rock': {
      const mb = new MeshBuilder(0.6), g = 0.72 + 0.12 * random(), size = prop.size;
      mb.icosphere([0, size * 0.35, 0], [size, size * 0.7, size * 0.9], 1, [g, g, g * 0.98], random, 0.18);
      return mb;
    }
    case 'mushroom': {
      const mb = new MeshBuilder();
      for (let k = 0, n = 1 + Math.floor(random() * 3); k < n; k++) {
        const o = [(random() - 0.5) * 0.5, 0, (random() - 0.5) * 0.5], s = 0.7 + 0.6 * random();
        mb.frustum(o, 0.05 * s, 0.04 * s, 0.22 * s, 6, [0.95, 0.92, 0.85]);
        mb.frustum([o[0], o[1] + 0.2 * s, o[2]], 0.17 * s, 0, 0.13 * s, 7, [0.88, 0.12, 0.1]);
      }
      return mb;
    }
    default:
      return null;
  }
}

/** Builds the scenery mesh and the obstacle list that the player and the ball collide with. */
export function buildScenery(layout) {
  const { props, grass } = resolveLayout(layout);
  const mesh = new MeshData();
  const obstacles = [];

  for (const prop of props) {
    const builder = propMesh(prop);
    if (!builder) continue;
    const frame = groundFrame(prop.x, prop.z, prop.yaw * Math.PI / 180, R);
    mesh.append(builder, frame, R);
    const { obstacle } = propSize(prop);
    if (obstacle) obstacles.push({ foot: column(frame, 3), ...obstacle });
  }

  for (const blade of grass) {
    const mb = new MeshBuilder(1);
    tuft(mb, randomSource(blade.seed));
    mesh.append(mb, groundFrame(blade.x, blade.z, blade.yaw * Math.PI / 180, R), R);
  }
  return { mesh: mesh.toBuffers(), obstacles };
}

// ------------------------------------------------------------------ golf gear (live frames)

export const buildFlag = () => {
  const mb = new MeshBuilder(0.6);
  mb.box([-0.025, 0, -0.025], [0.025, 2.4, 0.025], [0.95, 0.95, 0.92]);
  mb.tri([0, 2.38, 0], [0, 1.82, 0], [0.8, 2.1, 0], [0.95, 0.15, 0.12]);
  mb.icosphere([0, 2.43, 0], [0.05, 0.05, 0.05], 0, [0.95, 0.8, 0.2]);
  return bakeLocal(mb, R);
};

export const buildBall = () => {
  const mb = new MeshBuilder(1), r = BALL_RADIUS;
  mb.icosphere([0, 0, 0], [r, r, r], 2, [0.97, 0.97, 0.95]);
  return bakeLocal(mb, R);
};

/** Launch guide: dots along local +z from just past the ball, yellow to orange. */
export const buildGuide = (count = 9) => {
  const mb = new MeshBuilder(1);
  for (let k = 0; k < count; k++) {
    const t = k / Math.max(1, count - 1);
    const color = [1, 0.95 + (0.42 - 0.95) * t, 0.3 + (0.08 - 0.3) * t];
    const r = 0.032 - 0.0015 * k;
    mb.icosphere([0, 0, 0.24 + 0.2 * k], [r, r, r], 1, color);
  }
  return bakeLocal(mb, R);
};

/** Flat aiming arrow lying on the ground along +z, starting just past the ball. */
export const buildArrow = () => {
  const mb = new MeshBuilder(0.3), color = [1, 0.95, 0.55], y = 0.015;
  mb.quad([-0.04, y, 0.25], [0.04, y, 0.25], [0.04, y, 1.45], [-0.04, y, 1.45], color);
  mb.tri([-0.14, y, 1.45], [0.14, y, 1.45], [0, y, 1.75], color);
  return bakeLocal(mb, R);
};
