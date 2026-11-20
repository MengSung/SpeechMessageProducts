# P8-03a 教會覆寫接線與設定服務

## 目標

依 `文件/規劃設計書/實作計畫-3.5/08A_階段8_執行細則.md` §4.3、§5 P8-03a，將教會文字覆寫接入 ChurchReport 與 LineWebhook，並提供可由已授權教會範圍操作的核心設定服務。

## 需求

- 在 `GoodShepherd.Core/Settings` 新增 `ChurchTermWhitelist` 與 `ChurchSettingsService`。白名單只有 11 個 `Role.*` 鍵及 `Fees.Notify.Template`；任何 `Keyword.*` 或 `Webhook.Route.*` 均不可接受。
- 設定服務只接受伺服器完成授權後的教會型別與完整 `TextContext`；不可接受呼叫端傳來的教會字串或 Guid 作為授權依據。
- 儲存覆寫依序執行替代索引鍵 Upsert、更新教會 `new_textchangedon` 為 UTC、使本程序對應 scope 的快取立即失效。任一步失敗均不得回報成功；Upsert 失敗不得更新時間或失效快取。
- 還原白名單覆寫、管理教會奉獻類別，以及查詢僅屬本教會且最多 200 筆的修改紀錄。奉獻類別代碼由伺服器產生唯一值；類別清單維持教會範圍，不可跨教會讀寫。
- ChurchReport、LineWebhook 使用 `Localization:ChurchOverrides:Enabled`；缺省與缺值均關閉，只接受有效布林，無效設定拒絕啟動。正式 `appsettings.json` 缺省 false，Development 明確 true。
- 開關啟用時以 `AddChurchTextOverrides`、真實 `D365ChurchTextOverrideSource` 和 `TextChangePollingService`／`DataverseTextChangeSource` 接線；ChurchReport 重用既有 `"ChurchReport"` 連線 owner，不建立長期第二條連線。LineWebhook 以正式覆寫 provider 取代空 provider。關閉時各自維持不含覆寫的安全行為。
- 兩個主機的覆寫快取以完整組織／產品／教會 TextContext 隔離。停止時先停止並排空輪詢，再釋放快取，最後釋放既有連線 owner。
- 所有新增或修改的自行維護 `.cs`、`.cshtml`、設定伴隨文件皆符合 AGENTS.md 的繁體中文整體說明、UTF-8 無 BOM、CRLF、末尾 CRLF 規範；不修改計畫文件。
- 本單元完全離線，不連 D365、不執行 P8Live、不呼叫外部模型。

## 必須同名存在並通過的測試

`SpeechMessageProducts.GoodShepherd.Core.Tests`（net48、net10.0 兩個目標）：

- `Save_TooLong_Rejected`
- `Save_NonWhitelistedKey_Rejected`
- `Reset_RemovesOverride`
- `Whitelist_ContainsElevenRolesAndFeeTemplate`
- `Whitelist_RoutingAndKeywordKeys_NeverAllowed`
- `Save_TemplatePlaceholderMismatch_Rejected`
- `SettingsService_Save_UpsertsByAlternateKeyAndTouchesTextChangedOn`
- `SettingsService_SaveFailure_NoTouchNoInvalidate`
- `Category_AddDisableSortAccountingCode_ChurchScoped`
- `Category_NewCode_ServerGeneratedUnique`
- `Audit_OnlyThisChurchRecords_Max200`

`ChurchReport.MemberInfo.Tests`：

- `Overrides_ChurchReportWired_RealSourceAndPolling`
- `SettingsService_Save_InvalidatesLocalCacheImmediately`

`SpeechMessageProducts.LineWebhook.Tests`：

- `Overrides_LineWebhookWired_ReplacesEmptyProvider`
- `Overrides_SwitchMissing_DefaultsOff_InvalidRejectsStartup`
- `Overrides_ChurchAEdit_ChurchBSnapshotUnchanged`
- `Overrides_PollingStop_DrainsBeforeCacheDispose`
- `Relay_FeeNotify_UsesChurchOverrideTemplate`

## 驗收條件

- 上列 18 個名稱均在指定測試專案中實際宣告並通過；Core 測試在兩個 target 均通過。
- 完整方案 `dotnet test SpeechMessageProducts.sln -c Release` 通過。
- 套用 P8-03a 的所有主機、設定、服務與測試變更均有繁體中文檔案整體說明和適用成員註解；文件伴隨檢查及位元組檢查通過。
- `python tools/RepoQuality/check_file_documentation.py` 通過。
- 完整守門只在 `C:\smv\guard-p8` 重用；證據僅寫 `C:\smv\guard-p8-evidence`。提交訊息以 `P8-03` 開頭，推送目前 P8 分支並以 `git ls-remote` 核對 SHA。
- 產生一頁 P8-03a 回報、標記此單元驗收並只更新進度頁 DATA。
