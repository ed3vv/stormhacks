import { dot, sub, mul, len, norm, cross } from '../../core/vector.js';
import { Block, Kind, distance, groundHit, pointAt, rotateAbout } from './geometry.js';
import { LEVELS, OBSTACLES } from './levels.js';
import { Simulation } from './simulation.js';

const REACH = .45, PLAYER_RADIUS = .016;
const now = () => performance.now() / 1000;
const NAMES = { mirror: 'mirror', diffractor: 'diffractor' };

/** Placement, carrying, picking and level state for the laser maze. */
export class PuzzleGame {
  constructor(onLevelStart) {
    this.levels = LEVELS;
    this.onLevelStart = onLevelStart;
    this.load(0);
  }

  get level() { return this.levels[this.levelIndex]; }

  // Level flow

  load(index) {
    this.levelIndex = Math.max(0, Math.min(this.levels.length - 1, index));
    this.blocks = [...this.level.blocks];
    this.mirrorsLeft = this.level.mirrors;
    this.diffractorsLeft = this.level.diffractors;
    this.tool = this.level.mirrors > 0 ? 'mirror' : 'diffractor';
    this.solved = false;
    this.targetBlock = null;
    this.ghost = null;
    this.ghostValid = false;
    this.ghostAngle = 0;
    this.lastRotation = 0;
    this.preview = new Simulation();
    this.message = null;
    this.playerPosition = this.level.start;
    this.viewForward = this.level.startFacing;
    this.onLevelStart?.(this.level.start, this.level.startFacing);
    this.traceBeams();
  }

  restart() { this.load(this.levelIndex); }
  nextLevel() { if (this.levelIndex + 1 < this.levels.length) this.load(this.levelIndex + 1); }
  previousLevel() { if (this.levelIndex > 0) this.load(this.levelIndex - 1); }

  // Player actions

  select(tool) {
    this.tool = tool;
    if (this.count(tool) === 0) this.flash(`You have no ${NAMES[tool]}s in this level.`);
  }

  cycleTool() { this.select(this.tool === 'mirror' ? 'diffractor' : 'mirror'); }

  /** Click: pick up the block under the crosshair, otherwise put down the hologram. */
  primaryAction() { if (this.targetBlock !== null) this.pickUp(); else this.place(); }

  place() {
    if (this.count(this.tool) <= 0) { this.flash(`No ${NAMES[this.tool]}s left. Click a placed one to pick it up.`); return; }
    if (!this.ghost) { this.flash('Aim at the ground within reach to place.'); return; }
    if (!this.ghostValid) { this.flash('Not enough room there.'); return; }
    this.blocks.push(this.ghost);
    if (this.tool === 'mirror') this.mirrorsLeft--; else this.diffractorsLeft--;
    this.traceBeams();
  }

  /** Lifts a placed block back into your hands. It keeps its orientation and follows the crosshair as a hologram. */
  pickUp() {
    if (this.targetBlock === null) { this.flash('Look at a mirror or diffractor you placed to pick it up.'); return; }
    const [block] = this.blocks.splice(this.targetBlock, 1);
    if (block.kind === Kind.MIRROR) { this.mirrorsLeft++; this.tool = 'mirror'; }
    else { this.diffractorsLeft++; this.tool = 'diffractor'; }
    const away = this.awayDirection(block.position);
    this.ghostAngle = Math.atan2(dot(block.facing, cross(block.position, away)), dot(block.facing, away));
    this.targetBlock = null;
    this.lastRotation = 0;
    this.traceBeams();
    this.flash(`Picked up the ${NAMES[this.tool]}. Click to put it down again.`);
  }

  /** Turns the block under the crosshair, or the placement preview when there is none. */
  rotate(angle) {
    this.lastRotation = now();
    if (this.targetBlock !== null) {
      this.blocks[this.targetBlock] = this.blocks[this.targetBlock].rotated(angle);
      this.traceBeams();
    } else {
      this.ghostAngle += angle;
    }
  }

  blocksPlayer(p) {
    return this.blocks.some(b => b.footprintContains(p, PLAYER_RADIUS))
      || OBSTACLES.some(b => b.footprintContains(p, PLAYER_RADIUS));
  }

  // Per-frame update

  update(camera) {
    this.playerPosition = camera.position;
    this.viewForward = camera.forward.slice(0, 3);
    this.updateTargeting(camera);
    this.preview = this.ghost
      ? new Simulation([...this.blocks, this.ghost], OBSTACLES, this.blocks.length)
      : new Simulation();
  }

  /** True when putting the hologram down would light every goal. */
  get hologramSolves() {
    return !!this.ghost && !this.solved && this.preview.lightsAllGoals([...this.blocks, this.ghost]);
  }

  /** Unit tangent at `point` pointing away from the player. */
  awayDirection(point) {
    let away = sub(point, mul(this.playerPosition, dot(point, this.playerPosition)));
    if (len(away) < 1e-6) away = this.viewForward;
    return norm(sub(away, mul(point, dot(away, point))));
  }

  updateTargeting(camera) {
    // A mirror turned edge-on slips out from under the crosshair; keep turning it.
    if (this.targetBlock !== null && now() - this.lastRotation < .3
      && distance(this.blocks[this.targetBlock].position, this.playerPosition) < REACH) return;
    this.targetBlock = null;
    this.ghost = null;
    const x = camera.eye, d = camera.forward;
    let nearest = null;
    [...this.blocks, ...OBSTACLES].forEach((block, i) => {
      const hit = block.intersect(x, d);
      if (hit && hit.t < (nearest?.t ?? Infinity)) nearest = { index: i, t: hit.t };
    });
    const groundT = groundHit(x, d) ?? Infinity;
    if (nearest && nearest.t < groundT) {
      const block = this.blocks[nearest.index];
      if (block?.movable && nearest.t < REACH) this.targetBlock = nearest.index;
      return;
    }
    if (groundT >= Math.PI) return;
    const point = norm(pointAt(x, d, groundT).slice(0, 3));
    if (distance(point, this.playerPosition) > REACH) return;
    // Aiming at the ground right beside a thin mirror counts as aiming at it.
    const beside = this.blocks.findIndex(b => b.movable && b.footprintContains(point, .02));
    if (beside >= 0) { this.targetBlock = beside; return; }

    // With nothing left to place there is no hologram, only picking up.
    if (this.count(this.tool) <= 0) return;
    const facing = rotateAbout(this.awayDirection(point), point, this.ghostAngle);
    const candidate = this.tool === 'mirror' ? Block.mirror(point, facing) : Block.diffractor(point, facing);
    this.ghost = candidate;
    this.ghostValid = !candidate.footprintContains(this.playerPosition, PLAYER_RADIUS)
      && !this.blocks.some(b => candidate.footprintOverlaps(b, .004))
      && !OBSTACLES.some(b => candidate.footprintOverlaps(b, .004));
  }

  traceBeams() {
    this.light = new Simulation(this.blocks, OBSTACLES);
    this.solved = this.light.lightsAllGoals(this.blocks);
  }

  // HUD text

  get titleText() { return `LEVEL ${this.levelIndex + 1}/${this.levels.length} · ${this.level.title}`; }

  get inventoryText() {
    const parts = [];
    if (this.level.mirrors > 0) parts.push(`${this.tool === 'mirror' ? '▶' : '  '} [1] Mirror × ${this.mirrorsLeft}`);
    if (this.level.diffractors > 0) parts.push(`${this.tool === 'diffractor' ? '▶' : '  '} [2] Diffractor × ${this.diffractorsLeft}`);
    return parts.join('     ');
  }

  get statusText() {
    if (this.message && now() < this.message.until) return this.message.text;
    if (this.hologramSolves) return '✦ Put it down here and the goal lights up';
    const goal = this.blocks.find(b => b.kind === Kind.GOAL);
    if (!goal) return '';
    let text = `Distance to goal  you ${distance(this.playerPosition, goal.position).toFixed(2)}`;
    if (this.ghost) text += `  ·  placement ${distance(this.ghost.position, goal.position).toFixed(2)}`;
    return text;
  }

  get bannerText() {
    if (!this.solved) return null;
    return this.levelIndex + 1 < this.levels.length ? 'GOAL LIT\nPress N for the next level' : 'ALL PUZZLES SOLVED\nThanks for playing';
  }

  count(tool) { return tool === 'mirror' ? this.mirrorsLeft : this.diffractorsLeft; }
  flash(text) { this.message = { text, until: now() + 2.5 }; }
}
