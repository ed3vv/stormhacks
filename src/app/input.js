// Keyboard, mouse and pointer-lock state for one frame at a time.
export function createInput(canvas) {
  const held = new Set(), pressed = new Set();
  let deltaX = 0, deltaY = 0;
  let leftHeld = false, leftPressed = false, leftReleased = false, rightPressed = false, clicked = false;

  const onKeyDown = event => {
    if (event.code === 'Space' || event.code.startsWith('Arrow')) event.preventDefault();
    if (!event.repeat) pressed.add(event.code);
    held.add(event.code);
  };
  const onKeyUp = event => held.delete(event.code);
  const onBlur = () => { held.clear(); leftHeld = false; };

  const onMouseDown = event => {
    if (event.button === 0) clicked = true;   // any click on the screen, captured cursor or not
    if (document.pointerLockElement !== canvas) {
      canvas.requestPointerLock?.();      // the click that captures the cursor must not also swing
      return;
    }
    if (event.button === 0) { leftHeld = true; leftPressed = true; }
    if (event.button === 2) rightPressed = true;
  };
  const onMouseUp = event => { if (event.button === 0) { leftHeld = false; leftReleased = true; } };
  const onMouseMove = event => {
    if (document.pointerLockElement !== canvas) return;
    // the browser reports movementY growing downwards; flip it so up is positive, which is
    // what both looking around and the launch height expect
    deltaX += event.movementX; deltaY -= event.movementY;
  };
  const onContextMenu = event => event.preventDefault();

  addEventListener('keydown', onKeyDown);
  addEventListener('keyup', onKeyUp);
  addEventListener('blur', onBlur);
  addEventListener('mousemove', onMouseMove);
  canvas.addEventListener('mousedown', onMouseDown);
  addEventListener('mouseup', onMouseUp);
  canvas.addEventListener('contextmenu', onContextMenu);

  return {
    get locked() { return document.pointerLockElement === canvas; },
    get mouseX() { return deltaX; },
    get mouseY() { return deltaY; },
    get leftHeld() { return leftHeld; },
    get leftPressed() { return leftPressed; },
    get leftReleased() { return leftReleased; },
    get rightPressed() { return rightPressed; },
    get clicked() { return clicked; },
    held: code => held.has(code),
    pressed: code => pressed.has(code),
    /** Clears the one-frame edges. Call once at the end of every frame. */
    endFrame() {
      pressed.clear();
      deltaX = deltaY = 0;
      leftPressed = leftReleased = rightPressed = clicked = false;
    },
    dispose() {
      removeEventListener('keydown', onKeyDown);
      removeEventListener('keyup', onKeyUp);
      removeEventListener('blur', onBlur);
      removeEventListener('mousemove', onMouseMove);
      canvas.removeEventListener('mousedown', onMouseDown);
      removeEventListener('mouseup', onMouseUp);
      canvas.removeEventListener('contextmenu', onContextMenu);
      if (document.pointerLockElement === canvas) document.exitPointerLock?.();
    },
  };
}
