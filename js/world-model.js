import { BOXES, PHYSICS_BOX_COUNT } from './world-data.js';

const dot = (a,b) => a.reduce((s,v,i)=>s+v*b[i],0);
const add = (a,b) => a.map((v,i)=>v+b[i]);
const sub = (a,b) => a.map((v,i)=>v-b[i]);
const mul = (a,s) => a.map(v=>v*s);
const len = a => Math.hypot(...a);
const norm = a => { const l=len(a); return l>1e-9?mul(a,1/l):a.map(()=>0); };
const cross = (a,b) => [a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
const xyz = a => [a[0],a[1],a[2]];
const clamp = (x,a,b) => Math.max(a,Math.min(b,x));

export class WorldModel {
  constructor(){ this.ballRadius=.026; this.holeRadius=.052; this.holePosition=[Math.cos(1.65),Math.sin(1.65),0]; this.reset(); }
  reset(){
    this.ballPosition=[1,0,0]; this.aimDirection=[0,1,0]; this.baseAim=[0,1,0]; this.lockedCamera=[0,1,0];
    this.groundVelocity=[0,0,0]; this.airVelocity=[0,0,0,0]; this.support=null; this.supportAltitude=0;
    this.airCenter=this.restingCenter(this.ballPosition,0); this.airborne=false; this.complete=false;
    this.aimLocked=false; this.powerSelecting=false; this.aimPhase=0; this.powerPhase=0; this.power=0; this.strokes=0; this.pitch=-.2;
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
    this.powerSelecting=false; const speed=.42+this.power*1.18, angle=24*Math.PI/180;
    this.airCenter=this.restingCenter(this.ballPosition,this.supportAltitude);
    const horizontal=[...this.aimDirection,0], altitude=this.supportAltitude+this.ballRadius;
    const up=[-this.ballPosition[0]*Math.sin(altitude),-this.ballPosition[1]*Math.sin(altitude),-this.ballPosition[2]*Math.sin(altitude),Math.cos(altitude)];
    this.airVelocity=mul(add(mul(horizontal,Math.cos(angle)),mul(up,Math.sin(angle))),speed);
    this.groundVelocity=[0,0,0];this.lockedCamera=[...this.aimDirection];this.airborne=true;this.support=null;this.supportAltitude=0;this.strokes++;
  }
  look(dx,dy){
    if(!this.moving&&!this.complete&&!this.aimLocked){
      this.baseAim=this.rotated(this.baseAim,dx*.0025);
      this.aimDirection=this.rotated(this.baseAim,Math.sin(this.aimPhase*1.7)*.72);
    }
    this.pitch=clamp(this.pitch-dy*.0025,-.85,.42);this.updateCamera();
  }
  update(dt){
    const was=this.moving;
    if(!this.moving&&!this.complete){
      if(!this.aimLocked){this.aimPhase+=dt;this.aimDirection=this.rotated(this.baseAim,Math.sin(this.aimPhase*1.7)*.72);}
      else if(this.powerSelecting){this.powerPhase+=dt;this.power=.5-.5*Math.cos(this.powerPhase*3.2);}
    }
    if(this.airborne)this.updateAir(dt);else this.updateRolling(dt);
    if(was&&!this.moving&&!this.complete)this.prepareNext(); this.updateCamera();
  }
  rotated(direction,angle){const side=norm(cross(this.ballPosition,direction));return norm(add(mul(direction,Math.cos(angle)),mul(side,Math.sin(angle))));}
  prepareNext(){this.baseAim=[...this.aimDirection];this.lockedCamera=[...this.aimDirection];this.aimLocked=false;this.powerSelecting=false;this.aimPhase=0;this.powerPhase=0;this.power=0;}
  updateAir(dt){
    for(let remaining=dt;remaining>0&&this.airborne;){const h=Math.min(remaining,1/180);remaining-=h;
      const ground=norm(xyz(this.airCenter)), closest=[...ground,0];
      const gravity=norm(sub(closest,mul(this.airCenter,dot(this.airCenter,closest))));
      this.airVelocity=add(this.airVelocity,mul(gravity,1.32*h));
      this.airVelocity=sub(this.airVelocity,mul(this.airCenter,dot(this.airVelocity,this.airCenter)));
      this.airVelocity=mul(this.airVelocity,Math.exp(-.035*h));
      const speed=len(this.airVelocity), previous=[...this.airCenter];
      if(speed>.0001){const d=mul(this.airVelocity,1/speed),travel=speed*h;this.airCenter=norm(add(mul(previous,Math.cos(travel)),mul(d,Math.sin(travel))));const transported=norm(add(mul(previous,-Math.sin(travel)),mul(d,Math.cos(travel))));this.airVelocity=mul(transported,speed);}
      if(this.handleAirBoxes(previous)){if(!this.airborne)break;continue;}
      this.ballPosition=norm(xyz(this.airCenter));const hv=sub(xyz(this.airVelocity),mul(this.ballPosition,dot(xyz(this.airVelocity),this.ballPosition)));if(len(hv)>.001)this.aimDirection=norm(hv);
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
  updateCamera(){let cameraDir=this.moving?this.lockedCamera:this.baseAim;cameraDir=sub(cameraDir,mul(this.ballPosition,dot(cameraDir,this.ballPosition)));cameraDir=len(cameraDir)<1e-5?this.aimDirection:norm(cameraDir);const follow=.30;this.position=norm(sub(mul(this.ballPosition,Math.cos(follow)),mul(cameraDir,Math.sin(follow))));this.forward=norm(add(mul(this.ballPosition,Math.sin(follow)),mul(cameraDir,Math.cos(follow))));this.right=norm(cross(this.position,this.forward));this.cameraSupportAltitude=this.airborne?0:this.supportAltitude;}
}
