// Turns a layout into the concrete list of things standing on the ground: the hand-placed props
// first, then the scattered filler in whatever space the holes and those props leave free.
//
// Everything here works in the layout's flat metre coordinates; the keep-out tests convert to
// S^3 so they measure real walking distance rather than distance on the stretched map.
import { WORLD_RADIUS, distance, directionTo, groundPoint, isZero, walk } from '../../core/s3.js';

const R = WORLD_RADIUS;
/** Radius of the flat map. Every point on its rim is the same place: the origin's antipode. */
export const MAP_RADIUS = Math.PI * R;
export const GREEN_RADIUS = 3.5;

export const houseShape = style => ({
  width: 4.2 + style * 0.5,
  depth: 5 + style * 0.4,
  wallHeight: 2.8 + style * 0.3,
  rise: 1.9,
});

/** How much room a prop takes up: what it blocks, and how far the scatter must stay clear of it. */
export function propSize(prop) {
  switch (prop.type) {
    case 'house': {
      const { width, depth, wallHeight, rise } = houseShape(prop.style);
      const footprint = 0.5 * Math.hypot(width, depth);
      return { obstacle: { radius: footprint * 0.9, height: wallHeight + rise }, keepOut: footprint + 1.5, clearance: footprint };
    }
    case 'tree':
      return { obstacle: { radius: 0.3 * prop.scale, height: 6 }, keepOut: 1.6 * prop.scale, clearance: 1.8 };
    case 'rock':
      return { obstacle: { radius: prop.size * 0.85, height: prop.size }, keepOut: prop.size + 0.3, clearance: prop.size + 0.5 };
    case 'mushroom':
      return { obstacle: null, keepOut: 0, clearance: 0.4 };
    default:
      return { obstacle: null, keepOut: 0, clearance: 0.2 };
  }
}

// Deterministic so the world is identical on every load, like Unity's seeded System.Random.
export function randomSource(seed) {
  let state = seed >>> 0;
  return () => {
    state = (state + 0x6d2b79f5) >>> 0;
    let t = Math.imul(state ^ (state >>> 15), 1 | state);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/** A scattered prop's own seed, so editing one prop never reshuffles the others' shapes. */
const seedFrom = random => Math.floor(random() * 0x100000000);

function newProp(type, random) {
  const seed = seedFrom(random);
  if (type === 'tree') return { type, scale: 0.8 + 0.5 * random(), variant: random() < 0.65 ? 'round' : 'pine', seed };
  if (type === 'rock') return { type, size: 0.35 + 1.1 * random() * random(), seed };
  return { type, seed };
}

/**
 * Resolves a layout into { holes, props, grass }. Props carry flat map coordinates plus the
 * parameters the mesh builder and the physics need; grass is positions only.
 */
export function resolveLayout(layout) {
  const random = randomSource(layout.seed);
  const keepOut = [];

  const holes = layout.holes.map(hole => ({
    tee: hole.tee, cup: hole.cup, par: hole.par,
    teePoint: groundPoint(hole.tee[0], hole.tee[1], R),
    cupPoint: groundPoint(hole.cup[0], hole.cup[1], R),
  }));
  for (const hole of holes) {
    keepOut.push({ point: hole.teePoint, radius: 3 }, { point: hole.cupPoint, radius: GREEN_RADIUS + 1 });
    // keep a fairway clear along the geodesic from tee to cup
    const span = distance(hole.teePoint, hole.cupPoint), heading = directionTo(hole.teePoint, hole.cupPoint);
    if (isZero(heading)) continue;
    for (let s = 0; s < span * R; s += 1.5) keepOut.push({ point: walk(hole.teePoint, heading, s / R), radius: 1.4 });
  }

  const props = [];
  const keep = prop => {
    const { keepOut: radius } = propSize(prop);
    if (radius) keepOut.push({ point: groundPoint(prop.x, prop.z, R), radius });
  };
  for (const [index, prop] of layout.props.entries()) {
    const resolved = { yaw: 0, scale: 1, size: 1, style: 0, variant: 'round', seed: index + 1, ...prop };
    props.push(resolved);
    keep(resolved);
  }

  const tryPlace = (clearance, attempts) => {
    for (let a = 0; a < attempts; a++) {
      // uniform on the ground sphere: cos(distance) is uniform
      const d = Math.acos(1 - 2 * random()) * R, theta = 2 * Math.PI * random();
      const x = d * Math.cos(theta), z = d * Math.sin(theta), yaw = 360 * random();
      const point = groundPoint(x, z, R);
      if (keepOut.every(k => distance(point, k.point) * R >= k.radius + clearance)) return { x, z, yaw };
    }
    return null;
  };

  for (const type of ['tree', 'rock', 'mushroom']) {
    for (let i = 0; i < (layout.scatter[type] || 0); i++) {
      const prop = newProp(type, random);
      const spot = tryPlace(propSize(prop).clearance, 40);
      if (!spot) continue;
      const resolved = { ...prop, ...spot };
      props.push(resolved);
      keep(resolved);
    }
  }

  const grass = [];
  for (let i = 0; i < (layout.scatter.grass || 0); i++) {
    const seed = seedFrom(random);
    const spot = tryPlace(0.2, 4);
    if (spot) grass.push({ ...spot, seed });
  }

  return { holes, props, grass };
}
