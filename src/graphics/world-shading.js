// GLSL shared by every minigame: the camera projection, the checkered ground, sky
// haze, the golf course scenery boxes and the hole with its flag. Each game's
// fragment shader starts with this, so they all show exactly the same world.
export const worldShading = `#version 300 es
precision highp float;
precision highp sampler2D;
out vec4 outColor;
uniform vec2 resolution;
uniform vec4 player,forward,rightAxis,upAxis,hole;
uniform vec4 boxData[98];

const vec3 SKY=vec3(.3,.5,.82),FLAG_COLOR=vec3(.92,.08,.06);
// Angle covered by one pixel at the centre of the view.
float pixelAngle(){return 1.28/resolution.y;}

float sphereHit(vec4 c,float radius,vec4 d,float limit){
 float a=dot(player,c),b=dot(d,c),range=sqrt(a*a+b*b),cr=cos(radius);
 if(cr>range)return limit;float phase=atan(b,a),delta=acos(clamp(cr/range,-1.,1.));
 float x=mod(phase-delta+6.2831853,6.2831853),y=mod(phase+delta+6.2831853,6.2831853),r=limit;
 if(x>.001&&x<r)r=x;if(y>.001&&y<r)r=y;return r;
}

// The view geodesic for this pixel, with the slight downward curve at the sides.
vec4 viewRay(){
 vec2 uv=(gl_FragCoord.xy*2.-resolution)/resolution.y;uv*=.64;
 float curved=uv.y-.105*uv.x*uv.x;
 return normalize(forward+uv.x*rightAxis+curved*upAxis);
}

// First downward crossing of the ground along origin cos t + ray sin t.
float groundHit(vec4 origin,vec4 ray){return mod(atan(-origin.w,ray.w)+3.14159265,3.14159265);}

float flagHit(vec4 ray,float best){return sphereHit(normalize(vec4(hole.xyz,.31)),.055,ray,best);}

bool inHole(vec4 gp){return dot(gp.xyz,hole.xyz)>cos(hole.w);}

// Ground seen at distance d along origin cos t + ray sin t, lit from the origin and
// hazed toward the sky by fogDistance.
vec3 groundColor(vec4 origin,vec4 ray,float d,float fogDistance){
 vec4 gp=origin*cos(d)+ray*sin(d),gn=normalize(vec4(0,0,0,1)-gp*gp.w),light=normalize(origin-gp*dot(gp,origin));
 float diffuse=abs(dot(gn,light)),lon=atan(gp.y,gp.x),lat=asin(clamp(gp.z,-1.,1.));
 float checker=mod(floor(lon*1.9099)+floor(lat*3.8197),2.);vec3 base=mix(vec3(.13,.36,.2),vec3(.55,.62,.35),checker);
 vec3 color=mix(SKY,base*(.35+.75*diffuse),exp(-.13*fogDistance));
 if(inHole(gp))color=vec3(.018,.022,.025);
 return color;
}

// The course's boxes, in the gnomonic chart of their ground point. Updates best and the
// hit details and returns true when one is nearer than best.
bool traceScenery(vec4 ray,inout float best,inout int hitBox,inout vec3 hitLocal,inout vec3 hitHalf,inout vec3 hitColor){
 bool hit=false;
 for(int i=0;i<14;i++){
  int j=i*7;vec3 anchor=boxData[j].xyz,ax=boxData[j+1].xyz,ay=boxData[j+2].xyz;
  vec3 halfSize=boxData[j+3].xyz,objColor=boxData[j+4].xyz;vec4 sphereCenter=boxData[j+5];float sphereCos=boxData[j+6].x;
  float sa=dot(player,sphereCenter),sb=dot(ray,sphereCenter),sr=sqrt(sa*sa+sb*sb);if(sphereCos>sr)continue;
  float phase=atan(sb,sa),delta=acos(clamp(sphereCos/sr,-1.,1.));
  float sn=mod(phase-delta+6.2831853,6.2831853),sf=mod(phase+delta+6.2831853,6.2831853);
  bool inside=sa>sphereCos;if(!inside&&sn>=best&&sf>=best)continue;
  float ds=dot(player.xyz,anchor),dd=dot(ray.xyz,anchor);
  vec3 ns=vec3(dot(player.xyz,ax),dot(player.xyz,ay),player.w),nd=vec3(dot(ray.xyz,ax),dot(ray.xyz,ay),ray.w);
  vec3 center=vec3(0,0,halfSize.z);
  for(int f=0;f<6;f++){
   int dim=f/2;float side=(f-(f/2)*2)==0?-1.:1.;float boundary=center[dim]+side*halfSize[dim];
   float ca=ns[dim]-boundary*ds,cb=nd[dim]-boundary*dd,candidate=mod(atan(-ca,cb)+3.14159265,3.14159265);
   if(candidate<=.00005||candidate>=best)continue;float co=cos(candidate),si=sin(candidate),den=ds*co+dd*si;if(den<=.02)continue;
   vec3 local=(ns*co+nd*si)/den,centered=local-center,excess=abs(centered)-halfSize;
   if(excess.x<=.0005&&excess.y<=.0005&&excess.z<=.0005){best=candidate;hitBox=i;hitLocal=centered;hitHalf=halfSize;hitColor=objColor;hit=true;}
  }
 }
 return hit;
}

// Flat face shading for the scenery: tops brightest, then the two pairs of sides.
float faceLight(int axis){return axis==2?1.12:(axis==0?.72:.88);}
vec3 sceneryColor(vec3 hitLocal,vec3 hitHalf,vec3 hitColor){
 vec3 face=abs(hitLocal)/max(hitHalf,vec3(.0001));
 return hitColor*faceLight(face.z>face.x&&face.z>face.y?2:(face.x>face.y?0:1));
}
`;
