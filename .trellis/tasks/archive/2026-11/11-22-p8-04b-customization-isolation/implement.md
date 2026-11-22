# P8-04b 執行清單

- [x] 先紅後綠：Isolation_ChurchAB_CultureMixed_NoCrossTalk_ResourcesReturnToBaseline（5 項離線測試均已保存紅／綠執行結果）
- [x] 先紅後綠：Pseudo_Step2_KeywordsRecognized_AllSegmentsBracketed
- [x] 先紅後綠：Pseudo_Step3_FeeNotifyWithin60s_PseudoAndFormatted
- [x] 先紅後綠：Customization_Step5_LineOverrideWithin60s_ChurchBUnaffected（離線）
- [x] 先紅後綠：Pseudo_Step6_FixedClockTwoChurchTimeZones_SameOutput
- [x] 新增 Run-CustomizationCheck.cjs 與生命週期測試（2026-11-22 11:22 Asia/Taipei (UTC+08:00)：rules＋lifecycle 26/26；P6 lifecycle 4/4；本輪新增 response timeout／HTTP 分類測試）
- [x] 依 08A §4.5、D8-20 完成網頁 A／B 自訂實機子步（2026-11-22 11:52 Asia/Taipei (UTC+08:00)）：A 名詞預覽／名單 API／類別清單讀回，B 未串接，測試類別停用並讀回、覆寫還原、Bridge 殘留 0、Secrets 還原及資源 owner 關閉均由 `cleaned=true` 摘要確認；證據 `C:\smv\guard-p8-evidence\p8-04b-customization-live-20261122-exact-unmapped-role.log`。11:54 唯讀狀態核對符合預期，證據 `p8-04b-post-customization-verify-20261122-1210.log`。此項只驗收網頁子步，不代表 16.4 或 P8-04b 整包通過。
- [x] 16.4 第 1 步：列出既有 Production＋qps-ploc 拒絕啟動測試名稱；修訂 1 不要求 OS 子程序再次啟動，既有組合根測試結果另見回報。
- [x] 16.4 §E 第 5 步 LINE 覆寫實機（2026-11-22 18:32 Asia/Taipei (UTC+08:00)）：P8Live `Customization_Step5_LineOverrideWithin60s_ChurchBUnaffected` Release 1/1 通過；ChurchSettingsService 寫入 A 後，LineWebhook 主機以伺服器回應的 `UniqueName` 建立文字 scope，驗證 A 新詞／類別、B 不變並完成一次代送。證據 `C:\smv\guard-p8-evidence\p8-04b-customization-live-orgscopefix-20261122-1832.log`；後續唯讀 verify exit 0、狀態符合 off，證據 `p8-04b-customization-postlive-verify-orgscopefix-20261122-1845.log`。
- [x] 離線契約重驗：`P8CustomizationIsolationTests` Release 6/6，保存於 `C:\smv\guard-p8-evidence\p8-04b-isolation-release-20261122-1550.log`。
- [x] FeeNotify 類別 lookup 修正與離線回歸：有效同教會啟用類別採用資料庫名稱；跨教會與停用類別 fail closed。Release 指定組 9/9，完整輸出 `C:\smv\guard-p8-evidence\p8-04b-fee-category-override-release-20261122-1608.log`。
- [x] 本機全方案重驗（2026-11-22 16:30 Asia/Taipei (UTC+08:00)）：Release build exit 0、0 錯誤；22 個測試目標 7,551/7,551 通過。`LineWebhook.Tests` 預設 runsettings 未排除 P8Live，因此此命令包含一個未明確篩選的 P8Live 個案；逐項殘留數未保存，不列為 P8Live 驗收。後續唯讀 verify exit 0、狀態符合預期目標。證據 `C:\smv\guard-p8-evidence\p8-04b-local-release-build-eac65089-20261122-1620.log`、`p8-04b-local-release-tests-eac65089-20261122-1622.log`、`p8-04b-unintended-solution-live-test-postverify-20261122-1626.log`。不是正式 guard。
- [x] 本機補充 §8.1 檢查（2026-11-22 16:35 Asia/Taipei (UTC+08:00)）：Debug tracer 7/7；Changed 編碼 261 檔、issues 0（修正一份 Trellis 設計文件的換行）；duplicate scan 42 專案／1566 檔、groups/new/stale 0；I18nCheck 通過；ProgressPage 28/28；檔案說明 1902/1902。均為工作樹本機結果，不是固定 guard。
- [x] §E 第 5 步 LINE 教會覆寫實機已完成；同上 P8Live 1/1 與 post-live verify 證據。
- [x] 更新報告、決策、父任務與進度頁 DATA（2026-11-22 12:18 Asia/Taipei (UTC+08:00)：P8-04b 仍為進行中／未驗收；更正 11:22 類別停用結論，補列 A/B 子步、Production 組合根案例及 16.4 未完成缺口）
- [ ] 測試、文件、編碼、守門、提交、推送與遠端 SHA 核對（固定 guard 因會清理受保護副本，依決策紀錄未執行；其餘提交／推送待本單元收尾）
