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
