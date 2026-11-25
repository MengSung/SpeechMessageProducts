/* 檔案整體說明
目的與定位：三大產品共通層實作計畫 1.3 的進度資料（線 A），只供同資料夾的「實作進度.html」以 script 標籤讀取顯示，不是程式、不含秘密。
主要功能與流程：記錄線 A 各工作包（F0、F1、FS、F3、F5、F7、F9、M1）的狀態、開始與完成時間、提交 SHA、待補與報告路徑，以及逐筆進度紀錄；每包寫驗收報告的那個提交同時更新本檔。
使用邊界與維護：只有線 A 可以改本檔，線 B 改自己的「實作進度資料-線B.js」，兩條線都不改「實作進度.html」。狀態只能填 未開始／進行中／完成（自動測試通過）／完成（測試環境驗證），縮減時在後面加「（縮減）」，與 90 清冊一致。log 每筆以「YYYY-MM-DD HH:mm Asia/Taipei (UTC+08:00)｜」開頭，新的放最前面，舊紀錄不刪；事件時間不明時寫「補記 … （原事件時間未記錄）」。不得寫入 token、密碼、LINE userId 或個資。改完以 UTF-8 無 BOM、CRLF 存檔，並用瀏覽器開啟實作進度.html 確認沒有紅色警告。
*/
'use strict';
window.PROGRESS_LANES = window.PROGRESS_LANES || {};
window.PROGRESS_LANES.A = {
  lane: 'A',
  lastUpdated: '2026-11-25T22:15:00+08:00',
  packages: {
    F0: { status: '進行中', started: '2026-11-25 22:09', finished: '', commit: '', pending: '', report: '' },
    F1: { status: '未開始', started: '', finished: '', commit: '', pending: '', report: '' },
    FS: { status: '未開始', started: '', finished: '', commit: '', pending: '', report: '' },
    F3: { status: '未開始', started: '', finished: '', commit: '', pending: '', report: '' },
    F5: { status: '未開始', started: '', finished: '', commit: '', pending: '', report: '' },
    F7: { status: '未開始', started: '', finished: '', commit: '', pending: '', report: '' },
    F9: { status: '未開始', started: '', finished: '', commit: '', pending: '', report: '' },
    M1: { status: '未開始', started: '', finished: '', commit: '', pending: '', report: '' },
  },
  log: [
    '2026-11-25 22:09 Asia/Taipei (UTC+08:00)｜F0 開工（本包起點 e3518961），先做 Step 0 測試組織連線設定。來源：線 A 狀態檔；由 Claude 於 2026-11-25 22:15 建立本檔時寫入。',
    '2026-11-25 22:05 Asia/Taipei (UTC+08:00)｜建立工作樹 cl-lane-a、cl-lane-b（都從 e3518961）。來源：Codex 線 A 紀錄。',
    '2026-11-25 22:04 Asia/Taipei (UTC+08:00)｜開工切換：AGENTS.md 現行依據改指共通層 1.3，提交 e3518961 並推送；遠端與本機 SHA 一致。來源：git 提交紀錄。',
  ],
};
