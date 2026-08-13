# P7.4 認證憑證策略安全邊界檢查

## 檢查範圍

本 child 沒有 C# 或執行期設定變更。檢查的是決策與持久化紀錄是否如實反映 source contract，
且沒有把 planning artifact 誤說成認證遷移、CE evidence、P7.5 或 P8 evidence。

## 已驗證的 source facts

- `AuthenticationController.ValidateUserCredentials` 從 CRM `contact` 投影 `new_app_pass`，再與
  `viewModel.Password` 做明碼比較。
- `AuthenticationContactReadClient` 只輸出 immutable non-secret result，且會在 secret presence、
  response-kind、operation-correlation、zero／duplicate match 等不安全情況 fail closed。
- 既有 LINE login 和後續 `RetrieveUserData`／`InitializeUserSessionAsync` 使用 legacy CRM entity
  與 session-backed manager；不能由 read DTO 安全取代。

## 檢查結果

| 項目 | 結果 |
| --- | --- |
| 直接以 typed read 驗證帳密 | 拒絕；會造成驗證缺失或 secret leakage。 |
| DTO-to-CRM-Entity rehydration | 拒絕；會突破 typed boundary 並將 legacy Session 依賴偽裝成 cutover。 |
| typed dispatch 後 fallback 到 legacy | 拒絕；違反 fail-closed，且可能讓 timeout／ambiguous outcome 產生雙路徑。 |
| 未來 credential verify | 只保留為明確前置條件；本 child 未建立型別、operation、gate 或 CE cycle。 |
| CE／feature gate／traffic／P7.5／P8 | 未執行。 |
| 雙模型 | Gemini partial output，Claude quota/session blocked；**雙模型未完成**，不是 dual-model pass。 |

## 後續限制

這個結論不阻止 P7.4 繼續做其他完整 DTO／authorization／rollback shape 的 local-only child；
它只阻止把目前的 account/password 或 LINE legacy Session chain 誤接到 contact read boundary。
任何未來 credential verification task 都要重新做完整 spec、TDD、quality gate 與獨立 CE evidence
評估。
