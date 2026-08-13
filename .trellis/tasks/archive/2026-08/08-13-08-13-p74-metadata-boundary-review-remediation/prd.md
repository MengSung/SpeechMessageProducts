# P7.4 metadata boundary 審查修正

## 目標與背景

此 child 修正已封存 `08-13-p74-memberinfo-commitment-metadata-read-boundary` 的最終本機審查所揭露、
且仍存在於目前 HEAD 的兩個品質缺口：MemberInfo 未分組 action 的公開 lifecycle 文件必須完整描述
typed metadata gate 與 legacy connection owner；通用 Package02 contact-profile typed-client factory 必須在
任何 process-host／provider／handler／pool／credential composition 前拒絕空白 deployment ProfileAlias。

這是 P7.4 預設關閉、本機-only remediation。它不改變 ORG-CALL-00040 的業務資料輸出、不啟用任何
feature gate、不建立 CE fixture、不發出 CE request／mutation、不切換 ChurchReport 流量，也不推進
P7.5 ToolUtility removal 或 P8。

## 需求

1. `LoadUngroupedMembers` 的公開 XML 文件必須完整說明：server-derived scope、browser input 的非權限
   性質、Package03 metadata base/sub-gate 的 false/true 分支、request cancellation、legacy connection
   的唯一 owner、typed path 無 fallback/retry，以及 response 不保留跨使用者／profile／generation 狀態。
2. `MemberInfoTreeControllerContractTests.Controller_ExposesRequiredTreeActions` 的測試文件必須明確說明
   所保護的公開 action 簽章、故障注入方式與 decisive assertions；測試仍必須是純 source contract，
   不建立 CRM、Gateway、Session、cache 或背景資源。
3. `DonationDynamicsAccessBootstrap.TryCreatePackage02ContactProfileClient` 在 Package02 base gate 已開啟後，
   無論有無 injected facade，都必須先從 deployment-owned configuration 綁定並驗證非空 ProfileAlias，
   才能取得／建立 process host executor 或交還 facade。缺值、空白或不合法設定必須 fail closed，
   不可猜選 profile、降級 legacy 或建立部分資源圖。
4. 為上述 factory 行為新增 test-first regression：base gate=true 而 ProfileAlias 缺失時，必須觀察到
   ProfileAlias validation failure，而不是「host 未啟動」或下游 composition failure。測試不得接觸 CE。
5. 所有實質修改的 `.cs` 符合 AGENTS.md：完整繁體中文文件、UTF-8 無 BOM、CRLF、final CRLF，並保持
   A/B user/profile isolation 與 deterministic resource ownership。

## 明確不在範圍

- 修改 `LoadUngroupedMembers` 的查詢、排序、資料、授權決策或 response shape。
- 啟用 `Package02ContactProfileOperationsEnabled`、Package03 任一 gate、CE 讀寫、fixture、週報、
  traffic／routing、P7.5、P8、push 或 PR。
- 重試已 closed 的 P7.2 Slice C cycle，或修改任何 archived task 的歷史證據。

## 驗收條件

- [ ] `LoadUngroupedMembers` 和其 action-contract test 均具有可維護的繁體中文 lifecycle／fault／assertion 文件。
- [ ] 通用 Package02 contact-profile factory 在 enabled + empty ProfileAlias 時於 host resolution 前 fail closed；
      injected facade 不可繞過這個 deployment validation。
- [ ] 新 regression 先 RED，再以最小實作 GREEN；相關 focused tests、完整 ChurchReport tests、solution
      Release tests/build、UTF-8/CRLF/final CRLF、`git diff --check` 與 scope scan 全部通過。
- [ ] CCG Gemini／Claude 審查僅透過 self-healing runner 發起並至多等待 45 秒；若未完成，task record 精確
      標示「雙模型未完成」，不把本機檢查誤稱為完整雙模型審查。
- [ ] task 記錄清楚說明這是 local-only quality remediation，不提供 CE、cutover、P7.5 或 P8 證據。
