/*
 * 檔案整體說明
 * 目的與定位：供階段6 Node頁面驗收借用的SDK子程序橋接；編譯既有P6FixtureOwner來源
 * 至ignored artifacts，不建立csproj、不安裝套件，不把資料或秘密寫進編譯參數。
 * 主要流程：讀本機SDK與reference pack位置，借用既有Deployment輸出參考；序列JSON
 * 命令經stdin/stdout管線傳遞，最多128KiB回應／512KiB診斷，超量即拒絕。不轉送原始
 * stderr或SDK錯誤本文，只回固定狀態；生命週期owner必須finally呼叫close等清理結果。
 * 使用邊界與維護：只有Deployment白名單goodshepherdback；本模組不讀憑證或既有會員資料。
 * 子程序擁有SDK連線與本輪資料，finish/EOF會逆序刪除並驗證；close先關stdin等待清理，
 * 所有失敗路徑也有界drain；只有超時才終止，再確認close，不能將kill當成秘密已還原。
 * 未收到remaining=0與secretsRestored=true必須明列未確認，不得將未知狀態當成已清理。
 */
'use strict';
const fs = require('node:fs/promises');
const path = require('node:path');
const { spawn, spawnSync } = require('node:child_process');
const readline = require('node:readline');

/** 編譯不含秘密的輔助來源；所有編譯輸出限artifacts，既有Deployment先完成Release建置。 */
async function compileFixtureOwner(root) {
  const sdkResult = spawnSync('dotnet', ['--version'], { encoding: 'utf8' });
  if (sdkResult.status !== 0 || !/^\d+\.\d+\.\d+\s*$/.test(sdkResult.stdout)) throw new Error('P6_SDK_UNAVAILABLE');
  const dotnet = path.join(process.env.ProgramFiles, 'dotnet');
  const sdk = sdkResult.stdout.trim();
  const refRoot = path.join(dotnet, 'packs', 'Microsoft.NETCore.App.Ref');
  const versions = (await fs.readdir(refRoot)).filter(x => /^10\.\d+\.\d+$/.test(x))
    .sort((a, b) => a.localeCompare(b, undefined, { numeric: true }));
  if (!versions.length) throw new Error('P6_REFERENCE_PACK_UNAVAILABLE');
  const refs = path.join(refRoot, versions.at(-1), 'ref', 'net10.0');
  const bin = path.join(root, 'SpeechMessage.Dynamics.Deployment', 'bin', 'Release', 'net10.0');
  const artifact = path.join(root, 'artifacts', 'p6');
  await fs.mkdir(artifact, { recursive: true });
  const output = path.join(artifact, 'P6FixtureOwner.dll');
  const rsp = path.join(artifact, 'P6FixtureOwner.rsp');
  const source = path.join(root, 'tools', 'PageAcceptance', 'P6', 'P6FixtureOwner.cs');
  const lines = ['-nologo', '-target:exe', '-langversion:latest', '-nullable:enable', `-out:"${output}"`];
  for (const name of (await fs.readdir(refs)).filter(x => x.endsWith('.dll'))) lines.push(`-r:"${path.join(refs, name)}"`);
  for (const name of ['Microsoft.Xrm.Sdk.dll', 'Microsoft.Crm.Sdk.Proxy.dll', 'System.ServiceModel.Primitives.dll', 'System.Security.Cryptography.ProtectedData.dll'])
    lines.push(`-r:"${path.join(bin, name)}"`);
  lines.push(`"${source}"`);
  await fs.writeFile(rsp, lines.join('\r\n') + '\r\n', 'utf8');
  const result = spawnSync('dotnet', [path.join(dotnet, 'sdk', sdk, 'Roslyn', 'bincore', 'csc.dll'), '@' + rsp],
    { cwd: root, encoding: 'utf8', maxBuffer: 1024 * 1024 });
  if (result.status !== 0) throw new Error('P6_FIXTURE_COMPILATION_FAILED');
  return { output, bin };
}

/**
 * 先送EOF讓SDK owner清理與還原；不論協定／ready／cleanup是否失敗都等待close。
 * 正常drain最多timeoutMs，超時才終止，再等最多5秒close；kill不等於資料或秘密已還原。
 * timer由此呼叫finally清除，回傳純狀態供上層如實回報，不保存child或秘密。
 */
async function drainFixtureProcess(child, exited, timeoutMs = 60000) {
  let timer;
  const wait = async duration => {
    try {
      return await Promise.race([exited.then(code => ({ exited: true, code })),
        new Promise(resolve => { timer = setTimeout(() => resolve({ exited: false }), duration); })]);
    } finally { clearTimeout(timer); }
  };
  try { child.stdin.end(); } catch { /* 仍須等待已斷線子程序退出，不把pipe錯誤當成清理完成。 */ }
  let result = await wait(timeoutMs);
  if (result.exited) return { processExitCode: result.code, drainTimedOut: false, terminated: false, processExited: true };
  let terminated = false;
  try { terminated = child.kill(); } catch { /* kill失敗由processExited=false明確回報，不能吞成成功。 */ }
  result = await wait(Math.min(timeoutMs, 5000));
  return { processExitCode: result.exited ? result.code : null, drainTimedOut: true, terminated,
    processExited: result.exited };
}

/** 單一序列命令owner；可注入的runtime僅供離線故障回歸，正式呼叫沿用本機compiler／spawn與60秒上限。 */
async function createFixtureBridge(root, runtime = {}) {
  const compile = runtime.compile || compileFixtureOwner;
  const spawnProcess = runtime.spawnProcess || spawn;
  const responseTimeoutMs = runtime.responseTimeoutMs || 60000;
  const drainTimeoutMs = runtime.drainTimeoutMs || 60000;
  const { output, bin } = await compile(root);
  const child = spawnProcess('dotnet', ['exec', '--runtimeconfig', path.join(bin, 'SpeechMessage.Dynamics.Deployment.runtimeconfig.json'),
    '--depsfile', path.join(bin, 'SpeechMessage.Dynamics.Deployment.deps.json'), output],
  { cwd: root, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
  const queue = [];
  let waiter, ended = false, stderrBytes = 0;
  const lines = readline.createInterface({ input: child.stdout, crlfDelay: Infinity });
  const endInput = () => { try { child.stdin.end(); } catch { /* drain負責確認程序退出與還原。 */ } };
  lines.on('line', line => {
    if (Buffer.byteLength(line) > 128 * 1024) { endInput(); return; }
    let value;
    try { value = JSON.parse(line); } catch { endInput(); return; }
    if (waiter) { const resolve = waiter; waiter = undefined; resolve(value); }
    else if (queue.length < 4) queue.push(value);
    else endInput();
  });
  child.stderr.on('data', data => { stderrBytes += data.length; if (stderrBytes > 512 * 1024) endInput(); });
  const exited = new Promise(resolve => child.once('close', code => {
    ended = true;
    if (waiter) { const done = waiter; waiter = undefined; done({ error: 'P6_FIXTURE_EXITED' }); }
    resolve(code);
  }));
  child.once('error', endInput);
  child.stdin.on('error', endInput);
  async function next() {
    if (queue.length) return queue.shift();
    if (ended || waiter) throw new Error('P6_FIXTURE_PROTOCOL_STATE');
    let timeout;
    try {
      return await Promise.race([
        new Promise(resolve => { waiter = resolve; }),
        new Promise((_, reject) => { timeout = setTimeout(() => reject(new Error('P6_FIXTURE_RESPONSE_TIMEOUT')), responseTimeoutMs); })
      ]);
    } finally { clearTimeout(timeout); waiter = undefined; }
  }
  try {
    const ready = await next();
    if (!ready.ready) throw new Error('P6_FIXTURE_NOT_READY');
  } catch {
    const drained = await drainFixtureProcess(child, exited, drainTimeoutMs);
    lines.close(); queue.length = 0;
    const error = new Error('P6_FIXTURE_NOT_READY');
    error.cleanup = { cleanup: false, remaining: null, secretsRestored: true, ...drained };
    throw error;
  }
  return {
    async request(value) {
      const text = JSON.stringify(value);
      if (Buffer.byteLength(text) > 65536 || ended) throw new Error('P6_FIXTURE_COMMAND_REJECTED');
      child.stdin.write(text + '\n');
      const response = await next();
      if (!response.ok) { console.log(JSON.stringify({ fixtureFailure: value.operation, entity: value.entity || null, error: response.error, code: response.code })); throw new Error('P6_FIXTURE_OPERATION_FAILED'); }
      return response;
    },
    async close() {
      endInput();
      let cleanup, protocolFailed = false, drained;
      try { cleanup = await next(); } catch { protocolFailed = true; }
      finally {
        drained = await drainFixtureProcess(child, exited, drainTimeoutMs);
        lines.close(); queue.length = 0;
      }
      const result = { cleanup: cleanup?.cleanup === true, remaining: cleanup?.remaining ?? null,
        secretsRestored: cleanup?.secretsRestored === true, backupProtected: cleanup?.backupProtected === true, ...drained };
      if (protocolFailed || !result.cleanup || result.remaining !== 0 || !result.secretsRestored
          || !result.processExited || result.drainTimedOut) {
        const error = new Error('P6_FIXTURE_CLEANUP_FAILED');
        error.cleanup = result;
        throw error;
      }
      return result;
    }
  };
}
module.exports = { compileFixtureOwner, createFixtureBridge, drainFixtureProcess };
