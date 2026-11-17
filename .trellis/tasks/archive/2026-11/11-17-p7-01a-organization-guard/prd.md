# P7-01a 組織白名單與正式組織關卡

2026-11-17 17:44 Asia/Taipei (UTC+08:00)。前置C-b已驗收並推送be718559，使用者授權連續實作；不再要求任務建立／實作同意。
本單元全程離線，不連任何D365，不新增csproj、不放寬檢查。完成條件為00 §7.1五項與下列07A原文測試／產物。

**P7-01a：組織白名單、`--production`、連線**

- [ ] 先寫測試並確認紅燈：`Organizations_UnlistedOrganization_Rejected`（05 檔：非白名單組織拒絕）、`Organizations_DuplicateNameOrHost_Rejected`、`Organizations_UnknownField_Rejected`、`Organizations_ProductionWithoutFlag_Rejected`、`Organizations_ProductionFlagOnTestOrganization_Rejected`、`Organizations_ProductionWriteWithoutNameConfirmation_Rejected`、`Organizations_ProductionNameConfirmationMismatch_Rejected`（這四個對應 05 檔的「沒有 `--production` 或組織名稱確認不符時，正式組織拒絕執行」）、`Organizations_HostMismatchWithUserSecretsUrl_Rejected`、`Organizations_ServerOrganizationIdDiffersFromPlan_Rejected`、`Organizations_ProductionForbidsSmokeShadowModuleSwitchLiveCheckUiExport`、`Organizations_ProductionExportLegacy_ReportsEnabledWithoutStopping`、`Connector_TwoOrganizations_CredentialsNotShared`、`Organizations_GoodshepherdbackBehaviour_Unchanged`。
- [ ] 新增 `d365\registration\organizations.json`（§4.1，只有 goodshepherdback）與 `docs\file-purpose-companions.json` 的說明。
- [ ] 實作 `OrganizationCatalog`、`ProductionGuard`；把 F6 列出的每一處寫死改成向 `OrganizationCatalog` 查詢。原則：
  - 「只允許測試組織」的地方（`smoke`、`shadow`、`switch --module`、`ShadowScenarioRunner`、`ShadowTestIdentity`、`ui-export`）改成檢查 `kind=test`，行為不變。
  - 其餘改成「在白名單內，而且通過 `ProductionGuard`」。
  - 輸出路徑 `artifacts\d365\<組織>\`、結構快照 `d365\snapshots\<組織>\`、User Secrets 的鍵都依組織名稱。
  - `RegistrationManifestLoader` 對 `enabledByOrganization` 的規則不變（§4.2）。
- [ ] 組織名稱確認：從標準輸入讀一行，最多等 30 秒、最長 256 個字元，必須與組織名稱完全相同（區分大小寫）；逾時或不符都拒絕、不連線。沿用 `Channels\ChannelSecretInput.cs` 讀標準輸入的做法，不另寫一份。
- [ ] `--help` 的說明同步；`.trellis\spec\backend\` 裡描述佈署白名單的規格同步（`rg "goodshepherdback" .trellis\spec`）。

**P7-01a 完成：** 上列測試同名存在並通過；`rg "\"goodshepherdback\"" SpeechMessage.Dynamics.Deployment --glob "*.cs"` 只剩測試身分關卡（`ShadowTestIdentity`）與說明文字，逐筆在回報說明為什麼留著。提交 `P7-01 佈署專案改用組織清單並加入正式組織關卡`。
