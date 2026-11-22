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

## P8-04b 教會自訂與 A／B 隔離（2026-11-22 09:14 Asia/Taipei (UTC+08:00)）

**目前結論：離線必做與網頁 A／B 教會自訂實機驗收通過；P8-04b 尚未驗收，LINE／其餘 16.4 步驟及完整守門仍未完成。**

| 必做項目 | 證據 | 狀態 |
|---|---|---|
| A／B 教會 × zh-TW／qps-ploc 交錯 1,000 次 | `P8CustomizationIsolationTests.Isolation_ChurchAB_CultureMixed_NoCrossTalk_ResourcesReturnToBaseline`；快取 scope 上限 2、flight 每輪回 0、Dispose 後回基線 | 通過 |
| 偽語系關鍵字與括號契約 | `Pseudo_Step2_KeywordsRecognized_AllSegmentsBracketed`；使用正式 `GoodShepherdKeywordCatalog` | 通過 |
| 偽語系繳費通知 | `Pseudo_Step3_FeeNotifyWithin60s_PseudoAndFormatted`；正式 `FeeNotificationRule`、qps-ploc／en-US 格式文化，無實機推播 | 通過（離線） |
| LINE 覆寫 A／B 隔離 | `Customization_Step5_LineOverrideWithin60s_ChurchBUnaffected`；正式 `ChurchTextOverrideCache`／`GoodShepherdTextCatalog`，60 秒內發布 | 通過（離線） |
| 固定時鐘兩教會時區 | `Pseudo_Step6_FixedClockTwoChurchTimeZones_SameOutput`；固定 UTC 重跑結果穩定、時區差異明確 | 通過 |
| 網頁自訂腳本與 finally 清理 | `tools/PseudoLocaleAudit/Run-CustomizationCheck.cjs`；`p8-04b-customization-live-20261122-091440.log`：A 管理員改角色、新增類別、改通知範本，A 讀回、B 未串接、finally 清理完成；生命週期測試新增臨時教會中途失敗清理案，14/14 通過 | 通過（實機） |
| goodshepherdback 網頁實機：改角色、新增類別、停用類別、A/B 不串 | `p8-04b-customization-live-20261122-091440.log`：只連 goodshepherdback，A／B 管理員登入、角色／類別／範本讀回、B 未串接、最後清理布林均通過 | 通過（網頁實機） |
| build 22 適用外掛 | `p8-04b-build22-20261122-084938.log`、`p8-04b-build22-20261122-084938-assembly.log`；兩 DLL `Test-PluginAssembly.ps1` 結束碼 0 | 通過 |
| 組織回復 | `p8-04b-plugin-verify-20261122-085351.log`：verify 結束碼 0、狀態符合預期；沒有新增測試資料可清理 | 通過 |

本輪只連核准的 goodshepherdback；沒有連正式組織。已在 `P6FixtureOwner` 增加僅供本輪使用、以本輪產生 ID 擁有的臨時 account／contact 建立命令；登入、頁面操作、A／B 讀回與 finally 刪除均由 `Run-CustomizationCheck.cjs` 完成，原始輸出只保存非敏感布林摘要。此結果只證明網頁自訂步驟，不代替 LINE 實機、16.4 第 1～4、6 步或完整守門。

**驗證摘要：** `dotnet test SpeechMessageProducts.LineWebhook.Tests` Release 136/136；PseudoLocaleAudit rules＋lifecycle 14/14；文件說明 1,901 檔 0 失敗；`git diff --check` 通過。完整 guard 尚未對本提交執行；本段不得宣稱 P8-04b 或 P8-04a 已驗收。
