/*
 * 檔案整體說明
 * 目的與定位：集中載入 repo 外已安裝的 playwright-core，並以固定 Edge channel 建立頁面驗收 Browser。
 * 主要流程：載入使用者家目錄下既有套件，不下載或安裝瀏覽器；拒絕外部可控 executablePath 與任意 channel，僅將呼叫端核准的 viewport、locale 與 timeout options 傳給 Playwright。
 * 輸入輸出：options 是不含登入狀態的瀏覽器啟動參數，回傳 Browser 交由本次驗收唯一 owner 關閉。此模組不建立或保存 Context、Cookie、Page、Host、SDK、秘密或畫面文字。
 * 資源與隔離：呼叫端負責在 finally 關閉 Browser，並先關閉其 Context；每次呼叫建立新的 Browser 程序，沒有共用 session 或全域快取。啟動失敗由 Playwright 拋錯，上層只能輸出固定錯誤碼。
 * 適用範圍與驗證：供 P6 與 PseudoLocaleAudit 共用 Edge 啟動；以各自生命週期測試注入 Playwright，並以實際 Development Edge 頁面驗收確認瀏覽器相容性。
 */
'use strict';
const os = require('node:os');
const path = require('node:path');

/** 從固定 repo 外安裝位置載入 Playwright，避免引入專案套件或自動下載瀏覽器。 */
function loadPlaywright() {
  return require(path.join(os.homedir(), '.speechmessage', 'playwright', 'node_modules', 'playwright-core'));
}

/** 只啟動已安裝的 Microsoft Edge，不接受呼叫端指定任意執行檔或瀏覽器 channel。 */
async function launchEdge(options = {}) {
  if (Object.hasOwn(options, 'executablePath') || Object.hasOwn(options, 'channel'))
    throw new Error('PAGE_ACCEPTANCE_EDGE_OPTIONS_REJECTED');
  return loadPlaywright().chromium.launch({ ...options, channel: 'msedge' });
}

module.exports = { launchEdge, loadPlaywright };
