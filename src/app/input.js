export function bindGolfInput(canvas, model, camera, onViewModeChange) {
  const onKeyDown = event => {
    if (event.code === 'KeyV' && !event.repeat) {
      camera.setFreeView(!camera.freeView);
      onViewModeChange(camera.freeView);
      return;
    }
    if (camera.freeView) return;
    if (event.code === 'Space') {
      event.preventDefault();
      if (!event.repeat) model.pressAction();
    } else if (event.code === 'KeyR') {
      model.reset();
    }
  };

  const onMouseMove = event => {
    if (document.pointerLockElement === canvas || event.target === canvas) {
      if (camera.freeView) camera.look(event.movementX, event.movementY);
      else model.look(event.movementX, event.movementY);
    }
  };

  const capturePointer = () => canvas.requestPointerLock?.();

  addEventListener('keydown', onKeyDown);
  addEventListener('mousemove', onMouseMove);
  canvas.addEventListener('click', capturePointer);

  return () => {
    camera.setFreeView(false);
    onViewModeChange(false);
    removeEventListener('keydown', onKeyDown);
    removeEventListener('mousemove', onMouseMove);
    canvas.removeEventListener('click', capturePointer);
  };
}

// Walk with WASD / arrows, look with the captured mouse, click to place or pick up.
export function bindLaserInput(canvas, walker, game, hud) {
  const pressed = new Set();
  let shift = false;
  const locked = () => document.pointerLockElement === canvas;
  const held = (...codes) => codes.some(code => pressed.has(code)) ? 1 : 0;

  const onKeyDown = event => {
    shift = event.shiftKey;
    if (event.code === 'Tab') event.preventDefault();
    if (!event.repeat) {
      switch (event.code) {
        case 'KeyR': game.restart(); break;
        case 'KeyN': game.nextLevel(); break;
        case 'KeyP': game.previousLevel(); break;
        case 'Digit1': game.select('mirror'); break;
        case 'Digit2': game.select('diffractor'); break;
        case 'Tab': game.cycleTool(); break;
        case 'KeyX': game.pickUp(); break;
        case 'KeyH': hud.toggleHelp(); break;
      }
    }
    pressed.add(event.code);
  };
  const onKeyUp = event => { shift = event.shiftKey; pressed.delete(event.code); };
  const onBlur = () => pressed.clear();

  const onMouseMove = event => { if (locked()) walker.look(event.movementX, event.movementY); };
  const onMouseDown = event => {
    if (!locked()) { canvas.requestPointerLock?.(); return; }
    if (event.button === 2 || (event.button === 0 && event.ctrlKey)) game.pickUp();
    else if (event.button === 0) game.primaryAction();
  };
  const onContextMenu = event => event.preventDefault();
  const onWheel = event => {
    event.preventDefault();
    const scale = event.deltaMode === 0 ? .002 : .06;
    game.rotate(Math.max(-.3, Math.min(.3, -event.deltaY * scale)));
  };

  addEventListener('keydown', onKeyDown);
  addEventListener('keyup', onKeyUp);
  addEventListener('blur', onBlur);
  addEventListener('mousemove', onMouseMove);
  canvas.addEventListener('mousedown', onMouseDown);
  canvas.addEventListener('contextmenu', onContextMenu);
  canvas.addEventListener('wheel', onWheel, { passive: false });

  return {
    movement() {
      return {
        forward: held('KeyW', 'ArrowUp') - held('KeyS', 'ArrowDown'),
        strafe: held('KeyD', 'ArrowRight') - held('KeyA', 'ArrowLeft'),
        running: shift,
        turn: (held('KeyE') - held('KeyQ')) * (shift ? .15 : .8),
      };
    },
    unbind() {
      removeEventListener('keydown', onKeyDown);
      removeEventListener('keyup', onKeyUp);
      removeEventListener('blur', onBlur);
      removeEventListener('mousemove', onMouseMove);
      canvas.removeEventListener('mousedown', onMouseDown);
      canvas.removeEventListener('contextmenu', onContextMenu);
      canvas.removeEventListener('wheel', onWheel);
    },
  };
}
