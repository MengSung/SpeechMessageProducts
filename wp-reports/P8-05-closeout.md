# P8-05 收尾清理與之後再說

**結論：P8-05 的五項離線必做已完成；本單元可驗收。** 2026-11-22 16:05 Asia/Taipei (UTC+08:00) 完成盤點、保留決策、編碼盤點與後續清單。P8-04b 的實機 LINE／16.4 缺口仍由 P8-04b 報告如實揭露，本報告不把其他單元缺口改寫成 P8-05 通過。

| 必做項目 | 證據與結果 | 狀態 |
|---|---|---|
| `[Obsolete]` 逐一處理 | F20 清單中的 `Line.Messaging` 公開 SDK 成員保留（外部相容介面）；`SpeechMessage.Payments`、`SpeechMessage.Payments.AspNetCore`、`SpeechMessage.Payments.Workflows` 成員仍有相容呼叫面或屬公開模型，保留；ChurchReport 的 `ContextDictionary`、`DonationPaymentManager`、`FeeList`、`HappyGroupDataManager` 仍有呼叫點或相容建構面，保留。未發現可安全刪除且零呼叫的 F20 成員。 | 通過 |
| 設定開關 | `Localization:ChurchOverrides:Enabled`、`Messaging:NotifyRelay:Enabled` 是部署開關，保留；`LineWebhook:EnableLegacyCallback` 是相容路由部署開關，保留；ChurchReport 既有付款／例外／套件設定不是本次可安全刪除的過渡旗標，未改動。 | 通過 |
| `/api/callback` | 依 D8-28 保留相容路由；正式 Webhook 網址尚未核定，列入「上線後再說」及 `P7-LATER-001`／`P5-PROP-011`。 | 通過 |
| duplicate baseline | `tools/RepoQuality/duplicate-baseline.json` 的 `entries` 為空陣列；未修改基準檔。 | 通過 |
| 全樹編碼報告 | `powershell -NoProfile -File .\tools\RepoQuality\Test-TextEncoding.ps1 -Scope All -ReportPath .\artifacts\p8\encoding-all.json`；保存輸出於 `C:\smv\guard-p8-evidence\p8-05-encoding-20261122.log`。掃描 `checked=4276`、`excluded=3079`、`filesWithIssues=428`；前 20 個舊檔路徑見附錄，未在本單元擴大修正範圍。 | 通過 |
| 之後再說清單 | `wp-reports/P8-之後再說.md` 已按「上線前一定要決定／上線後再說」分列，涵蓋進度頁 later 與所有待核定提案。 | 通過 |

## 編碼盤點前 20 個舊檔（本單元不修）

1. `.ccg/tasks/annotate-all-cs-files/implement.md`
2. `.ccg/tasks/annotate-all-cs-files/requirements.md`
3. `.ccg/tasks/annotate-all-cs-files/review.md`
4. `.ccg/tasks/annotate-all-cs-files/task.json`
5. `.ccg/tasks/annotate-richmenu-cs-files/requirements.md`
6. `.ccg/tasks/annotate-richmenu-cs-files/task.json`
7. `.ccg/tasks/archive/2026-07/ccg-dual-model-auto-recovery-entrypoint/requirements.md`
8. `.ccg/tasks/archive/2026-07/ccg-dual-model-auto-recovery-entrypoint/review.md`
9. `.ccg/tasks/archive/2026-07/ccg-dual-model-auto-recovery-entrypoint/task.json`
10. `.ccg/tasks/archive/2026-07/fix-atm-copy-excludes-line-result/task.json`
11. `.ccg/tasks/archive/2026-07/fix-atm-line-notification/.turns.json`
12. `.ccg/tasks/archive/2026-07/fix-dual-model-operation/requirements.md`
13. `.ccg/tasks/archive/2026-07/line-admin-error-notification-convergence/requirements.md`
14. `.ccg/tasks/archive/2026-07/line-admin-error-notification-convergence/review.md`
15. `.ccg/tasks/archive/2026-07/line-messaging-sdk-p0-fixes/review.md`
16. `.ccg/tasks/archive/2026-07/line-messaging-sdk-p0-fixes/task.json`
17. `.ccg/tasks/archive/2026-07/line-processor-sdk-backed-send-message/review.md`
18. `.ccg/tasks/archive/2026-07/line-processor-sdk-backed-send-message/task.json`
19. `.ccg/tasks/archive/2026-07/line-pushutility-best-effort-sdk-convergence/claude-review-rerun.raw.md`
20. `.ccg/tasks/archive/2026-07/line-pushutility-best-effort-sdk-convergence/claude-review.raw.md`

## 完成後處置

- `artifacts/p8` 的一次性 D34／SMV-TEST 探針檔已不列為 P8-05 產物；本單元只保留必要的 `encoding-all.json` 報告。
- 本單元沒有新增秘密、沒有連線組織、沒有修改守門 clone。
- 完整 Release 建置／測試與守門依 `C:\smv\guard-p8-evidence\guard-f4152847-full.log`；該證據支持本單元前置的全方案綠燈，但不替代 P8-04b 尚缺的實機步驟。

### 2026-11-22 19:05 Asia/Taipei (UTC+08:00)：一次性探針清理受執行政策阻擋

依 P8-05 指示確認 `artifacts\p8` 仍有 `D34ReadOnlyDiagnostic.cs`／`.dll` 與 `SmvTestResidueProbe.cs`／`.dll` 四個一次性探針，四檔均未被 Git 追蹤。嘗試以 PowerShell `Remove-Item -LiteralPath` 逐檔刪除時遭本機執行政策阻擋，未繞過政策、未改動檔案；故清理項目記為「未完成／環境限制」，並保留原檔供後續使用者處理。
