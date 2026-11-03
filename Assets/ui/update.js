(() => {
  'use strict';
  const bridge=window.chrome.webview,app=document.querySelector('.lab-update');
  const post=action=>bridge.postMessage({action});
  app.querySelectorAll('[data-choice]').forEach(button=>button.addEventListener('click',()=>post(button.dataset.choice)));
  app.querySelectorAll('[data-action]').forEach(button=>button.addEventListener('click',()=>post(button.dataset.action)));
  document.addEventListener('keydown',event=>{if(event.key==='Escape')post('no');});
  bridge.addEventListener('message',event=>{
    const state=event.data;
    app.querySelector('[data-app-version]').textContent=state.appVersion;
    app.querySelector('[data-message]').textContent=state.message;
    app.querySelector('[data-heading]').textContent=state.downloading?'Updating LAB':state.failed?'Update unavailable':'Update available';
    app.querySelectorAll('[data-choice]').forEach(button=>button.hidden=state.downloading?button.dataset.choice!=='no':state.failed?button.dataset.choice!=='continue':button.dataset.choice==='continue');
    app.querySelector('[data-choice=no]').textContent=state.downloading?'Cancel':'No';
  });
  const emblem=new Image();emblem.src='../lab.png';
  Promise.all([document.fonts.ready,emblem.decode()]).then(()=>post('ready'));
})();
