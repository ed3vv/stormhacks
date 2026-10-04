import { dot, sub, mul, len } from '../core/vector.js';
import { vertexShader } from './shaders.js';
import { laserFragmentShader } from './laser-shaders.js';
import { packedBoxData, HOLE_POSITION, HOLE_RADIUS } from '../game/world/scenery.js';
import { complement, pointAt, tangentAt } from '../game/laser/geometry.js';

const BLOCK_TEXELS = 5, BEAM_TEXELS = 6, BEAM_HALO = .006;
const BEAM_STYLE = {
  laser: { color: [1, .12, .08, 1], intensity: 1 },
  diffracted: { color: [1, .55, .2, 1], intensity: .55 },
  preview: { color: [.3, .9, 1, 2], intensity: .8 },
};

/**
 * Draws the laser maze. Blocks and beam pieces go to the shader as float textures,
 * one row each:
 *   block: [facing, kind] [side, state] [position, bounding cos] [lower bounds] [upper bounds]
 *   beam:  [normal / cutoff] [eye offset dir] [start] [start direction] [color] [length, intensity, eye offset]
 */
export class LaserRenderer {
  constructor(canvas) {
    this.canvas = canvas;
    this.gl = canvas.getContext('webgl2', { antialias: false });
    if (!this.gl) throw new Error('WebGL2 is unavailable in this browser.');
    const gl = this.gl, compile = (type, source) => {
      const s = gl.createShader(type); gl.shaderSource(s, source); gl.compileShader(s);
      if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s));
      return s;
    };
    this.program = gl.createProgram();
    gl.attachShader(this.program, compile(gl.VERTEX_SHADER, vertexShader));
    gl.attachShader(this.program, compile(gl.FRAGMENT_SHADER, laserFragmentShader));
    gl.linkProgram(this.program);
    if (!gl.getProgramParameter(this.program, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(this.program));
    gl.useProgram(this.program); gl.bindVertexArray(gl.createVertexArray());
    this.locations = {};
    for (const n of ['resolution', 'player', 'forward', 'rightAxis', 'upAxis', 'hole', 'time', 'counts', 'ghostTint', 'boxData[0]', 'blockData', 'beamData']) {
      this.locations[n] = gl.getUniformLocation(this.program, n);
    }
    gl.uniform4fv(this.locations['boxData[0]'], packedBoxData());
    gl.uniform4fv(this.locations.hole, [...HOLE_POSITION, HOLE_RADIUS]);
    this.blockTexture = this.createDataTexture(0, 'blockData');
    this.beamTexture = this.createDataTexture(1, 'beamData');
  }

  createDataTexture(unit, name) {
    const gl = this.gl, texture = gl.createTexture();
    gl.activeTexture(gl.TEXTURE0 + unit); gl.bindTexture(gl.TEXTURE_2D, texture);
    for (const p of [gl.TEXTURE_MIN_FILTER, gl.TEXTURE_MAG_FILTER]) gl.texParameteri(gl.TEXTURE_2D, p, gl.NEAREST);
    gl.uniform1i(this.locations[name], unit);
    return texture;
  }

  upload(unit, texture, width, rows) {
    const gl = this.gl, data = new Float32Array(Math.max(rows.length, 1) * width * 4);
    rows.forEach((row, i) => data.set(row, i * width * 4));
    gl.activeTexture(gl.TEXTURE0 + unit); gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA32F, width, Math.max(rows.length, 1), 0, gl.RGBA, gl.FLOAT, data);
  }

  draw(camera, game, time) {
    const gl = this.gl, dpr = Math.min(devicePixelRatio || 1, 1.5), w = Math.max(1, innerWidth * dpr | 0), h = Math.max(1, innerHeight * dpr | 0);
    if (this.canvas.width !== w || this.canvas.height !== h) { this.canvas.width = w; this.canvas.height = h; }
    const pixelAngle = 1.28 / h, light = game.light, preview = game.preview;

    const blocks = game.blocks.map((block, i) => {
      let state = 0;
      if (light.litGoals.has(i) || light.activeDiffractors.has(i)) state = 1;
      else if (preview.litGoals.has(i)) state = 2;
      return blockRow(block, state);
    });
    const ghostIndex = game.ghost ? blocks.length : -1;
    if (game.ghost) blocks.push(blockRow(game.ghost, 0));
    const beams = [];
    for (const segment of [...light.segments, ...preview.segments]) appendBeams(segment, camera.eye, pixelAngle, beams);
    this.upload(0, this.blockTexture, BLOCK_TEXELS, blocks);
    this.upload(1, this.beamTexture, BEAM_TEXELS, beams);

    gl.viewport(0, 0, w, h); gl.useProgram(this.program);
    gl.uniform2f(this.locations.resolution, w, h);
    gl.uniform4fv(this.locations.player, camera.eye); gl.uniform4fv(this.locations.forward, camera.forward);
    gl.uniform4fv(this.locations.rightAxis, camera.right); gl.uniform4fv(this.locations.upAxis, camera.up);
    gl.uniform1f(this.locations.time, time);
    gl.uniform4i(this.locations.counts, blocks.length, beams.length, ghostIndex, game.targetBlock ?? -1);
    gl.uniform4fv(this.locations.ghostTint, game.ghostValid ? [.45, 1, .55, 1] : [1, .35, .3, 0]);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
  }
}

function blockRow(block, state) {
  return [...block.facing, block.kind, ...block.side, state, ...block.position, block.boundingCos, ...block.lo, 0, ...block.hi, 0];
}

/**
 * Splits a segment into pieces shorter than a full turn (so a piece's arc parameter is
 * unambiguous) and precomputes, for the eye, the unit normal of the great sphere
 * through the eye and the piece, the eye's unit offset from the piece's plane and its
 * length, and the sine of the widest angle at which the piece can light a pixel.
 */
function appendBeams(segment, eye, pixelAngle, beams) {
  const style = segment.preview ? BEAM_STYLE.preview : segment.diffracted ? BEAM_STYLE.diffracted : BEAM_STYLE.laser;
  const pieces = Math.max(1, Math.ceil(segment.length / 6)), pieceLength = segment.length / pieces;
  for (let k = 0; k < pieces; k++) {
    const u = pointAt(segment.start, segment.direction, k * pieceLength);
    const v = tangentAt(segment.start, segment.direction, k * pieceLength);
    const perpendicular = sub(sub(eye, mul(u, dot(eye, u))), mul(v, dot(eye, v)));
    const offset = len(perpendicular);
    // The eye lies on the beam's own great circle: it is seen end-on, so skip it.
    if (offset < 1e-5) continue;
    const w = mul(perpendicular, 1 / offset);
    const cutoff = Math.sin(Math.min(3 * Math.max(BEAM_HALO / offset, 2.5 * pixelAngle), Math.PI / 2));
    beams.push([...mul(complement(u, v, w), 1 / cutoff), ...w, ...u, ...v, ...style.color, pieceLength, style.intensity, offset, 0]);
  }
}
