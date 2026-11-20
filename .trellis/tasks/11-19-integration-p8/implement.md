# 階段 8 執行清單

## 固定基準

- 起點：`6a37f16ba111943eb023c14dea3c9a006a0f0a5c`
- 分支：`1.0.0.11.Integration.P8.Worktree`
- P7 最末完整守門：Release 7,341；Debug tracer 7。
- Deployment.Tests 977 僅是 P7 收尾階段候選檢查點，非最後完整守門基準。
- P7 註冊清冊：live 12／pass 1／dormant 21。
- `cjk-literals.json` 專案 count 合計 3,764。

## 續跑更正

2026-11-20 18:42 Asia/Taipei (UTC+08:00)：依使用者新指示完成 P8-01c 續跑。build 20 assembly plan 僅含兩筆 UpdateAssembly，apply 後 verify 符合 off；已存在 lookup 結構的本輪 plan additions=0、verify 無差異；備份 ID-only 97 筆，稽核開啟。migration plan 重現同一標籤歧義 InvalidDataException，verify 拒絕；依 D8-6/D8-7 fail closed，不 apply、不執行冪等第二次 apply 或指定 P8Live。最近完整殘留盤點 15:34 為實體及 workflow 0，此後沒有建立測試資料／流程的組織寫入；build 20 最終 verify 符合 off，舊步驟／流程 0/0。P8-01c 與 P8-01 依 D8-26 縮減驗收，部署阻擋列 P8-PROP-008。詳細結果與證據索引見 `wp-reports/P8-01c-migration.md` 18:42 段。下一單元 P8-03a。

2026-11-20 16:07 Asia/Taipei (UTC+08:00)：新會話依 08A §0.4 再執行一次唯讀 `verify --org goodshepherdback`，結束碼 0，輸出符合 `off`；安全摘要僅存於 `C:\smv\guard-p8-evidence\p8-resume-verify-20261120-160740.log`。先前 `InvalidDataException` 的原因已確認是 Plugin DLL FileVersion 未帶 build number；依使用者更正，修正組件版本後執行是條件已改變的新操作，不是相同條件重試，也不構成停止條件。

## 順序與收尾

- [ ] P8-00 開工切換與前置缺項檢查
- [x] P8-01a 奉獻類別結構與轉換指令（2026-11-20；守門 commit bfc713f2；完整證據 `C:\smv\guard-p8-evidence\guard-bfc713f2-full-rerun.log`；實機 option labels／verify 狀態未查證，依離線單元契約不阻擋驗收）
- [x] P8-01b ChurchReport 奉獻類別清單（2026-11-20；程式提交 97d260c1；完整守門 `C:\smv\guard-p8-evidence\guard-97d260c1-full.log`；離線單元驗收，未執行組織操作）
- [x] P8-02 角色代碼與字典（2026-11-20；程式提交 `e85a5784`、回報與進度提交 `a1291979`；Core net10.0／net48 各 398/398、ChurchReport 669/669；完整守門 42 專案建置 0 錯誤、全方案測試 7,446 通過、Debug tracer 7/7、編碼 121 檔零問題、duplicate 42 專案 new/stale 皆 0、I18nCheck、檔案說明 1,852 檔零失敗、進度頁 28/28；證據 `C:\smv\guard-p8-evidence\guard-e85a5784-full.log`；分支推送與 `git ls-remote` 已核對 SHA `a12919798b1687503be73f29890796c75acf1d14`）
- [x] P8-01c goodshepherdback 轉換與核對（2026-11-20 18:42 Asia/Taipei (UTC+08:00) 依 D8-26 縮減驗收：build20 assembly apply/off verify、lookup schema plan additions=0/verify、ID-only 備份 97 筆、稽核已開啟、舊步驟／流程 0/0；五組同值異標籤使 migration plan/verify fail closed，未 apply、未執行冪等第二次 apply 或指定 P8Live。依 P8-PROP-008 列新版 ChurchReport 上線阻擋與回填前置。）
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
