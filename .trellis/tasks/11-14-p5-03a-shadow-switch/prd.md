# P5-03a 佈署清單與影子切換

## 目標
依 05A 修訂 1 完成 Deployment 的 manifest v2、活動註冊、舊註冊唯讀匯出、`switch` 與 `shadow` 離線契約；實機只允許測試組織 `goodshepherdback`，且必須先確認 User Secrets 的 `ShadowTestChurchId`／`ShadowTestContactId`，不以名稱猜測。

## 已確認約束
- `switch --to legacy` 先自動 export，先通過 D5-18；一次只啟用一個模組的舊步驟，新步驟必須全停用。
- `switch --to enabled` 只啟用新步驟；本包禁止 Live 模式，所有新步驟必須 Shadow。
- `switch --to off` 必須在成功、失敗、取消後都停用舊／新步驟並還原追蹤設定。
- `shadow` 固定先舊後新兩輪：舊輪建立一批 seed、trigger、observe、刪除、停用；新輪使用另一批同樣情境、Shadow trace parser；兩輪互比且各自與 expected 比對。
- 不碰正式組織、不發 LINE；實機只在 `goodshepherdback`，離線測試不得冒充實機證據。

## 驗收條件
1. manifest v2 可表達多組件、多步驟、images、legacy 與活動註冊，缺欄位／重複 ID／Shadow secure 設定會拒絕。
2. `export-legacy` 只讀輸出舊步驟摘要，不輸出 token、LINE ID、訊息正文或秘密。
3. `switch` 具組織白名單、模式互斥、D5-18 關卡及 finally 還原；任何未允許組織拒絕且零寫入。
4. `shadow` 具 bounded trace、overflow 不可比對、取消／例外還原、seed 清理與雙輪比較契約。
5. 活動第一輪的讀取／註冊流程以合成 SDK 測試驗證；不把 User Secrets 缺鍵補成假 GUID。
6. Release 測試、架構／打包／文件／編碼／進度固定 guard 全通過後，才建立 `P5-03a` 提交；實機前若 Secrets 兩個 ID 仍不存在，停在此處並請使用者提供或確認。

## 不在本包
- 不在任何正式組織啟用步驟。
- 不實作 P5-03b 的 UI 匯出入與完整活動測試流程。
- 不啟動 P5-04 以上模組。
