# 三產品共同底層與 D365 遠端佈建討論文件

## 需求與授權
沿用本對話已授權的三產品規劃；本輪只研究、討論、建立 HTML 圖解與報告，不修改產品程式、不連線 VM／D365／LINE、不建立 schema。
使用者新事實：goodshepherd20 從 goodshepherdback clone；construction 好管家；association 好學會。截圖顯示三組織啟用及版本 9.1.49.13。三個 LINE OA 已建立，但 Messaging API／Provider／Channel ID／Webhook 尚無證據。

## 驗收
- 明確回答先做最小共用基礎與產品逐功能的順序。
- 區分共用程式、每組織共用 schema 副本、獨立設定與資料、平台技術儲存。
- 證明既有 schema 引擎與白名單限制，指出原 manifest 含教會專屬欄位。
- 解釋 WinRM→部署程式→受支援 SDK metadata 的角色、權限與 second hop。
- 提供 clone 檢查、schema 候選、逐組織 plan/apply/verify、回復與驗收邊界。
- HTML 可離線、桌面與手機可讀；報告有事實來源、建議與待決策項。

## 待決策
三個組織是開發／測試或預定 Pilot 正式？本輪不依此連線，待下一輪回答。
