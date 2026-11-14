# P5-01 外殼基礎與測試替身

## Goal

依 05A §4.1、§4A 第 1～3 項建立 Plugin 外殼、Shadow 記錄器、設定解析、測試替身與必要 Core 支援，供後續模組只在離線測試先驗證；本包不部署任何模組、不啟用 goodshepherdback 舊或新步驟。

## Requirements

- 新增 `SpeechMessage.Dynamics.Plugins\Infrastructure\` 八個檔案，以及 Core 的 `BusinessRuleException.cs`、`DeterministicGuid.cs`、`Notifications\IShadowPushSink.cs`。
- 修改 `MemberNotificationService.cs`，加入 `NotifyComposedAsync`；Shadow 不解密、不呼叫推播 client，Live 只在明確模式下推播一次。
- 新增 Plugin 測試替身四個檔案與指定測試；修改 `SharedText.resx`，必要時同步兩個打包子集連結。
- `PluginShellBase.Execute` 必須依序取得服務、套用 DepthPolicy、建立 PluginInput、依模式建立 PluginServices、執行規則、在 Shadow 記錄追蹤；BusinessRuleException 轉 InvalidPluginExecutionException 並附字典文字與 CorrelationId，其他例外只記型別後原樣重新拋出。
- StepConfiguration 限長 4,096、不准重複屬性、mode 僅 Shadow 或 Live；Shadow 搭配非空 secure config 必須拒絕且不得回顯原設定。
- FakeOrganizationService 只支援舊程式實際需要的查詢子集；不支援的查詢語法必須以具名例外拒絕，不得靜默回空集合。所有 request/context/result 狀態必須是 Execute 操作區域狀態，不得以可變 static、無界 cache 或背景工作跨次保留。
- 不修改 `DiagnosticsPingPlugin`；不接觸正式組織、正式 LINE channel、秘密或舊方案原始檔。

## Acceptance Criteria

- [x] 測試同名存在且通過：`Shell_DepthExceeded_DoesNothing`（三種例外條件各一組）、`Shell_ShadowMode_NoWritesExecuted_AllRecorded`、`Shell_ShadowMode_NoPushCalled`、`Shell_BusinessError_ThrowsInvalidPluginExecutionWithEventId`、`Shell_UnexpectedError_RethrownNotSwallowed`、`StepConfig_InvalidJson_ThrowsChineseMessage`、`Recording_LargePayload_SplitIntoChunks`。
- [x] §4A 第 1～3 項：`Shell_InterleavedChurches_NoStateShared`、`Recording_CreateThenUpdate_ServedFromOverlay`、`Recording_UnknownWriteRequest_FailsClosed` 同名存在且通過。
- [x] `StepConfig_ShadowWithSecureConfig_Rejected`、`NotifyComposed_Shadow_RecordsWithoutDecrypt`、`NotifyComposed_Live_PushesOnce`、`NotifyComposed_ChurchMismatch_SameInBothModes`（兩個目標）、`DeterministicGuid_SameInputs_SameGuidOnBothTargets`、`Fake_UnsupportedQuery_ThrowsNamedFeature` 同名存在且通過。
- [x] `dotnet test SpeechMessage.Dynamics.Plugins.Tests -c Release`、`dotnet test SpeechMessageProducts.GoodShepherd.Core.Tests -c Release`、`dotnet test SpeechMessage.Hosting.Tests -c Release --filter "FullyQualifiedName~Architecture"` 通過。
- [x] 新增或實質修改的 `.cs`、`.csproj`、`.resx` 符合繁中檔首說明、XML 註解、UTF-8 無 BOM、CRLF、末尾 CRLF；JSON 伴隨說明逐檔登錄。
- [x] 完成一次打包，`Test-PluginAssembly.ps1` 通過；固定 guard、附錄 F 一頁回報、進度頁 DATA 與遠端 SHA 核對完成。
- [x] 提交訊息為 `P5-01 新增外殼基礎、影子記錄器與測試替身`，本包驗收後推送 P5 分支；不推 main、不開 PR。

## Constraints

舊、新步驟不得同時啟用；新步驟不得 Live；本包完全離線，任何實機影子比對留到 D5-18 通過後的 P5-03a／P5-03b。

## 完成定義（00 §7.1）
- [x] 1. 指定同名測試通過，Core雙目標各111/111。
- [x] 2. fee2e909固定guard完整§8.1八項通過；Release5485/5485。
- [x] 3. 沒有新增專案，覆蓋率不適用。
- [x] 4. 本包沒有實機項目，不適用。
- [x] 5. 一頁回報與進度頁已更新，推送SHA已核對。
