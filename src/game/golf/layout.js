// The map, authored on a flat plane: metres on the exp-map chart around the world origin. The
// whole world is the disk of radius pi * R = 37.7 m, and that rim is the single point
// antipodal to the centre. See placement.js for the details.
//
// Written by /map-editor.html.
export const LAYOUT = {
  seed: 11,

  holes: [
    { tee: [0, 3], cup: [0, 24], par: 3 },
    { tee: [9.7, 23.5], cup: [-22, 8], par: 3 },
    { tee: [-20, 6], cup: [16.1, -4.8], par: 4 },
    { tee: [16.7, -10.6], cup: [-2, -20], par: 3 },
    { tee: [16.9, -13.2], cup: [-16.6, -1.8], par: 3 },
  ],

  props: [
    { type: 'house', x: 7, z: 12, yaw: 200, style: 0 },
    { type: 'house', x: -12, z: -3, yaw: 75, style: 1 },
    { type: 'house', x: 11, z: -11, yaw: -35, style: 2 },
    { type: 'house', x: -21, z: -14, yaw: 130, style: 0 },
    { type: 'house', x: -6.7, z: 21.3, yaw: 0, style: 0 },
  ],

  scatter: { tree: 34, rock: 22, mushroom: 26, grass: 1100 },
};
