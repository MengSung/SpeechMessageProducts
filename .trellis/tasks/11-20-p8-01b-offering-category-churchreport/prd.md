# P8-01b ChurchReport 奉獻類別清單

## 目標

依實作計畫 3.5 `08A_階段8_執行細則.md` §4.1 與 P8-01b 清單，將 ChurchReport 奉獻類別讀取、表單、建單與通知改用 P8-01a 的 `OfferingCategory` 領域型別。此單元全程離線，不執行任何組織連線或實機驗收。

## 需求與限制

- 以伺服器已驗證的登入者教會 ID 查詢奉獻類別；不得採信路由、表單或其他呼叫端自報教會 ID。不得跨教會顯示、讀取或寫入類別。
- 新收費單與認獻預約一律寫入 `new_offeringcategoryid`；若類別含 `LegacyOptionValue`，同步寫入舊選項欄。08A §9 的 D8-9 決策表已核定兩個舊選項欄為必填，故沒有舊值的類別不得建單。
- 表單選項以正式類別資料庫 ID 為值；同名但不同 ID 的類別都要保留。不得以文字作為身分、不得接受跨教會或不存在／停用的提交 ID。
- 未設定教會類別時，僅依 08A 指定規則回退到 OptionSet 中繼資料；讀取錯誤不得以硬編碼名稱掩蓋。
- 移除奉獻類別數值／文字的衝突對照，歷史類別文字依共用 `OfferingCategoryText` 決定，未知值回退通用文字，不得冒充什一／十一奉獻。
- `DonationGivingHistory.DefaultPinned` 取該教會排序前四個啟用類別；Recurring donation 訊息文字使用共用類別文字轉換。
- 維持請求／使用者隔離，不新增跨請求可變快取或資源 owner；借入的 CRM SDK 由既有呼叫端管理。
- 不修改計畫文件、部署設定或對組織進行讀寫；不放寬檢查或使用 NoWarn。

## 驗收條件

- 以下測試名稱逐字存在且通過：
  - `Read_LookupPreferredOverOptionSet`
  - `Settings_ChurchA_CannotSeeChurchB`
  - `OfferingText_ConflictingSwitchMapsRemoved`
  - `OfferingText_UnknownFallsBackToGenericText_NotTithe`
  - `Form_CategoryListFromChurch_NoHardCodedNames`
  - `Form_DefaultCategory_FirstActiveBySortOrder`
  - `Form_ChurchWithoutCategories_UsesOptionSetMetadata`
  - `Write_LookupAndLegacyOptionBothSet`
  - `Write_CategoryWithoutLegacyValue_FollowsRequiredLevelRule`
  - `Write_CategoryFromOtherChurch_Rejected`
  - `Message_CategoryText_FromCategoryName`
- ChurchReport 既有測試全綠；GoodShepherd Core 測試及 I18nCheck verify 全綠。
- 變更程式檔符合繁體中文檔案整體說明、UTF-8 無 BOM、CRLF、末尾 CRLF；`check_file_documentation.py` 通過。
- 更新 `.trellis/tasks/11-19-integration-p8/wp-reports/` 單元回報與 P8 decisions，單元提交訊息以 `P8-01b` 開頭。
- 單元提交後執行 `C:\smv\guard-p8` 完整守門並記錄一頁結果；標記驗收、推送 P8 分支並以 `git ls-remote` 比對 SHA，最後只更新進度頁 DATA。
- 不把離線測試描述成實機結果。
