/*
 * 檔案整體說明
 * 目的與定位：階段6兩輪實機驗收的唯一總owner，依P5→停止→清理→P6順序操作。
 * 主要流程：從Deployment User Secrets在記憶體取測試身分；SDK子程序在DPAPI備份後
 * 暫存Host測試token，finally還原比對。每輪建立fixture、啟動localhost Development站、
 * 操作Edge與讀回SDK欄位，再停止子程序與清理。只把去除秘密的合成對照及狀態寫artifacts。
 * 使用邊界與維護：既有會員帳密不讀不改；隨機登入帳密不經CLI、檔案、輸出。Host stdout/stderr
 * 只掃描固定啟動訊號並丟棄，不保存可能含token的原文。每次等待有上限，finally關Browser、
 * 結束Host並有界等待退出（包含spawn error），再要求SDK清理及秘密還原；任何清理失敗明確失敗，不假稱殘留0。
 * 正規化只使用fixture正式ID代號與明列日期欄位是否寫入，其餘值逐欄比較。Push成功數必須
 * 從測試LINE統計的前後差計算；沒有可辨識成功數則明列未驗證，不能用呼叫次數冒充送達。
 */
'use strict';
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const { spawn, spawnSync } = require('node:child_process');
const { randomBytes } = require('node:crypto');
const { createFixtureBridge } = require('./P6FixtureBridge.cjs');
const { createRoundFixture, readRoundFixture } = require('./P6Fixtures.cjs');
const { runBrowserRound, normalize, compareRounds } = require('./Run-P6PageAcceptance.cjs');

/** 以精確鍵名讀取User Secrets，不對終端或檔案輸出任何值，JSON root在本次呼叫後釋放。 */
async function readSettings() {
  const filename = path.join(process.env.APPDATA || path.join(os.homedir(), 'AppData', 'Roaming'),
    'Microsoft', 'UserSecrets', 'SpeechMessage.Dynamics.Deployment.P0', 'secrets.json');
  const bytes = await fs.readFile(filename);
  if (bytes.length > 1024 * 1024) throw new Error('P6_SECRETS_BOUND');
  const values = JSON.parse(bytes.toString('utf8').replace(/^\uFEFF/, ''));
  const flattened = {};
  function visit(value, prefix = '') {
    for (const [key, item] of Object.entries(value)) {
      const name = prefix ? prefix + ':' + key : key;
      if (item && typeof item === 'object') visit(item, name); else flattened[name] = item;
    }
  }
  visit(values);
  const prefix = 'Deployment:Organizations:goodshepherdback:';
  const churchId = flattened[prefix + 'ShadowTestChurchId'];
  const contactId = flattened[prefix + 'ShadowTestContactId'];
  const lineUserId = flattened[prefix + 'TestLineUserId'];
  if (![churchId, contactId].every(x => typeof x === 'string' && /^[0-9a-f-]{36}$/i.test(x))
      || typeof lineUserId !== 'string' || !lineUserId) throw new Error('P6_TEST_IDENTITY_MISSING');
  const day = new Date();
  day.setHours(0, 0, 0, 0); day.setDate(day.getDate() - day.getDay());
  const sunday = day.toISOString();
  bytes.fill(0);
  return { churchId, contactId, lineUserId, sunday, testToken: flattened[prefix + 'TestChannelToken'] };
}

/** 啟動固定組件，不用shell與launch profile，原始Host輸出不落檔；等待有界並驗證啟動訊號。 */
async function startHost(root, port) {
  const product = path.join(root, 'SpeechMessageProducts.ChurchReport');
  const assembly = path.join(product, 'bin', 'Release', 'net10.0', 'SpeechMessageProducts.ChurchReport.dll');
  const env = { ...process.env, DOTNET_ENVIRONMENT: 'Development', ASPNETCORE_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: `http://localhost:${port}`, ExceptionNotifications__SendLine: 'false' };
  delete env.SPEECHMESSAGE_R2_WRITE_GOLDEN;
  const child = spawn('dotnet', [assembly], { cwd: product, env, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  let listening = false, rejected = false, closed = false;
  const scan = chunk => { const text = chunk.toString('utf8'); if (text.includes('Now listening on:')) listening = true;
    if (text.includes('Development 測試憑證必須來自 User Secrets')) rejected = true; };
  child.stdout.on('data', scan); child.stderr.on('data', scan);
  const exited = new Promise(resolve => child.once('close', code => { closed = true; resolve(code); }));
  child.once('error', () => { rejected = true; });
  async function stop() {
    if (!closed) child.kill();
    let timer;
    try {
      await Promise.race([exited, new Promise((_, reject) => {
        timer = setTimeout(() => reject(new Error('P6_HOST_DRAIN_TIMEOUT')), 30000);
      })]);
    } finally { clearTimeout(timer); }
  }
  const deadline = Date.now() + 60000;
  while (Date.now() < deadline && !closed && !rejected && !listening)
    await new Promise(resolve => setTimeout(resolve, 200));
  if (!listening || rejected) { await stop(); throw new Error('P6_HOST_START_FAILED'); }
  return { stop, origin: `http://localhost:${port}` };
}

/** 固定commit及工作樹檢查，不讀憑證；起點工作樹存在未提交來源即拒絕。 */
function checkBaseline(root) {
  const head = spawnSync('git', ['-C', root, 'rev-parse', 'HEAD'], { encoding: 'utf8' });
  const status = spawnSync('git', ['-C', root, 'status', '--porcelain'], { encoding: 'utf8' });
  if (head.status !== 0 || !head.stdout.startsWith('9b7ac0a0') || status.status !== 0 || status.stdout.trim())
    throw new Error('P6_BASELINE_CHANGED');
}

/** 只向測試Channel查當月計費推播用量；只回整數、不保存token，不把請求次數當成功送達。 */
async function readLineUsage(token) {
  const response = await fetch('https://api.line.me/v2/bot/message/quota/consumption', {
    headers: { Authorization: 'Bearer ' + token }, signal: AbortSignal.timeout(10000)
  });
  if (!response.ok) throw new Error('P6_LINE_USAGE_UNAVAILABLE');
  const value = await response.json();
  if (!Number.isSafeInteger(value.totalUsage)) throw new Error('P6_LINE_USAGE_INVALID');
  return value.totalUsage;
}

/** 兩輪唯一owner；狀態證據不含秘密，失敗碼固定且finally完成秘密還原後才回報。 */
async function runAcceptance(root) {
  const baseline = path.resolve(root, '..', '1.0.0.11.Integration.P5.Worktree');
  checkBaseline(baseline);
  const settings = await readSettings();
  const credentials = { account: 'P6-' + randomBytes(12).toString('hex'), password: randomBytes(24).toString('base64url') };
  const outputDirectory = path.join(root, 'artifacts', 'p6', 'page-acceptance-' + new Date().toISOString().replaceAll(':', '-'));
  await fs.mkdir(outputDirectory, { recursive: true });
  const bridge = await createFixtureBridge(root);
  const rounds = [];
  let cleanup, failure, secretsPrepared = false;
  try {
    const backup = await bridge.request({ operation: 'prepareSecrets' });
    if (!backup.backupProtected || !backup.backupVerified) throw new Error('P6_BACKUP_UNVERIFIED');
    secretsPrepared = true;
    for (const [label, location] of [['before', baseline], ['after', root]]) {
      const fixture = await createRoundFixture(bridge, settings, credentials);
      let host;
      try {
        host = await startHost(location, 5166);
        const countBefore = await readLineUsage(settings.testToken);
        const pages = await runBrowserRound({ origin: host.origin, fixture, outputDirectory, label });
        const countAfter = await readLineUsage(settings.testToken);
        const written = await readRoundFixture(bridge, fixture);
        const aliases = new Map(Object.entries(fixture.ids).map(([key, id]) => [id, '<' + key + '>']));
        aliases.set(settings.contactId, '<shadow-contact>'); aliases.set(settings.churchId, '<shadow-church>');
        const times = new Set(['new_1_signon_time', 'new_1_sign_on_time', 'new_sunday_first_qr_on_time', 'new_small_group_signing_time']);
        rounds.push(normalize({ pages, written, pushUsageDifference: countAfter - countBefore }, aliases, times));
      } finally { if (host) await host.stop(); }
      const drain = await bridge.request({ operation: 'reset' });
      if (drain.remaining !== 0) throw new Error('P6_FIXTURE_RESIDUE');
    }
  } catch (error) {
    failure = error instanceof Error && /^P6_[A-Z_]+$/.test(error.message) ? error.message : 'P6_ACCEPTANCE_OPERATION_FAILED';
  } finally {
    try { cleanup = await bridge.close(); } catch (error) {
      cleanup = error.cleanup || { cleanup: false, remaining: null, secretsRestored: false };
      failure = 'P6_CLEANUP_OR_SECRET_RESTORE_FAILED';
    }
    credentials.account = ''; credentials.password = ''; settings.lineUserId = ''; settings.testToken = '';
  }
  const comparison = rounds.length === 2 ? compareRounds(rounds[0], rounds[1]) : { equal: false };
  const report = { startedAfterP607: true, secretsPrepared, cleanup, roundsCompleted: rounds.length,
    comparison, failure: failure || null, pushCountVerified: rounds.length === 2 && rounds[0].pushUsageDifference > 0 && rounds[0].pushUsageDifference === rounds[1].pushUsageDifference };
  await fs.writeFile(path.join(outputDirectory, 'result.json'), JSON.stringify(report, null, 2), 'utf8');
  console.log(JSON.stringify({ roundsCompleted: rounds.length, equal: comparison.equal, cleanup,
    failure: failure || null, pushCountVerified: rounds.length === 2 && rounds[0].pushUsageDifference > 0 && rounds[0].pushUsageDifference === rounds[1].pushUsageDifference }));
  if (failure || !comparison.equal) process.exitCode = 1;
  return report;
}
module.exports = { readSettings, startHost, runAcceptance };
if (require.main === module) runAcceptance(path.resolve(__dirname, '../../..')).catch(() => {
  console.log('P6_ACCEPTANCE_PREPARATION_FAILED'); process.exitCode = 1;
});
