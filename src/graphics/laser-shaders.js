import { worldShading } from './world-shading.js';

// The laser maze in the shared world. Puzzle blocks and beam pieces arrive in float
// textures, one row each (see LaserRenderer for the layout).
export const laserFragmentShader = worldShading + `
uniform float time;
uniform ivec4 counts; // blocks, beams, ghost index, highlighted index (-1 for none)
uniform vec4 ghostTint; // rgb tint, a: 1 if the placement is valid
uniform sampler2D blockData,beamData;

const float TAU=6.2831853,PI=3.14159265,BEAM_CORE=.0028,BEAM_HALO=.006;

struct BoxHit{float t;int axis;float side;vec3 local;};

float hash3(vec3 p){p=fract(p*.1031);p+=dot(p,p.yzx+33.33);return fract((p.x+p.y)*p.z);}
float noise3(vec3 p){
 vec3 c=floor(p),f=fract(p);f=f*f*(3.-2.*f);
 return mix(mix(mix(hash3(c),hash3(c+vec3(1,0,0)),f.x),mix(hash3(c+vec3(0,1,0)),hash3(c+vec3(1,1,0)),f.x),f.y),
            mix(mix(hash3(c+vec3(0,0,1)),hash3(c+vec3(1,0,1)),f.x),mix(hash3(c+vec3(0,1,1)),hash3(c+vec3(1,1,1)),f.x),f.y),f.z);
}
vec3 palette(float t){return .5+.5*cos(TAU*(t+vec3(0,.33,.67)));}

vec4 blockTexel(int i,int field){return texelFetch(blockData,ivec2(field,i),0);}

// First entry of the view geodesic into block i, in the gnomonic chart of its ground point.
bool intersectBlock(int i,vec4 d,out BoxHit hit){
 hit=BoxHit(0.,0,0.,vec3(0));
 vec3 e1=blockTexel(i,0).xyz,e2=blockTexel(i,1).xyz;vec4 c=blockTexel(i,2);
 vec4 pl=vec4(dot(player.xyz,e1),dot(player.xyz,e2),player.w,dot(player.xyz,c.xyz));
 vec4 dl=vec4(dot(d.xyz,e1),dot(d.xyz,e2),d.w,dot(d.xyz,c.xyz));
 if(length(vec2(pl.w,dl.w))<c.w)return false;
 // Reparametrise from the point nearest the block, where the chart point moves along a
 // straight line linear in tan(t - tc): an ordinary slab test.
 float tc=atan(dl.w,pl.w),ct=cos(tc),st=sin(tc);
 vec4 q=pl*ct+dl*st,v=dl*ct-pl*st;
 vec3 o=q.xyz/q.w,dir=v.xyz/q.w;
 vec3 inv=1./mix(dir,vec3(1e-9),lessThan(abs(dir),vec3(1e-9)));
 vec3 ta=(blockTexel(i,3).xyz-o)*inv,tb=(blockTexel(i,4).xyz-o)*inv,tmin=min(ta,tb),tmax=max(ta,tb);
 float enter=max(max(tmin.x,tmin.y),tmin.z),exit=min(min(tmax.x,tmax.y),tmax.z);
 if(enter>exit)return false;
 float t=tc+atan(enter);if(t<1e-4)t+=TAU;
 int axis=(tmin.x>=tmin.y&&tmin.x>=tmin.z)?0:(tmin.y>=tmin.z?1:2);
 hit=BoxHit(t,axis,dir[axis]<0.?1.:-1.,o+dir*enter);
 return true;
}

vec4 blockNormal(int i,BoxHit hit){
 vec4 basis=hit.axis==0?vec4(blockTexel(i,0).xyz,0):(hit.axis==1?vec4(blockTexel(i,1).xyz,0):vec4(0,0,0,1));
 float bound=hit.side>0.?blockTexel(i,4)[hit.axis]:blockTexel(i,3)[hit.axis];
 return normalize(hit.side*(basis-bound*vec4(blockTexel(i,2).xyz,0)));
}

// Chart distance from the hit to the nearest visible edge of its face.
float edgeDistance(int i,BoxHit hit){
 vec3 lo=blockTexel(i,3).xyz,hi=blockTexel(i,4).xyz;float result=1.;
 for(int k=0;k<3;k++){
  if(k==hit.axis)continue;
  result=min(result,min(hi[k]-hit.local[k],k==2?1.:hit.local[k]-lo[k]));
 }
 return result;
}

// Coordinates across the hit face: x runs horizontally, y is height (or depth on top).
vec2 faceCoordinates(BoxHit hit){
 if(hit.axis==0)return hit.local.yz;
 if(hit.axis==1)return hit.local.xz;
 return hit.local.xy;
}

vec3 shadeBlock(int i,BoxHit hit,vec4 point,vec4 normal,vec4 view,vec4 toEye,bool highlighted){
 int kind=int(blockTexel(i,0).w+.5);float state=blockTexel(i,1).w;
 bool lit=state>.5&&state<1.5,previewed=state>1.5;
 float facingEye=abs(dot(normal,toEye)),light=faceLight(hit.axis);
 vec2 face=faceCoordinates(hit);
 float outlineWidth=max(.0022,1.3*pixelAngle()*sin(min(hit.t,3.)));
 float outline=1.-smoothstep(outlineWidth*.6,outlineWidth*1.4,edgeDistance(i,hit));
 vec3 color,emission=vec3(0);
 if(kind==0){
  // Stone wall with a brick pattern on the sides.
  vec3 stone=vec3(.56,.53,.49)*(.85+.3*noise3(point.xyz*260.));
  if(hit.axis!=2){
   float row=floor(face.y/.0115),course=fract((face.x+(mod(row,2.)>.5?.016:0.))/.032);
   stone=mix(stone,vec3(.32,.3,.28),min(step(course,.07)+step(fract(face.y/.0115),.12),1.));
  }
  color=stone*light*(1.-.45*outline);
 }else if(kind==1){
  // Emitter: a dark turret with a glowing red lens where the beam leaves at ground level.
  color=vec3(.17,.18,.22)*light;
  if(hit.axis==2)emission+=vec3(1,.15,.08)*(1.-smoothstep(.004,.006,abs(face.y)))*.8;
  if(hit.axis==0&&hit.side>0.){
   float lens=length(face);
   emission+=vec3(1,.18,.1)*(1.-smoothstep(.009,.012,lens))+vec3(1,.9,.8)*(1.-smoothstep(.002,.005,lens));
  }
  color=mix(color,vec3(.9,.55,.1)*light,outline*.8);
 }else if(kind==2){
  // Goal: a receiver marked with target rings that blazes when lit.
  float pulse=.5+.5*sin(time*4.),rings=step(.5,fract(length(face)/.009));
  vec3 base=lit?mix(vec3(1,.78,.25),vec3(1,.95,.7),rings):mix(vec3(.13,.2,.26),vec3(.25,.75,.8),rings*.6);
  color=base*light;
  if(lit)emission+=vec3(1,.8,.35)*(.35+.35*pulse);
  if(previewed)emission+=vec3(.25,.85,1)*rings*(.25+.3*pulse);
  color=mix(color,lit?vec3(1):vec3(.4,.95,1),outline*.9);
 }else if(kind==3){
  // Mirror: the large faces reflect the ground, the rim is brushed metal.
  if(hit.axis==0){
   vec4 reflected=view-2.*dot(view,normal)*normal;
   float t2=groundHit(point,reflected);
   color=groundColor(point,reflected,t2,hit.t+t2)*.82+vec3(.06,.08,.1)+.12*pow(facingEye,12.);
  }else color=vec3(.55,.57,.62)*light;
  color=mix(color,vec3(.62,.64,.7)*light,outline);
 }else{
  // Diffractor: an iridescent crystal.
  float hue=dot(hit.local,vec3(19,23,29))+facingEye*.6+time*.15;
  color=mix(palette(hue),vec3(.92,.96,1),.45)*(.45+.5*light);
  if(lit)emission+=palette(hue+.3)*(.35+.25*sin(time*6.));
  color=mix(color,vec3(1),outline*.7);
 }
 if(highlighted){color=color*1.15+vec3(.05,.1,.12);color=mix(color,vec3(.55,1,1),outline);}
 return color+emission;
}

// Light from beam i at a pixel, given the view's closest approach to the beam's great circle:
// across (distance from it), tb (distance along the view) and s (where on it, from the start).
vec3 beamLight(vec4 color,vec4 params,float across,float tb,float s,float best){
 if(tb>best+.012)return vec3(0);
 // Past either end, measure to the nearer end point instead, giving rounded caps.
 float arc=params.x;if(s<0.)s+=TAU;
 float overshoot=s<=arc?0.:min(s-arc,TAU-s);
 float pixel=pixelAngle()*max(sin(tb),.002),rc=max(BEAM_CORE,pixel*1.3),rh=max(BEAM_HALO,pixel*2.5);
 if(overshoot>3.*rh)return vec3(0);
 float dist=sqrt(across*across+overshoot*overshoot);
 float core=exp(-2.*(dist/rc)*(dist/rc))*clamp(BEAM_CORE/rc,.3,1.);
 float halo=pow(clamp(1.-dist/(3.*rh),0.,1.),3.)*.4*clamp(BEAM_HALO/rh,.35,1.);
 float intensity=params.y;
 if(color.a>1.5){
  // Hologram light: dashes streaming along the beam, smoothed out when tiny.
  float period=.03,phase=fract(s/period-time*1.2);
  float dash=smoothstep(0.,.12,phase)*(1.-smoothstep(.45,.57,phase));
  intensity*=mix(.55,.25+dash,clamp((period/pixel-4.)/8.,0.,1.));
 }
 return color.rgb*(halo+core)*intensity+vec3(1,.95,.9)*core*.6*intensity;
}

// Every geodesic from the eye appears as a line on screen, so a beam can only touch
// pixels whose view lies close to the great sphere through the eye and the beam. The
// first texel is that sphere's normal divided by the cutoff sine: one dot product
// rejects everything else.
vec3 beamGlow(int i,vec4 dir,float best){
 vec4 scaled=texelFetch(beamData,ivec2(0,i),0);
 float dn=dot(dir,scaled);if(abs(dn)>1.)return vec3(0);
 float cutoff=1./length(scaled);vec4 n=scaled*cutoff;dn*=cutoff;
 vec4 w=texelFetch(beamData,ivec2(1,i),0),u=texelFetch(beamData,ivec2(2,i),0),v=texelFetch(beamData,ivec2(3,i),0);
 vec4 color=texelFetch(beamData,ivec2(4,i),0),params=texelFetch(beamData,ivec2(5,i),0);
 vec4 inPlane=dir-dn*n;float planeLength=length(inPlane);if(planeLength<1e-4)return vec3(0);
 inPlane/=planeLength;
 float tb=atan(params.z,-dot(inPlane,w));
 vec4 nearest=player*cos(tb)+inPlane*sin(tb);
 float s=atan(dot(nearest,v),dot(nearest,u));
 float across=asin(min(abs(dn),1.))*max(sin(tb),.002);
 return beamLight(color,params,across,tb,s,best);
}

void main(){
 vec4 ray=viewRay();
 float groundDistance=groundHit(player,ray),best=groundDistance;
 int hitBox=-1;vec3 hitLocal=vec3(0),hitHalf=vec3(1),hitColor=vec3(1);
 bool hitFlag=false;
 float ft=flagHit(ray,best);if(ft<best){best=ft;hitFlag=true;}
 if(traceScenery(ray,best,hitBox,hitLocal,hitHalf,hitColor))hitFlag=false;
 int hitBlock=-1;BoxHit blockHit=BoxHit(0.,0,0.,vec3(0));
 for(int i=0;i<counts.x;i++){
  if(i==counts.z)continue;
  BoxHit candidate;
  if(intersectBlock(i,ray,candidate)&&candidate.t<best){best=candidate.t;blockHit=candidate;hitBlock=i;}
 }

 vec3 color;
 vec4 point=player*cos(best)+ray*sin(best),view=-player*sin(best)+ray*cos(best),toEye=normalize(player-point*dot(point,player));
 if(hitBlock>=0)color=shadeBlock(hitBlock,blockHit,point,blockNormal(hitBlock,blockHit),view,toEye,hitBlock==counts.w);
 else if(hitBox>=0)color=sceneryColor(hitLocal,hitHalf,hitColor);
 else if(hitFlag)color=FLAG_COLOR;
 else color=groundColor(player,ray,groundDistance,groundDistance);

 // The placement preview is drawn see-through, on top of whatever is behind it.
 if(counts.z>=0){
  BoxHit g;
  if(intersectBlock(counts.z,ray,g)&&g.t<best+.004){
   vec4 gp=player*cos(g.t)+ray*sin(g.t),gl=normalize(player-gp*dot(gp,player));
   float shade=.45+.55*abs(dot(blockNormal(counts.z,g),gl)),pulse=.75+.25*sin(time*5.);
   float outline=1.-smoothstep(.0015,.0035,edgeDistance(counts.z,g));
   color=mix(color,ghostTint.rgb*shade,.45*pulse);
   color=mix(color,ghostTint.rgb,outline*.8);
  }
 }

 for(int i=0;i<counts.y;i++)color+=beamGlow(i,ray,best);
 outColor=vec4(min(color,1.),1);
}`;
