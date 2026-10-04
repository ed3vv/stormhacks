// Collects flat-shaded, vertex-coloured triangles in local Euclidean metre coordinates, then
// bakes them onto S^3. Faces are pre-tessellated to maxEdge so the exponential map bends them
// smoothly instead of leaving long straight chords across a curved world.
import { exponentialWithNormal, transform } from '../core/s3.js';

const faceNormal = (a, b, c) => {
  const u = [b[0] - a[0], b[1] - a[1], b[2] - a[2]], v = [c[0] - a[0], c[1] - a[1], c[2] - a[2]];
  const n = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]];
  const length = Math.hypot(...n);
  return length > 1e-7 ? [n[0] / length, n[1] / length, n[2] / length] : [0, 1, 0];
};
const lerp3 = (a, b, t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
const dist3 = (a, b) => Math.hypot(a[0] - b[0], a[1] - b[1], a[2] - b[2]);
export const shade = (color, k) => [color[0] * k, color[1] * k, color[2] * k];

export class MeshBuilder {
  constructor(maxEdge = 0.5) { this.maxEdge = maxEdge; this.verts = []; this.normals = []; this.colors = []; this.tris = []; }

  add(p, n, color) { this.verts.push(p); this.normals.push(n); this.colors.push(color); return this.verts.length - 1; }
  segments(length) { return Math.max(1, Math.ceil(length / this.maxEdge - 1e-4)); }

  /** Quad a-b-c-d (in order around the edge), tessellated as a grid. */
  quad(a, b, c, d, color) {
    const n = faceNormal(a, b, d);
    const nu = this.segments(Math.max(dist3(a, b), dist3(d, c)));
    const nv = this.segments(Math.max(dist3(a, d), dist3(b, c)));
    const start = this.verts.length;
    for (let j = 0; j <= nv; j++) for (let i = 0; i <= nu; i++)
      this.add(lerp3(lerp3(a, b, i / nu), lerp3(d, c, i / nu), j / nv), n, color);
    for (let j = 0; j < nv; j++) for (let i = 0; i < nu; i++) {
      const k = start + j * (nu + 1) + i;
      this.tris.push(k, k + nu + 1, k + 1, k + 1, k + nu + 1, k + nu + 2);
    }
  }

  /** Triangle, tessellated barycentrically. */
  tri(a, b, c, color) {
    const n = faceNormal(a, b, c);
    const k = this.segments(Math.max(dist3(a, b), dist3(b, c), dist3(c, a)));
    const index = [];
    for (let i = 0; i <= k; i++) {
      index.push([]);
      for (let j = 0; j <= k - i; j++) {
        const p = [0, 1, 2].map(t => a[t] + (b[t] - a[t]) * (i / k) + (c[t] - a[t]) * (j / k));
        index[i].push(this.add(p, n, color));
      }
    }
    for (let i = 0; i < k; i++) for (let j = 0; j < k - i; j++) {
      this.tris.push(index[i][j], index[i + 1][j], index[i][j + 1]);
      if (i + j < k - 1) this.tris.push(index[i + 1][j], index[i + 1][j + 1], index[i][j + 1]);
    }
  }

  /** Axis-aligned box. */
  box(min, max, color) {
    const P = (x, y, z) => [x ? max[0] : min[0], y ? max[1] : min[1], z ? max[2] : min[2]];
    this.quad(P(0, 0, 0), P(1, 0, 0), P(1, 1, 0), P(0, 1, 0), color);
    this.quad(P(1, 0, 1), P(0, 0, 1), P(0, 1, 1), P(1, 1, 1), color);
    this.quad(P(0, 0, 1), P(0, 0, 0), P(0, 1, 0), P(0, 1, 1), color);
    this.quad(P(1, 0, 0), P(1, 0, 1), P(1, 1, 1), P(1, 1, 0), color);
    this.quad(P(0, 1, 0), P(1, 1, 0), P(1, 1, 1), P(0, 1, 1), color);
    this.quad(P(0, 0, 1), P(1, 0, 1), P(1, 0, 0), P(0, 0, 0), color);
  }

  /** A thick plate: quad a-b-c-d extruded by thickness along its upward-facing normal. */
  slab(a, b, c, d, thickness, color, edge) {
    let n = faceNormal(a, b, d);
    if (n[1] < 0) n = [-n[0], -n[1], -n[2]];
    const o = [n[0] * thickness, n[1] * thickness, n[2] * thickness];
    const up = p => [p[0] + o[0], p[1] + o[1], p[2] + o[2]];
    this.quad(a, b, c, d, color);
    this.quad(up(a), up(b), up(c), up(d), color);
    for (const [p, q] of [[a, b], [b, c], [c, d], [d, a]]) this.quad(p, q, up(q), up(p), edge);
  }

  /** Vertical frustum (cylinder, or cone when topRadius is 0) standing on baseCenter. */
  frustum(baseCenter, baseRadius, topRadius, height, sides, color, phase = 0) {
    const top = [baseCenter[0], baseCenter[1] + height, baseCenter[2]];
    const at = (center, d, r) => [center[0] + d[0] * r, center[1], center[2] + d[2] * r];
    for (let i = 0; i < sides; i++) {
      const a0 = phase + 2 * Math.PI * i / sides, a1 = phase + 2 * Math.PI * (i + 1) / sides;
      const d0 = [Math.cos(a0), 0, Math.sin(a0)], d1 = [Math.cos(a1), 0, Math.sin(a1)];
      if (topRadius > 1e-4) {
        this.quad(at(baseCenter, d0, baseRadius), at(baseCenter, d1, baseRadius), at(top, d1, topRadius), at(top, d0, topRadius), color);
        this.tri(at(top, d0, topRadius), at(top, d1, topRadius), top, color);
      } else this.tri(at(baseCenter, d0, baseRadius), at(baseCenter, d1, baseRadius), top, color);
      this.tri(at(baseCenter, d1, baseRadius), at(baseCenter, d0, baseRadius), baseCenter, color);
    }
  }

  /** Low-poly ellipsoid (subdivided icosahedron) with optional radial jitter. */
  icosphere(center, radii, subdivisions, color, random = null, jitter = 0, faceColor = null) {
    const t = (1 + Math.sqrt(5)) / 2;
    const points = [
      [-1, t, 0], [1, t, 0], [-1, -t, 0], [1, -t, 0], [0, -1, t], [0, 1, t],
      [0, -1, -t], [0, 1, -t], [t, 0, -1], [t, 0, 1], [-t, 0, -1], [-t, 0, 1],
    ].map(p => { const l = Math.hypot(...p); return [p[0] / l, p[1] / l, p[2] / l]; });
    let faces = [
      0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
      3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
    ];
    for (let s = 0; s < subdivisions; s++) {
      const mid = new Map();
      const midpoint = (a, b) => {
        const key = a < b ? `${a}_${b}` : `${b}_${a}`;
        if (mid.has(key)) return mid.get(key);
        const m = [0, 1, 2].map(i => (points[a][i] + points[b][i]) / 2), l = Math.hypot(...m);
        points.push([m[0] / l, m[1] / l, m[2] / l]);
        mid.set(key, points.length - 1);
        return points.length - 1;
      };
      const next = [];
      for (let f = 0; f < faces.length; f += 3) {
        const [a, b, c] = [faces[f], faces[f + 1], faces[f + 2]];
        const ab = midpoint(a, b), bc = midpoint(b, c), ca = midpoint(c, a);
        next.push(a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca);
      }
      faces = next;
    }
    const scaled = points.map(p => {
      const k = 1 + (random ? jitter * (random() * 2 - 1) : 0);
      return [0, 1, 2].map(i => center[i] + p[i] * k * radii[i]);
    });
    for (let f = 0; f < faces.length; f += 3) {
      const centroid = [0, 1, 2].map(i => (points[faces[f]][i] + points[faces[f + 1]][i] + points[faces[f + 2]][i]) / 3);
      this.tri(scaled[faces[f]], scaled[faces[f + 1]], scaled[faces[f + 2]], faceColor ? faceColor(centroid) : color);
    }
  }
}

/** Triangles already living on S^3: 4D positions, 4D tangent normals and vertex colours. */
export class MeshData {
  constructor() { this.positions = []; this.normals = []; this.colors = []; this.indices = []; }
  get vertexCount() { return this.positions.length / 4; }

  addVertex(point, normal, color) {
    this.positions.push(point[0], point[1], point[2], point[3]);
    this.normals.push(normal[0], normal[1], normal[2], normal[3]);
    this.colors.push(color[0], color[1], color[2]);
    return this.vertexCount - 1;
  }

  addTriangle(a, b, c) { this.indices.push(a, b, c); }

  /** Places a builder's local-metre geometry on S^3 at the given frame. */
  append(builder, frame, radius) {
    const offset = this.vertexCount;
    for (let i = 0; i < builder.verts.length; i++) {
      const { point, normal } = exponentialWithNormal(builder.verts[i], builder.normals[i], radius);
      this.addVertex(transform(frame, point), transform(frame, normal), builder.colors[i]);
    }
    for (const index of builder.tris) this.indices.push(index + offset);
  }

  toBuffers() {
    return {
      positions: new Float32Array(this.positions),
      normals: new Float32Array(this.normals),
      colors: new Float32Array(this.colors),
      indices: new Uint32Array(this.indices),
    };
  }
}

/** Bakes a builder on its own, for props drawn with a live per-frame model matrix. */
export function bakeLocal(builder, radius) {
  const mesh = new MeshData();
  mesh.append(builder, new Float64Array([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]), radius);
  return mesh.toBuffers();
}
