# P7.4 metadata boundary 審查修正實作計畫

## Task 1：建立 fail-first Package02 profile 驗證

**檔案：**

- Modify: `ChurchReport.MemberInfo.Tests/DonationDynamicsAccessBootstrapLifecycleTests.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`

- [ ] 在 lifecycle test 新增 `Package02_contact_profile_client_rejects_an_empty_deployment_profile_before_host_resolution`。
  設定只開啟 `DynamicsAccess:Package02ContactProfileOperationsEnabled=true`、不提供 `ProfileAlias`，並注入
  resource-free fake facade。斷言 factory 丟出含 `ProfileAlias` 的 `InvalidOperationException`，證明 injected
  facade 不能繞過 deployment profile validation。
- [ ] 執行該單一 test，預期 RED：目前 factory 直接回傳 injected facade，因而沒有丟出預期例外。
- [ ] 最小修改通用 Package02 factory：gate=true 後 `BindOptions`、`EnsureNonEmptyProductProfile`，再處理
  injected facade；非 injected branch 將同一 options 傳入只接受已驗證 options 的 executor helper，避免
  第二次 bind 或 host resolution 先於 validation。
- [ ] 重新執行單一 test，預期 GREEN；再執行 Package02/Package03 bootstrap focused tests，確認 disabled gate
  仍 short-circuit 且其他 gate 組合不變。

## Task 2：補齊公開 action 與測試文件

**檔案：**

- Modify: `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`
- Modify: `ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs`

- [ ] 在 `LoadUngroupedMembers` 前新增完整 XML 文件，涵蓋 server-derived scope、deployment-owned metadata gate、
  cancellation/no-fallback、legacy connection owner 和 request-local isolation。
- [ ] 強化 `Controller_ExposesRequiredTreeActions` 的 XML 文件，逐項寫出 public signature fault injection、
  async/sync decisive assertions 與 resource-free source-contract boundary。
- [ ] 執行 `MemberInfoTreeControllerContractTests` focused suite，確認不改變 action route/signature 行為。

## Task 3：審查與交付

- [ ] 先透過 `Start-CcgDualModelRun.ps1` 執行 architecture/review request；最多等待 45 秒。若 timeout／quota，
  停止等待並紀錄「雙模型未完成」。
- [ ] 執行 targeted tests、完整 ChurchReport/solution Release tests、Release build、byte-level UTF-8 no-BOM／
  CRLF／final CRLF check、`git diff --check`、forbidden source/scope scan、Trellis task validation。
- [ ] 將測試、審查與 local-only evidence 寫入 `check.jsonl` 及 CCG review record；更新適用 spec 的
  fail-closed composition contract；scope-only commit 並 archive child。
