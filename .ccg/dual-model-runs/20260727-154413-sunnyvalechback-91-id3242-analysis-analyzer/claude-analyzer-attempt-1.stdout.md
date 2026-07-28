分析報告已完成並存檔至 `.ccg/dual-model-runs/20260727-154413-sunnyvalechback-91-id3242-analysis-analyzer/claude-analyzer-attempt-1.stdout.md`，與既有的 Gemini 輸出並列保存。以下是完整報告內容：

---

## Root Cause Ranking（已用原始碼逐行驗證，非僅採信證據文件）

1. **【Primary，已驗證】網域前綴與目標 ADFS 場不匹配**：`Startup.cs:302-349` 只讀取 `ServerUrl`/`Username`/`Password`，`CrmConnection:Domain` 完全未被使用；`CrmConnectionService.CreateOnPremiseClient` 的簽章也沒有 domain 參數；`OnPremiseClient.cs:111,171-252` 把 `Username` 字串原樣送進 WS-Trust UsernameToken 交換。實際送到 `adfsdev91`（DYNAMICS-365 網域）的是 `SPEECHMESSAGE\Administrator`，網域不符是唯一與程式碼行為一致的直接病因。
2. **【已驗證但非直接病因】`Domain` 欄位是死配置** — 容易誤導操作者以為改了它就有效。
3. **【已驗證，非本次症狀但會擋住後續驗證】`DynamicsAccess` 區塊仍為 jesus 8.2 殘留設定**（ProfileAlias、CeVersion、Embedded.* 全數未切換）。
4. **【推論，需 ADFS 側確認】** 即使改對網域前綴，該帳號是否確實存在於 `adfsdev91` 目錄、密碼是否相同、Relying Party Trust 是否已核發，無法從程式碼或公開 WSDL 確認。

## 建議的最小修復
**僅做配置修正**：將 `CrmConnection:Username` 改為該環境接受的格式（`DYNAMICS-365\<帳號>` 或對應 UPN）。**不建議**修改程式碼讓其自動組合 Domain+Username — 因為現有 Username 已含網域前綴，程式碼需先偵測並剝除，容易產生雙重網域字串的新 bug，且會波及仍運作正常的 jesus/jesusback 環境。

## 拒絕的替代方案
- 字串盲目串接 `Domain\Username`（未檢查既有前綴）
- 為除錯把密碼/完整帳號印出或寫入本報告
- 捕捉 `ID3242` 後靜默 fallback 到匿名/預設憑證（掩蓋錯誤、安全性退化）

## 需額外對齊的一項（Gemini 報告未列出）
`DynamicsAccess.Embedded.ClientId` 目前仍是 jesus 在 `speechmessagests` ADFS 場註冊的識別碼。ADFS 應用程式註冊通常綁定單一 ADFS 場，沿用此值到 `adfsdev91` 未經確認就是後續 9.1 OAuth 驗證的潛在失敗點。

## Findings
- 🔴 **Critical**：`appsettings.json` 的 `CrmConnection.Username` 網域前綴與 sunnyvalechback ADFS 場不符，且 `Domain` 欄位對此路徑無效——這是 ID3242 的直接成因。
- ⚠️ **Warning**：`DynamicsAccess` 區塊（含新發現的 `ClientId`）仍為 jesus 8.2 殘留值；`CrmConnection.Password` 明文儲存於進版控制檔案中（既有狀態，建議另案處理，非本次必要條件）。
- ℹ️ **Info**：缺少啟動期「網域前綴 vs Domain 一致性」檢查與遮罩化 WhoAmI fail-fast 探測，建議新增但僅記錄遮罩後前綴與例外型別。

## 驗證順序（避免混淆後端服務憑證與網頁會員帳密）
Stage 0 靜態一致性檢查 → Stage 1 **僅**呼叫 `ValidateConnection`（WhoAmI，非瀏覽器）確認 CRM 服務帳號可用 → Stage 2 才用會員帳號 `zz` 走網頁登入 → Stage 3（選擇性）在 Package01 仍關閉的前提下用診斷工具驗證 9.1 OAuth，不透過切換生產旗標測試。

全程未讀取、推論或輸出任何實際密碼或完整帳號值。與 Gemini 分析的根因判斷一致，本報告額外提供了三層呼叫鏈的原始碼證據與 `ClientId` 風險項。

---
SESSION_ID: 3dc31e19-d5d7-474b-b0e4-960a7643d72d
