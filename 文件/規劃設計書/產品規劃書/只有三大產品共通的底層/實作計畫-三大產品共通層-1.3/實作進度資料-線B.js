/* 檔案整體說明
目的與定位：三大產品共通層實作計畫 1.3 的進度資料（線 B），只供同資料夾的「實作進度.html」以 script 標籤讀取顯示，不是程式、不含秘密。
主要功能與流程：記錄線 B 各工作包（P0 的線 B 項目、F10、F2、F6、F4、F8、F12、F11）的狀態、開始與完成時間、提交 SHA、待補與報告路徑，以及逐筆進度紀錄；每包寫驗收報告的那個提交同時更新本檔。
使用邊界與維護：只有線 B 可以改本檔，線 A 改自己的「實作進度資料-線A.js」，兩條線都不改「實作進度.html」。狀態只能填 未開始／進行中／完成（自動測試通過）／完成（測試環境驗證），縮減時在後面加「（縮減）」，與 90 清冊一致。log 每筆以「YYYY-MM-DD HH:mm Asia/Taipei (UTC+08:00)｜」開頭，新的放最前面，舊紀錄不刪；事件時間不明時寫「補記 … （原事件時間未記錄）」。不得寫入 token、密碼、LINE userId 或個資。改完以 UTF-8 無 BOM、CRLF 存檔，並用瀏覽器開啟實作進度.html 確認沒有紅色警告。
*/
'use strict';
window.PROGRESS_LANES = window.PROGRESS_LANES || {};
window.PROGRESS_LANES.B = {
  lane: 'B',
  lastUpdated: '2026-11-25T22:15:00+08:00',
  packages: {
    P0: { status: '進行中', started: '2026-11-25 22:12', finished: '', commit: '', pending: '', report: '' },
    F10: { status: '未開始', started: '', finished: '', commit: '', pending: '', report: '' },
    F2: { status: '未開始', started: '', finished: '', commit: '', pending: '', report: '' },
    F6: { status: '未開始', started: '', finished: '', commit: '', pending: '', report: '' },
    F4: { status: '未開始', started: '', finished: '', commit: '', pending: '', report: '' },
    F8: { status: '未開始', started: '', finished: '', commit: '', pending: '', report: '' },
    F12: { status: '未開始', started: '', finished: '', commit: '', pending: '', report: '' },
    F11: { status: '未開始', started: '', finished: '', commit: '', pending: '', report: '' },
  },
  log: [
    '2026-11-25 22:12 Asia/Taipei (UTC+08:00)｜P0（P0-2④ 平台 SQL、P0-3 測試網址、P0-6 測試資料規則）開工，先做 02 §6 的開工檢查。來源：線 B 狀態檔；由 Claude 於 2026-11-25 22:15 建立本檔時寫入。',
    '2026-11-25 22:01 Asia/Taipei (UTC+08:00)｜線 B 對話開始，等待線 A 建立 cl-lane-b。來源：Codex 線 B 紀錄。',
  ],
};
