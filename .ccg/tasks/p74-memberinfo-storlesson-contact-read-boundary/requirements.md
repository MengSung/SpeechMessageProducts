# P7.4 MemberInfo 上課紀錄讀取授權邊界稽核

## 任務

確認 `ORG-CALL-00027` 能否脫離目前 MemberInfo 的 Session／InMemoryContext／ListManager
授權流程，成為 bounded、server-authorized、DTO-only 的 local typed-read capability。若無法證明，
建立精確 source-only local design no-go；不得產生 runtime、CE、gate、consumer、traffic、P7.5
或 P8 變更。

## 不可違反的條件

- 完整 Church 與 Shepherd consumer 語意都必須以已驗證 principal 衍生的 immutable request-local
  authorization scope 保護；不可用局部 Church route 宣稱完成。
- 禁止 Session、shared mutable context、static user-state cache、saved credential loader、ToolUtility、
  browser locator 或 caller inputs 成為 Gateway authority。
- 禁止新增 sub-gate、registry、executor、ProductClient、SDK bridge、fallback、retry 或 consumer wiring。
- 外部模型每次最多等待 45 秒；若未完成立即採本機驗證並記錄「雙模型未完成」。
