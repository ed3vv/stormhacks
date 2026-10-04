// The five-hole course, in local ground coordinates (metres) around the world origin.
import { WORLD_RADIUS, distance, foot, groundPoint } from '../../core/s3.js';

export const BALL_RADIUS = 0.1;
export const CUP_RADIUS = 0.18;
export const GREEN_RADIUS = 3.5;
export const SCENERY_SEED = 11;

export const COURSE = [
  { tee: [0, 3], cup: [0, 24], par: 3 },        // straight ahead, the cup sits just past the horizon
  { tee: [3, 27], cup: [-22, 8], par: 3 },
  { tee: [-20, 6], cup: [16.1, -4.8], par: 4 }, // cup is the tee's antipode: every direction leads there
  { tee: [19, -6], cup: [-2, -20], par: 3 },
  { tee: [-2, -24], cup: [-3, -4], par: 3 },
];

// The only club. With R = 12 it takes 19.9 m/s straight up to crest the pole, so an iron
// cannot send the ball over - raise maxSpeed if that shot should be back on the table.
export const CLUB = { name: 'Iron', maxSpeed: 14 };

export const HOLES = COURSE.map(hole => ({
  tee: groundPoint(hole.tee[0], hole.tee[1], WORLD_RADIUS),
  cup: groundPoint(hole.cup[0], hole.cup[1], WORLD_RADIUS),
  par: hole.par,
}));

export const TOTAL_PAR = HOLES.reduce((sum, hole) => sum + hole.par, 0);

/** Ground distance in metres between two points of S^3, measured between their feet. */
export const groundDistance = (a, b) => distance(foot(a), foot(b)) * WORLD_RADIUS;
