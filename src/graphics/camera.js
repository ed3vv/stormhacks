import { dot, add, mul, len, norm, xyz, clamp, tangent, cross4 } from '../core/vector.js';

const LEASH=.34, MIN_EYE_ALTITUDE=.08, LOOK_RATE=5, SETTLE_TIME=.7;
const ease = t => t*t*(3-2*t);

// Tangent direction at p pointing away from the nearest ground, or null at the center.
function localUp(p){
  const horizontal=len(xyz(p));if(horizontal<1e-4)return null;
  const q=mul(xyz(p),1/horizontal),side=p[3]>=0?1:-1;
  return [...mul(q,-Math.abs(p[3])),side*horizontal];
}

// Completes an orthonormal frame; right is chosen so det[eye;forward;up;right] = -1,
// matching the handedness the shader was written for.
function finishFrame(eye,forward,up,fallbackUp){
  forward=norm(tangent(forward,eye));
  let u=tangent(tangent(up,eye),forward);
  if(len(u)<1e-4)u=tangent(tangent(fallbackUp,eye),forward);
  u=norm(u);
  return {eye,forward,up:u,right:mul(cross4(eye,forward,u),-1)};
}

function slerp(a,b,t){
  const c=clamp(dot(a,b),-1,1),angle=Math.acos(c);
  if(angle<1e-6)return [...b];
  const s=Math.sin(angle);
  return norm(add(mul(a,Math.sin((1-t)*angle)/s),mul(b,Math.sin(t*angle)/s)));
}

// The standing golfer view: behind the ball, pitched by the model's camera pitch.
function groundFrame(m){
  const height=.16+m.cameraSupportAltitude,p=m.position,f=m.forward,ch=Math.cos(height),sh=Math.sin(height),cp=Math.cos(m.pitch),sp=Math.sin(m.pitch);
  const eye=[...mul(p,ch),sh],gf=[...f,0],gu=[...mul(p,-sh),ch];
  return {eye,forward:add(mul(gf,cp),mul(gu,sp)),up:add(mul(gf,-sp),mul(gu,cp)),right:[...m.right,0]};
}

export class Camera {
  constructor(){this.frame=null;this.wasAirborne=false;this.settleFrom=null;this.settle=1;}
  update(m,dt){
    const ground=groundFrame(m);
    if(!this.frame){this.frame=ground;return;}
    if(m.airborne)this.frame=this.follow(m,dt);
    else{
      if(this.wasAirborne){this.settleFrom=this.frame;this.settle=0;}
      this.settle=Math.min(1,this.settle+dt/SETTLE_TIME);
      this.frame=this.settle<1?this.blend(this.settleFrom,ground,ease(this.settle)):ground;
    }
    this.wasAirborne=m.airborne;
  }
  // Trails the ball on a leash, turning smoothly to keep it in view. Through the center of
  // the world "up" flips to the opposite wall, and the camera rolls over to match.
  follow(m,dt){
    const ball=m.airCenter,prev=this.frame;
    let eye=prev.eye;
    const distance=Math.acos(clamp(dot(eye,ball),-1,1));
    if(distance>LEASH){const toward=norm(tangent(ball,eye)),t=distance-LEASH;eye=norm(add(mul(eye,Math.cos(t)),mul(toward,Math.sin(t))));}
    const altitude=Math.atan2(Math.abs(eye[3]),len(xyz(eye)));
    if(altitude<MIN_EYE_ALTITUDE){const q=norm(xyz(eye)),side=eye[3]>=0?1:-1;eye=[...mul(q,Math.cos(MIN_EYE_ALTITUDE)),side*Math.sin(MIN_EYE_ALTITUDE)];}
    const k=1-Math.exp(-LOOK_RATE*dt),forward=tangent(prev.forward,eye),look=norm(tangent(ball,eye));
    let up=tangent(prev.up,eye);
    const worldUp=localUp(eye);
    if(worldUp){const target=tangent(worldUp,look);if(len(target)>.05)up=add(mul(up,1-k),mul(norm(target),k));}
    return finishFrame(eye,add(mul(forward,1-k),mul(look,k)),up,prev.up);
  }
  blend(a,b,t){
    const eye=slerp(a.eye,b.eye,t);let forward=add(mul(a.forward,1-t),mul(b.forward,t));
    if(len(tangent(forward,eye))<1e-3)forward=b.forward;
    return finishFrame(eye,forward,add(mul(a.up,1-t),mul(b.up,t)),b.up);
  }
}
