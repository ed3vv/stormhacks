export function bindGolfInput(canvas, model) {
  const onKeyDown = event => {
    if (event.code === 'Space') {
      event.preventDefault();
      if (!event.repeat) model.pressAction();
    } else if (event.code === 'KeyR') {
      model.reset();
    }
  };

  const onMouseMove = event => {
    if (document.pointerLockElement === canvas || event.target === canvas) {
      model.look(event.movementX, event.movementY);
    }
  };

  const capturePointer = () => canvas.requestPointerLock?.();

  addEventListener('keydown', onKeyDown);
  addEventListener('mousemove', onMouseMove);
  canvas.addEventListener('click', capturePointer);

  return () => {
    removeEventListener('keydown', onKeyDown);
    removeEventListener('mousemove', onMouseMove);
    canvas.removeEventListener('click', capturePointer);
  };
}
