# 階段 5 執行計畫

## 開工

1. 完成 P5-00：更新 AGENTS.md 的現行階段指向、進度頁 DATA、父任務前置缺項、
   P4 審查任務封存與 05A 納入；不改 3.5 計畫正文。
2. 讀取並確認 D365、金鑰存在性、測試教會／會友 GUID、舊方案與固定 guard 的可達性；
   只記鍵名與狀態，不輸出秘密。
3. 先完成 P4 H-1 修補，再建立 P5-01 的紅燈測試與外殼／替身。

## 依序實作

- P5-01：PluginShellBase、StepConfiguration、RecordingOrganizationService、
  ShadowPushRecorder、FakeOrganizationService、NotifyComposedAsync 與隔離／生命週期測試。
- 更新回復前置：PluginFileVersion 追蹤、固定版本與 rollback 證據；未完成前不部署任何模組。
- P5-02：加入 net48 PluginHarness 至方案，執行時以 Roslyn 編譯舊原始檔並產生 golden。
- P5-03a／b：註冊清單 v2、映像／活動、shadow／switch、UI 匯出入與活動測試流程。
- P5-04～P5-12：依 05A 各模組固定七步驟，逐一完成外殼、規則、同名測試、golden、
  影子情境與回報；P5-07、P5-12 依核定 a／b 切法拆分提交。
- P5-13：登錄 36 個舊專案去向、未移植理由與 `Source_NoLineNotifyEndpoint`。

## 每包驗證與提交

每包先確認指定測試紅燈且不提交紅燈，再實作至綠燈；補齊繁中檔首說明、XML 註解、
UTF-8 無 BOM、CRLF 與 final CRLF。新增 `.csproj` 同步加入 `.sln`，新增 JSON 同步
登錄 `docs/file-purpose-companions.json`。每個產品程式提交前在 `C:\smv\guard`
執行完整 §8.1；回報與紀錄型提交依 §8.3。驗收後才推送本階段分支並用
`git ls-remote` 核對 SHA。

## 最終階段審查

核對 05 §「階段 5 完成判準」、13 包狀態、兩個 DLL、註冊清單、I18nCheck、BannedApi、
Find-DuplicateCode、verify=0、所有資源清理與跨教會隔離證據；依 AGENTS.md 以
`Start-CcgDualModelRun.ps1 -Role reviewer` 執行外部審查，Critical／High 必修。
完成一頁階段回報、推送並核對遠端 SHA，然後停止。
