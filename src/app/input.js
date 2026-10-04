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
