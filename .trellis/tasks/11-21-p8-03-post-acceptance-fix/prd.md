# P8-03 驗收後修正：Area 發佈與預覽載入

## Goal

修正 ChurchSettings Area Razor 發佈、Terms GET 冷覆寫快照載入與 MVC Area view discovery；依使用者核定以獨立 P8-03 fix 驗收，不改動 L1 檔案。

## Requirements

- 將 ChurchReport Web 專案的明確 Content glob 修正為涵蓋 `Areas\**\*.cshtml`，並以實際 MSBuild Content 項目評估確認 Area 的 Terms view 帶有 `CopyToPublishDirectory` metadata。
- ChurchSettings controller 必須宣告 `Area("ChurchSettings")`；以正式 MVC action descriptor 的 area route value 驗證 Razor view engine 找到預期 Area 路徑。
- Terms GET 在呼叫同步文字 catalog 預覽前，必須先以已授權的完整教會 scope 載入覆寫快照。冷快取測試必須使用正式 `ChurchTextOverrideCache` 與產品 catalog，並驗證呈現覆寫文字。
- 三項指定測試必須同名存在且各自先紅後綠；紅燈須由三項產品缺陷造成，不接受編譯失敗或測試替身設定錯誤作為紅燈證據。
- 保留 R2 的唯讀黃金比對。只有 Area metadata 及因此納入編譯的 Area Razor 生成型別造成的差異可由既有產生入口更新；不可略過或放寬 R2。
- 僅處理本次 P8-03 fix、其必要 R2 黃金、此子任務及 `wp-reports/P8-03b-教會設定網頁.md` 的「驗收後修正」追加段；不得納入 L1 變更、既有 `.turns.json` 掛鉤紀錄或四個 P3 副本。

## Acceptance Criteria

- [x] `Publish_AreaRazorViews_IncludedInPublishOutput` 以實際 MSBuild evaluation 驗證 Area Terms view 發佈項目及 `CopyToPublishDirectory`。
- [x] `Terms_Get_LoadsOverrideSnapshotBeforePreview` 從冷快取呼叫 Terms GET，回傳產品 catalog 解析出的教會覆寫預覽。
- [x] `Area_Views_ResolvedUnderChurchSettingsArea` 由 MVC 的實際 `ControllerActionDescriptor` 與 composite view engine 驗證 Area route metadata 及命中 view 路徑。
- [x] 三項測試本次先紅後綠；紅燈及綠燈原始輸出存於 `C:\smv\guard-p8-evidence`。
- [x] R2 Debug／Release 黃金依既有環境變數入口產生並保持唯讀比對；沒有改變測試斷言或排除範圍。
- [x] ChurchReport.MemberInfo.Tests Release 全套通過；完整 §8.1 guard、程式提交與報告追加完成。推送因目前分支包含未驗收 L1 祖先而延後，依本次核定執行清單 §5 記錄後接續 L1。

## Notes

- Keep `prd.md` focused on requirements, constraints, and acceptance criteria.
- Lightweight tasks can remain PRD-only.
- For complex tasks, add `design.md` for technical design and `implement.md` for execution planning before `task.py start`.
