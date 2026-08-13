# P7.4 認獻單讀取 disabled boundary 檢查結果

## 範圍與安全檢查

- 程式變更限於 ChurchReport 的 disabled read boundary、其設定與 focused tests；沒有修改
  `DonationBookingService.FillBookingList`、付款 write chain、週報、P7.2 fixture 或外部環境。
- base/sub gate 都必須為 true 才可 composition；checked-in appsettings、Development 和 launch profile
  全部維持 false。
- gate=false 直接 short-circuit，沒有 options bind、host resolution、client／pool／handler／credential
  graph 或 CE I/O。
- gate=true 時 ProfileAlias 在 injected client 與 host resolution 前驗證；connection mode 支援
  Embedded、DedicatedGateway、CentralGateway，且 Embedded RequestGuard allowlist 含 operation ID。
- service／adapter 只處理 scalar DTO 與 request-local model，沒有 CRM SDK bridge、retry、fallback、
  sync-over-async、static mutable state、Session 或 shared response cache。

## 新鮮驗證結果

| 檢查 | 結果 |
| --- | --- |
| focused boundary tests | 33 passed、0 failed、0 skipped |
| ChurchReport.MemberInfo.Tests | 612 passed、0 failed、14 skipped（既有 live evidence 類別） |
| SpeechMessage.Dynamics.Tests | 753 passed、0 failed、7 skipped（既有 live SQL 類別） |
| Release build | 0 warnings、0 errors |
| C# byte-level encoding | 五檔皆 UTF-8 無 BOM、CRLF、final CRLF |
| `git diff --check` | exit 0 |

## 審查結論

CCG architecture analysis 由 Gemini 與 Claude 皆成功完成。最終 reviewer run 中 Gemini 完成，
Claude 因 provider quota/session limit 未產生輸出；依專案允許的降級規則，以 single-model
fallback 加本機審查繼續，明確不稱為完成雙模型審查。

Gemini 提出 UTF-8 BOM critical finding，但本機 strict UTF-8／BOM／CRLF 位元組檢查顯示五個
受影響 C# 檔均為無 BOM，因此此 finding 不成立。Gemini 對 source-string contract test 的 warning
已接受：factory route 與 private Embedded allowlist 是 deployment composition 靜態契約，安全的
無 transport unit-test environment 無法建立完整 ProcessHost，因此以 source contract 加上 factory
lifecycle tests 覆蓋；沒有遺留未驗證的 runtime I/O。

## 結論

本 child 可作為 disabled-by-default 的本機 P7.4 candidate 封存。它不提供 CE、capacity、parity、
soak、drain、rollback 或 traffic-cutover evidence；P7.5 ToolUtility removal 與 P8 Central Gateway
仍維持 gated。
