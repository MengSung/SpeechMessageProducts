# 設計：P8-03 驗收後修正

## 範圍與資料流

修正只涵蓋 ChurchReport 的 Area Razor 發佈項目、MVC Area metadata 及 Terms GET 的文字覆寫快照載入。每個 HTTP request 仍先由 `ChurchSettingsAccess` 重新解析登入 contact 與 account 教會範圍；快照只在該授權 scope 建立後載入，之後同步 catalog 讀取完整不可變快照。Access 借用 Host 擁有的 cache，不另行建立或 Dispose cache。

MSBuild 測試直接評估 ChurchReport 專案的 Content 項目，確認特定 Area `.cshtml` 檔被展開並指定 `CopyToPublishDirectory`。MVC 測試從正式 application part 取得 `ControllerActionDescriptor`，以其 area route value 建立 `ActionContext`，再要求 composite view engine 找到 Terms 的實際 Razor 路徑。

## 隔離與資源生命週期

Terms 測試只使用固定合成 contact、account 與單一合成覆寫列；cache 由測試建立並以 `await using` 排空及釋放。MVC service provider、DiagnosticListener 與 MSBuild process 均為案例區域資源；MSBuild 等待上限為兩分鐘，逾時時終止整個 process tree。測試不建立 Web Host、不連 D365／LINE、不讀秘密，也不共享快取或身份到其他案例。

## 黃金與相容性

`[Area]` 會改變既有 R2 route-table metadata；Area Razor 被正式納入內容後會增加編譯檢視型別。使用 `R2VisibilityContractTests` 既有且明確啟用的產生入口更新 Debug／Release 黃金；更新後再以未啟用產生旗標的測試執行唯讀逐位元組比對。不得以刪除型別、篩選範圍或豁免斷言處理差異。
