export const dot = (a,b) => a.reduce((s,v,i)=>s+v*b[i],0);
export const add = (a,b) => a.map((v,i)=>v+b[i]);
export const sub = (a,b) => a.map((v,i)=>v-b[i]);
export const mul = (a,s) => a.map(v=>v*s);
export const len = a => Math.hypot(...a);
export const norm = a => { const l=len(a); return l>1e-9?mul(a,1/l):a.map(()=>0); };
export const cross = (a,b) => [a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
export const xyz = a => [a[0],a[1],a[2]];
export const clamp = (x,a,b) => Math.max(a,Math.min(b,x));
// Component of v tangent to the unit hypersphere at p.
export const tangent = (v,p) => sub(v,mul(p,dot(v,p)));

const det3 = (a,b,c) => a[0]*(b[1]*c[2]-b[2]*c[1])-a[1]*(b[0]*c[2]-b[2]*c[0])+a[2]*(b[0]*c[1]-b[1]*c[0]);
// 4D vector orthogonal to a, b and c, with dot(cross4(a,b,c),v) = det[a;b;c;v].
export const cross4 = (a,b,c) => [0,1,2,3].map(i=>{const cols=[0,1,2,3].filter(j=>j!==i),pick=v=>cols.map(j=>v[j]);return (i%2?1:-1)*det3(pick(a),pick(b),pick(c));});
