import { bindGolfInput } from '../../app/input.js';
import { createHud } from '../../app/ui.js';
import { Camera } from '../../graphics/camera.js';
import { Renderer } from '../../graphics/renderer.js';
import { WorldModel } from './world-model.js';

export function createGolfGame(canvas) {
  const model = new WorldModel();
  const camera = new Camera();
  const renderer = new Renderer(canvas, model, camera);
  const hud = createHud();
  const unbindInput = bindGolfInput(canvas, model);
  let animationFrame = 0;
  let last = performance.now();
  let statsTime = last;
  let frames = 0;

  const frame = now => {
    const dt = Math.min((now - last) / 1000, 0.05);
    last = now;
    model.update(dt);
    camera.update(model, dt);
    renderer.draw();
    frames++;

    if (now - statsTime >= 250) {
      hud.update(frames, now - statsTime, model);
      statsTime = now;
      frames = 0;
    }
    animationFrame = requestAnimationFrame(frame);
  };

  animationFrame = requestAnimationFrame(frame);
  return {
    model,
    stop() {
      cancelAnimationFrame(animationFrame);
      unbindInput();
    },
  };
}
