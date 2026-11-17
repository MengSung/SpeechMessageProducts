# P7-01b 以plan切換回復與流程改指

2026-11-17 18:38 Asia/Taipei (UTC+08:00)：前段a已驗收推送ab2a4422，接本段；全程離線、不連D365，不重提D7核定決策。完成依00 §7.1與以下07A固定產物／同名方法。

**P7-01b：以 plan 執行的切換與回復、流程改指**

- [ ] 先寫測試並確認紅燈：`SwitchTarget_StateAndTierTable_MatchesSpecification`（§4.3 的五列）、`SwitchPlan_ToLive_OrdersConfigLegacyWorkflowEnable`、`SwitchPlan_DormantTier_StaysDisabledShadow`、`SwitchPlan_TierOverride_TypeBeatsModule`、`SwitchPlan_LegacyAndNewSameModule_NeverBothEnabled`、`SwitchPlan_LegacyEnabledForDormantItem_KeptAndListedForDecision`、`SwitchPlan_ProductionLiveWithoutEvidence_Rejected`、`SwitchPlan_LiveTokenStep_UsesPlaceholderNeverValue`、`SwitchPlan_AssemblyNotDeployedOrContentDiffers_Rejected`、`SwitchPlan_WorkflowMixingRepointAndKeptActivities_ListedAsKeepLegacy`、`Switch_ThenRollback_StateEqualsExport`（05 檔：對稱性）、`Switch_UnreviewedPlanHashMismatch_Rejected`（05 檔）、`Switch_StalePlanOrganizationChanged_Rejected`（05 檔）、`Switch_FailureMidRun_RollsBackExecutedOperations`、`Switch_CancelledMidRun_RollsBackWithUncancelledToken`、`Switch_WorkflowRepoint_PreservesActivationState`、`Switch_WorkflowRepoint_RewritesOnlyActivityReference`、`Rollback_FromSwitchLog_RestoresConfigStateAndXamlBytes`、`Rollback_ClearsSecureConfigOfSteps_BackToShadow`、`Verify_WithSwitchPlan_ComparesAgainstPlanTarget`、`Verify_OrganizationsState_MismatchReportsDifference`。
- [ ] 實作 §4.4。指令的參數組合沿用 `DeploymentCli.TryParse` 的嚴格寫法（多一個、少一個選項都拒絕）。
- [ ] `WorkflowRepointer`：改寫定義的邏輯取自 `ActivityProbe`（抽成共用，`ActivityProbe` 改呼叫它，行為與既有兩個測試不變）。流程的啟停用正式的 `SetState`。改之前把原定義完整存進 `switch-log.json`（本機、不進版控）；定義超過 1 MiB 的流程不處理，列成 `Manual`。
- [ ] `switch-plan.md` 的版面：最上面三行（組織、從哪個狀態到哪個狀態、plan 雜湊），接著「會做的事」「不會做、需要決定的事（`KeepLegacy`）」「人工步驟（`Manual`）」「不切的項目與原因（`dormant`）」「仍登錄的舊組件」五段。

**P7-01b 完成：** 上列測試同名存在並通過；既有 `switch --module`、`rollback --from <export.json>` 的測試全綠。提交 `P7-01 新增以 plan 執行的組織切換、回復與流程改指`。
