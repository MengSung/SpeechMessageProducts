# 階段 8 執行清單

## 固定基準

- 起點：`6a37f16ba111943eb023c14dea3c9a006a0f0a5c`
- 分支：`1.0.0.11.Integration.P8.Worktree`
- P7 最末完整守門：Release 7,341；Debug tracer 7。
- Deployment.Tests 977 僅是 P7 收尾階段候選檢查點，非最後完整守門基準。
- P7 註冊清冊：live 12／pass 1／dormant 21。
- `cjk-literals.json` 專案 count 合計 3,764。

## 續跑更正

2026-11-20 20:54 Asia/Taipei (UTC+08:00)：歷史續跑紀錄曾依當時判斷將 P8-01c 與 P8-01 暫列 D8-26 縮減驗收；此結論已於 20:15 依使用者核定 D8-6 更正。D8-6 plan/apply/兩次 verify、第二次 apply 無 lookup 更新批次、指定 P8Live 1/1、D34 子檢查、off verify=0 及殘留基線 0 均已有證據。P8-01c 待本次提交完整守門後正式標記與推送；其後才按固定順序接續 P8-03a。先前 verify 的 FileVersion 根因已確認；排除該根因後的 verify 是條件改變的新操作，不屬相同條件重試。

2026-11-20 16:07 Asia/Taipei (UTC+08:00)：新會話依 08A §0.4 再執行一次唯讀 `verify --org goodshepherdback`，結束碼 0，輸出符合 `off`；安全摘要僅存於 `C:\smv\guard-p8-evidence\p8-resume-verify-20261120-160740.log`。先前 `InvalidDataException` 的原因已確認是 Plugin DLL FileVersion 未帶 build number；依使用者更正，修正組件版本後執行是條件已改變的新操作，不是相同條件重試，也不構成停止條件。

## 順序與收尾

- [x] P8-00 開工切換與前置缺項檢查（2026-11-19；原提交 `29740bf5`、`26e87280`）
- [x] P8-01a 奉獻類別結構與轉換指令（2026-11-20；守門 commit bfc713f2；完整證據 `C:\smv\guard-p8-evidence\guard-bfc713f2-full-rerun.log`；實機 option labels／verify 狀態未查證，依離線單元契約不阻擋驗收）
- [x] P8-01b ChurchReport 奉獻類別清單（2026-11-20；程式提交 97d260c1；完整守門 `C:\smv\guard-p8-evidence\guard-97d260c1-full.log`；離線單元驗收，未執行組織操作）
- [x] P8-02 角色代碼與字典（2026-11-20；程式提交 `e85a5784`、回報與進度提交 `a1291979`；Core net10.0／net48 各 398/398、ChurchReport 669/669；完整守門 42 專案建置 0 錯誤、全方案測試 7,446 通過、Debug tracer 7/7、編碼 121 檔零問題、duplicate 42 專案 new/stale 皆 0、I18nCheck、檔案說明 1,852 檔零失敗、進度頁 28/28；證據 `C:\smv\guard-p8-evidence\guard-e85a5784-full.log`；分支推送與 `git ls-remote` 已核對 SHA `a12919798b1687503be73f29890796c75acf1d14`）
- [x] P8-01c goodshepherdback 轉換與核對（2026-11-20 21:46 Asia/Taipei；D8-6 migration/apply/verify/冪等性、指定 P8Live、off／verify=0／殘留0皆完成；程式提交 `4000b3f8f6e86d247ef31805bee49391909be76a` 唯一 guard-p8 完整 §8.1 全通過，證據 `C:\smv\guard-p8-evidence\guard-4000b3f8-full-rerun.log`。第二次 apply 的零筆數指無 lookup 更新批次；category alternate-key Upsert 仍會呼叫。回報及 DATA 已更新；待本收尾提交推送並核對遠端 SHA。）
- [x] P8-01 整包驗收：P8-01a、P8-01b、P8-01c 均已驗收；P8-PROP-003 登錄五組 metadata 標籤差異，正式組織部署仍須先轉換並回填。
- [x] P8-03a 教會覆寫接線與設定服務（2026-11-21 00:08 Asia/Taipei (UTC+08:00)；程式提交 `9850d0ab`、`27cd4eb6`；18 個指定測試同名存在並通過，Core net10.0／net48 各 411、ChurchReport 671、LineWebhook 130；完整 §8.1 守門通過，42 專案 Release 建置 0 錯誤、全方案測試 0 失敗、Debug tracer 7/7、編碼 170 檔零問題、重複程式 groups/new/stale 皆 0、I18nCheck、檔案說明 1,866 檔零失敗、ProgressPage 28/28；證據 `C:\smv\guard-p8-evidence\guard-27cd4eb6-full.log`；回報 `wp-reports/P8-03a-教會覆寫接線與設定服務.md`；全程離線，未連 D365／LINE。）
- [x] P8-03b 教會設定網頁（2026-11-21；程式提交 `b648dd77`；唯一 `C:\smv\guard-p8` 完整 §8.1 守門全通過；收尾提交 `b64902d71941f668f825d37c63a43beb1b6d12ea` 已推送並以 `git ls-remote` 核對；報告 `wp-reports/P8-03b-教會設定網頁.md`）
- [x] P8-03 驗收後修正（2026-11-21 11:14 Asia/Taipei (UTC+08:00)；`c8d088fe11c9b45565dd0e149f98b8b2e04ed480` 修正 Area Razor 發佈、Terms 冷快取及 Area view discovery；三項先紅後綠、ChurchReport Release 693/693、R2 Debug／Release 各 2/2；唯一 guard-p8 完整 §8.1 結束碼 0，證據 `C:\smv\guard-p8-evidence\guard-c8d088fe-full.log`。報告已追加事後修正段。11:14 暫緩推送的決定已被 2026-11-21 11:39 Asia/Taipei (UTC+08:00) 依使用者最新指示更正；修正與推送核對紀錄已分別提交，11:39 遠端 SHA `6f68c68c062486b513ca2426731326c7bf0bdc4a` 後續於 2026-11-21 12:18 Asia/Taipei (UTC+08:00) 被重核 SHA `4ed8a87236ee79ed9896845b527acc64ae18832d` 更正為目前本機／遠端一致值。守門清理事故另記 P8-decisions。）
- [x] 遺留 L1 原因補齊與唯讀調查（2026-11-21 20:48 Asia/Taipei (UTC+08:00)：依使用者核定 08A 修訂 1「已驗收（縮減）」。程式成果沿用 `c819da1e`；Relay 測試實際在 LineWebhook.Tests 且僅覆蓋 formatter、六項先紅原始輸出未找到；R7 目前證據不足、R9 去向無法確認、church-defaults 目前證據不足，均列上線前作業，不宣稱技術通過。L1 原提交早於 P8-03 fix，且 L2 提交 `88f806bc` 先於本結案紀錄，歷史順序偏差保留揭露。Trellis archive 自動建立非 `P8-L1` 提交 `35680dd9`，本機未推送，未改寫；後續以 `P8-L1` 提交本結案紀錄。詳見 `wp-reports/P8-L1-狀態與證據.md`。）
- [x] 遺留 L2 離線活動輸入契約與身份檢核（2026-11-21 22:32 Asia/Taipei (UTC+08:00) 完成證據核對；21:34 僅為修訂 1 範圍核定時間，已於 22:37 更正。依核定範圍縮減結案：保留可編譯、有測試且全方案 Release 通過的 PE input contract、shape projector、strong-name／輸入驗證及相關 CLI／測試；單元 Release 7,535/7,535，完整守門 checkout `477c8136147d7168ebc8faf869a8e1761004db93` 的 23 個測試目標 7,542/7,542、Debug tracer 7/7、ProgressPage 28/28、文件說明 1,891 檔零失敗。證據 `C:\smv\guard-p8-evidence\guard-477c8136-full.log`、`guard-477c8136-full-output.log`。真實 XAML golden、builder、完整十案、PREP-09／10、新流程 lifecycle、Appointment 情境及 goodshepherdback 實機移至好牧人 9.1 上線前作業；七活動維持 dormant，LiveActivityProbe 與 registration tier/evidence 不變。最新可核證 verify 為 20:15 exit 1、僅 UpdateAssembly，不宣稱 `off`／`verify=0`。細節見 `wp-reports/P8-L2-workflow-template.md`。）
- [x] 遺留 L3 Live 補驗範圍處置（2026-11-21 22:45 Asia/Taipei (UTC+08:00)：依 goal-objective.md 修訂 1 不執行 Live 切換／補驗；因無 Live evidence 的項目不能切入正式組織，延後不產生部署風險。`plugins.registration.json` 的 tier/evidence/reason 保持原樣；七個 dormant Workflow 活動與 L3 模組實機移至好牧人 9.1 上線前作業。此為範圍處置完成，不代表 Live 通過或組織狀態驗收。詳見 `wp-reports/P8-L3-live-recheck.md`。）
- [ ] P8-04a 偽語系稽核工具與網頁修正
- [ ] P8-04b 假語系驗收腳本、教會自訂及 A/B 隔離
- [ ] P8-05 收尾清理與「之後再說」
- [ ] 整體驗收表、ClaudeOnly 階段審查、終點回報、上線前置清單與 AGENTS 終點文字

## 每單元執行守則

2026-11-21 17:53 Asia/Taipei (UTC+08:00)：遺留 L2 繼續進行中、未驗收。新會話唯讀 verify exit 1、僅 `UpdateAssembly`，摘要 `C:\smv\guard-p8-evidence\p8-l2-resume-verify-20261121-1753.log`；不 apply、不宣稱 `off`／`verify=0`。Repo 搜尋沒有找到可替代的核定真實 XAML golden；現有 Appointment 情境是 Plugin，沒有 AppointmentSigningActivity Workflow 案例，因此不猜測固定值 binding 或 EntityReference 輸入格式。十案只有兩案同名存在，L2 必做仍缺，狀態／決策／進度已同步，依 D8-26 在單元未到期前繼續可驗證工作。

2026-11-21 18:29 Asia/Taipei (UTC+08:00)：L2 投影器先紅後綠修正 OutArgument 誤分類；重新唯讀查詢仍為 20 個候選、11 個 InArgument record-field、0 個 fixed-value／雙繫結。完整 Deployment Release 1056/1056。舊計數判斷已在原回報處更正；XAML golden、builder、十案中八案、probe lifecycle、七活動情境、實機與完整 guard 仍未完成，L2 維持未驗收並繼續。

逐單元依 08A §5 讀取清單、建立 Trellis 子任務、執行指定測試紅燈與綠燈、補文件說明、提交、在 `C:\smv\guard-p8` 完成完整 00 §8.1 守門、寫一頁回報、驗收、推送及 `git ls-remote` 核對，最後只更新進度頁 DATA。若單元依 D8-26 縮減或未能驗收，清楚記錄未完成條件並按固定順序繼續。

2026-11-21 20:15 Asia/Taipei (UTC+08:00)：L2 續跑唯讀 `verify --org goodshepherdback` exit 1、唯一差異 `UpdateAssembly`；未 apply，不宣稱 `off`／`verify=0`。證據 `C:\smv\guard-p8-evidence\p8-l2-resume-verify-20261121-2015.log` SHA-256 `7BF132F5387AF37110F75EED70391135F58B489366244D3D2B37FE40C8D6B433`。狀態與缺口詳見 L2 子任務、一頁回報及 `P8-decisions.md`；L2 繼續進行中、未驗收。

2026-11-21 20:24 Asia/Taipei (UTC+08:00)：續核既有 20:25 唯讀 workflow-shape 輸出；唯一候選20、輸入 record-field 11、fixed-value 0、雙繫結0。脫敏診斷仍非 golden，SHA-256 與完整缺口見 L2 回報及 `P8-decisions.md`。沒有新的組織查詢或寫入；L2 維持進行中、未驗收。

2026-11-21 19:29 Asia/Taipei (UTC+08:00)：strong-name 指定測試先紅後綠；完整 Deployment Release 1,058/1,058。十項指定案例目前三案同名、七案缺失，L2 維持 active、未驗收；具體紅綠與差異見 `wp-reports/P8-L2-workflow-template.md`。

2026-11-21 19:50 Asia/Taipei (UTC+08:00)：全 P7 情境樹遞迴掃描測試先紅後綠；完整樹確認六個核定案例及 Appointment Workflow 案例缺失，未新增情境；WorkflowTemplate 篩選6/6、Deployment Release 1,059/1,059。舊投影有 OutArgument 誤分類，不升格 golden；L2 維持未驗收，詳見一頁回報與 P8-decisions。

2026-11-21 19:05 Asia/Taipei (UTC+08:00)：遺留 L2 續跑完整 Deployment Release 通過 1,057/1,057；ProgressPage 28/28、檔案說明 1,890 檔零失敗、`git diff --check` exit 0，並逐 byte 核對 18 份自有變更檔。證據雜湊及逐項缺口見 `wp-reports/P8-L2-workflow-template.md`。08A 十案缺八案、沒有合格 golden、builder 未輸出 XAML、probe lifecycle／七活動完整 inputs／實機／`off`／`verify=0`／零殘留與安全完整 guard 未完成；L2 維持 active、未驗收，依 D8-26 繼續，不把局部綠燈當驗收。

2026-11-21 21:34 Asia/Taipei (UTC+08:00)：上述 L2 未驗收狀態已依 08A 修訂 1 的核定範圍改為預定縮減結案；原時間戳不代表當時已完成測試或守門。此結論已於 2026-11-21 22:37 Asia/Taipei (UTC+08:00) 更正：21:50 全方案 Release 為 7,535/7,535；22:21 完整守門通過，22:32 保存的同 SHA 逐命令輸出為 7,542/7,542。XAML golden／builder、完整十案、PREP-09／10、新 probe lifecycle、七活動完整情境及實機明確列入好牧人 9.1 上線前作業；七活動維持 dormant，LiveActivityProbe 與 registration tier/evidence 不變。最新可核證 verify 仍為 20:15 exit 1、唯一 UpdateAssembly，不宣稱 off／verify=0。完整證據見 `wp-reports/P8-L2-workflow-template.md`。

## 終止條件

僅於 08A §7.3 所列四種情況停止。完成 §6 全部條件後停止，不建立階段 9、不開 PR、不推 main。
