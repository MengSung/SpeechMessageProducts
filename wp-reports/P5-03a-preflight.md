# P5-03a 前置核對與 F16 停止紀錄

更新時間：2026-11-14 22:47 Asia/Taipei (UTC+08:00)；原前置記錄時間為 21:28。
本頁是進行中的前置證據，不是 P5-03a 或 P5-03 整包驗收回報。

## 已完成的本地提交

- `419f24a9 P5-02 fix:`：出席基準更正為 `reading`，修正 D5-18 的實際建構式路徑，登錄 P7-02 補驗；沒有重新驗收 P5-02。
- `a6eed0d2 P5-03a`：依權威欄位型別轉換 SDK scalar，拒絕數字精度損失。
- `02cbb811 P5-03a`：`reading`、`legacy-golden`、`new` 只執行新輪，舊側不可比較；`legacy-live-only` 仍真正執行舊輪。
- `9de66ceb P5-03a`：唯讀 metadata adapter，核對正式主鍵、欄位存在與 Create／Update 可寫性。
- `f5c294be P5-03a`：新輪同時檢查最終欄值及完整意圖，防止額外欄位或中間寫入被最終值掩蓋。
- `dbb9dd6b P5-03a`：新增 CRUD 情境 factory，接線 metadata、核定共享別名、owned lookup 與安全 expected 投影；19 項新測試含真正 runtime 清理，未支援種類明確拒絕。
- `0323f4cd P5-03a`：修正 ActivityFeeds 的 Microsoft 來源分類；兩個已查證完整組件名同時具公開簽章識別及 managed=true 才排除，同名偽裝／缺值仍保留。

最近已驗證離線結果：Deployment Release **701／701**；檔案說明 **1562 檔、0 失敗**；本次各提交的 Changed 位元組檢查 **0 問題**。
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

2026-11-14 21:48～21:52 Asia/Taipei (UTC+08:00)：唯讀核對兩個組件的
`publickeytoken=31bf3856ad364e35`、`ismanaged=true`，兩者均有 managed
`ActivityFeedsCore` 的正式方案關聯，其發布者為 `microsoftdynamics`、前綴 `msdyn`。
只讀 metadata，業務與註冊寫入皆為 0；證據
`activityfeeds-publisher-20261114-214849.json`，
SHA-256 `705E589882575053B0F910C90A00496DB55F1DBF481C93E5AEDAFFF59E393FD7`。
八項分類回歸測試包含兩項先紅後綠及六項保守保留，修正後 CLI 唯讀匯出回 0：
31 個舊組件、18 個舊步驟，啟用步驟 **0**、啟用流程 **0**。
證據 `legacy-20261114-135228-5435182-feeac1853b094aa99e35c50a8b9455a6.json`，
SHA-256 `4AB26B858FA9B89CE64E8A997102529CDBE9D9CD2FB2DCEFAFC4E46519BCF812`。
兩份證據都只在本機上述 artifacts 目錄；39 個 ActivityFeeds 步驟完全沒有啟停。
F16 為分類誤判已釐清，撤回使用者處置需求；不需要使用者停用任何平台步驟。


## 2026-11-14 23:02～23:16 Asia/Taipei (UTC+08:00)：diagnostics 實機回歸嘗試

- 只讀 `export-legacy --org goodshepherdback` 回報啟用舊步驟 0、啟用舊流程 0，產生本機 ignored 證據 `legacy-20261114-150221-4580240-6d259a66744b4d81ae50394823156765.json`；沒有舊步驟或舊流程被變更。
- 依固定順序執行 `switch --to enabled` → `smoke --scenario p0-ping-trace`；新版 step 啟用成功，但 smoke 在三分鐘有界等待後回報失敗，沒有把結果寫成通過，也沒有輸出追蹤內容。
- 以唯讀查詢觀察本次 task 的 async operation：`statecode=3`、`statuscode=30`、`operationtype=1`，沒有 `plugintracelog` 列，錯誤碼與安全可讀訊息欄位均未提供內容；前次把此狀態誤判為 Plugin async 執行失敗；更正：30 是 Succeeded，只能確認 smoke 未取得追蹤。
- 立即以精確 `[smoke]` 前綴刪除本次殘留 task，再執行 `switch --to off` → `verify --org goodshepherdback`，兩者成功；step 已回到停用、verify 符合清單。diagnostics 實機驗收仍未完成，需查明成功作業未取得追蹤的原因，不能據此要求重啟服務。

## 後續未完成

- 受控 legacy switch 與 live 基準的受審 token 欄位盤點接線、推播意圖、外部修改摘要、映像接線與整合驗證；同步追蹤 RequestId 已離線接通，仍須實機查證。
- 完整固定 guard 與 diagnostics 實機驗收（已執行但 smoke 未通過，不能標驗收）。
- P5-03b 及 P5-03 整包驗收。
- 原訂 2026-11-14 23:30 Asia/Taipei (UTC+08:00) 驗收估計不再承諾；CLI 與離線契約仍未完成，完成後再估實機與整包驗收時間。
- P5-02 工具修正的三小時時限仍未開始；維持 P5-03 驗收後、P5-05 前執行。

## 2026-11-14 22:33 Asia/Taipei (UTC+08:00)：真 CLI 與同步追蹤接線

- `35448977`、`edc7c060`：完整 CLR 型別名不再誤判成 JWT；獨立掃描點分字串內的 LINE ID／長憑證，四項 reader 新測試包含先紅後綠。
- `3b88dd97`：公開 `shadow --scenario-root` 使用固定模組黃金目錄、既有同組織寫鎖及單一 lease。new／reading／legacy-golden 新輪串接身分、部署 PE／註冊比對、metadata、factory、runtime、runner 與報告；live 基準尚無受審盤點接線時拒絕，不靜默跳過舊輪。
- `247bdfe2`：正式同步 mode 改送獨立 RequestId，查詢同 requestid／pluginstepid／typename／mode／時間窗的唯一追蹤；不等待 asyncoperation。18 項真 CLI 測試涵蓋同步歧義、同步／非同步送達後取消、失敗清理、秘密 ID 錯配、註冊漂移、寫鎖及同日報告歷史。
- 本次離線全套 648 項通過、0 略過／失敗；`C:\smv\p5-03a-final-local.log` SHA-256 `EA25C0A63A5CB062091CED3920B5936F3DBC0CFA9323CEECA1B0329BA46B4E85`。文件 1560 檔零失敗，個別提交編碼零問題；不是整包固定 guard 或實機驗收。
- 22:32 唯讀 ls-remote 仍為 `a653de0887d96c51de280ad06192dfa0dd42c68a`；本次沒有推送或實機寫入，沒有等待使用者的事項。
- StorLesson 只讀重核：csproj 明列 ProjectReference 到同份 LineUtility；四處呼叫仍是兩參數，但現存 LineUtilityClass.cs 第 100 行只有三參數建構式。來源版本不一致仍未證明實際 DLL 的後備路徑，不能憑兩參數呼叫宣稱已確認寫死 token，亦不可未核對就執行舊輪。

## 2026-11-14 22:47 Asia/Taipei (UTC+08:00)：關聯觀察、動態身分及清理

- `6ac56760`：reader／metadata／factory／CLI／runtime／runner 的 related lookup 完整接線；僅接受 owned 父 seed，Create 期望核對相同 lookup，trigger 後再讀取動態別名。Create 後 Delete 仍比較完整意圖；同 selector 多列拒絕配對，清理保留全部有界子列。
- `721dd5c5`：子列回應分頁、錯父 lookup、重複 ID 或刪除失敗，保留父列及失敗狀態；解除合成故障後可重試排空，不刪不同父 ID 的既有列。
- 新增 26 項離線測試；完整 Deployment Release 674／674、0 略過／失敗。紀錄 `C:\smv\p5-03a-related-cleanup-full.log`，SHA-256 `D64B0C2BAEEB79B96D80151B68D1F9F4E1F2B6DEDE68F0FF315CE378F5020343`。
- 這些是合成 SDK 證據；沒有實機寫入、步驟啟停或推送，整包驗收與固定 guard 仍待完成。

## 2026-11-14 23:53 Asia/Taipei (UTC+08:00)：diagnostics 未取得追蹤證據補記（狀態判讀已更正）

本次重試確認遠端匯出組件內容的 SHA-256 與本機 `artifacts/plugins/SpeechMessage.Dynamics.Plugins.dll` 相同；清單中的 diagnostics step 仍是同一個 step ID、PostOperation／Asynchronous、trace 設定。async operation 狀態 30 為成功但沒有 trace（原失敗判讀撤回），不能以資料庫 DLL hash 取代執行證據，也不把失敗歸因於特定快取或服務元件；目前保留為 P5-03a 未完成項。

## 2026-11-15 02:20 Asia/Taipei (UTC+08:00)：成功狀態更正與追蹤設定實測

2026-11-15 02:20 Asia/Taipei (UTC+08:00)：P5-03a續作；更正前次判讀：async statecode=3／statuscode=30為成功，不能寫成Plugin執行失敗。唯讀確認原追蹤為Exceptions(1)；本輪暫改All(2)並讀回，再執行enabled→smoke→off→verify。smoke三分鐘未取得trace回1，off及verify回0，原追蹤1已還原，同一步驟停用、測試task為0；時間窗有1筆該步驟成功作業、trace為0，尚未證明job殘留為0。未證實服務故障，撤回要求使用者重啟的判斷，不再盲目重試。離線最近已驗證684/684；2f946479已接通受審token舊輪shadow CLI，獨立legacy switch、推播比較／外部修改摘要與其餘接線待完成。P5-03未驗收、未推送，完整固定guard未跑；驗收時間待剩餘接線及追蹤原因釐清後重估。

官方狀態對照：Microsoft Learn AsyncOperation EntityType 的 statuscode 明列 30=Succeeded、31=Failed；plugintracelog 文件說明 Exceptions(1) 僅在例外時產生追蹤，All(2) 包含成功執行。這只更正狀態語意，不能證明本輪沒有追蹤的唯一原因。

## 2026-11-15 02:26 Asia/Taipei (UTC+08:00)：推播比較核心

2026-11-15 02:26 Asia/Taipei (UTC+08:00)：P5-03a續作，45813c77補上runner的推播完整意圖比較；新增3案先紅後綠，涵蓋吻合、內容摘要差異及缺少推播，欄位三方相同仍不得掩蓋推播差異。收件者及Retry Key僅依明確ID別名正規化；Deployment Release687/687、文件1562檔零失敗、變更編碼零問題。CLI情境推播接線、獨立受控legacy switch、外部修改摘要等尚待完成。diagnostics本輪在All(2)下仍無trace，smoke回1，off及verify回0，同一步驟停用、原追蹤1還原、測試task為0；async狀態30為成功，未證實服務故障，不要求使用者重啟，job零殘留尚未證明。P5-03未驗收、未推送，完整固定guard未跑；P5-02工具三小時修正尚未開始。


## 2026-11-15 02:39 Asia/Taipei (UTC+08:00)：推播期望 CLI 接線

2026-11-15 02:39 Asia/Taipei (UTC+08:00)：P5-03a續作，b8953380接通expected推播摘要至reader／factory／真CLI；新增14項離線測試，含內容差異、缺摘要／錯收件者／不受控Retry Key拒絕及清理。修正Push中繼Entity須為空的parser一致性，保留嚴格驗證。Deployment Release701/701、文件1562檔零失敗、變更編碼零問題。獨立受控legacy switch、外部修改摘要、映像接線及同步實機識別仍待完成。diagnostics在All(2)下未取得trace的既有結果不變；未再實機重試。P5-03未驗收、未推送，完整固定guard未跑；驗收時間待接線及追蹤原因釐清後重估，沒有等待使用者的事項；P5-02工具三小時修正尚未開始。
