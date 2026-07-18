# 會友資訊 Session 深入註解與 UTF-8 設計

## 目標

讓後續維護者不必重走本次除錯歷程，就能從程式旁的註解理解會友資訊的權限、資料組裝、排序、搜尋狀態、行動版互動與 CRM 欄位防護，同時確保繁體中文註解以有效 UTF-8 保存。

## 註解分層

### C# 業務邏輯

- Controller 註解聚焦「先查詢、再批次授權、再組裝完整列」的安全資料流，以及生日無效預設值的正規化原因。
- District tree 註解說明區長未填的顯示與排序是兩個不同規則：牧區空白不顯示佔位字，未填區長排在所有已填區長後、無小組前。
- Search builder 註解說明以授權後 contact id 過濾、忽略大小寫去重及穩定排序，避免搜尋結果洩漏或重複。
- Access resolver 註解說明特殊職稱字串代表的全教會範圍，避免未來把它誤當純 UI 標籤移除。
- ViewModel 註解說明 DTO 契約、日期 nullable 語意與搜尋結果直接承載完整資料列的目的。

### Razor、HTML、CSS、JavaScript

- 使用檔案級導覽註解標出工具列、階層樹、資料表、搜尋狀態機、載入動畫與明細彈窗的責任。
- CSS 註解說明單列工具列、區長／小組長視覺層級、單一 DevExtreme 捲軸、原生觸控水平滑動，以及 Bootstrap 3 根字級造成 iOS focus zoom 的陷阱。
- JavaScript 註解說明 AJAX 取消與逾時、browse/results/searching 狀態切換、先恢復 UI 再 dispose grid 的順序、授權後結果直接替代表格及 loading overlay 的生命週期。
- 明細 partial 註解說明性別 OptionSet 顯示文字、生日格式化與未設定狀態。

### 測試與專案設定

- 測試註解說明每組 Arrange 資料代表的角色，以及斷言防止的具體回歸；不逐條翻譯 FluentAssertions。
- `.csproj` XML 註解說明正式 Web 專案排除 `Tests/**` 的原因，避免測試原始碼被主專案重複編譯或發佈。

## 編碼策略

- 以嚴格 UTF-8 decoder 驗證所有目標文字檔；解碼例外即失敗。
- 額外檢查 U+FFFD，避免「可解碼但內容已被替換」的假 UTF-8 成功。
- 新文件採 UTF-8；既有 UTF-8 BOM 檔案仍屬 UTF-8，不做與需求無關的全檔正規化。
- PowerShell 主控台顯示亂碼不能直接視為檔案損壞，判定一律以原始 bytes 的嚴格解碼與 `rg`/編譯結果為準。

## 非目標

- 不改功能、API、CRM 查詢、畫面外觀或測試預期。
- 不替整個 8,623 行會友資訊功能逐行加註解。
- 不修改本 session 以外的共用框架與第三方檔案。
- 不 Commit 或合併分支。

## 驗證

- 修改前後執行完整 `ChurchReport.MemberInfo.Tests`。
- 對所有目標檔執行嚴格 UTF-8、U+FFFD 與 BOM inventory。
- 執行 `git diff --check`、功能性 token 差異人工審查及 Worktree 邊界檢查。
- 由獨立檔案所有權的實作者互不重疊修改，整合後再做雙模型與主代理審查；外部模型若不可用，必須記錄實際錯誤而不得宣稱通過。
