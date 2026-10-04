import { vertexShader, fragmentShader } from './shaders.js';
import { packedBoxData } from '../game/world/scenery.js';

export class Renderer {
  constructor(canvas,model,camera){
    this.canvas=canvas;this.model=model;this.camera=camera;this.gl=canvas.getContext('webgl2',{antialias:false});
    if(!this.gl)throw new Error('WebGL2 is unavailable in this browser.');
    const gl=this.gl,compile=(type,source)=>{const s=gl.createShader(type);gl.shaderSource(s,source);gl.compileShader(s);if(!gl.getShaderParameter(s,gl.COMPILE_STATUS))throw new Error(gl.getShaderInfoLog(s));return s;};
    this.program=gl.createProgram();gl.attachShader(this.program,compile(gl.VERTEX_SHADER,vertexShader));gl.attachShader(this.program,compile(gl.FRAGMENT_SHADER,fragmentShader));gl.linkProgram(this.program);if(!gl.getProgramParameter(this.program,gl.LINK_STATUS))throw new Error(gl.getProgramInfoLog(this.program));
    gl.useProgram(this.program);gl.bindVertexArray(gl.createVertexArray());
    this.locations={};for(const n of ['resolution','player','forward','rightAxis','upAxis','golfBall','hole','aim','game','boxData[0]','guide[0]'])this.locations[n]=gl.getUniformLocation(this.program,n);
    gl.uniform4fv(this.locations['boxData[0]'],packedBoxData());
  }
  draw(){
    const gl=this.gl,m=this.model,c=this.camera.frame,dpr=Math.min(devicePixelRatio||1,1.5),w=Math.max(1,innerWidth*dpr|0),h=Math.max(1,innerHeight*dpr|0);
    if(this.canvas.width!==w||this.canvas.height!==h){this.canvas.width=w;this.canvas.height=h;}
    gl.viewport(0,0,w,h);gl.useProgram(this.program);gl.uniform2f(this.locations.resolution,w,h);
    gl.uniform4fv(this.locations.player,c.eye);gl.uniform4fv(this.locations.forward,c.forward);gl.uniform4fv(this.locations.rightAxis,c.right);gl.uniform4fv(this.locations.upAxis,c.up);
    gl.uniform4fv(this.locations.golfBall,m.renderCenter);gl.uniform4fv(this.locations.hole,[...m.holePosition,m.holeRadius]);gl.uniform4fv(this.locations.aim,[...m.aimDirection,m.power]);gl.uniform4fv(this.locations.game,[m.moving?1:0,m.complete?1:0,m.ballRadius,m.airborne?1:0]);
    if(!m.moving&&!m.complete)gl.uniform4fv(this.locations['guide[0]'],m.guidePoints);
    gl.drawArrays(gl.TRIANGLES,0,3);
  }
}
