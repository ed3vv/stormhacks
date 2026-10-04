import { dot, sub, mul, len, norm, cross } from '../../core/vector.js';
import { Kind, TAU, UP, groundHit, pointAt, tangentAt, reflect } from './geometry.js';

/** Elevation (radians) and number of rays in each ring a diffractor emits. */
const DIFFRACTION_RINGS = [[0, 12], [.33, 12], [.72, 10], [1.13, 8]];

/**
 * All the laser light in a scene and what it reaches. `blocks` are the puzzle's own
 * blocks (indices in litGoals refer to them); `obstacles` are the world's scenery,
 * which simply absorbs light. With a `hologram` index, only light that has been
 * reflected or re-emitted by that block is kept, marked as a preview.
 */
export class Simulation {
  constructor(blocks = [], obstacles = [], hologram = null) {
    this.segments = [];
    this.litGoals = new Set();
    this.activeDiffractors = new Set();
    this.all = blocks.concat(obstacles);
    this.hologram = hologram;
    const diffractorHits = [];
    for (const block of blocks) {
      if (block.kind !== Kind.EMITTER) continue;
      this.trace([...block.position, 0], [...block.facing, 0], 2 * TAU, 24, false, false,
        (index, incoming, via) => diffractorHits.push({ index, incoming, via }));
    }
    for (const hit of diffractorHits) {
      if (this.activeDiffractors.has(hit.index)) continue;
      this.activeDiffractors.add(hit.index);
      const onlyViaHologram = hit.index === hologram
        || diffractorHits.every(h => h.index !== hit.index || h.via);
      this.diffract(blocks[hit.index], hit.incoming, onlyViaHologram);
    }
  }

  lightsAllGoals(blocks) {
    const goals = blocks.map((b, i) => b.kind === Kind.GOAL ? i : -1).filter(i => i >= 0);
    return goals.length > 0 && goals.every(i => this.litGoals.has(i));
  }

  /** Re-emits light from the diffractor's core in every upward direction. It all meets again at the antipode, π away. */
  diffract(block, incoming, viaHologram) {
    const g = block.position;
    let a = sub(incoming.slice(0, 3), mul(g, dot(incoming.slice(0, 3), g)));
    a = len(a) < 1e-6 ? block.facing : norm(a);
    const b = cross(g, a), directions = [UP];
    DIFFRACTION_RINGS.forEach(([elevation, count], ring) => {
      for (let k = 0; k < count; k++) {
        const azimuth = TAU * (k + (ring % 2 ? .5 : 0)) / count;
        const ca = Math.cos(azimuth) * Math.cos(elevation), sa = Math.sin(azimuth) * Math.cos(elevation);
        directions.push([a[0] * ca + b[0] * sa, a[1] * ca + b[1] * sa, a[2] * ca + b[2] * sa, Math.sin(elevation)]);
      }
    });
    for (const d of directions) this.trace([...g, 0], d, Math.PI + .1, 6, true, viaHologram, () => {});
  }

  trace(start, direction, budget, bounces, diffracted, viaHologram, onDiffractor) {
    let x = start, d = direction, remaining = budget, via = viaHologram;
    for (let bounce = 0; bounce <= bounces; bounce++) {
      let bestT = remaining, hit = null;
      for (let i = 0; i < this.all.length; i++) {
        const info = this.all[i].intersect(x, d);
        if (info && info.t < bestT) { bestT = info.t; hit = { index: i, info }; }
      }
      const g = groundHit(x, d);
      if (g !== null && g < bestT) { bestT = g; hit = null; }
      if (bestT > 1e-6 && (this.hologram === null || via)) {
        this.segments.push({ start: x, direction: d, length: bestT, diffracted, preview: this.hologram !== null });
      }
      if (!hit) return;
      const hitPoint = pointAt(x, d, bestT), hitDirection = tangentAt(x, d, bestT);
      const kind = this.all[hit.index].kind;
      if (kind === Kind.MIRROR && hit.info.axis === 0) {
        x = norm(hitPoint);
        d = reflect(hitDirection, hit.info.normal);
        d = norm(sub(d, mul(x, dot(d, x))));
        remaining -= bestT;
        via = via || hit.index === this.hologram;
      } else {
        if (kind === Kind.GOAL) this.litGoals.add(hit.index);
        else if (kind === Kind.DIFFRACTOR && !diffracted) onDiffractor(hit.index, hitDirection, via);
        return;
      }
    }
  }
}
