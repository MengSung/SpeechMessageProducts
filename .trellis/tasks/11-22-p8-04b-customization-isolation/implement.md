# P8-04b 執行清單

- [x] 先紅後綠：Isolation_ChurchAB_CultureMixed_NoCrossTalk_ResourcesReturnToBaseline（5 項離線測試均已保存紅／綠執行結果）
- [x] 先紅後綠：Pseudo_Step2_KeywordsRecognized_AllSegmentsBracketed
- [x] 先紅後綠：Pseudo_Step3_FeeNotifyWithin60s_PseudoAndFormatted
- [x] 先紅後綠：Customization_Step5_LineOverrideWithin60s_ChurchBUnaffected（離線）
- [x] 先紅後綠：Pseudo_Step6_FixedClockTwoChurchTimeZones_SameOutput
- [x] 新增 Run-CustomizationCheck.cjs 與生命週期測試（2026-11-22 11:22 Asia/Taipei (UTC+08:00)：rules＋lifecycle 26/26；P6 lifecycle 4/4；本輪新增 response timeout／HTTP 分類測試）
- [x] 依 08A §4.5、D8-20 完成網頁 A／B 自訂實機子步（2026-11-22 11:52 Asia/Taipei (UTC+08:00)）：A 名詞預覽／名單 API／類別清單讀回，B 未串接，測試類別停用並讀回、覆寫還原、Bridge 殘留 0、Secrets 還原及資源 owner 關閉均由 `cleaned=true` 摘要確認；證據 `C:\smv\guard-p8-evidence\p8-04b-customization-live-20261122-exact-unmapped-role.log`。11:54 唯讀狀態核對符合預期，證據 `p8-04b-post-customization-verify-20261122-1210.log`。此項只驗收網頁子步，不代表 16.4 或 P8-04b 整包通過。
- [x] 16.4 第 1 步：列出既有 Production＋qps-ploc 拒絕啟動測試名稱；修訂 1 不要求 OS 子程序再次啟動，既有組合根測試結果另見回報。
- [ ] 16.4 §E 第 5 步 LINE 覆寫實機：尚缺由 ChurchSettingsService 寫入 A 後，以 LineWebhook 主機組合核對 A 新詞／類別及 B 不變，並實際代送一次；離線服務契約測試不取代此項。
- [x] 離線契約重驗：`P8CustomizationIsolationTests` Release 6/6，保存於 `C:\smv\guard-p8-evidence\p8-04b-isolation-release-20261122-1550.log`。
- [x] FeeNotify 類別 lookup 修正與離線回歸：有效同教會啟用類別採用資料庫名稱；跨教會與停用類別 fail closed。Release 指定組 9/9，完整輸出 `C:\smv\guard-p8-evidence\p8-04b-fee-category-override-release-20261122-1608.log`。
- [ ] §E 第 5 步 LINE 教會覆寫實機：仍缺設定頁經 `ChurchSettingsService` 寫入 A 後，由 LineWebhook 主機組合驗證 A 新詞／類別及 B 不變，並實際代送一次。
- [x] 更新報告、決策、父任務與進度頁 DATA（2026-11-22 12:18 Asia/Taipei (UTC+08:00)：P8-04b 仍為進行中／未驗收；更正 11:22 類別停用結論，補列 A/B 子步、Production 組合根案例及 16.4 未完成缺口）
- [ ] 測試、文件、編碼、守門、提交、推送與遠端 SHA 核對（守門／提交／推送待實機缺口處置後）
