# 階段 8 執行清單

## 固定基準

- 起點：`6a37f16ba111943eb023c14dea3c9a006a0f0a5c`
- 分支：`1.0.0.11.Integration.P8.Worktree`
- P7 最末完整守門：Release 7,341；Debug tracer 7。
- Deployment.Tests 977 僅是 P7 收尾階段候選檢查點，非最後完整守門基準。
- P7 註冊清冊：live 12／pass 1／dormant 21。
- `cjk-literals.json` 專案 count 合計 3,764。

## 續跑更正

2026-11-20 15:34 Asia/Taipei (UTC+08:00)：續跑 P8-01c 已完成兩個指定 lookup 欄 schema plan/apply/verify，並確認組織稽核已開啟、15 個實體記錄與 workflow 殘留均為 0、唯讀 verify 符合 `off`。qrcodes live-check 到 `LIVE-PREP-04`；D34 子檢查通過，但整體 Live 未通過。migration plan 因五組 metadata labels 衝突 fail closed，未執行資料 apply、冪等 apply 或指定 P8Live；P8-01c/P8-01 維持未驗收，依 D8-26 接續 P8-03a。使用者更正後的 build 20 verify 是排除已查證 FileVersion 根因的新操作。

## 順序與收尾

- [ ] P8-00 開工切換與前置缺項檢查
- [x] P8-01a 奉獻類別結構與轉換指令（2026-11-20；守門 commit bfc713f2；完整證據 `C:\smv\guard-p8-evidence\guard-bfc713f2-full-rerun.log`；實機 option labels／verify 狀態未查證，依離線單元契約不阻擋驗收）
- [x] P8-01b ChurchReport 奉獻類別清單（2026-11-20；程式提交 97d260c1；完整守門 `C:\smv\guard-p8-evidence\guard-97d260c1-full.log`；離線單元驗收，未執行組織操作）
- [x] P8-02 角色代碼與字典（2026-11-20；程式提交 `e85a5784`、回報與進度提交 `a1291979`；Core net10.0／net48 各 398/398、ChurchReport 669/669；完整守門 42 專案建置 0 錯誤、全方案測試 7,446 通過、Debug tracer 7/7、編碼 121 檔零問題、duplicate 42 專案 new/stale 皆 0、I18nCheck、檔案說明 1,852 檔零失敗、進度頁 28/28；證據 `C:\smv\guard-p8-evidence\guard-e85a5784-full.log`；分支推送與 `git ls-remote` 已核對 SHA `a12919798b1687503be73f29890796c75acf1d14`）
- [ ] P8-01c goodshepherdback 轉換與核對（P8-02 已驗收後回頭執行。build 20 assembly 更新已核對；lookup 結構 plan/apply/verify 只涉及兩個指定查閱欄，組織稽核已開啟。15:30 唯讀 verify 符合 `off`；15:34 殘留盤點 15 個實體記錄 0、workflow 0。qrcodes live-check 到 `LIVE-PREP-04`，D34 子檢查通過但整體 Live 未通過。migration plan 因五組共享值標籤不同 fail closed；不猜標籤、不執行資料 apply、冪等 apply 或指定 P8Live。RequiredLevel 均 None。P8-01c/P8-01 未驗收；詳細證據與剩餘事項見 `P8-01c-migration.md`。依 D8-26 接續 P8-03a。）
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
