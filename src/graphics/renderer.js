import { vertexShader, fragmentShader, replayVertexShader, replayFragmentShader } from './shaders.js';
import { buildArrow, buildBall, buildFlag, buildGround, buildGuide, buildMarker } from './scenery.js';
import { CUP_RADIUS, GREEN_RADIUS } from '../game/golf/course.js';
import { WORLD_RADIUS, column, multiply } from '../core/s3.js';

const IDENTITY = new Float32Array([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);
const NEAR_CLIP = 0.004;          // geometry this close to the eye (or to its antipode) is clipped
const FOCAL = 1 / 0.64;           // vertical half-field of view
const AMBIENT = 0.62;
const SUN = (() => { const s = [0.55, 0.75, 0.35, 0.20], l = Math.hypot(...s); return s.map(c => c / l); })();
const HAZE = [0.82, 0.90, 0.96, 0.22];
const SPACE = [0.05, 0.07, 0.12];     // behind the world, in the replay view
const SHELL_ALPHA = 0.17, SCENERY_ALPHA = 0.5;   // replay glass: the ground is thinner than the props

// VAOs are shared between the two programs, so both must agree on where the attributes live.
const ATTRIBUTES = ['position', 'normal', 'color'];

const UNIFORMS = ['model', 'eye', 'forward', 'rightAxis', 'upAxis', 'focal', 'aspect', 'nearClip',
  'sun', 'haze', 'hole', 'ambient', 'groundPattern', 'worldRadius', 'cupRadius', 'greenRadius'];

const REPLAY_UNIFORMS = ['model', 'viewProjection', 'fromS3', 'markerOffset', 'markerScale',
  'cameraPosition', 'lightDirection', 'tint', 'shell', 'alpha', 'ambient'];

// --- the replay's ordinary 3D camera -------------------------------------------------------

const REPLAY_FOV = 42 * Math.PI / 180;
const REPLAY_LIGHT = (() => { const l = [0.4, 0.75, 0.5], n = Math.hypot(...l); return l.map(c => c / n); })();

function perspective(fovy, aspect, near, far) {
  const f = 1 / Math.tan(fovy / 2), m = new Float64Array(16);
  m[0] = f / aspect; m[5] = f;
  m[10] = (far + near) / (near - far); m[11] = -1;
  m[14] = 2 * far * near / (near - far);
  return m;
}

function lookAt(eye, up) {
  const normalize = v => { const l = Math.hypot(...v); return v.map(c => c / l); };
  const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
  const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
  const forward = normalize(eye.map(c => -c));          // every replay looks at the world's centre
  const side = normalize(cross(forward, up));
  const u = cross(side, forward);
  return new Float64Array([
    side[0], u[0], -forward[0], 0,
    side[1], u[1], -forward[1], 0,
    side[2], u[2], -forward[2], 0,
    -dot(side, eye), -dot(u, eye), dot(forward, eye), 1,
  ]);
}

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
    const link = (vertex, fragment) => {
      const program = gl.createProgram();
      gl.attachShader(program, compile(gl.VERTEX_SHADER, vertex));
      gl.attachShader(program, compile(gl.FRAGMENT_SHADER, fragment));
      for (const [index, name] of ATTRIBUTES.entries()) gl.bindAttribLocation(program, index, name);
      gl.linkProgram(program);
      if (!gl.getProgramParameter(program, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(program));
      return program;
    };
    const uniformsOf = (program, names) => {
      const locations = {};
      for (const name of names) locations[name] = gl.getUniformLocation(program, name);
      return locations;
    };

    this.program = link(vertexShader, fragmentShader);
    this.locations = uniformsOf(this.program, UNIFORMS);
    this.replayProgram = link(replayVertexShader, replayFragmentShader);
    this.replayLocations = uniformsOf(this.replayProgram, REPLAY_UNIFORMS);

    this.ground = this.upload(buildGround());
    this.scenery = this.upload(sceneryMesh);
    this.ball = this.upload(buildBall());
    this.flag = this.upload(buildFlag());
    this.guide = this.upload(buildGuide());
    this.arrow = this.upload(buildArrow());
    this.marker = this.upload(buildMarker());
    this.trail = this.createTrail();
    this.modelMatrix = new Float32Array(16);
    this.viewProjection = new Float32Array(16);

    gl.useProgram(this.program);
    gl.enable(gl.DEPTH_TEST);
    gl.depthFunc(gl.LEQUAL);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
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
      gl.enableVertexAttribArray(index);
      gl.vertexAttribPointer(index, size, gl.FLOAT, false, 0, 0);
    }
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, gl.createBuffer());
    gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, indices, gl.STATIC_DRAW);
    gl.bindVertexArray(null);
    return { vao, count: indices.length };
  }

  /** A line strip the replay refills per shot: the flight path, in the ball's own 3D coordinates. */
  createTrail() {
    const gl = this.gl, vao = gl.createVertexArray();
    gl.bindVertexArray(vao);
    const position = gl.createBuffer(), color = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, position);
    gl.enableVertexAttribArray(0);
    gl.vertexAttribPointer(0, 4, gl.FLOAT, false, 0, 0);
    gl.bindBuffer(gl.ARRAY_BUFFER, color);
    gl.enableVertexAttribArray(2);
    gl.vertexAttribPointer(2, 3, gl.FLOAT, false, 0, 0);
    gl.bindVertexArray(null);
    return { vao, position, color, count: 0 };
  }

  /** points: the flight path already projected into the ball, one [x, y, z] per sample. */
  uploadTrail(points) {
    const gl = this.gl;
    const positions = new Float32Array(points.length * 4), colors = new Float32Array(points.length * 3);
    for (const [index, point] of points.entries()) {
      positions.set([point[0], point[1], point[2], 0], index * 4);
      const t = points.length > 1 ? index / (points.length - 1) : 0;
      colors.set([1, 0.95 - 0.5 * t, 0.35 - 0.25 * t], index * 3);   // yellow at the tee, orange at the cup
    }
    gl.bindBuffer(gl.ARRAY_BUFFER, this.trail.position);
    gl.bufferData(gl.ARRAY_BUFFER, positions, gl.DYNAMIC_DRAW);
    gl.bindBuffer(gl.ARRAY_BUFFER, this.trail.color);
    gl.bufferData(gl.ARRAY_BUFFER, colors, gl.DYNAMIC_DRAW);
    this.trail.count = points.length;
  }

  resize() {
    const dpr = Math.min(devicePixelRatio || 1, 1.5);
    const width = Math.max(1, innerWidth * dpr | 0), height = Math.max(1, innerHeight * dpr | 0);
    if (this.canvas.width !== width || this.canvas.height !== height) { this.canvas.width = width; this.canvas.height = height; }
    this.gl.viewport(0, 0, width, height);
    return width / height;
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
    const gl = this.gl, aspect = this.resize();
    gl.useProgram(this.program);
    gl.disable(gl.BLEND);
    gl.disable(gl.CULL_FACE);           // props are two-sided, as in the Unity build
    gl.depthMask(true);
    gl.clearColor(HAZE[0], HAZE[1], HAZE[2], 1);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);

    gl.uniform1f(this.locations.aspect, aspect);
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

  replayPiece(mesh, { frame = null, offset = [0, 0, 0], scale = 1, tint = [1, 1, 1], ambient = 1, alpha = 1, shell = 0, strip = 0 }) {
    const gl = this.gl, l = this.replayLocations;
    this.modelMatrix.set(frame || IDENTITY);
    gl.uniformMatrix4fv(l.model, false, this.modelMatrix);
    gl.uniform1f(l.fromS3, frame ? 1 : 0);
    gl.uniform3fv(l.markerOffset, offset);
    gl.uniform1f(l.markerScale, scale);
    gl.uniform3fv(l.tint, tint);
    gl.uniform1f(l.ambient, ambient);
    gl.uniform1f(l.alpha, alpha);
    gl.uniform1f(l.shell, shell);
    gl.bindVertexArray(mesh.vao);
    if (strip) gl.drawArrays(gl.LINE_STRIP, 0, strip);
    else gl.drawElements(gl.TRIANGLES, mesh.count, gl.UNSIGNED_INT, 0);
  }

  /**
   * The world from outside, as a glass ball: the ground is its surface and height above the
   * ground is depth towards its centre, so the whole flight is visible at once.
   *
   * replay: { eye, up, ball, cup, flag, drawnSamples } in the ball's 3D coordinates.
   */
  drawReplay(replay) {
    const gl = this.gl, l = this.replayLocations, aspect = this.resize();
    gl.useProgram(this.replayProgram);
    gl.clearColor(SPACE[0], SPACE[1], SPACE[2], 1);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);

    this.viewProjection.set(multiply(perspective(REPLAY_FOV, aspect, 0.05, 20), lookAt(replay.eye, replay.up)));
    gl.uniformMatrix4fv(l.viewProjection, false, this.viewProjection);
    gl.uniform3fv(l.cameraPosition, replay.eye);
    gl.uniform3fv(l.lightDirection, REPLAY_LIGHT);

    // the far half of the shell first, writing no depth, so everything inside draws over it
    gl.enable(gl.BLEND);
    gl.enable(gl.CULL_FACE);
    gl.depthMask(false);
    gl.cullFace(gl.FRONT);
    this.replayPiece(this.ground, { frame: IDENTITY, tint: [0.42, 0.68, 0.40], ambient: 0.55, alpha: SHELL_ALPHA, shell: 1 });

    // then the solid things inside the glass
    gl.disable(gl.BLEND);
    gl.disable(gl.CULL_FACE);
    gl.depthMask(true);
    if (replay.flag) this.replayPiece(this.flag, { frame: replay.flag, ambient: 0.6 });
    this.replayPiece(this.marker, { offset: replay.cup, scale: 0.022, tint: [0.95, 0.2, 0.15] });
    const drawn = Math.min(replay.drawnSamples, this.trail.count);
    if (drawn > 1) this.replayPiece(this.trail, { strip: drawn });
    this.replayPiece(this.marker, { offset: replay.ball, scale: 0.026, tint: [1, 1, 1] });

    // the houses and trees are glass too, but thicker than the shell: they go after the solid
    // things and write no depth, so the flight shows through whatever stands in front of it
    gl.enable(gl.BLEND);
    gl.depthMask(false);
    this.replayPiece(this.scenery, { frame: IDENTITY, tint: [0.78, 0.78, 0.82], ambient: 0.5, alpha: SCENERY_ALPHA });

    // and the near half of the shell over the top
    gl.enable(gl.CULL_FACE);
    gl.depthMask(false);
    gl.cullFace(gl.BACK);
    this.replayPiece(this.ground, { frame: IDENTITY, tint: [0.42, 0.68, 0.40], ambient: 0.55, alpha: SHELL_ALPHA, shell: 1 });

    gl.disable(gl.BLEND);
    gl.disable(gl.CULL_FACE);
    gl.depthMask(true);
  }
}
