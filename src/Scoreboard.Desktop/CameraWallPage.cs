namespace Scoreboard.Desktop;

/// <summary>
/// The browser side of the camera walls. Both pages use the same controller as the desktop booth:
/// arrow keys change the selection, Enter opens the big picture, and the Replay Booth adds a
/// configurable-length scrubber, recorded clocks and a stoppage stopwatch. The Multiview page also
/// renders the operator's saved layout tiles, not only the camera wall.
/// </summary>
public static class CameraWallPage
{
    public static string ReplayBooth() => Template
        .Replace("__MODE__", "replay")
        .Replace("__TITLE__", "REPLAY BOOTH")
        .Replace("__REPLAY__", "true");

    public static string Multiview() => Template
        .Replace("__MODE__", "multiview")
        .Replace("__TITLE__", "MULTIVIEW")
        .Replace("__REPLAY__", "false");

    const string Template = """
        <!doctype html><html lang="en"><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <title>ScorePlus — __TITLE__</title>
        <style>
        :root{--ink:#eaf1f8;--muted:#8c9bad;--line:#293443;--accent:#38d7a3;--panel:#121922;--black:#070a0e}
        *{box-sizing:border-box}
        html,body{margin:0;min-height:100%;background:var(--black);color:var(--ink);font:14px/1.35 'Segoe UI',Arial,sans-serif}
        body{display:flex;flex-direction:column;height:100vh;overflow:hidden}
        header{display:flex;align-items:center;justify-content:space-between;gap:12px;padding:10px 14px;background:#10161f;border-bottom:1px solid var(--line)}
        .brand{font-weight:800;letter-spacing:.08em}.sub{color:var(--muted);font-size:11px;margin-left:8px;letter-spacing:.06em}
        .tools{display:flex;align-items:center;gap:10px;color:var(--muted);font-size:12px}
        .tools label{display:flex;align-items:center;gap:5px;cursor:pointer}
        .tools a{color:var(--accent);text-decoration:none;border:1px solid #2b6654;border-radius:6px;padding:4px 8px}
        main{flex:1;min-height:0;padding:6px;overflow:hidden}
        #wall{height:100%;display:grid;grid-template-columns:repeat(auto-fit,minmax(230px,1fr));grid-auto-rows:1fr;gap:6px}
        .tile{position:relative;min-height:130px;background:#000;border:2px solid var(--line);border-radius:7px;overflow:hidden;cursor:pointer}
        .tile.selected{border-color:var(--accent);box-shadow:0 0 0 1px #0b5a45 inset}
        .tile.blank{background:#0b1118;border-style:dashed}
        .tile img{position:absolute;inset:0;width:100%;height:100%;object-fit:contain;display:block}
        .name{position:absolute;left:8px;bottom:8px;max-width:calc(100% - 16px);padding:4px 8px;border-radius:5px;background:rgba(0,0,0,.8);font-size:11px;font-weight:700;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
        .empty{display:flex;align-items:center;justify-content:center;height:100%;color:var(--muted);text-align:center;padding:30px}
        #bigview{display:none;height:100%;grid-template-rows:minmax(0,1fr) auto}
        #bigview.open{display:grid}
        #wall.hidden{display:none}
        #bigpix{position:relative;min-height:0;background:#000;display:flex;align-items:center;justify-content:center}
        #bigimg{max-width:100%;max-height:100%;object-fit:contain}
        #bigempty{position:absolute;color:var(--muted);text-align:center;padding:24px}
        #bigname{position:absolute;left:14px;bottom:14px;background:rgba(0,0,0,.82);padding:6px 10px;border-radius:6px;font-weight:700}
        #controls{padding:9px 14px 12px;background:#111821;border-top:1px solid var(--line)}
        #meta{color:var(--muted);font-size:12px;min-height:18px}
        #metabold{color:#ffd266;font-weight:700;margin-left:8px}
        .row{display:flex;align-items:center;gap:7px;justify-content:center;margin-top:6px;flex-wrap:wrap}
        .row.stopwatch{justify-content:flex-end;color:var(--muted);font-size:11px}
        .row.stopwatch b{color:#ffd266;min-width:44px;text-align:center}
        button{background:#1d2936;color:var(--ink);border:1px solid #354457;border-radius:5px;padding:6px 10px;font:inherit;cursor:pointer}
        button:hover{border-color:var(--accent)} button.primary{background:#17634d;border-color:#2b8e6d;font-weight:700}
        #scrub{flex:1;min-width:100px;max-width:600px;accent-color:var(--accent)}
        #windowLabel{font-size:10px;color:var(--muted);white-space:nowrap}
        footer{padding:5px 14px 8px;color:var(--muted);font-size:11px;background:#0c1118}
        @media(max-width:700px){#wall{grid-template-columns:repeat(2,minmax(120px,1fr));gap:4px}.sub{display:none}.tile{min-height:90px}body{font-size:13px}}
        </style></head><body>
        <header><div><span class="brand">__TITLE__</span><span class="sub" id="sub">ARROWS SELECT · ENTER ENLARGE · ESC WALL</span></div>
        <div class="tools"><label><input id="labels" type="checkbox" checked> Names</label><a href="/">Console</a></div></header>
        <main><div id="wall"></div><div id="bigview"><div id="bigpix"><img id="bigimg" alt=""><div id="bigempty">Select a tile, then press Enter.</div><div id="bigname"></div></div>
        <div id="controls"><div><span id="meta"></span><span id="metabold"></span></div>
        <div class="row"><button id="back15">−15s</button><button id="back5">−5s</button><button id="play" class="primary">Play</button><button id="fwd5">+5s</button><button id="fwd15">+15s</button><button id="live">Live edge</button></div>
        <div class="row"><span style="font-size:10px;color:var(--accent)">LIVE</span><input id="scrub" type="range" min="0" max="120" value="0" step="0.1"><span id="windowLabel">2 MIN</span></div>
        <div class="row stopwatch" id="stoppageRow"><span>⏱ STOPPAGE</span><b id="stoppageValue">0:00</b><button id="stoppageToggle">Start</button><button id="stoppageReset">Reset</button></div></div></div></main>
        <footer id="status">Connecting to ScorePlus…</footer>
        <script>(function(){
          var mode='__MODE__', supportsReplay=__REPLAY__;
          var wall=document.getElementById('wall'), big=document.getElementById('bigview'), bigimg=document.getElementById('bigimg');
          var bigempty=document.getElementById('bigempty'), bigname=document.getElementById('bigname'), meta=document.getElementById('meta'), metabold=document.getElementById('metabold');
          var scrub=document.getElementById('scrub'), play=document.getElementById('play'), labels=document.getElementById('labels'), status=document.getElementById('status');
          var windowLabel=document.getElementById('windowLabel'), stoppageRow=document.getElementById('stoppageRow');
          var stoppageValue=document.getElementById('stoppageValue'), stoppageToggle=document.getElementById('stoppageToggle');
          var state={cameras:[],tiles:[]}, expanded=false, selected=-1, tileIndex=0, signature='', imageNodes={}, scrubbing=false, seekTimer=0;
          if(!supportsReplay)document.getElementById('controls').style.display='none';
          function q(path,params){var p=new URLSearchParams(params||{});p.set('mode',mode);return path+'?'+p.toString();}
          function act(params){var p=new URLSearchParams(params||{});p.set('mode',mode);p.set('a',params.a);return fetch('/api/camera-wall/action',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:p.toString()}).catch(function(){});}
          function cameraVersion(index){var camera=(state.cameras||[]).find(function(c){return c.index===index;});return camera&&camera.frame?camera.frame:0;}
          // A stable version per camera is intentional: selecting a tile must not change every
          // other tile's URL, which made the browser reload all of them and flash black.
          function frame(camera,replay,version){var p=new URLSearchParams({mode:mode,camera:camera,v:String(version||0)});if(replay)p.set('replay','1');return '/api/camera-wall/frame?'+p.toString();}
          function useTiles(){return mode==='multiview'&&Array.isArray(state.tiles)&&state.tiles.length>0;}
          function labelVisible(){return state.showLabels!==false;}
          function gridColumns(){
            var tracks=(getComputedStyle(wall).gridTemplateColumns||'').split(' ').filter(function(x){return x;});
            return Math.max(1,tracks.length);
          }
          function visualVersion(){return state.tilesVersion||0;}
          function imageFor(item,live){
            if(!item)return '';
            if(item.kind==='camera')return frame(item.camera,false,live?Date.now():cameraVersion(item.camera));
            if(item.kind==='replay'){
              var camera=state.selected>=0?state.selected:(item.camera>=0?item.camera:0);
              var version=live?Date.now():Math.round(Number(state.back||0)*10)+'-'+(state.playing?'1':'0')+'-'+camera;
              return frame(camera,true,version);
            }
            if(item.kind==='preview'){
              var base='/api/camera-wall/layout?display='+encodeURIComponent(item.display||'')+'&v='+visualVersion();
              return item.layout?base+'&layout='+encodeURIComponent(item.layout):base;
            }
            if(item.kind==='audience')return '/watch.png?display='+encodeURIComponent(item.display||'')+'&v='+visualVersion();
            return '';
          }
          function renderWall(){
            var items=useTiles()?state.tiles:state.cameras.map(function(c){return {kind:'camera',label:c.label,camera:c.index};});
            var sig=items.map(function(item){return item.kind+'|'+item.label+'|'+(item.camera==null?'':item.camera)+'|'+(item.display||'')+'|'+(item.layout||'');}).join('~');
            if(sig!==signature||wall.children.length!==items.length){signature=sig;wall.innerHTML='';imageNodes={};
              if(!items.length){wall.innerHTML='<div class="empty">No cameras or layout tiles.<br>Add them in the ScorePlus Media tab or Multiview editor.</div>';}
              items.forEach(function(item,index){
                var tile=document.createElement('div');tile.className='tile'+(item.kind==='blank'?' blank':'');tile.dataset.index=String(index);
                var img=document.createElement('img');img.alt=item.label||'';
                var incoming=document.createElement('img');incoming.alt='';incoming.style.display='none';
                if(item.kind!=='blank'){tile.appendChild(img);tile.appendChild(incoming);}
                var name=document.createElement('div');name.className='name';name.textContent=item.label||'';tile.appendChild(name);
                tile.addEventListener('click',function(){
                  if(useTiles()){tileIndex=index;renderWall();}
                  else act({a:'select',index:item.camera});
                });
                tile.addEventListener('dblclick',function(){
                  if(useTiles()){tileIndex=index;expanded=true;showBig();}
                  else {act({a:'select',index:item.camera});expanded=true;showBig();}
                });
                wall.appendChild(tile);imageNodes[index]={tile:tile,imgs:[img,incoming],active:0,src:'',name:name,item:item};
              });
            }
            items.forEach(function(item,index){
              var node=imageNodes[index];if(!node)return;
              node.tile.classList.toggle('selected',useTiles()?index===tileIndex:item.camera===state.selected);
              node.name.style.display=labelVisible()&&item.label?'block':'none';
              if(node.imgs&&item.kind!=='blank'){
                var next=imageFor(item);
                if(node.src!==next){
                  node.src=next;
                  // Load into the hidden layer and swap only after it decodes. Replacing the visible
                  // image directly left a black frame between the old and new JPEGs.
                  var front=node.imgs[node.active], back=node.imgs[1-node.active];
                  back.onload=function(){back.style.display='block';front.style.display='none';node.active=1-node.active;back.onload=null;};
                  back.onerror=function(){back.onload=null;};
                  back.src=next;
                }
              }
            });
          }
          function showBig(){
            wall.classList.toggle('hidden',expanded);big.classList.toggle('open',expanded);
            document.getElementById('sub').textContent=expanded?'ARROWS CHANGE CAMERA · ENTER / ESC WALL':'ARROWS SELECT · ENTER ENLARGE · ESC WALL';
          }
          function renderBig(){
            if(!expanded)return;
            var item=useTiles()?(state.tiles[tileIndex]||null):null;
            var camera=state.cameras.find(function(x){return x.index===state.selected;})||null;
            bigname.textContent=item?item.label:(camera?camera.label:'No camera');bigname.style.display=labelVisible()?'block':'none';
            var src=item?imageFor(item,true):(camera?frame(camera.index,supportsReplay,Date.now()):'');
            if(src){bigimg.src=src;bigimg.style.display='block';bigempty.style.display='none';}else{bigimg.style.display='none';bigempty.style.display='block';}
            if(!supportsReplay)return;
            var game=state.gameClock||'--:--',playClock=state.playClock||':--',stoppage=state.stoppage||'0:00';
            meta.textContent='GAME '+game+'   ·   PLAY '+playClock+(state.hasStoppage?'   ·   STOPPAGE '+stoppage:'')+'   ·   '+(state.period||'');
            metabold.textContent=state.hasReplay?(state.back<0.15?'LIVE EDGE':Math.round(state.back)+'s BACK'):'WAITING FOR RECORDING';
            var windowSeconds=state.windowSeconds||120;
            windowLabel.textContent=windowSeconds%60===0?(windowSeconds/60)+' MIN':windowSeconds+' SEC';
            if(!scrubbing){scrub.max=Math.max(1,windowSeconds);scrub.value=Math.min(Number(state.back||0),Number(scrub.max));}
            play.textContent=state.playing?'Pause':'Play';
            stoppageRow.style.display=state.hasStoppage?'flex':'none';
            stoppageValue.textContent=stoppage;
            stoppageToggle.textContent=state.stoppageRunning?'Stop':'Start';
          }
          function render(){renderWall();renderBig();labels.checked=state.showLabels!==false;
            var count=useTiles()?state.tiles.length:state.cameras.length;
            status.textContent='Connected · '+count+(useTiles()?' tile':' camera')+(count===1?'':'s')+' · click a tile or use the arrow keys';
          }
          function tick(){fetch(q('/api/camera-wall'),{cache:'no-store'}).then(function(r){return r.ok?r.json():null;}).then(function(s){if(!s)return;state=s;if(!useTiles())selected=s.selected;render();}).catch(function(){status.textContent='Waiting for ScorePlus…';}).then(function(){setTimeout(tick,250);});}
          function moveCamera(dx,dy){
            var cameras=state.cameras||[];if(!cameras.length)return;
            var index=Math.max(0,cameras.findIndex(function(c){return c.index===state.selected;}));
            var columns=gridColumns();
            if(dy){var row=Math.floor(index/columns)+dy;index=Math.min(Math.max(0,row)*columns+(index%columns),cameras.length-1);}
            if(dx)index=Math.max(0,Math.min(cameras.length-1,index+dx));
            act({a:'select',index:cameras[index].index});
          }
          function moveTile(dx,dy){
            var tiles=state.tiles||[];if(!tiles.length)return;
            var index=Math.max(0,Math.min(tileIndex,tiles.length-1)),columns=gridColumns();
            if(dy){var row=Math.floor(index/columns)+dy;index=Math.min(Math.max(0,row)*columns+(index%columns),tiles.length-1);}
            if(dx)index=Math.max(0,Math.min(tiles.length-1,index+dx));
            tileIndex=index;renderWall();
          }
          function parseGesture(text){
            var parts=String(text||'').split('+');if(!parts.length)return null;
            var gesture={key:parts.pop(),ctrl:false,shift:false,alt:false,win:false};
            parts.forEach(function(part){var p=part.toLowerCase();if(p==='ctrl'||p==='control')gesture.ctrl=true;else if(p==='shift')gesture.shift=true;else if(p==='alt')gesture.alt=true;else if(p==='win'||p==='windows')gesture.win=true;});
            return gesture;
          }
          function eventKey(e){
            var names={ArrowLeft:'Left',ArrowRight:'Right',ArrowUp:'Up',ArrowDown:'Down',Escape:'Esc',' ':'Space',Enter:'Enter'};
            return names[e.key]||(e.key.length===1?e.key.toUpperCase():e.key);
          }
          function boundAction(e){
            var key=eventKey(e),bindings=state.bindings||{};
            for(var id in bindings){
              var g=parseGesture(bindings[id]);if(!g)continue;
              if(g.key.toLowerCase()!==key.toLowerCase())continue;
              if(g.ctrl!==e.ctrlKey||g.shift!==e.shiftKey||g.alt!==e.altKey||g.win!==e.metaKey)continue;
              return id;
            }
            return '';
          }
          function handleAction(id){
            if(id.indexOf('camera.')===0){
              if(id==='camera.left')useTiles()?moveTile(-1,0):moveCamera(-1,0);
              else if(id==='camera.right')useTiles()?moveTile(1,0):moveCamera(1,0);
              else if(id==='camera.up')useTiles()?moveTile(0,-1):moveCamera(0,-1);
              else if(id==='camera.down')useTiles()?moveTile(0,1):moveCamera(0,1);
              else if(id==='camera.enter'){expanded=!expanded;showBig();}
              else if(id==='camera.back'){expanded=false;showBig();}
              return;
            }
            if(!supportsReplay)return;
            if(id==='replay.back')act({a:'step',delta:'2'});
            else if(id==='replay.forward')act({a:'step',delta:'-2'});
            else if(id==='replay.live'){act({a:'seek',back:'0'});act({a:'pause'});}
            else if(id==='replay.start')act({a:'seek',back:String(state.depth||0)});
            else if(id==='replay.playpause')act({a:'toggle'});
          }
          labels.addEventListener('change',function(){act({a:'labels',value:labels.checked?'1':'0'});});
          document.addEventListener('keydown',function(e){
            if(['INPUT','TEXTAREA','SELECT'].indexOf(document.activeElement.tagName)>=0)return;
            var action=boundAction(e);
            // Only consume bindings this page can actually run. Swallowing a console binding here
            // made Enter (and the arrow keys) stop working when one of them shared the same key.
            if(action&&(action.indexOf('camera.')===0||(supportsReplay&&action.indexOf('replay.')===0))){handleAction(action);e.preventDefault();return;}
            if(useTiles()){
              if(e.key==='ArrowLeft'){moveTile(-1,0);e.preventDefault();}
              else if(e.key==='ArrowRight'){moveTile(1,0);e.preventDefault();}
              else if(e.key==='ArrowUp'){moveTile(0,-1);e.preventDefault();}
              else if(e.key==='ArrowDown'){moveTile(0,1);e.preventDefault();}
              else if(e.key==='Enter'){expanded=!expanded;showBig();e.preventDefault();}
              else if(e.key==='Escape'){expanded=false;showBig();}
              return;
            }
            if(supportsReplay){
              if(e.key==='ArrowLeft'){moveCamera(-1,0);e.preventDefault();}
              else if(e.key==='ArrowRight'){moveCamera(1,0);e.preventDefault();}
              else if(e.key==='ArrowUp'){moveCamera(0,-1);e.preventDefault();}
              else if(e.key==='ArrowDown'){moveCamera(0,1);e.preventDefault();}
              else if(e.key==='Enter'){expanded=!expanded;showBig();e.preventDefault();}
              else if(e.key==='Escape'){expanded=false;showBig();}
              else if(e.key==='a'||e.key==='A'){act({a:'step',delta:'2'});e.preventDefault();}
              else if(e.key==='d'||e.key==='D'){act({a:'step',delta:'-2'});e.preventDefault();}
              else if(e.key==='w'||e.key==='W'){act({a:'seek',back:'0'});act({a:'pause'});e.preventDefault();}
              else if(e.key==='s'||e.key==='S'){act({a:'seek',back:String(state.depth||0)});e.preventDefault();}
            }
          });
          document.getElementById('back15').addEventListener('click',function(){act({a:'step',delta:'15'});});
          document.getElementById('back5').addEventListener('click',function(){act({a:'step',delta:'5'});});
          document.getElementById('fwd5').addEventListener('click',function(){act({a:'step',delta:'-5'});});
          document.getElementById('fwd15').addEventListener('click',function(){act({a:'step',delta:'-15'});});
          document.getElementById('live').addEventListener('click',function(){act({a:'seek',back:'0'});act({a:'pause'});});
          play.addEventListener('click',function(){act({a:'toggle'});});
          stoppageToggle.addEventListener('click',function(){act({a:'stoppage',value:'toggle'});});
          document.getElementById('stoppageReset').addEventListener('click',function(){act({a:'stoppage',value:'reset'});});
          scrub.addEventListener('input',function(){scrubbing=true;clearTimeout(seekTimer);var value=scrub.value;seekTimer=setTimeout(function(){act({a:'seek',back:value});scrubbing=false;},90);});
          tick();
        })();</script></body></html>
        """;
}
