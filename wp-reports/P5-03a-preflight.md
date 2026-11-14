# P5-03a 前置核對與 F16 停止紀錄

更新時間：2026-11-14 21:46 Asia/Taipei (UTC+08:00)；原前置記錄時間為 21:28。
本頁是進行中的前置證據，不是 P5-03a 或 P5-03 整包驗收回報。

## 已完成的本地提交

- `419f24a9 P5-02 fix:`：出席基準更正為 `reading`，修正 D5-18 的實際建構式路徑，登錄 P7-02 補驗；沒有重新驗收 P5-02。
- `a6eed0d2 P5-03a`：依權威欄位型別轉換 SDK scalar，拒絕數字精度損失。
- `02cbb811 P5-03a`：`reading`、`legacy-golden`、`new` 只執行新輪，舊側不可比較；`legacy-live-only` 仍真正執行舊輪。
- `9de66ceb P5-03a`：唯讀 metadata adapter，核對正式主鍵、欄位存在與 Create／Update 可寫性。
- `f5c294be P5-03a`：新輪同時檢查最終欄值及完整意圖，防止額外欄位或中間寫入被最終值掩蓋。
- `dbb9dd6b P5-03a`：新增 CRUD 情境 factory，接線 metadata、核定共享別名、owned lookup 與安全 expected 投影；19 項新測試含真正 runtime 清理，未支援種類明確拒絕。

最新離線結果：Deployment Release **618／618**；檔案說明 **1558 檔、0 失敗**；本次兩個新 C# 檔的 Changed 位元組檢查 **0 問題**。
這些不是完整固定 guard 或實機影子驗收結果。

## Shadow 測試身分

2026-11-14 21:27 Asia/Taipei (UTC+08:00)：以本程序 User Secrets 的測試 LINE ID，
呼叫 `ShadowTestIdentity.Discover` 及 `Verify`，唯讀確認：

1. 伺服器組織是唯一白名單 `goodshepherdback`。
2. D34 非空 LINE 綁定只有一筆，且精確測試 LINE ID 查到同一個 contact。
3. contact 的 `parentcustomerid` 是實際存在的 account；沒有按顯示名稱猜測。
4. 核對成功後，將正式 GUID 分別存入 User Secrets 的 `ShadowTestChurchId`、
   `ShadowTestContactId`；不在 repo、輸出或本頁保存 GUID／LINE ID／token。

本步驟沒有寫入任何 Dataverse 業務資料，也沒有啟停步驟。

## F16 實際停止點

2026-11-14 21:27:52 Asia/Taipei (UTC+08:00)：正式 Deployment CLI 執行：

```powershell
dotnet run --project SpeechMessage.Dynamics.Deployment/SpeechMessage.Dynamics.Deployment.csproj -c Release --no-build -- export-legacy --org goodshepherdback
```

結果：

| 項目 | 實際結果 |
| --- | --- |
| 啟用舊步驟 | 39 |
| 啟用舊流程 | 0 |
| 程序結束碼 | 1 |
| 後續操作 | 依 05A F16 停止並回報，未自行停用或啟用 |

本機原始證據（不提交版控）：
`artifacts/d365/goodshepherdback/legacy-20261114-132752-4072733-f728f56ad99148a7b1539b61d9865f04.json`。
SHA-256：
`635779F2261D72C883F58FF4F003CBD7B8445E471086F170E5BD9AE9371FDD95`。

本次沒有執行 diagnostics enabled／smoke／off、沒有執行任何舊輪、新輪或推送；
未宣稱遠端步驟皆停用、`verify=0` 或實機殘留為零。原因及這些步驟的處置須先釐清，
不根據較早的前置摘要推定目前狀態，也不推測啟用者或啟用時間。

2026-11-14 21:46 Asia/Taipei (UTC+08:00)：只離線讀取上述同一份匯出，依正式 type／assembly lookup 分組，
39 個啟用步驟全屬 `ActivityFeeds.Plugins`（29）及 `ActivityFeeds.Filtering.Plugins`（10）。
其他匯出組件的啟用步驟為 0；尚無發布者／簽章證據可確認這兩個應排除。
現行匯出器僅依 `Microsoft.`／`SpeechMessage.` 前綴分類，因此暫不改分類、不再跑實機或自行停用。
21:45 唯讀 `git ls-remote` 查得遠端為 `a653de0887d96c51de280ad06192dfa0dd42c68a`；
本輪未推送，不能把它描述為本包已驗收或遠端等於本機。

## 後續未完成

- shadow CLI 與受控 legacy switch 接線、同步追蹤識別、關聯 observe、新增列及推播意圖、實機映像一致性與整合驗證。
- F16 處置後的固定 guard 與 diagnostics 實機驗收。
- P5-03b 及 P5-03 整包驗收。
- 原訂 2026-11-14 23:30 Asia/Taipei (UTC+08:00) 驗收估計暫停，待 F16 狀態釐清重估。
- P5-02 工具修正的三小時時限仍未開始；維持 P5-03 驗收後、P5-05 前執行。
