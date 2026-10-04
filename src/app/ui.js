const byId = id => document.getElementById(id);

export function createMenu() {
  const root = byId('menu');
  const main = byId('menu-main');
  const games = byId('menu-games');
  const note = byId('menu-note');
  let startGolf = () => {};
  let startLaser = () => {};

  byId('play').addEventListener('click', () => {
    main.hidden = true;
    games.hidden = false;
  });
  byId('play-golf').addEventListener('click', () => startGolf());
  byId('play-laser').addEventListener('click', () => startLaser());
  byId('play-nextbots').addEventListener('click', () => { note.hidden = false; });

  return {
    hide() {
      root.hidden = true;
      byId('hud').hidden = false;
    },
    onGolf(callback) { startGolf = callback; },
    onLaser(callback) { startLaser = callback; },
  };
}

export function createHud() {
  const fps = byId('fps');
  const game = byId('game');
  const help = document.querySelector('#hud .help');

  return {
    setViewMode(enabled) {
      game.hidden = enabled;
      help.hidden = enabled;
    },
    update(frames, elapsedMilliseconds, model) {
      fps.textContent = `FPS: ${Math.round(frames * 1000 / elapsedMilliseconds)}`;
      const filled = Math.round(model.power * 12);
      const meter = '█'.repeat(filled) + '░'.repeat(12 - filled);
      const height = Math.round(model.launchAngle * 180 / Math.PI);
      game.textContent = `STROKES: ${model.strokes}   POWER [${meter}]   HEIGHT ${height}°   ${model.status}`;
    },
  };
}

export function createLaserHud() {
  const fps = byId('fps');
  const title = byId('game');
  const hint = byId('hint');
  const help = byId('help');
  const status = byId('status');
  const inventory = byId('inventory');
  const banner = byId('banner');
  const crosshair = byId('crosshair');
  help.textContent = 'Click to capture the mouse · WASD walk · Shift run · Click place or pick up · '
    + 'Right-click / X pick up · Q / E or scroll turn (Shift: fine) · 1 mirror · 2 diffractor · '
    + 'R restart · N / P next / previous level · H hide help · Esc release mouse';
  title.textContent = '';
  byId('laser-hud').hidden = false;
  hint.hidden = false;
  crosshair.hidden = false;

  return {
    update(frames, elapsedMilliseconds, game) {
      fps.textContent = `FPS: ${Math.round(frames * 1000 / elapsedMilliseconds)}`;
      title.textContent = game.titleText;
      hint.textContent = game.level.hint;
      status.textContent = game.statusText;
      status.hidden = !game.statusText;
      inventory.textContent = game.inventoryText;
      inventory.hidden = !game.inventoryText;
      banner.textContent = game.bannerText ?? '';
      banner.hidden = !game.bannerText;
      crosshair.className = game.targetBlock !== null ? 'target' : game.ghost && game.ghostValid ? 'place' : '';
    },
    toggleHelp() { help.hidden = !help.hidden; },
  };
}

export function showFatalError(title, error) {
  const node = byId('error');
  node.hidden = false;
  node.textContent = `${title}:\n${error.stack || error}`;
}
