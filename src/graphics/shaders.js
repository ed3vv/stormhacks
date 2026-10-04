import { worldShading } from './world-shading.js';

export const vertexShader = `#version 300 es
precision highp float;
const vec2 V[3]=vec2[3](vec2(-1,-1),vec2(3,-1),vec2(-1,3));
void main(){gl_Position=vec4(V[gl_VertexID],0,1);}`;

export const fragmentShader = worldShading + `
uniform vec4 golfBall,aim,game;
uniform vec4 guide[9];

void main(){
 vec4 ray=viewRay();
 float groundDistance=groundHit(player,ray),best=groundDistance;
 int hitBox=-1,hitSphere=0,hitGuide=0;vec3 hitLocal=vec3(0),hitHalf=vec3(1),hitColor=vec3(1);
 if(game.y<.5){float t=sphereHit(golfBall,game.z,ray,best);if(t<best){best=t;hitSphere=1;}}
 if(game.x<.5&&game.y<.5)for(int i=0;i<9;i++){float t=sphereHit(guide[i],.0024,ray,best);if(t<best){best=t;hitSphere=3;hitGuide=i;}}
 float ft=flagHit(ray,best);if(ft<best){best=ft;hitSphere=2;}
 if(traceScenery(ray,best,hitBox,hitLocal,hitHalf,hitColor))hitSphere=0;
 if(hitBox>=0){outColor=vec4(sceneryColor(hitLocal,hitHalf,hitColor),1);return;}
 if(hitSphere>0){vec4 p=player*cos(best)+ray*sin(best);if(hitSphere==1){vec4 n=normalize(p*dot(p,golfBall)-golfBall);float h=.62+.38*abs(dot(n,player));outColor=vec4(vec3(.96,.97,1)*h,1);}else if(hitSphere==3)outColor=vec4(mix(vec3(1,.9,.3),vec3(1,.55,.15),float(hitGuide)/8.),1);else outColor=vec4(FLAG_COLOR,1);return;}
 vec4 gp=player*cos(groundDistance)+ray*sin(groundDistance);
 vec3 color=groundColor(player,ray,groundDistance,groundDistance);
 if(!inHole(gp)&&game.x<.5&&game.y<.5){vec3 bg=normalize(golfBall.xyz),side=normalize(cross(bg,aim.xyz));float along=atan(dot(gp.xyz,aim.xyz),dot(gp.xyz,bg)),lateral=asin(clamp(dot(gp.xyz,side),-1.,1.));
  const float start=.045,total=.72,spacing=.052;if(along>start&&along<total){float number=floor((along-start)/spacing+.5),center=start+number*spacing,dist=length(vec2(along-center,lateral));float circle=1.-smoothstep(.0025,.004,dist),fade=pow(clamp(1.-along/total,0.,1.),.72);color=mix(color,vec3(1),circle*fade);}}
 outColor=vec4(color,1);
}`;
