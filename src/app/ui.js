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

const MIX = (a, b, t) => a.map((v, i) => Math.round(v + (b[i] - v) * t));

export function createHud() {
  const nodes = {};
  for (const id of ['scoreline', 'clubline', 'fps', 'hint', 'message', 'aim', 'meter', 'meter-label', 'meter-fill', 'meter-pole'])
    nodes[id] = byId(id);
  const set = (node, text) => { if (node.textContent !== text) node.textContent = text; };

  return {
    setFps(frames, elapsedMilliseconds) {
      set(nodes.fps, `FPS: ${Math.round(frames * 1000 / elapsedMilliseconds)}`);
    },
    update(view) {
      set(nodes.scoreline, `Hole ${view.hole}/${view.holes}   Par ${view.par}   Strokes ${view.strokes}   Total ${view.total}`);
      set(nodes.clubline, `Club: ${view.club}      Pin: ${view.pin.toFixed(1)} m      Pole: ${view.pole.toFixed(1)} m straight up`);
      set(nodes.hint, view.hint);
      set(nodes.message, view.message);

      nodes.aim.hidden = !view.aiming;
      if (view.aiming) set(nodes.aim, `HEIGHT ${Math.round(view.aimAngle)}°`);

      nodes.meter.hidden = !view.charging;
      if (view.charging) {
        set(nodes['meter-label'], `${view.club}  ${view.shotSpeed.toFixed(1)} m/s`);
        nodes['meter-fill'].style.width = `${view.power * 100}%`;
        nodes['meter-fill'].style.background = `rgb(${MIX([77, 230, 77], [255, 64, 38], view.power).join(' ')})`;
        // where the meter has to reach for a vertical shot to crest the pole
        const showPole = view.aiming && view.aimAngle > 75 && view.poleMark <= 1;
        nodes['meter-pole'].hidden = !showPole;
        if (showPole) nodes['meter-pole'].style.left = `calc(${view.poleMark * 100}% - 1px)`;
      }
    },
  };
}

export function showFatalError(title, error) {
  const node = byId('error');
  node.hidden = false;
  node.textContent = `${title}:\n${error.stack || error}`;
}
