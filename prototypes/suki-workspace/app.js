const $ = s => document.querySelector(s);
const $$ = s => [...document.querySelectorAll(s)];
const state = { target: 'novel', selected: new Set([0,1,2,3]), mode: 'ready', configured: true, timer: null, stage: 0, audioExported: false };
const targets = {
  script: {title:'整理原始剧情，保留对白与场景', description:'生成 Markdown 与 HTML 剧本，方便阅读和归档。', action:'导出剧本', last:'导出剧本', cost:'剧本整理在本地完成，无需文本模型。'},
  novel: {title:'将剧情整理为连贯的小说', description:'保留原始剧本，同时生成小说 Markdown 与 EPUB。', action:'生成小说', last:'小说与 EPUB', cost:'小说生成会调用模型，费用由服务商收取。'},
  audio: {title:'从剧情到小说，再为角色配音', description:'先生成小说，再进入配音工作台选择声音、合成与导出。', action:'生成小说并准备配音', last:'小说 → 配音', cost:'小说与语音会分别调用服务，费用由服务商收取。'}
};
function icons(root=document) { root.querySelectorAll('[data-icon]').forEach(el => el.style.setProperty('--icon', `url("${iconURLs[el.dataset.icon]}")`)); }
function icon(name) { return `<i data-icon="${name}" aria-hidden="true"></i>`; }
function toast(message) { $('#toast span').textContent = message; $('#toast').hidden=false; clearTimeout(toast.timer); toast.timer=setTimeout(()=>$('#toast').hidden=true,4000); }
function prefix() { return $('#story').value==='孤星'?'CW':$('#story').value==='巴别塔'?'BB':$('#story').value==='怀黍离'?'HS':$('#categories .active').textContent==='主线剧情'?'1':'OP'; }
function chapters() {
  $('#chapters').innerHTML = Array.from({length:8},(_,i)=>`<label class="chapter"><input type="checkbox" data-chapter="${i}" ${state.selected.has(i)?'checked':''}><span class="chapter-id">${prefix()}-${Math.floor(i/2)+1}</span><span class="chapter-name">${i%2?'行动后':'行动前'}</span><span class="chapter-kind">剧情</span></label>`).join('');
  $$('#chapters input').forEach(el=>el.onchange=()=>{if(el.checked)state.selected.add(+el.dataset.chapter);else state.selected.delete(+el.dataset.chapter);update();});
  update();
}
function update() {
  const t=targets[state.target], n=state.selected.size;
  $('#selection-count').textContent=`已选 ${n} / 8 章`;
  $('#target-title').textContent=t.title; $('#target-description').textContent=t.description;
  $('#last-step').textContent=t.last;
  $('.flow-preview').innerHTML=`<span>获取剧情</span>${icon('chevron-right')}<span>整理剧本</span>${$('#vision').checked?icon('chevron-right')+'<span>画面描述</span>':''}${icon('chevron-right')}<span id="last-step">${t.last}</span>`;
  $('#cost-note span').textContent=state.target==='script'&&$('#vision').checked?'画面描述会调用视觉模型，费用由服务商收取。':t.cost;
  $('#start>span:first-of-type').textContent=state.target!=='script'&&!state.configured?'配置模型后继续':t.action;
  $('.button-count').textContent=`${n} 章`; $('#start').disabled=n===0;
  $('.model-row').hidden=state.target==='script';
  $('#model-label').textContent=state.configured?$('#provider').value:'尚未连接模型';
  $('#model-detail').textContent=state.configured?$('#model').value:'选择服务并填写 API Key';
  $('#model-status').textContent=state.configured?'已配置':'待配置';
  $('#model-status').classList.toggle('warning',!state.configured);
  $('#options-summary').textContent=`${$('#cache').checked?'复用缓存':'重新生成'} · 画面描述${$('#vision').checked?'开启':'关闭'}`;
  $('#folder').innerHTML=`output / ${$('#story').value}${icon('chevron-right')}`;
  $('#voice-count').textContent=`${n} 章`;
  const locked=!['ready','missing'].includes(state.mode);
  $$('#chapters input, #categories button, #story, #language, #select-all, #clear').forEach(el=>el.disabled=locked);
  $$('.target').forEach(el=>el.disabled=state.mode==='running');
  icons();
}
function setTarget(target) {
  stopTimer(); state.target=target; state.mode='ready'; state.audioExported=false;
  $$('.target').forEach(el=>{const active=el.dataset.target===target;el.classList.toggle('active',active);el.setAttribute('aria-pressed',active);});
  renderState();
}
function stopTimer(){clearInterval(state.timer);state.timer=null;}
function renderState() {
  const mode=state.mode,n=state.selected.size,script=state.target==='script';
  $('#plan-view').hidden=!['ready','missing'].includes(mode);$('#run-view').hidden=['ready','missing'].includes(mode);
  $('.output-panel').classList.toggle('is-running',mode==='running');
  $('#scenario').value=['ready','running','partial','done','missing'].includes(mode)?mode:'ready';
  update(); if($('#run-view').hidden)return;
  const complete=mode==='done',partial=mode==='partial',cancelled=mode==='cancelled';
  $('#run-title').textContent=complete?(script?'剧本已导出':state.target==='audio'?'小说已就绪，可以配音':'小说已生成'):partial?'部分章节未完成':cancelled?'已停止生成':script?'正在整理剧本':'正在生成小说';
  $('#run-detail').textContent=complete?`已完成 ${n} / ${n} 章 · ${script?'Markdown + HTML':'Markdown + EPUB'}`:partial?`已完成 ${Math.max(0,n-1)} / ${n} 章 · 1 章需要重试`:cancelled?'已完成的结果已保留，再次生成会复用缓存。':`已完成 ${Math.min(state.stage,n-1)} / ${n} 章 · 正在处理所选剧情`;
  $('.run-symbol').innerHTML=icon(complete?'check':partial?'alert-circle-outline':cancelled?'pause':'refresh');
  const steps=[['获取剧情','所选章节与资源已下载'],['整理剧本','对白、场景与人物信息已整理'],[script?'导出文件':'小说生成',complete?(script?'Markdown / HTML 已保存':'所选章节已生成'):partial?'1 章请求超时，可单独重试':cancelled?'已停止后续请求':'正在调用模型 · 等待当前章节返回'],[script?'完成':'导出 EPUB',complete?'文件已保存到输出目录':partial?'已导出成功章节':'等待上一步完成']];
  $('#stepper').innerHTML=steps.map((s,i)=>{
    const cls=complete||i<2||(partial&&i===3)?'done':i===2?(partial?'error':cancelled?'':'current'):'';
    return `<li class="step ${cls}"><span class="step-number">${cls==='done'?icon('check'):i+1}</span><div><b>${s[0]}</b><small>${s[1]}</small></div>${i<2?'<small>已完成</small>':''}</li>`;
  }).join('');
  $('#run-message').classList.toggle('warning',partial);
  $('#run-message').textContent=complete?(state.target==='audio'?'下一步确认声音并合成。音频尚未生成。':'可以查看文件，或调整章节后继续生成。'):partial?'已完成章节与 EPUB 已保留。重试仅处理失败章节。':cancelled?'停止不会删除已经生成的文件。':'已完成的章节会保留，可随时停止。';
  $('#log-lines').textContent=`[完成] 剧情资源准备完成\n[完成] 剧本整理完成\n${partial?'[失败] 模型请求超时，1 章未完成':complete?'[完成] 所选章节与文件导出完成':cancelled?'[停止] 用户停止生成':'[进行中] 正在处理当前章节，等待服务响应'}\n[说明] 原型演示，不调用真实服务`;
  $('#run-actions').innerHTML=complete?`<button class="basic" data-action="reset">返回选择</button><button class="primary" data-action="${state.target==='audio'?'voices':'files'}">${icon(state.target==='audio'?'headphones':'folder-outline')}${state.target==='audio'?'确认声音':'查看文件'}</button>`:partial?'<button class="basic" data-action="files">查看已完成文件</button><button class="primary" data-action="retry">重试失败章节</button>':cancelled?'<button class="basic" data-action="reset">返回选择</button><button class="primary" data-action="retry">继续生成</button>':'<button class="basic" data-action="stop">停止生成</button>';
  $$('#run-actions button').forEach(b=>b.onclick=()=>{const a=b.dataset.action;if(a==='reset'){state.mode='ready';renderState();}else if(a==='stop'){stopTimer();state.mode='cancelled';renderState();}else if(a==='files')showFiles();else if(a==='voices')$('#voices').showModal();else start();});
  icons();
}
function start(){
  if(!state.selected.size)return;
  if(state.target!=='script'&&!state.configured){$('#settings').showModal();return;}
  stopTimer();state.stage=0;state.mode='running';renderState();
  state.timer=setInterval(()=>{state.stage++;if(state.stage>=state.selected.size){stopTimer();state.mode='done';}renderState();},1600);
}
function showFiles(){
  $('#files-location').textContent=`output / ${$('#story').value}`;
  const hasOutput=['done','partial','cancelled'].includes(state.mode);
  const files=hasOutput?[['file-document-outline','原始剧本','Markdown · HTML'],...(state.target==='script'?[]:[['book-open-page-variant-outline','小说','Markdown · EPUB']]),...(state.audioExported?[['headphones','有声书','按章导出的 MP3']]:[])]:[];
  $('#file-list').innerHTML=files.length?files.map(f=>`<div class="file-row">${icon(f[0])}<div><b>${f[1]}</b><small>${f[2]}${state.mode==='partial'?' · 仅成功章节':''}</small></div><span class="status-pill">已保存</span></div>`).join(''):'<p class="dialog-hint">生成后，剧本、小说和音频会按类型显示在这里。</p>';
  icons();$('#files').showModal();
}
$$('.target').forEach(b=>b.onclick=()=>setTarget(b.dataset.target));
$('#select-all').onclick=()=>{state.selected=new Set(Array.from({length:8},(_,i)=>i));chapters();};
$('#clear').onclick=()=>{state.selected.clear();chapters();};
$('#story').onchange=()=>{stopTimer();state.mode='ready';state.selected=new Set([0,1,2,3]);chapters();renderState();};
$('#language').onchange=()=>toast(`已切换资源语言：${$('#language').value}（演示）`);
$$('#categories button').forEach(b=>b.onclick=()=>{$$('#categories button').forEach(x=>{x.classList.toggle('active',x===b);x.setAttribute('aria-pressed',x===b);});const names=b.textContent==='活动剧情'?['孤星','巴别塔','怀黍离']:b.textContent==='主线剧情'?['第一章 · 黑暗时代','第二章 · 异卵同生']:['阿米娅','凯尔希'];$('#story').innerHTML=names.map(n=>`<option>${n}</option>`).join('');$('.badge').textContent=b.textContent==='活动剧情'?'SideStory':b.textContent==='主线剧情'?'Main Story':'Operator';$('#story').onchange();});
$('#theme').onclick=()=>{const dark=document.documentElement.dataset.theme!=='dark';document.documentElement.dataset.theme=dark?'dark':'light';$('#theme i').dataset.icon=dark?'white-balance-sunny':'weather-night';icons();};
$$('[data-settings]').forEach(b=>b.onclick=()=>$('#settings').showModal());
$$('[data-close]').forEach(b=>b.onclick=()=>$('#'+b.dataset.close).close());
$('#save-settings').onclick=e=>{if(!$('#key').value.trim()||!$('#model').value.trim()){e.preventDefault();toast('请填写模型名称和演示 API Key。');return;}state.configured=true;if(state.mode==='missing')state.mode='ready';renderState();toast('已应用到当前原型，不会保存凭据。');};
$('#provider').onchange=()=>$('#model').value=$('#provider').value==='DeepSeek'?'deepseek-chat':$('#provider').value==='阿里云百炼'?'qwen-plus':'自定义模型';
$('#vision').onchange=()=>{update();if($('#vision').checked)toast('将增加画面描述阶段；正式界面需检查视觉模型配置。');};
$('#cache').onchange=update;
$('#start').onclick=start;
$('#folder').onclick=showFiles;
$('#sync').onclick=()=>toast('剧情目录已同步（示例数据）。');
$('#scenario').onchange=()=>{stopTimer();state.mode=$('#scenario').value;state.configured=state.mode!=='missing';if(!state.selected.size){state.selected=new Set([0,1,2,3]);chapters();}state.stage=1;renderState();};
$('#audio-demo').onclick=()=>{state.audioExported=true;$('#voices').close();showFiles();toast('演示合成完成：音频已列入输出文件。');};
icons();chapters();renderState();
