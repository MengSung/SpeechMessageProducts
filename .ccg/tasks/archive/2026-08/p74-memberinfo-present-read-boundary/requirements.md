# P7.4 MemberInfo 個人出席紀錄 typed read boundary

此 CCG task 與 Trellis child `08-13-p74-memberinfo-present-read-boundary` 同步。它只處理
`ORG-CALL-00026` 的已授權 contact 個人出席紀錄讀取。

- 建立獨立、DTO-only、server-owned、disabled-by-default 的 Data8/ProductClient/ChurchReport path。
- false gate 的既有 ToolUtility 路徑維持相容；true gate 不允許 fallback、retry、partial publish 或 SDK graph。
- ProfileAlias/workload 是 deployment-owned；browser contact locator 必須在 user/session/object authorization 後
  才能 dispatch，不能選擇 connector/endpoint/owner/credential。
- 本工作僅本機設計、實作與驗證。不得 CE、fixture、traffic、P7.5、P8、push 或 PR。歷史 Slice C 永遠不得重試。
- 完整 requirements、acceptance、設計和執行計畫以同名 Trellis task 的 `prd.md`、`design.md`、`implement.md` 為準。
