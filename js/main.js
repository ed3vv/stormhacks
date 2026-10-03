import { WorldModel } from './world-model.js';
import { Renderer } from './renderer.js';

const canvas=document.getElementById('world'),fpsNode=document.getElementById('fps'),gameNode=document.getElementById('game'),errorNode=document.getElementById('error');
try{
  const model=new WorldModel(),renderer=new Renderer(canvas,model);
  addEventListener('keydown',event=>{if(event.code==='Space'){event.preventDefault();if(!event.repeat)model.pressAction();}else if(event.code==='KeyR')model.reset();});
  canvas.addEventListener('click',()=>canvas.requestPointerLock?.());
  addEventListener('mousemove',event=>{
    if(document.pointerLockElement===canvas||event.target===canvas)model.look(event.movementX,event.movementY);
  });
  let last=performance.now(),statsTime=last,frames=0;
  function frame(now){const dt=Math.min((now-last)/1000,.05);last=now;model.update(dt);renderer.draw();frames++;
    if(now-statsTime>=250){fpsNode.textContent=`FPS: ${Math.round(frames*1000/(now-statsTime))}`;const filled=Math.round(model.power*12),meter='█'.repeat(filled)+'░'.repeat(12-filled);gameNode.textContent=`STROKES: ${model.strokes}   POWER [${meter}]   ${model.status}`;statsTime=now;frames=0;}
    requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);
}catch(error){errorNode.hidden=false;errorNode.textContent=`Could not start Spherical Golf:\n${error.stack||error}`;throw error;}
