# P8-04a 偽語系稽核工具與網頁驗收

**結論：程式與實機稽核完成，但尚未驗收。** 2026-11-22 00:21 Asia/Taipei (UTC+08:00) 以 Development ChurchReport 與 Edge 實際開啟四頁，四頁 HTTP 與偽語系稽核均符合契約；清理完成、User Secrets 還原、Host／Browser 關閉，結果檔 SHA-256 為 `8384439BBCA13AFA6960D7A02CF6DB2AF6F76B9ABBC0638D372A65AC753C7E7A`。

| 必做項目 | 證據 | 狀態 |
|---|---|---|
| P6 共用生命週期 | `node tools/PageAcceptance/P6/Tests/P6Lifecycle.cjs`：4/4 通過 | 通過 |
| 偽語系規則與生命週期測試 | `node --test tools/PseudoLocaleAudit/Tests/PseudoAuditRules.Tests.cjs tools/PseudoLocaleAudit/Tests/PseudoAuditLifecycle.Tests.cjs`：13/13 通過 | 通過 |
| Terms | `artifacts/p8/pseudo-audit-2026-11-22T00-21-55.759Z/result.json`；HTTP 200、未包覆中文 0、截斷 0 | 通過 |
| OfferingCategories | 同上；HTTP 200、未包覆中文 0、截斷 0 | 通過 |
| Audit | 同上；HTTP 200、未包覆中文 0、截斷 0 | 通過 |
| 非管理員 403 | 同上；HTTP 403、未包覆中文 0、截斷 0；qps-ploc 非管理員會友登入後驗證 | 通過 |
| 動態資料處理 | Audit 動態列加 `data-pseudo-locale-exempt="dynamic"`；固定 UI 文案仍檢查 | 通過 |
| 清理與秘密隔離 | 結果只保存狀態／布林摘要、數量、固定路徑與遮罩證據；User Secrets 還原、remaining=0、Host／Browser 關閉成功 | 通過 |
| 文件／進度／差異檢查 | 文件說明 1,899 檔 0 失敗；`git diff --check` 通過 | 通過 |

本單元只使用 goodshepherdback 白名單與 localhost Development，未連正式組織，也沒有對 D365 寫入。頁面語系修正沿用 `P8-03 fix:` 提交 `3c7db3301`；本單元新增稽核工具與動態資料標記。唯一 `C:\smv\guard-p8` 對 `565072ed` 的完整守門於 Release 全方案測試執行至 `SpeechMessage.Hosting.Tests` 約 220 秒仍未結束，手動中止；保存的 guard transcript 沒有 PASS／FAIL 結果，故本單元不標驗收、不宣稱 guard 通過。另一次 Hosting hang diagnostic 亦未取得結果；證據只記錄在 `C:\smv\guard-p8-evidence\guard-565072ed-full.log` 與 `p8-04a-hosting-hang-diagnostic-20261122-0834.log`。P8-03 fix 與 L2 修正仍待後續守門及推送核對。

**後續：** 依 D8-26 可繼續 P8-04b，但 P8-04a 必須在完成唯一 guard 並核對推送後才可標 done；本階段尚未結案。

## P8-04b 教會自訂與 A／B 隔離（現況更正：2026-11-22 11:17 Asia/Taipei (UTC+08:00)）

**目前結論：P8-04b 未驗收。** 離線測試通過；11:52 網頁 A／B 自訂子步成功，含類別新增、停用讀回、覆寫還原與 owner 清理。16.4 第 1 步的本機 Production 組合根拒絕偽語系測試通過，但 OS 子程序實際啟動遭本機執行政策拒絕；16.4 仍未完成，LINE 實機與第 2、3、4、6 步缺證，完整守門尚未通過。

以下 09:14 的結果保留為歷史 A／B 頁面讀回證據。原 2026-11-22 11:17 曾因 `CUSTOMIZATION_CATEGORY_FIND_ROW_FAILED`、`categoryInactive=false` 判定停用未證明；該結論已於 12:04 依 11:52 新摘要更正：`p8-04b-customization-live-20261122-exact-unmapped-role.log` 回報 `aWithin60s=true`、`bUnaffected=true`、`cleaned=true`。執行程式只有在測試類別停用並讀回、原覆寫恢復、Bridge 殘留為 0、Secrets 還原及 owner 關閉都成功時才回報 `cleaned=true`。11:54 唯讀狀態核對符合預期，證據 `p8-04b-post-customization-verify-20261122-1210.log`。此修正只涵蓋網頁自訂子步，不能代替 LINE 或其他 16.4 步驟。

| 必做項目 | 證據 | 狀態 |
|---|---|---|
| A／B 教會 × zh-TW／qps-ploc 交錯 1,000 次 | `P8CustomizationIsolationTests.Isolation_ChurchAB_CultureMixed_NoCrossTalk_ResourcesReturnToBaseline`；快取 scope 上限 2、flight 每輪回 0、Dispose 後回基線 | 通過 |
| 偽語系關鍵字與括號契約 | `Pseudo_Step2_KeywordsRecognized_AllSegmentsBracketed`；使用正式 `GoodShepherdKeywordCatalog` | 通過 |
| 偽語系繳費通知 | `Pseudo_Step3_FeeNotifyWithin60s_PseudoAndFormatted`；正式 `FeeNotificationRule`、qps-ploc／en-US 格式文化，無實機推播 | 通過（離線） |
| LINE 覆寫 A／B 隔離 | `Customization_Step5_LineOverrideWithin60s_ChurchBUnaffected`；正式 `ChurchTextOverrideCache`／`GoodShepherdTextCatalog`，60 秒內發布 | 通過（離線） |
| 固定時鐘兩教會時區 | `Pseudo_Step6_FixedClockTwoChurchTimeZones_SameOutput`；固定 UTC 重跑結果穩定、時區差異明確 | 通過 |
| 網頁自訂腳本與 finally 清理 | 11:52 `p8-04b-customization-live-20261122-exact-unmapped-role.log`：`aWithin60s=true`、`bUnaffected=true`、`cleaned=true`；依當次程式碼，清理成功含類別停用讀回、覆寫還原、Bridge 殘留 0、Secrets 與 owner 清理 | 通過（網頁子步） |
| goodshepherdback 網頁實機：改角色、新增類別、停用類別、A/B 不串 | 11:52 摘要成功；A/B 驗收後由 finally 停用測試類別並回讀、清理完成。11:54 唯讀狀態核對符合預期 | 通過（網頁子步）；不代表 16.4 全部通過 |
| build 22 適用外掛 | `p8-04b-build22-20261122-084938.log`、`p8-04b-build22-20261122-084938-assembly.log`；兩 DLL `Test-PluginAssembly.ps1` 結束碼 0 | 通過 |
| 組織部署狀態 | `p8-04b-plugin-verify-20261122-085351.log` 與 `p8-04b-postlive-verify-20261122-111831.log`：唯讀 verify 符合預期 | 通過；不代表資料清理完成 |
| 16.4 第 1 步 Production 偽語系拒絕 | 既有 `SpeechMessage.Hosting.Tests.StartupGuardsTests.Production_rejects_pseudo_locale_configuration` 1/1；新增 `P8CustomizationIsolationTests.Pseudo_ProductionLineWebhookComposition_RejectsBeforeHostBuild` 1/1，保存於 `C:\smv\guard-p8-evidence\p8-04b-host-composition-and-isolation-20261122-1218.log`。一次性 OS 子程序啟動命令遭本機執行政策拒絕，應用程序未啟動 | 本機組合根測試通過；OS 子程序啟動未驗證，第 1 步未完成 |

本輪只連核准的 goodshepherdback，沒有連正式組織。11:52 A/B 子步摘要與當時 cleanup 實作證明測試類別停用並讀回、角色／範本還原、Secrets 還原及 owner 關閉；`remaining=0` 是 fixture bridge 暫存資料清理結果。16.4 的實機 LINE 步驟仍未執行。原始輸出均留在 `C:\smv\guard-p8-evidence`，不含未遮罩例外本文。

**最新離線驗證摘要（2026-11-22 12:18 Asia/Taipei (UTC+08:00)）：** PseudoAudit rules＋lifecycle 30/30（`p8-04b-pseudo-audit-node-20261122-1220.log`）；P6 lifecycle 4/4（`p8-04b-p6-lifecycle-20261122-1220.log`）；文件說明 1,901 檔零失敗（`p8-04b-file-documentation-20261122-1220.log`）；ProgressPage 28/28（`p8-04b-progress-page-20261122-1220.log`）。這些不代表完整 guard 或 P8-04b 驗收。網頁子步已通過；16.4 第 1 步 OS 子程序啟動未驗證，第 2～4、6 步、LINE 實機及完整守門仍待完成。

### 2026-11-22 11:17 Asia/Taipei (UTC+08:00)：停用診斷與實機失敗（已於 12:04 更正結論）

當時舊瀏覽器流程等待 navigation，無法安全區分 POST rejection 與讀回失敗；改為等待精確的 OfferingCategories/Save POST response、僅記錄固定 HTTP 狀態碼，之後另行 GET 讀回。11:17 的診斷輸出曾回報尋列失敗，因此當時正確地不推定 9.1 拒絕一般 Update。該執行結果已由 11:52 的新操作取代；舊輸出保留不改寫，詳見下方 12:04 更正。

### 2026-11-22 12:04 Asia/Taipei (UTC+08:00)：P8-04b 網頁子步證據更正

較新的 `p8-04b-customization-live-20261122-exact-unmapped-role.log` 回報 `aWithin60s=true`、`bUnaffected=true`、`cleaned=true`；當前 cleanup 契約只有在類別停用後讀回、A 覆寫還原、Bridge 殘留 0、Secrets 還原及 Host／Browser／Bridge 關閉均成功時才會給出成功。`p8-04b-post-customization-verify-20261122-1210.log` 的唯讀狀態核對符合預期。這只確認 08A §4.5 網頁 A／B 自訂子步；LINE 實機及 16.4 第 1～4、6 步仍缺，故 P8-04b 未驗收。

### 2026-11-22 12:18 Asia/Taipei (UTC+08:00)：16.4 第 1 步的安全替代驗證

既有 Production 啟動政策測試 `Production_rejects_pseudo_locale_configuration` Release 1/1；新增的真實 LineWebhook `Program.BuildApplication` Production 組合根案例以合成偽語系設定確認 Host 建置前拒絕，Release 1/1。LineWebhook Release 建置 0 警告／錯誤。一次性 OS 子程序啟動命令遭本機執行政策拒絕，未啟動應用程序；未重試相同命令，因此不能宣稱完成 16.4 第 1 步。組合根案例及 P8CustomizationIsolation 6/6 輸出見 `C:\smv\guard-p8-evidence\p8-04b-host-composition-and-isolation-20261122-1218.log`。

### 2026-11-22 13:25 Asia/Taipei (UTC+08:00)：16.4 第 2 步離線正式收件組合補測

Pseudo_Step2_KeywordsRecognized_AllSegmentsBracketed Release 1/1 通過；真 Kestrel、正式 HMAC 驗簽、有界 queue、dispatcher、入站 letter Upsert、qps-ploc 關鍵字及 Reply HTTP transport 均由測試組合執行，回覆文字逐段含 qps-ploc 括號。測試輸入使用 qps-ploc 對應的 SERVICE、REPORT、?? 關鍵字；主日出席與 Report 依現行契約不產生 Reply，唯一 Help Reply 作為回覆文字斷言。此為離線組合證據，不取代 goodshepherdback LINE 實機。

### 2026-11-22 13:30 Asia/Taipei (UTC+08:00)：P8-04b 最新完整守門結果

以唯一 C:\smv\guard-p8 checkout 2ed809d14 執行 §8.1；Release build 0 錯誤，已保存的 Release 全方案測試輸出中多個測試專案完成且通過，但 SpeechMessage.Hosting.Tests 後續未在時間盒內結束，依前次同類 hang 取證後中止。C:\smv\guard-p8-evidence\guard-2ed809d1-full.log 沒有 PASS 標記，因此本次仍不得宣稱完整守門通過；P8-04b 維持未驗收，未推送本輪提交。guard 工作樹保持乾淨。

### 2026-11-22 15:00 Asia/Taipei (UTC+08:00)：P8-04b 完整守門重跑通過

唯一 C:\smv\guard-p8 對提交 4152847c 的 §8.1 已全部通過：Release build 0 錯誤；Release 全方案測試 0 失敗（Hosting 165、LineWebhook 138 等均保存於守門輸出）；Debug tracer 7/7；文字編碼 256 檔 0 問題；duplicate projects=42、files=1566、new=0、stale=0；I18nCheck、檔案說明 1902/1902、進度頁 28/28 均通過。證據 C:\smv\guard-p8-evidence\guard-f4152847-full.log。這只證明守門，不改變 16.4 LINE 實機缺口；P8-04b 仍未驗收。

### 2026-11-22 16:12 Asia/Taipei (UTC+08:00)：核准測試組織代送實機補證

`SpeechMessageProducts.LineWebhook.Tests.RelayLiveAcceptanceTests.Live_Relay_PushesOnceToTestContact` 以 Release、`Category=P7Live` 在 goodshepherdback 通過 1/1，耗時 2 分 10 秒；保存輸出 `C:\smv\guard-p8-evidence\p8-04b-p7live-relay-20261122.log`。測試實際建立並清理測試收費單與代送待辦，主機 worker 完成一次代送，attempts=0、測試 Channel 用量差為 1，finally 清理 fees=0、relay tasks=0。這證明核准測試組織的主機代送與資源清理路徑；因該既有案例不是 qps-ploc P8 專用案例，不能把它改稱 16.4 第 3 步偽語系通知通過。其後唯讀 `verify --org goodshepherdback` 通過，狀態符合 off，輸出保存於 `C:\smv\guard-p8-evidence\p8-04b-post-p7live-verify-20261122.log`。

目前 P8-04b 必做缺口仍是：P8 專用 qps-ploc 實機通知的格式證據、16.4 第 1 步 OS 子程序啟動、以及第 6 步的正式固定時鐘組合；離線／替代證據已逐項標明，不冒充實機通過。

### 2026-11-22 16:25 Asia/Taipei (UTC+08:00)：P8-04b 指定離線測試重跑

Release 指定測試 7/7 通過：`P8CustomizationIsolationTests` 的 Production 組合根拒絕、A/B 混合隔離 1,000 次、偽語系第 2 步、第 3 步、第 5 步、第 6 步，以及 `PseudoWebhookDispatchAcceptanceTests.Pseudo_Step2_KeywordsRecognized_AllSegmentsBracketed`。原始輸出保存於 `C:\smv\guard-p8-evidence\p8-04b-required-offline-20261122-1625.log`。這是最新離線結果，不改寫仍缺少的 P8 專用 qps-ploc 實機通知證據。

### 2026-11-22 16:32 Asia/Taipei (UTC+08:00)：Production OS 啟動再次確認為環境限制

依既有失敗條件未重試相同 OS 子程序；核准的本機 `Start-Process` 路徑再次在應用程序啟動前被執行政策拒絕，未建立 Host、未連組織、未寫入資料。保存 `C:\smv\guard-p8-evidence\p8-04b-production-host-pseudo-20261122-1632-policy.log`；因此只採用既有真實 LineWebhook 組合根測試，仍不宣稱 OS 層第 1 步完成。

### 2026-11-22 17:05 Asia/Taipei (UTC+08:00)：P8Live 偽語系繳費代送補驗

新增並執行 `RelayLiveAcceptanceTests.Pseudo_Step3_FeeNotifyWithin60s_PseudoAndFormatted`（`Category=P8Live`）兩次；第二次在加強 finally 語系回復後 Release 1/1 通過，耗時 2 分 05 秒。案例只對核准 goodshepherdback 的測試會友建立 qps-ploc 語系租約，建立 `SMV-TEST-P8-Relay-` 收費單與代送待辦，主機 worker 在 60 秒內完成，最後回復會友原語系並清理收費單／待辦；輸出保存於 `C:\smv\guard-p8-evidence\p8-04b-p8live-pseudo-feenotify-20261122-rerun.log`。其後唯讀 `verify --org goodshepherdback` 結束碼 0、狀態符合 off，保存於 `C:\smv\guard-p8-evidence\p8-04b-post-p8live-verify-20261122.log`。

此補驗證明 qps-ploc 收費通知的真實主機代送與語系租約回復；LINE API 不提供收件本文回讀，因此訊息括號、金額與日期格式仍以最新離線 7/7 的正式組合證據判定，沒有把 Channel 用量差冒稱為本文內容證據。P8-04b 尚缺 P8 專用教會覆寫的主機實機步驟，以及 Production OS 子程序啟動證據，維持未驗收。
