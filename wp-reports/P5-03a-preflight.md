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

最新離線結果：Deployment Release **674／674**；檔案說明 **1560 檔、0 失敗**；本次各提交的 Changed 位元組檢查 **0 問題**。
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

## 後續未完成

- 受控 legacy switch 與 live 基準的受審 token 欄位盤點接線、推播意圖、外部修改摘要、映像接線與整合驗證；同步追蹤 RequestId 已離線接通，仍須實機查證。
- 完整固定 guard 與 diagnostics 實機驗收（F16 誤分類已解除，但本次仍未執行）。
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
