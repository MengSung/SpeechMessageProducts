# 階段 8 執行清單

## 固定基準

- 起點：`6a37f16ba111943eb023c14dea3c9a006a0f0a5c`
- 分支：`1.0.0.11.Integration.P8.Worktree`
- P7 最末完整守門：Release 7,341；Debug tracer 7。
- Deployment.Tests 977 僅是 P7 收尾階段候選檢查點，非最後完整守門基準。
- P7 註冊清冊：live 12／pass 1／dormant 21。
- `cjk-literals.json` 專案 count 合計 3,764。

## 順序與收尾

- [ ] P8-00 開工切換與前置缺項檢查
- [x] P8-01a 奉獻類別結構與轉換指令（2026-11-20；守門 commit bfc713f2；完整證據 `C:\smv\guard-p8-evidence\guard-bfc713f2-full-rerun.log`；實機 option labels／verify 狀態未查證，依離線單元契約不阻擋驗收）
- [x] P8-01b ChurchReport 奉獻類別清單（2026-11-20；程式提交 97d260c1；完整守門 `C:\smv\guard-p8-evidence\guard-97d260c1-full.log`；離線單元驗收，goodshepherdback 狀態未查證且不再重試）
- [x] P8-02 角色代碼與字典（2026-11-20；程式提交 `e85a5784`；Core net10.0／net48 各 398/398、ChurchReport 669/669；完整守門 42 專案建置 0 錯誤、全方案測試 7,446 通過、Debug tracer 7/7、編碼 121 檔零問題、duplicate 42 專案 new/stale 皆 0、I18nCheck、檔案說明 1,852 檔零失敗、進度頁 28/28；證據 `C:\smv\guard-p8-evidence\guard-e85a5784-full.log`；紀錄型收尾／推送待完成）
- [ ] P8-01c goodshepherdback 轉換與核對（使用者最新續跑順序：先完成 P8-02，再依 08A §5 執行本單元。操作前以 `SpeechMessagePackagePlugin=true`、`SpeechMessageBuildNumber=20` 重打包兩個 Plugin DLL，通過 `Test-PluginAssembly.ps1`，再唯讀執行 `verify --org goodshepherdback`。先前 `InvalidDataException` 根因為 DLL FileVersion 仍為 1.0.0.1、組織版本為 1.0.0.19；已排除根因後屬條件改變的新操作，不計為相同條件重試。依使用者更正及 08A D8-2、§7.2、§7.3(2) 繼續；未取得新證據前不得宣稱 verify 通過或狀態為 off）
- [ ] P8-03a 教會覆寫接線與設定服務
- [ ] P8-03b 教會設定網頁
- [ ] 遺留 L1 原因補齊與唯讀調查
- [ ] 遺留 L2 Workflow 流程樣板工具
- [ ] 遺留 L3 Live 補驗與分級定案
- [ ] P8-04a 偽語系稽核工具與網頁修正
- [ ] P8-04b 假語系驗收腳本、教會自訂及 A/B 隔離
- [ ] P8-05 收尾清理與「之後再說」
- [ ] 整體驗收表、ClaudeOnly 階段審查、終點回報、上線前置清單與 AGENTS 終點文字

## 每單元執行守則

逐單元依 08A §5 讀取清單、建立 Trellis 子任務、執行指定測試紅燈與綠燈、補文件說明、提交、在 `C:\smv\guard-p8` 完成完整 00 §8.1 守門、寫一頁回報、驗收、推送及 `git ls-remote` 核對，最後只更新進度頁 DATA。若單元依 D8-26 縮減或未能驗收，清楚記錄未完成條件並按固定順序繼續。

## 終止條件

僅於 08A §7.3 所列四種情況停止。完成 §6 全部條件後停止，不建立階段 9、不開 PR、不推 main。
