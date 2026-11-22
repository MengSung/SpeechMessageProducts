/*
 * 檔案整體說明
 * 目的與定位：提供頁面驗收共用的 Development 組態載入及 localhost Host owner，供 P6 與 P8 工具使用。
 * 主要流程：只讀取固定 User Secrets 檔與 goodshepherdback 測試識別，驗證大小及必要格式後把秘密限制在本次記憶體；以無 shell 子程序啟動既有 ChurchReport Release 組件，將通知外送關閉，只掃描固定啟動訊號並丟棄原始輸出。
 * 輸入輸出：呼叫端提供工作樹根目錄及本機連接埠；回傳測試識別、固定日期與 Host stop owner。任何原始設定值、例外本文及 Host 輸出均不寫入檔案或終端。
 * 資源與隔離：每次呼叫獨立讀取設定與建立子程序，不保留跨案例狀態。呼叫端是設定物件的唯一使用者，應在 fixture 清理後清空其可變秘密欄位；Host owner 負責有界等待程序結束。啟動失敗時先關閉子程序再回固定錯誤，未知退出狀態不能視為已停止。
 * 適用範圍與驗證：只允許本機 Development ChurchReport，組織必須由部署 User Secrets 白名單固定為 goodshepherdback；不負責 SDK 記錄清理或秘密檔改寫。以 P6Lifecycle 與 PseudoAudit lifecycle 測試檢查失敗清理，實際頁面通過另須保存瀏覽器證據。
 */
'use strict';
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const { spawn } = require('node:child_process');

/** 以固定鍵讀取有限 User Secrets，不輸出值；回傳物件只供當輪 owner 使用。 */
async function readSettings() {
  const filename = path.join(process.env.APPDATA || path.join(os.homedir(), 'AppData', 'Roaming'),
    'Microsoft', 'UserSecrets', 'SpeechMessage.Dynamics.Deployment.P0', 'secrets.json');
  const bytes = await fs.readFile(filename);
  if (bytes.length > 1024 * 1024) throw new Error('P6_SECRETS_BOUND');
  try {
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
        || typeof lineUserId !== 'string' || !lineUserId
        || typeof flattened[prefix + 'TestChannelToken'] !== 'string')
      throw new Error('P6_TEST_IDENTITY_MISSING');
    const day = new Date();
    day.setHours(0, 0, 0, 0);
    day.setDate(day.getDate() - day.getDay());
    return { churchId, contactId, lineUserId, sunday: day.toISOString(),
      testToken: flattened[prefix + 'TestChannelToken'] };
  } catch (error) {
    if (error?.message?.startsWith('P6_')) throw error;
    throw new Error('P6_SECRETS_INVALID');
  } finally {
    bytes.fill(0);
  }
}

/** 建立單次測試 Host 環境；除明確要求外，不覆寫既有啟動設定。 */
function buildDevelopmentHostEnvironment(port, {
  allowPseudoLocale = false,
  enableChurchOverrides = false
} = {}, inherited = process.env) {
  const env = { ...inherited,
    DOTNET_ENVIRONMENT: 'Development',
    ASPNETCORE_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: `http://localhost:${port}`,
    ExceptionNotifications__SendLine: 'false',
    Localization__AllowPseudoLocale: allowPseudoLocale ? 'true' : 'false'
  };
  if (enableChurchOverrides) env.Localization__ChurchOverrides__Enabled = 'true';
  delete env.SPEECHMESSAGE_R2_WRITE_GOLDEN;
  return env;
}

/** 啟動固定 Development 組件；輸出只用於辨識成功啟動，不保留原始主機文字。 */
async function startHost(root, port, { allowPseudoLocale = false, enableChurchOverrides = false } = {}) {
  if (!Number.isInteger(port) || port < 1024 || port > 65535) throw new Error('P6_HOST_PORT_INVALID');
  const product = path.join(root, 'SpeechMessageProducts.ChurchReport');
  const assembly = path.join(product, 'bin', 'Release', 'net10.0', 'SpeechMessageProducts.ChurchReport.dll');
  const env = buildDevelopmentHostEnvironment(port, { allowPseudoLocale, enableChurchOverrides });
  const child = spawn('dotnet', [assembly], { cwd: product, env, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  let listening = false, rejected = false, closed = false, exitCode = null;
  const scan = chunk => {
    const text = chunk.toString('utf8');
    if (text.includes('Now listening on:')) listening = true;
    if (text.includes('Development 測試憑證必須來自 User Secrets')) rejected = true;
  };
  child.stdout.on('data', scan);
  child.stderr.on('data', scan);
  const exited = new Promise(resolve => child.once('close', code => {
    closed = true;
    exitCode = code;
    resolve(code);
  }));
  child.once('error', () => { rejected = true; });
  async function stop() {
    if (!closed) child.kill();
    let timer;
    try {
      await Promise.race([exited, new Promise((_, reject) => {
        timer = setTimeout(() => reject(new Error('P6_HOST_DRAIN_TIMEOUT')), 30000);
      })]);
    } finally { clearTimeout(timer); }
    if (!closed) throw new Error('P6_HOST_DRAIN_UNCONFIRMED');
    return { stopped: true, exitCode };
  }
  const deadline = Date.now() + 60000;
  while (Date.now() < deadline && !closed && !rejected && !listening)
    await new Promise(resolve => setTimeout(resolve, 200));
  if (!listening || rejected) {
    await stop();
    throw new Error('P6_HOST_START_FAILED');
  }
  return { stop, origin: `http://localhost:${port}` };
}

module.exports = { buildDevelopmentHostEnvironment, readSettings, startHost };
