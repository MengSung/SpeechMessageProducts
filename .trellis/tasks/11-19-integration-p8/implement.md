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
- [ ] P8-01b ChurchReport 奉獻類別清單
- [ ] P8-01c goodshepherdback 轉換與核對
- [ ] P8-02 角色代碼與字典
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
