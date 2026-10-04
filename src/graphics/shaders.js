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
