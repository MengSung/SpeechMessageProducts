# P7.2 週日出席與週報寫入能力家族：品質檢查

## 範圍與證據判讀

本 child 僅交付本機 attendance reducer／plan 的重驗與 QR production mutation graph 的安全稽核。
它沒有變更 ChurchReport production C#、QR controller、Data8 executor、ProductClient、CE fixture、
feature gate、產品流量或歷史 Slice C artifact。歷史 Slice C 的 `write-not-committed` no-go 與 exact
cleanup 保持不可重試。

local design no-go 是本 child 的預期且正確結果：目前 QR 路徑在 server authorization 前，將 browser／route
值寫入 process-wide `InMemoryContext`，且一個掃碼路徑混合多個 CRM mutation 與通知副作用。這不足以證明
request-local、server-derived isolation boundary、idempotency、ledger、read-back、reconcile 與 cleanup owner，
所以不可把已存在的 local reducer 升格為 CE dispatch 或 consumer cutover。

## 已執行驗證

| 檢查 | 結果 | 證據範圍 |
| --- | --- | --- |
| attendance focused tests | 通過：32 passed、0 failed | weekly zero/exact/duplicate/unavailable、upsert cardinality、timeout／ambiguous no-replay、A/B isolation、local plan flags |
| Release build | 通過：0 warnings、0 errors | `SpeechMessageProducts.sln`，`Release --no-restore -m:1` |
| full solution tests | 最終通過 | 最後序列化 rerun：Dynamics 859 passed、7 skipped；其餘 solution test projects 通過。第一次 transient 結果詳見下節 |
| Kestrel 單測 | 通過：1 passed | `Kestrel_http11_rejects_declared_and_chunked_limit_plus_one` |
| Kestrel 同類 suite | 通過：45 passed、0 failed | `GatewayRequestBodyBoundaryTests` |
| `git diff --check` | 通過 | 沒有 task-owned whitespace error |
| task-owned 文件 encoding | 通過 | UTF-8 無 BOM、CRLF-only、final CRLF；涵蓋本 child 文件 |
| source／scope review | 通過 | task-owned 文件與 parent child-link；未碰既有非本任務 dirty paths |

## 完整測試的暫時性傳輸失敗

序列化完整 solution test 第一次在
`GatewayRequestBodyBoundaryTests.Kestrel_http11_rejects_declared_and_chunked_limit_plus_one` 收到
`HttpIOException: ResponseEnded`。該測試檔不在本 child 的變更範圍，且已有同一 Kestrel transport
symptom 的歷史紀錄。依系統化除錯：

1. 以相同 Release 設定單獨重跑該 case，通過。
2. 以相同 Release 設定重跑整個 `GatewayRequestBodyBoundaryTests`，45 passed、0 failed。
3. 因為失敗不穩定、沒有 task-owned code diff，且 targeted／同類 suite 均通過，將它記為 test-host
   transport 波動，不修改不相干 Gateway code，也不把它視為 QR attendance 或本 child 的回歸。
4. 最後再次以序列化 `dotnet test .\\SpeechMessageProducts.sln -c Release --no-restore -m:1` 重跑完整
   solution suite，所有 test project 通過；Dynamics 為 859 passed、7 skipped。

這次 final rerun 是完整 solution test 成功證據；後續 P7 wave／release candidate 仍必須再次執行完整 suite，
並在同一 failure 若變成穩定重現時另開啟 Gateway boundary debug child。

## CCG 審查狀態

使用專案指定的 `Start-CcgDualModelRun.ps1`，設定最多 45 秒、一次嘗試：

- Gemini：逾時；雖輸出部分內容，但 runner 未將其列為完成 backend。
- Claude：無可用輸出。

因此結論是「雙模型未完成」，而非完整雙模型審查。沒有未經本機驗證的外部 finding 用於修改程式或證據狀態。

## Spec 回饋判斷

此 child 發現的規則會重複影響 QR、browser locator 與未來 consumer cutover：browser／route locator 不得在
server authorization 前被寫入 `InMemoryContext` 或其他 process-wide mutable state。這已新增至
`cross-user-isolation-and-performance.md` 的 executable scenario；它不是單一 QR UI 的偶發細節。

## 結論與下一步

本 child 的 local design no-go、32 個 attendance contract tests 與本機 build evidence 均完成。它不阻擋其他
不依賴 QR mutation 的 P7 capability。下一個 child 必須從 authoritative matrix 選取具 bounded DTO、
server-derived authorization、無 shared mutable bridge、無 write adjacency 且有明確 rollback owner 的 family；
P7.5 與 P8 閘門維持不變。
