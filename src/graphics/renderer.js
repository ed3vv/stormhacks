import { vertexShader, fragmentShader } from './shaders.js';
import { buildArrow, buildBall, buildFlag, buildGround, buildGuide } from './scenery.js';
import { CUP_RADIUS, GREEN_RADIUS } from '../game/golf/course.js';
import { WORLD_RADIUS, column } from '../core/s3.js';

const IDENTITY = new Float32Array([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);
const NEAR_CLIP = 0.004;          // geometry this close to the eye (or to its antipode) is clipped
const FOCAL = 1 / 0.64;           // vertical half-field of view
const AMBIENT = 0.62;
const SUN = (() => { const s = [0.55, 0.75, 0.35, 0.20], l = Math.hypot(...s); return s.map(c => c / l); })();
const HAZE = [0.82, 0.90, 0.96, 0.22];

const UNIFORMS = ['model', 'eye', 'forward', 'rightAxis', 'upAxis', 'focal', 'aspect', 'nearClip',
  'sun', 'haze', 'hole', 'ambient', 'groundPattern', 'worldRadius', 'cupRadius', 'greenRadius'];

export class Renderer {
  constructor(canvas, sceneryMesh) {
    this.canvas = canvas;
    const gl = canvas.getContext('webgl2', { antialias: true });
    if (!gl) throw new Error('WebGL2 is unavailable in this browser.');
    this.gl = gl;

    const compile = (type, source) => {
      const shader = gl.createShader(type);
      gl.shaderSource(shader, source); gl.compileShader(shader);
      if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(shader));
      return shader;
    };
    this.program = gl.createProgram();
    gl.attachShader(this.program, compile(gl.VERTEX_SHADER, vertexShader));
    gl.attachShader(this.program, compile(gl.FRAGMENT_SHADER, fragmentShader));
    gl.linkProgram(this.program);
    if (!gl.getProgramParameter(this.program, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(this.program));
    gl.useProgram(this.program);

    this.locations = {};
    for (const name of UNIFORMS) this.locations[name] = gl.getUniformLocation(this.program, name);
    this.attributes = ['position', 'normal', 'color'].map(name => gl.getAttribLocation(this.program, name));

    this.ground = this.upload(buildGround());
    this.scenery = this.upload(sceneryMesh);
    this.ball = this.upload(buildBall());
    this.flag = this.upload(buildFlag());
    this.guide = this.upload(buildGuide());
    this.arrow = this.upload(buildArrow());
    this.modelMatrix = new Float32Array(16);

    gl.enable(gl.DEPTH_TEST);
    gl.depthFunc(gl.LEQUAL);
    gl.disable(gl.CULL_FACE);           // props are two-sided, as in the Unity build
    gl.clearColor(HAZE[0], HAZE[1], HAZE[2], 1);
    gl.uniform1f(this.locations.focal, FOCAL);
    gl.uniform1f(this.locations.nearClip, NEAR_CLIP);
    gl.uniform1f(this.locations.ambient, AMBIENT);
    gl.uniform1f(this.locations.worldRadius, WORLD_RADIUS);
    gl.uniform1f(this.locations.cupRadius, CUP_RADIUS);
    gl.uniform1f(this.locations.greenRadius, GREEN_RADIUS);
    gl.uniform4fv(this.locations.sun, SUN);
    gl.uniform4fv(this.locations.haze, HAZE);
  }

  upload({ positions, normals, colors, indices }) {
    const gl = this.gl, vao = gl.createVertexArray();
    gl.bindVertexArray(vao);
    for (const [index, data, size] of [[0, positions, 4], [1, normals, 4], [2, colors, 3]]) {
      gl.bindBuffer(gl.ARRAY_BUFFER, gl.createBuffer());
      gl.bufferData(gl.ARRAY_BUFFER, data, gl.STATIC_DRAW);
      gl.enableVertexAttribArray(this.attributes[index]);
      gl.vertexAttribPointer(this.attributes[index], size, gl.FLOAT, false, 0, 0);
    }
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, gl.createBuffer());
    gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, indices, gl.STATIC_DRAW);
    gl.bindVertexArray(null);
    return { vao, count: indices.length };
  }

  drawMesh(mesh, frame, ground = 0) {
    const gl = this.gl;
    this.modelMatrix.set(frame);
    gl.uniformMatrix4fv(this.locations.model, false, this.modelMatrix);
    gl.uniform1f(this.locations.groundPattern, ground);
    gl.bindVertexArray(mesh.vao);
    gl.drawElements(gl.TRIANGLES, mesh.count, gl.UNSIGNED_INT, 0);
  }

  /** scene: { view, cup, flag, ball, arrow, guide } - frames are SO(4), or null to hide a prop. */
  draw(scene) {
    const gl = this.gl;
    const dpr = Math.min(devicePixelRatio || 1, 1.5);
    const width = Math.max(1, innerWidth * dpr | 0), height = Math.max(1, innerHeight * dpr | 0);
    if (this.canvas.width !== width || this.canvas.height !== height) { this.canvas.width = width; this.canvas.height = height; }
    gl.viewport(0, 0, width, height);
    gl.useProgram(this.program);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);

    gl.uniform1f(this.locations.aspect, width / height);
    gl.uniform4fv(this.locations.rightAxis, column(scene.view, 0));
    gl.uniform4fv(this.locations.upAxis, column(scene.view, 1));
    gl.uniform4fv(this.locations.forward, column(scene.view, 2));
    gl.uniform4fv(this.locations.eye, column(scene.view, 3));
    gl.uniform4fv(this.locations.hole, scene.cup);

    this.drawMesh(this.ground, IDENTITY, 1);
    this.drawMesh(this.scenery, IDENTITY);
    for (const [mesh, frame] of [[this.flag, scene.flag], [this.ball, scene.ball], [this.arrow, scene.arrow], [this.guide, scene.guide]])
      if (frame) this.drawMesh(mesh, frame);
  }
}
