// Proves every laser level starts unsolved, does not trap the player, and is solved
// by its stored solution in the shared world (golf scenery included).
const path = require('node:path');
const { pathToFileURL } = require('node:url');

const load = file => import(pathToFileURL(path.resolve(__dirname, '..', file)).href);

async function main() {
  const { LEVELS, OBSTACLES } = await load('src/game/laser/levels.js');
  const { Simulation } = await load('src/game/laser/simulation.js');
  const PLAYER_RADIUS = .016;
  let failures = 0;
  LEVELS.forEach((level, index) => {
    const problems = [];
    const before = new Simulation(level.blocks, OBSTACLES);
    if (before.lightsAllGoals(level.blocks)) problems.push('solved before any blocks are placed');
    if ([...level.blocks, ...OBSTACLES].some(b => b.footprintContains(level.start, PLAYER_RADIUS))) problems.push('player starts inside an obstacle');
    for (const block of level.blocks) {
      if (OBSTACLES.some(o => block.footprintOverlaps(o, 0))) problems.push('level block overlaps the scenery');
    }
    for (const block of level.solution) {
      if ([...level.blocks, ...OBSTACLES].some(o => block.footprintOverlaps(o, 0))) problems.push('solution block overlaps the level');
    }
    const solvedBlocks = [...level.blocks, ...level.solution];
    const after = new Simulation(solvedBlocks, OBSTACLES);
    if (!after.lightsAllGoals(solvedBlocks)) problems.push('stored solution does not light the goal');
    console.log(`${problems.length ? 'FAIL' : 'ok  '} level ${index + 1} ${level.title}: ${after.segments.length} beam segments`
      + (problems.length ? ' — ' + problems.join('; ') : ''));
    failures += problems.length ? 1 : 0;
  });
  process.exitCode = failures ? 1 : 0;
}

main();
