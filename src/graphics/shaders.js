export const vertexShader = `#version 300 es
precision highp float;
const vec2 V[3]=vec2[3](vec2(-1,-1),vec2(3,-1),vec2(-1,3));
void main(){gl_Position=vec4(V[gl_VertexID],0,1);}`;

export const fragmentShader = `#version 300 es
precision highp float;
out vec4 outColor;
uniform vec2 resolution;
uniform vec4 player,forward,rightAxis,upAxis,golfBall,hole,aim,game;
uniform vec4 boxData[98];
uniform vec4 guide[9];

float sphereHit(vec4 c,float radius,vec4 d,float limit){
 float a=dot(player,c),b=dot(d,c),range=sqrt(a*a+b*b),cr=cos(radius);
 if(cr>range)return limit;float phase=atan(b,a),delta=acos(clamp(cr/range,-1.,1.));
 float x=mod(phase-delta+6.2831853,6.2831853),y=mod(phase+delta+6.2831853,6.2831853),r=limit;
 if(x>.001&&x<r)r=x;if(y>.001&&y<r)r=y;return r;
}

void main(){
 vec2 uv=(gl_FragCoord.xy*2.-resolution)/resolution.y;uv*=.64;
 float curved=uv.y-.105*uv.x*uv.x;
 vec4 ray=normalize(forward+uv.x*rightAxis+curved*upAxis);
 float groundDistance=mod(atan(-player.w,ray.w)+3.14159265,3.14159265),best=groundDistance;
 int hitBox=-1,hitSphere=0,hitGuide=0;vec3 hitLocal=vec3(0),hitHalf=vec3(1),hitColor=vec3(1);
 if(game.y<.5){float t=sphereHit(golfBall,game.z,ray,best);if(t<best){best=t;hitSphere=1;}}
 if(game.x<.5&&game.y<.5)for(int i=0;i<9;i++){float t=sphereHit(guide[i],.0024,ray,best);if(t<best){best=t;hitSphere=3;hitGuide=i;}}
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
   if(excess.x<=.0005&&excess.y<=.0005&&excess.z<=.0005){best=candidate;hitBox=i;hitLocal=centered;hitHalf=halfSize;hitColor=objColor;hitSphere=0;}
  }
 }
 if(hitBox>=0){vec3 face=abs(hitLocal)/max(hitHalf,vec3(.0001));float light=face.z>face.x&&face.z>face.y?1.12:(face.x>face.y?.72:.88);outColor=vec4(hitColor*light,1);return;}
 if(hitSphere>0){vec4 p=player*cos(best)+ray*sin(best);if(hitSphere==1){vec4 n=normalize(p*dot(p,golfBall)-golfBall);float h=.62+.38*abs(dot(n,player));outColor=vec4(vec3(.96,.97,1)*h,1);}else if(hitSphere==3)outColor=vec4(mix(vec3(1,.9,.3),vec3(1,.55,.15),float(hitGuide)/8.),1);else outColor=vec4(.92,.08,.06,1);return;}
 vec4 gp=player*cos(groundDistance)+ray*sin(groundDistance),gn=normalize(vec4(0,0,0,1)-gp*gp.w),light=normalize(player-gp*dot(gp,player));
 float diffuse=abs(dot(gn,light)),lon=atan(gp.y,gp.x),lat=asin(clamp(gp.z,-1.,1.));
 float checker=mod(floor(lon*1.9099)+floor(lat*3.8197),2.);vec3 base=mix(vec3(.13,.36,.2),vec3(.55,.62,.35),checker),sky=vec3(.3,.5,.82);
 vec3 color=mix(sky,base*(.35+.75*diffuse),exp(-.13*groundDistance));
 if(dot(gp.xyz,hole.xyz)>cos(hole.w))color=vec3(.018,.022,.025);
 else if(game.x<.5&&game.y<.5){vec3 bg=normalize(golfBall.xyz),side=normalize(cross(bg,aim.xyz));float along=atan(dot(gp.xyz,aim.xyz),dot(gp.xyz,bg)),lateral=asin(clamp(dot(gp.xyz,side),-1.,1.));
  const float start=.045,total=.72,spacing=.052;if(along>start&&along<total){float number=floor((along-start)/spacing+.5),center=start+number*spacing,dist=length(vec2(along-center,lateral));float circle=1.-smoothstep(.0025,.004,dist),fade=pow(clamp(1.-along/total,0.,1.),.72);color=mix(color,vec3(1),circle*fade);}}
 outColor=vec4(color,1);
}`;
