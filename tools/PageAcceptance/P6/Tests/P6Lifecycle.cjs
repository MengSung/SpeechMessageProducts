/*
 * 檔案整體說明
 * 目的與定位：保護階段6驗收工具失敗時的資源owner與秘密狀態契約，不執行真實SDK、
 * 瀏覽器、D365或LINE，不讀User Secrets。此檔只針對階段審查已重現的清理缺陷回歸。
 * 主要功能與流程：以VM載入真正bridge／browser來源，僅替換compiler、spawn與
 * Playwright邊界；注入ready逾時、錯誤cleanup、cleanup逾時及context.close例外。
 * 決定性斷言要求送EOF並等child close、逾時有界終止、未知秘密不得標已還原，以及
 * context關閉失敗仍呼叫browser.close。before模式只讀3ea8a133舊來源證明四案紅燈。
 * 使用邊界與維護：每案唯一持有PassThrough與合成child，finally關閉串流及child；
 * timer最多10ms，close事件釋放等待，無跨案例狀態或真實憑證。所有輸出都是合成
 * 斷言與固定模式，不把此離線回歸當成頁面／秘密還原的實機結果。
 */
'use strict';
const assert=require('node:assert/strict');const vm=require('node:vm');const fs=require('node:fs');
const path=require('node:path');const {EventEmitter}=require('node:events');const {PassThrough}=require('node:stream');const {execFileSync}=require('node:child_process');
const root=path.resolve(__dirname,'../../../..');
const baseline=process.argv[2]==='before';
function source(file){if(baseline)return execFileSync('git',['show','3ea8a133:tools/PageAcceptance/P6/'+file],{encoding:'utf8',cwd:root});const current=file==='P6FixtureBridge.cjs'?path.join(root,'tools/PageAcceptance/Shared/FixtureBridge.cjs'):path.join(root,'tools/PageAcceptance/P6',file);return fs.readFileSync(current,'utf8');}
function bridgeModule(child){const module={exports:{}};const fakeFs={readdir:async p=>p.endsWith('net10.0')?['System.Runtime.dll']:['10.0.15'],mkdir:async()=>{},writeFile:async()=>{}};
 const fakeCp={spawn:()=>child,spawnSync:(_cmd,args)=>({status:0,stdout:args[0]==='--version'?'10.0.404\n':''})};
 const context={module,exports:module.exports,process:{env:{ProgramFiles:'C:/Program Files'}},Buffer,console,
 setTimeout:(fn,ms)=>setTimeout(fn,Math.min(ms,10)),clearTimeout,require:n=>n==='node:fs/promises'?fakeFs:n==='node:child_process'?fakeCp:require(n)};
 vm.runInNewContext(source('P6FixtureBridge.cjs'),context);return module.exports;}
function childFixture({ready=true,cleanup=false,closeDelay=20}){
 const child=new EventEmitter();child.stdout=new PassThrough();child.stderr=new PassThrough();child.closed=false;child.ended=0;let sent=false;
 child.stdin=new EventEmitter();child.stdin.write=()=>true;child.stdin.end=()=>{child.ended++;if(sent)return;sent=true;
 if(cleanup)queueMicrotask(()=>child.stdout.write(JSON.stringify({cleanup:true,remaining:1,secretsRestored:false})+'\n'));
 setTimeout(()=>{if(!child.closed){child.closed=true;child.emit('close',1);}},closeDelay);};
 child.kill=()=>{if(!child.closed){child.closed=true;child.emit('close',1);}return true;};
 if(ready)setImmediate(()=>child.stdout.write('{"ready":true}\n'));return child;}
async function readyFailure(){const child=childFixture({ready:false,closeDelay:2});try{await assert.rejects(bridgeModule(child).createFixtureBridge('C:/fixture'));assert.ok(child.ended>0,'ready timeout must send EOF');assert.equal(child.closed,true,'ready timeout must drain child before rejection');}finally{child.kill();child.stdout.end();child.stderr.end();}}
async function cleanupFailure(){const child=childFixture({cleanup:true,closeDelay:5});try{const bridge=await bridgeModule(child).createFixtureBridge('C:/fixture');let error;try{await bridge.close();}catch(e){error=e;}assert.ok(error,'bad cleanup must reject');assert.equal(child.closed,true,'cleanup failure must await child close');assert.equal(error.cleanup?.secretsRestored,false,'unverified secrets must remain false');}finally{child.kill();child.stdout.end();child.stderr.end();}}
async function cleanupTimeout(){const child=childFixture({cleanup:false,closeDelay:100});try{const bridge=await bridgeModule(child).createFixtureBridge('C:/fixture');let error;try{await bridge.close();}catch(e){error=e;}assert.ok(error);assert.equal(child.closed,true,'cleanup timeout must bounded-drain then terminate');assert.equal(error.cleanup?.secretsRestored,false,'timeout cannot claim restore');assert.equal(error.cleanup?.processExited,true);}finally{child.kill();child.stdout.end();child.stderr.end();}}
async function browserFailure(){let browserClosed=false;const browser={newContext:async()=>({newPage:async()=>{throw Error('synthetic page fault');},close:async()=>{throw Error('synthetic context close fault');}}),close:async()=>{browserClosed=true;}};
 const module={exports:{}};vm.runInNewContext(source('Run-P6PageAcceptance.cjs'),{module,exports:module.exports,require:n=>n.includes('Shared/EdgeBrowser.cjs')?{launchEdge:async()=>browser}:n.includes('playwright-core')?{chromium:{launch:async()=>browser}}:require(n),URL,console});
 await assert.rejects(module.exports.runBrowserRound({origin:'http://localhost:5166',fixture:{},outputDirectory:'unused',label:'before'}));assert.equal(browserClosed,true,'browser close must run after context close throws');}
(async()=>{let failures=0;for(const [name,test] of Object.entries({Ready_timeout_drains_child:readyFailure,Invalid_cleanup_drains_and_preserves_unknown:cleanupFailure,Cleanup_timeout_bounds_child_and_never_claims_restore:cleanupTimeout,Context_close_fault_still_closes_browser:browserFailure})){try{await test();console.log('PASS '+name);}catch(e){failures++;console.log('FAIL '+name+': '+e.message);}}console.log(JSON.stringify({mode:baseline?'before':'after',passed:4-failures,failed:failures,realServices:false,secrets:false}));process.exitCode=failures?1:0;})();
