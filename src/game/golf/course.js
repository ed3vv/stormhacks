// The course, read off the flat map in layout.js.
import { LAYOUT } from './layout.js';
import { resolveLayout, GREEN_RADIUS } from './placement.js';
import { WORLD_RADIUS, distance, foot } from '../../core/s3.js';

export const BALL_RADIUS = 0.1;
export const CUP_RADIUS = 0.18;
export { GREEN_RADIUS };

/**
 * Launch speed from the power meter: a gentle low end, then a p^4 kick so that the top of the
 * meter can throw the ball clean over the pole. 0 % is 5.0 m/s, 95 % just crests the pole, and
 * 100 % is 27.6 m/s.
 */
export const launchSpeed = p => WORLD_RADIUS * (0.42 + 1.18 * p + 0.7 * p ** 4);

export const MAP = resolveLayout(LAYOUT);

export const HOLES = MAP.holes.map(hole => ({ tee: hole.teePoint, cup: hole.cupPoint, par: hole.par }));
export const TOTAL_PAR = HOLES.reduce((sum, hole) => sum + hole.par, 0);

/** Ground distance in metres between two points of S^3, measured between their feet. */
export const groundDistance = (a, b) => distance(foot(a), foot(b)) * WORLD_RADIUS;
