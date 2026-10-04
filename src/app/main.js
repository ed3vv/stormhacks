import { createGolfGame } from '../game/golf/game.js';
import { createLaserGame } from '../game/laser/game.js';
import { createMenu, showFatalError } from './ui.js';

const canvas = document.getElementById('world');
const menu = createMenu();

menu.onGolf(() => {
  try {
    menu.hide();
    createGolfGame(canvas);
  } catch (error) {
    showFatalError('Could not start Spherical Golf', error);
    throw error;
  }
});

menu.onLaser(() => {
  try {
    menu.hide();
    createLaserGame(canvas);
  } catch (error) {
    showFatalError('Could not start the laser maze', error);
    throw error;
  }
});
