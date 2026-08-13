# P7.4 MemberInfo 關係／目標唯讀授權邊界稽核

## 任務

確認 ORG-CALL-00033 能否脫離目前 MemberInfo Session／InMemoryContext／
ListManager 授權流程，成為 bounded、server-authorized、DTO-only 的 local
typed-read capability。若無法證明，建立精確 source-only no-go；不得產生
runtime、CE、gate、consumer、traffic、P7.5 或 P8 變更。

## 不可違反的條件

- 完整 Church 與 Shepherd consumer 語意皆需被安全授權；不可局部遷移。
- 禁止 Session、shared mutable context、saved credential loader、ToolUtility、
  browser locator 或 caller inputs 成為 Gateway authority。
- 禁止無界 relation expansion、partial/timeout fault 假裝成功、raw CRM state
  穿越 DTO boundary 或 request-time fallback/retry。
- 外部模型每次僅等 45 秒；逾時立即本機驗證並記錄雙模型未完成。
