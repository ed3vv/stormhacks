const byId = id => document.getElementById(id);

export function createMenu() {
  const root = byId('menu');
  const main = byId('menu-main');
  const games = byId('menu-games');
  const note = byId('menu-note');
  let startGolf = () => {};

  byId('play').addEventListener('click', () => {
    main.hidden = true;
    games.hidden = false;
  });
  byId('play-golf').addEventListener('click', () => startGolf());
  for (const id of ['play-laser', 'play-nextbots']) {
    byId(id).addEventListener('click', () => { note.hidden = false; });
  }

  return {
    hide() {
      root.hidden = true;
      byId('hud').hidden = false;
    },
    onGolf(callback) { startGolf = callback; },
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

export function showFatalError(title, error) {
  const node = byId('error');
  node.hidden = false;
  node.textContent = `${title}:\n${error.stack || error}`;
}
