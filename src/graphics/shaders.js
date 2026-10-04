// Renders meshes living in the 3-sphere, ported from the Unity build's "S3/Object" shader.
//
// Vertices arrive as points of S^3 with a tangent normal. The vertex shader rotates them into
// the eye's frame and projects the 4D point homogeneously: the screen position depends only on
// the direction (x, y, z), which is exactly where the great circle from the eye reaches it.
// Great circles map to straight lines, so triangles stay triangles. Depth comes from
// w = cos(distance), which is monotonic over the whole half-lap, so objects past the equator
// still sort correctly - and grow as they recede. That is the reverse perspective of a
// spherical world. The other image of every object lies behind the ground, so one pass is enough.
export const vertexShader = `#version 300 es
precision highp float;
in vec4 position;
in vec4 normal;
in vec3 color;
uniform mat4 model;
uniform vec4 eye,forward,rightAxis,upAxis;
uniform float focal,aspect,nearClip;
out vec3 vColor;
out vec4 vPoint,vNormal,vView;
void main(){
 vec4 p=model*position,n=model*normal;
 vec4 q=vec4(dot(rightAxis,p),dot(upAxis,p),-dot(forward,p),dot(eye,p));
 vColor=color;vPoint=p;vNormal=n;vView=q;
 gl_Position=vec4(q.x*focal/aspect,q.y*focal,-nearClip*q.w,-q.z);
}`;

export const fragmentShader = `#version 300 es
precision highp float;
in vec3 vColor;
in vec4 vPoint,vNormal,vView;
out vec4 outColor;
uniform vec4 eye,sun,haze,hole;
uniform float ambient,groundPattern,worldRadius,cupRadius,greenRadius;

float hash(vec3 p){p=fract(p*.3183099+.1);p*=17.;return fract(p.x*p.y*p.z*(p.x+p.y+p.z));}
float noise(vec3 x){
 vec3 i=floor(x),f=fract(x);f=f*f*(3.-2.*f);
 return mix(mix(mix(hash(i+vec3(0,0,0)),hash(i+vec3(1,0,0)),f.x),mix(hash(i+vec3(0,1,0)),hash(i+vec3(1,1,0)),f.x),f.y),
            mix(mix(hash(i+vec3(0,0,1)),hash(i+vec3(1,0,1)),f.x),mix(hash(i+vec3(0,1,1)),hash(i+vec3(1,1,1)),f.x),f.y),f.z);
}
float sphereDistance(vec4 a,vec4 b){return 2.*asin(clamp(length(a-b)*.5,0.,1.));}

vec3 groundColor(vec4 p){
 vec3 g=p.xyz*worldRadius;
 float n1=noise(g*.22),n2=noise(g*1.1),n3=noise(g*5.);
 vec3 grass=mix(vec3(.36,.62,.24),vec3(.50,.74,.30),n1);
 grass*=.86+.18*n2+.08*n3;
 float toCup=sphereDistance(p,hole)*worldRadius;
 float green=1.-smoothstep(greenRadius-.25,greenRadius,toCup);
 vec3 putting=vec3(.47,.78,.34)*(.96+.06*n3);
 putting*=.97+.05*step(.5,fract(g.x*.5));
 grass=mix(grass,putting,green);
 float cup=1.-smoothstep(cupRadius-.012,cupRadius+.012,toCup);
 float rim=(1.-smoothstep(cupRadius+.012,cupRadius+.04,toCup))*(1.-cup);
 grass=mix(grass,vec3(.92,.92,.88),rim*.6);
 return mix(grass,vec3(.04,.05,.03),cup);
}

void main(){
 vec4 p=normalize(vPoint);
 vec4 n=normalize(vNormal-dot(vNormal,p)*p);
 vec4 toEye=eye-dot(eye,p)*p;
 if(dot(n,toEye)<0.)n=-n;                     // two-sided: always light the face we see
 vec4 light=sun-dot(sun,p)*p;
 float lit=length(light);
 light=lit>1e-5?light/lit:n;
 float diffuse=clamp(dot(n,light),0.,1.);
 vec3 base=vColor;
 if(groundPattern>.5)base*=groundColor(p);
 vec3 col=base*(ambient+(1.-ambient)*diffuse);
 float away=atan(length(vView.xyz),vView.w);   // geodesic distance from the eye
 col=mix(col,haze.rgb,haze.a*smoothstep(.45*3.14159265,3.14159265,away));
 outColor=vec4(col,1);
}`;

// ---------------------------------------------------------------- the replay view
//
// The same world seen from outside, as a glass ball. The ground is the great 2-sphere y = 0, so
// its points are (x, 0, z, w) with x^2 + z^2 + w^2 = 1: dropping y leaves a plain unit 2-sphere
// in R^3. A point h radians above the ground is cos(h) * ground + sin(h) * e_y, so its (x, z, w)
// part points at the ground beneath it. Placing it along that direction, nearer the centre the
// higher it is, carries the whole playable hemisphere onto the solid unit ball: the ground becomes
// its surface, height above ground becomes depth towards the centre, and the pole - a quarter lap
// straight up - is the centre itself. A drive that crosses the pole is seen passing clean through
// the middle of the world.
//
// The (x, z, w) part is itself cos(h) long, but using that as the depth squashes everything near
// the ground flat (1 - cos(h) is only h^2 / 2). The depth used instead is 1 - h + b * h^2 with b
// chosen to reach 0 at the pole: one for one with height at the ground, so houses and trees keep
// their true proportions and stand up off the surface. toBall in game.js is the same map.
//
// That picture is Euclidean, so from here on it is an ordinary perspective camera.
export const replayVertexShader = `#version 300 es
precision highp float;
in vec4 position;
in vec3 color;
uniform mat4 model;              // SO(4) placement, for geometry given as points of S^3
uniform mat4 viewProjection;
uniform float fromS3;            // 1: project a point of S^3; 0: position.xyz is already in R^3
uniform vec3 markerOffset;
uniform float markerScale;
const float BEND=(1.5707963-1.)/(1.5707963*1.5707963);
out vec3 vColor;
out vec3 vWorld;
void main(){
 vec3 q;
 if(fromS3>.5){
  vec4 p=model*position;
  vec3 g=vec3(p.x,p.z,p.w);
  float l=length(g),h=atan(p.y,l);
  q=l>1e-6?g/l*(1.-h+BEND*h*h):vec3(0);
 }
 else q=markerOffset+position.xyz*markerScale;
 vColor=color;vWorld=q;
 gl_Position=viewProjection*vec4(q,1);
}`;

export const replayFragmentShader = `#version 300 es
precision highp float;
in vec3 vColor;
in vec3 vWorld;
out vec4 outColor;
uniform vec3 cameraPosition,lightDirection,tint;
uniform float shell,alpha,ambient;
void main(){
 // the shell is a smooth sphere, so its normal is radial; everything else is flat-shaded, and
 // screen-space derivatives give the true facet normal of the projected geometry for free
 // a line has no facet at all (the trail), so it simply faces the eye
 vec3 toEye=normalize(cameraPosition-vWorld);
 vec3 facet=cross(dFdx(vWorld),dFdy(vWorld));
 vec3 n=shell>.5?normalize(vWorld):dot(facet,facet)>0.?normalize(facet):toEye;
 if(dot(n,toEye)<0.)n=-n;
 vec3 col=vColor*tint*(ambient+(1.-ambient)*max(dot(n,lightDirection),0.));
 float a=alpha;
 if(shell>.5){
  float rim=pow(1.-abs(dot(n,toEye)),2.5);          // glass: edge-on is bright and opaque
  col=mix(col,vec3(.85,.94,1.),rim*.7);
  a=alpha+(1.-alpha)*rim*.8;
 }
 outColor=vec4(col,a);
}`;
