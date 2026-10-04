import { dot, add, sub, mul, norm, cross } from '../../core/vector.js';
import { BOXES } from '../world/scenery.js';
import { Block, Kind, SINK, expMap, direction, transport } from './geometry.js';

/** The golf course's boxes, as solid blocks that stop light and people. */
export const OBSTACLES = BOXES.map(([anchor, ax, , halfSize]) =>
  new Block(Kind.SCENERY, anchor, ax, [-halfSize[0], -halfSize[1], -Math.tan(SINK)], [halfSize[0], halfSize[1], 2 * halfSize[2]]));

/**
 * Where the puzzles sit in the shared world: the ground point the player starts on
 * (just beside the golf tee) and compass north there. It was found by searching for a
 * spot where no golf scenery stands in any level or at its antipode, and none of the
 * light in a solved level, including beams that go round the world, touches it.
 */
export const LASER_FRAME = {
  origin: [0.8242963, -0.0427034, 0.5645459],
  north: [-0.3819944, -0.7779275, 0.4989079],
};

export function buildLevels(frame) {
  const origin = norm(frame.origin), north = norm(sub(frame.north, mul(origin, dot(frame.north, origin))));
  const east = cross(origin, north);
  /** The ground point reached by walking from the origin with these northward and eastward components. */
  const spot = (n, e) => expMap(origin, add(mul(north, n), mul(east, e)));
  /** Compass heading in degrees (0 north, 90 east) at p, carrying the origin's directions over. */
  const heading = (degrees, p) => {
    const a = degrees * Math.PI / 180;
    return norm(transport(add(mul(north, Math.cos(a)), mul(east, Math.sin(a))), origin, p));
  };
  /** Mirror at m, struck by a beam straight from source, sending it toward target (or directly away from it). */
  const mirror = (m, source, target, away = false) => {
    const incoming = mul(direction(m, source), -1), outgoing = mul(direction(m, target), away ? -1 : 1);
    return Block.mirror(m, norm(add(incoming, mul(outgoing, -1))));
  };
  /** The point of the great circle through p with tangent f that is nearest to q. */
  const nearestOnCircle = (p, f, q) => {
    const t = Math.atan2(dot(q, f), dot(q, p));
    return norm(add(mul(p, Math.cos(t)), mul(f, Math.sin(t))));
  };
  /** Walls along `sides` of a square around c (0 north, 1 east, 2 south, 3 west). */
  const enclosure = (c, r, sides) => {
    const f = heading(0, c), e = cross(c, f);
    const corner = (a, b) => expMap(c, add(mul(f, a * r), mul(e, b * r)));
    const ne = corner(1, 1), se = corner(-1, 1), sw = corner(-1, -1), nw = corner(1, -1);
    const edges = [[nw, ne], [ne, se], [se, sw], [sw, nw]];
    return sides.map(s => Block.wall(...edges[s]));
  };
  const neg = v => mul(v, -1);
  const level = (fields) => ({ start: origin, startFacing: north, mirrors: 0, diffractors: 0, solution: [], ...fields });

  const reflection = () => {
    const e = spot(.35, -.3), g = spot(.8, .25), ef = heading(90, e);
    const m = nearestOnCircle(e, ef, spot(.35, .25));
    return level({
      title: 'Reflection',
      hint: 'Guide the red laser into the goal. Look at the ground and click to place a mirror: '
        + 'its hologram shows where the beam would go. Hold Q / E to turn it. '
        + 'Click a placed mirror to pick it up and carry it somewhere else.',
      blocks: [Block.emitter(e, ef), Block.goal(g, heading(0, g))],
      mirrors: 1,
      solution: [mirror(m, e, g)],
    });
  };

  const longWay = () => {
    const e = spot(.1, -.25), g = spot(.45, .3), ef = heading(0, e);
    const m = nearestOnCircle(e, ef, spot(.45, -.25));
    return level({
      title: 'The Long Way Round',
      hint: 'This side of the goal is walled off. But the world is a closed sphere: '
        + 'a beam sent the other way goes all the way round and comes back from behind.',
      blocks: [Block.emitter(e, ef), Block.goal(g, heading(0, g)), ...enclosure(g, .075, [0, 2, 3])],
      mirrors: 1,
      solution: [mirror(m, e, g, true)],
    });
  };

  const antipode = () => {
    const g = spot(.42, 0), e = spot(.42, -.45);
    return level({
      title: 'Antipode',
      hint: 'The goal is sealed in a fort. In this curved space every ray leaving a point meets '
        + 'again at its antipode, π ≈ 3.14 away. Put the diffractor on the beam exactly π from '
        + 'the goal: from there, the goal hangs straight overhead.',
      blocks: [Block.emitter(e, neg(direction(e, g))), Block.goal(g, heading(0, g)), ...enclosure(g, .085, [0, 1, 2, 3])],
      diffractors: 1,
      solution: [Block.diffractor(neg(g), heading(0, neg(g)))],
    });
  };

  const zigzag = () => {
    const e = spot(.05, -.35), g = spot(.62, .35), ef = heading(0, e);
    const m1 = nearestOnCircle(e, ef, spot(.25, -.35)), m2 = spot(.25, .35);
    return level({
      title: 'Zigzag',
      hint: 'Two mirrors. The goal only accepts light from the south.',
      blocks: [Block.emitter(e, ef), Block.goal(g, heading(0, g)),
        Block.wall(spot(.5, -.46), spot(.5, -.24)),
        Block.wall(spot(.36, 0), spot(.85, 0)),
        ...enclosure(g, .07, [0, 1, 3]),
        Block.wall(spot(.48, .28), spot(.555, .28)),
        Block.wall(spot(.48, .42), spot(.555, .42))],
      mirrors: 2,
      solution: [mirror(m1, e, m2), mirror(m2, m1, g)],
    });
  };

  const fullCircle = () => {
    const e = spot(-.1, -.25), g = spot(.3, .25), ef = heading(0, e);
    const m = nearestOnCircle(e, ef, spot(.3, -.25));
    // The reflected beam heads straight away from the goal, so it passes the antipode.
    return level({
      title: 'Full Circle',
      hint: 'Everything you have learned. One mirror, one diffractor.',
      blocks: [Block.emitter(e, ef), Block.goal(g, heading(0, g)), ...enclosure(g, .085, [0, 1, 2, 3])],
      mirrors: 1,
      diffractors: 1,
      solution: [mirror(m, e, g, true), Block.diffractor(neg(g), heading(0, neg(g)))],
    });
  };

  return [reflection(), longWay(), antipode(), zigzag(), fullCircle()];
}

export const LEVELS = buildLevels(LASER_FRAME);
