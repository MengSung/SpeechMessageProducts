# P4-06 實機驗收：永豐測試環境

## 目標

依 04A §5 在開發機以 `Category=PaymentsLive` 驗證 D365 `goodshepherdback` 測試組織與永豐沙箱；不連正式組織、正式商店或正式 LINE Channel，不進行真實付款。

## 範圍與限制

- 修改 `SpeechMessageProducts.LineWebhook.csproj`，加入新的 `UserSecretsId`。
- 新增 `SpeechMessageProducts.LineWebhook.Tests/PaymentsLive.runsettings`，讓一般測試排除 `Category=PaymentsLive`；新增 `PaymentsLiveAcceptanceTests.cs`。
- 新增 `文件/規劃設計書/實作計畫-3.5/驗收/P4_金流驗收步驟.md`。
- 以標準輸入將核准的開發／沙箱設定寫入 LineWebhook User Secrets；秘密值不得進入命令列參數、repo 檔案、輸出或回報。不得修改 ChurchReport `appsettings*.json`。
- D365 只使用 `goodshepherdback`。`plan` 只允許建立並納入 `account.new_sinopacprofile`；任何其他差異都停止套用，依使用者規定回報。
- 不重新部署 `LineWebhookP309`，不在本機啟動完整 LineWebhook，不擴充 `tools/LegacyGolden`，不新增 `.csproj`。

## 驗收條件

- 以下三個測試名稱逐字存在且通過：
  - `Live_AllotQuery_SandboxRoundTrip`
  - `Live_AllotWriteBack_SyntheticBatch_MatchesLegacy`
  - `Live_SameFeeEnqueuedTwice_ExecutesOnce`
- 測試以 `[Trait("Category", "PaymentsLive")]` 標記；指定篩選命令通過，一般 `dotnet test` 不執行這三案。
- `Live_AllotQuery_SandboxRoundTrip` 使用真實永豐沙箱；60 秒內待辦完成且驗簽成功。無撥款資料時四個撥款欄位維持不變，實際狀態碼只記在本機操作紀錄。
- `Live_AllotWriteBack_SyntheticBatch_MatchesLegacy` 的永豐回應是合成資料，並明確標示；從 D365 讀回的四個欄位符合舊公式，`new_allot_date` 為同一天。
- `Live_SameFeeEnqueuedTwice_ExecutesOnce` 第二次建立回到相同待辦 ID，`new_attempts` 與收費單修改時間不變。
- D365 `verify` 退出碼為 0；測試收費單、測試會友及本次待辦殘留皆為 0；教會 A 原設定檔名稱已還原；`new_sinopacprofile` 欄位保留。
- 00 §7.1 五項、固定 guard §8.1、P4-06 指定測試、步驟文件與一頁 WP 回報均完成；操作紀錄與原始 TRX／log 留在本機 evidence，只提交檔名及 SHA-256。

