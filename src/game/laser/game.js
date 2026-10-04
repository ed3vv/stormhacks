import { bindLaserInput } from '../../app/input.js';
import { createLaserHud } from '../../app/ui.js';
import { LaserRenderer } from '../../graphics/laser-renderer.js';
import { PuzzleGame } from './puzzle.js';
import { Walker } from './walker.js';

export function createLaserGame(canvas) {
  const walker = new Walker(p => game.blocksPlayer(p));
  const game = new PuzzleGame((start, facing) => walker.reset(start, facing));
  const renderer = new LaserRenderer(canvas);
  const hud = createLaserHud();
  const input = bindLaserInput(canvas, walker, game, hud);
  let animationFrame = 0;
  const startTime = performance.now();
  let last = startTime;
  let statsTime = last;
  let frames = 0;

  const frame = now => {
    const dt = Math.min((now - last) / 1000, 0.05);
    last = now;
    const move = input.movement();
    walker.update(move.forward, move.strafe, dt, move.running);
    if (move.turn) game.rotate(move.turn * dt);
    const camera = walker.camera;
    game.update(camera);
    renderer.draw(camera, game, (now - startTime) / 1000);
    frames++;

    if (now - statsTime >= 100) {
      hud.update(frames, now - statsTime, game);
      statsTime = now;
      frames = 0;
    }
    animationFrame = requestAnimationFrame(frame);
  };

  animationFrame = requestAnimationFrame(frame);
  return {
    game,
    walker,
    stop() {
      cancelAnimationFrame(animationFrame);
      input.unbind();
    },
  };
}
