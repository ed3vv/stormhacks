import { BOXES, PHYSICS_BOX_COUNT, HOLE_POSITION, HOLE_RADIUS } from '../world/scenery.js';
import { dot, add, sub, mul, len, norm, cross, xyz, clamp } from '../../core/vector.js';

const DEFAULT_LAUNCH=24*Math.PI/180, MIN_LAUNCH=2*Math.PI/180, MAX_LAUNCH=Math.PI/2;
export const GUIDE_DOTS=9;
// The p^4 term leaves everyday shots feeling as before but gives full power enough
// speed to climb straight up through the center and land on the far wall.
const launchSpeed = p => .42+p*1.18+.7*p**4;

export class WorldModel {
  constructor(){ this.ballRadius=.01; this.holeRadius=HOLE_RADIUS; this.holePosition=[...HOLE_POSITION]; this.reset(); }
  reset(){
    this.ballPosition=[1,0,0]; this.aimDirection=[0,1,0]; this.baseAim=[0,1,0]; this.lockedCamera=[0,1,0];
    this.groundVelocity=[0,0,0]; this.airVelocity=[0,0,0,0]; this.support=null; this.supportAltitude=0;
    this.airCenter=this.restingCenter(this.ballPosition,0); this.airborne=false; this.complete=false;
    this.aimLocked=false; this.powerSelecting=false; this.aimPhase=0; this.powerPhase=0; this.power=0; this.strokes=0; this.launchAngle=DEFAULT_LAUNCH; this.lastGravity=[0,0,0,0];
    this.updateCamera();
  }
  get moving(){ return this.airborne || len(this.groundVelocity)>.001; }
  get renderCenter(){ return this.airborne?this.airCenter:this.restingCenter(this.ballPosition,this.supportAltitude); }
  get status(){
    if(this.complete)return 'HOLED! Press R to play again'; if(this.airborne)return 'Ball airborne…'; if(this.moving)return 'Ball rolling…';
    return this.powerSelecting?'Power moving — press Space to shoot':'Aim moving — press Space to lock direction';
  }
  pressAction(){
    if(this.moving||this.complete)return;
    if(!this.aimLocked){this.aimLocked=true;this.powerSelecting=true;this.power=0;this.powerPhase=0;return;}
    if(!this.powerSelecting)return;
    this.powerSelecting=false;
    this.airCenter=this.restingCenter(this.ballPosition,this.supportAltitude);
    this.airVelocity=mul(this.launchDirection(),launchSpeed(this.power));
    this.groundVelocity=[0,0,0];this.lockedCamera=[...this.aimDirection];this.airborne=true;this.support=null;this.supportAltitude=0;this.strokes++;
  }
  // Unit 4D launch velocity at the resting ball: aim direction tilted up by launchAngle.
  launchDirection(){
    const horizontal=[...this.aimDirection,0], altitude=this.supportAltitude+this.ballRadius;
    const up=[...mul(this.ballPosition,-Math.sin(altitude)),Math.cos(altitude)];
    return add(mul(horizontal,Math.cos(this.launchAngle)),mul(up,Math.sin(this.launchAngle)));
  }
  // Dots along the initial launch line, so the chosen height is visible before shooting.
  get guidePoints(){
    const c=this.restingCenter(this.ballPosition,this.supportAltitude),d=this.launchDirection(),points=[];
    for(let i=0;i<GUIDE_DOTS;i++){const t=.06+i*.035;points.push(...add(mul(c,Math.cos(t)),mul(d,Math.sin(t))));}
    return points;
  }
  look(dx,dy){
    if(this.moving||this.complete)return;
    if(!this.aimLocked){
      this.baseAim=this.rotated(this.baseAim,dx*.0025);
      this.aimDirection=this.rotated(this.baseAim,Math.sin(this.aimPhase*1.7)*.72);
    }
    this.launchAngle=clamp(this.launchAngle-dy*.004,MIN_LAUNCH,MAX_LAUNCH);this.updateCamera();
  }
  update(dt){
    const was=this.moving,wasAirborne=this.airborne;
    if(!this.moving&&!this.complete){
      if(!this.aimLocked){this.aimPhase+=dt;this.aimDirection=this.rotated(this.baseAim,Math.sin(this.aimPhase*1.7)*.72);}
      else if(this.powerSelecting){this.powerPhase+=dt;this.power=.5-.5*Math.cos(this.powerPhase*3.2);}
    }
    if(this.airborne)this.updateAir(dt);else this.updateRolling(dt);
    // A long flight can carry the ball far around (or through) the world, so re-aim the
    // rolling camera along the direction it actually landed in.
    if(wasAirborne&&!this.airborne)this.lockedCamera=[...this.aimDirection];
    if(was&&!this.moving&&!this.complete)this.prepareNext(); this.updateCamera();
  }
  rotated(direction,angle){const side=norm(cross(this.ballPosition,direction));return norm(add(mul(direction,Math.cos(angle)),mul(side,Math.sin(angle))));}
  prepareNext(){this.baseAim=[...this.aimDirection];this.lockedCamera=[...this.aimDirection];this.aimLocked=false;this.powerSelecting=false;this.aimPhase=0;this.powerPhase=0;this.power=0;}
  updateAir(dt){
    for(let remaining=dt;remaining>0&&this.airborne;){const h=Math.min(remaining,1/180);remaining-=h;
      // Gravity points along the shortest path to the ground. At the exact center of the
      // world every direction is "down", so keep pulling the way gravity last pointed.
      const ground=len(xyz(this.airCenter))>1e-5?norm(xyz(this.airCenter)):null;
      const pull=ground?[...ground,0]:this.lastGravity,gravity=norm(sub(pull,mul(this.airCenter,dot(this.airCenter,pull))));
      this.lastGravity=gravity;
      this.airVelocity=add(this.airVelocity,mul(gravity,1.32*h));
      this.airVelocity=sub(this.airVelocity,mul(this.airCenter,dot(this.airVelocity,this.airCenter)));
      this.airVelocity=mul(this.airVelocity,Math.exp(-.035*h));
      const speed=len(this.airVelocity), previous=[...this.airCenter];
      if(speed>.0001){const d=mul(this.airVelocity,1/speed),travel=speed*h;this.airCenter=norm(add(mul(previous,Math.cos(travel)),mul(d,Math.sin(travel))));const transported=norm(add(mul(previous,-Math.sin(travel)),mul(d,Math.cos(travel))));this.airVelocity=mul(transported,speed);}
      if(this.handleAirBoxes(previous)){if(!this.airborne)break;continue;}
      if(len(xyz(this.airCenter))>1e-5)this.ballPosition=norm(xyz(this.airCenter));const hv=sub(xyz(this.airVelocity),mul(this.ballPosition,dot(xyz(this.airVelocity),this.ballPosition)));if(len(hv)>.001)this.aimDirection=norm(hv);
      const altitude=Math.atan2(Math.abs(this.airCenter[3]),len(xyz(this.airCenter))),side=this.airCenter[3]>=0?1:-1;
      const outward=[...mul(this.ballPosition,-Math.sin(altitude)),side*Math.cos(altitude)],outSpeed=dot(this.airVelocity,outward);
      if(altitude<=this.ballRadius&&outSpeed<=0){if(-outSpeed>.14){this.airCenter=this.restingCenter(this.ballPosition,.001);this.reflectAir(outward,.38);}else this.landGround();}
    }
  }
  handleAirBoxes(previousCenter){
    const margin=Math.tan(this.ballRadius);
    for(let i=0;i<PHYSICS_BOX_COUNT;i++){const b=BOXES[i],prev=this.local(previousCenter,b),cur=this.local(this.airCenter,b);if(!prev||!cur)continue;
      const top=b[3][2]*2,ex=b[3][0]+margin,ey=b[3][1]+margin;if(Math.abs(cur[0])>ex||Math.abs(cur[1])>ey)continue;
      const landing=top+margin;
      if(prev[2]>=landing&&cur[2]<=landing){this.ballPosition=norm(xyz(this.airCenter));const den=Math.max(dot(this.ballPosition,b[0]),.02),alt=Math.atan(top*den);const n=this.faceNormal(b,2,1,landing,this.airCenter),impact=Math.max(0,-dot(this.airVelocity,n));
        if(impact>.14){this.airCenter=this.restingCenter(this.ballPosition,alt+.001);this.reflectAir(n,.42);}else{this.support=i;this.supportAltitude=alt;let hv=sub(xyz(this.airVelocity),mul(this.ballPosition,dot(xyz(this.airVelocity),this.ballPosition)));this.groundVelocity=mul(hv,1/Math.max(Math.cos(alt+this.ballRadius),.001));if(len(this.groundVelocity)>.001)this.aimDirection=norm(this.groundVelocity);this.airCenter=this.restingCenter(this.ballPosition,alt);this.airVelocity=[0,0,0,0];this.airborne=false;}return true;}
      if(cur[2]>=-margin&&cur[2]<=top+margin){this.airCenter=previousCenter;const ds=[ex-Math.abs(cur[0]),ey-Math.abs(cur[1]),cur[2]+margin,top+margin-cur[2]];let face=ds.indexOf(Math.min(...ds)),dim=face<2?face:2,side=face===0?(cur[0]>=0?1:-1):face===1?(cur[1]>=0?1:-1):face===2?-1:1,bound=face===0?ex*side:face===1?ey*side:face===2?-margin:top+margin;this.reflectAir(this.faceNormal(b,dim,side,bound,this.airCenter),.58);return true;}
    } return false;
  }
  landGround(){this.ballPosition=norm(xyz(this.airCenter));let hv=sub(xyz(this.airVelocity),mul(this.ballPosition,dot(xyz(this.airVelocity),this.ballPosition)));this.groundVelocity=mul(hv,1/Math.cos(this.ballRadius));if(len(this.groundVelocity)>.001)this.aimDirection=norm(this.groundVelocity);this.support=null;this.supportAltitude=0;this.airCenter=this.restingCenter(this.ballPosition,0);this.airVelocity=[0,0,0,0];this.airborne=false;this.checkHole();}
  updateRolling(dt){
    let speed=len(this.groundVelocity);if(speed<=.001){this.groundVelocity=[0,0,0];return;}const d=mul(this.groundVelocity,1/speed),travel=speed*dt,next=norm(add(mul(this.ballPosition,Math.cos(travel)),mul(d,Math.sin(travel)))),transported=norm(add(mul(this.ballPosition,-Math.sin(travel)),mul(d,Math.cos(travel)))),margin=Math.tan(this.ballRadius);
    if(this.support!==null){const b=BOXES[this.support],den=dot(next,b[0]),x=dot(next,b[1])/Math.max(den,.02),y=dot(next,b[2])/Math.max(den,.02);if(den<=.02||Math.abs(x)>b[3][0]-margin||Math.abs(y)>b[3][1]-margin){this.ballPosition=next;this.aimDirection=transported;const alt=this.supportAltitude+this.ballRadius;this.airCenter=this.restingCenter(next,this.supportAltitude);this.airVelocity=[...mul(transported,speed*Math.cos(alt)),0];this.airborne=true;this.support=null;this.supportAltitude=0;return;}this.supportAltitude=Math.atan(b[3][2]*2*den);}
    for(let i=0;i<PHYSICS_BOX_COUNT;i++){if(i===this.support)continue;const b=BOXES[i],den=dot(next,b[0]);if(den<=.02)continue;const x=dot(next,b[1])/den,y=dot(next,b[2])/den,ex=b[3][0]+margin,ey=b[3][1]+margin;if(Math.abs(x)<=ex&&Math.abs(y)<=ey){const useX=Math.abs(x)/ex>Math.abs(y)/ey,s=useX?(x>=0?1:-1):(y>=0?1:-1),raw=sub(mul(useX?b[1]:b[2],s),mul(b[0],useX?ex:ey)),n=norm(sub(raw,mul(this.ballPosition,dot(raw,this.ballPosition)))),vn=dot(this.groundVelocity,n);if(vn<0){this.groundVelocity=add(mul(sub(this.groundVelocity,mul(n,vn)),.92),mul(n,-vn*.68));this.aimDirection=norm(this.groundVelocity);}this.power=0;return;}}
    this.ballPosition=next;this.aimDirection=transported;if(this.support===null)this.checkHole();if(this.complete)return;speed=Math.max(0,speed-.54*dt);this.groundVelocity=speed>.001?mul(transported,speed):[0,0,0];
  }
  checkHole(){if(dot(this.ballPosition,this.holePosition)>Math.cos(this.holeRadius)){this.ballPosition=[...this.holePosition];this.groundVelocity=[0,0,0];this.airVelocity=[0,0,0,0];this.airborne=false;this.complete=true;this.power=0;}}
  local(center,b){const den=dot(xyz(center),b[0]);return den>.02?[dot(xyz(center),b[1])/den,dot(xyz(center),b[2])/den,center[3]/den]:null;}
  faceNormal(b,dim,side,bound,center){let raw;if(dim<2){const spatial=sub(mul(b[dim+1],side),mul(b[0],Math.abs(bound)));raw=[...spatial,0];}else raw=[...mul(b[0],-Math.abs(bound)),side];return norm(sub(raw,mul(center,dot(raw,center))));}
  reflectAir(n,e){const vn=dot(this.airVelocity,n);if(vn>=0)return;this.airVelocity=add(mul(sub(this.airVelocity,mul(n,vn)),.9),mul(n,-vn*e));this.airVelocity=sub(this.airVelocity,mul(this.airCenter,dot(this.airVelocity,this.airCenter)));}
  restingCenter(q,alt){const a=alt+this.ballRadius;return [...mul(q,Math.cos(a)),Math.sin(a)];}
  updateCamera(){this.pitch=clamp(-.2+.26*(this.launchAngle-DEFAULT_LAUNCH),-.85,.42);let cameraDir=this.moving?this.lockedCamera:this.baseAim;cameraDir=sub(cameraDir,mul(this.ballPosition,dot(cameraDir,this.ballPosition)));cameraDir=len(cameraDir)<1e-5?this.aimDirection:norm(cameraDir);const follow=.30;this.position=norm(sub(mul(this.ballPosition,Math.cos(follow)),mul(cameraDir,Math.sin(follow))));this.forward=norm(add(mul(this.ballPosition,Math.sin(follow)),mul(cameraDir,Math.cos(follow))));this.right=norm(cross(this.position,this.forward));this.cameraSupportAltitude=this.airborne?0:this.supportAltitude;}
}
