# P8-02 角色代碼與字典

## 目標

依實作計畫 3.5 的 `08A_階段8_執行細則.md` §4.2、§5 P8-02，將新功能使用的會籍角色識別改為穩定代碼，角色顯示文字改由 GoodShepherd 字典及教會覆寫文字目錄提供，同時保留 ChurchReport 既有 D365 標籤存檔契約。

## 已核定需求與限制

- 新增 `PastorCouple`、`ZoneShepherd`、`ZoneLeader`、`GroupLeader`、`AssistantGroupLeader`、`CoreWorker`、`Member`、`NotInGroup`、`Newcomer`、`OtherChurch`、`Closed` 共 11 個角色代碼及相應 `Role.*` 字典鍵。
- `contact.customertypecode` 選項值與角色代碼對照只能根據 goodshepherdback 的唯讀 metadata 建立；metadata 黃金檔只保存選項值及標籤，不含會友資料。無法匹配名稱時，保留角色代碼、選項值留空並列明原因；未知選項不得取得代碼或權限，畫面顯示 D365 原始標籤。
- 角色顯示名稱透過固定字典及 `ITextCatalog` 取得；ChurchReport 本單元只接入空覆寫提供者，真正教會覆寫留至 P8-03a。
- `GetMembershipStatusList` 保留既有 `data`，另回傳 `items`；每筆 `value` 為 D365 原始標籤、`text` 為字典顯示名稱、`code` 為角色代碼鍵或空值。存檔仍使用 `value`，不得將顯示文字或選項整數寫回 D365。
- `_GeneralGroupGrids.cshtml` 兩個 lookup 使用 `items`、`valueExpr: "value"`、`displayExpr: "text"`；Ajax 失敗備用選項由伺服器 RoleCatalog 產生，兩份 ChurchReport 目標檔不得保留 11 個角色中文字面值。
- ChurchReport 其他以中文標籤判斷角色的既有業務程式不在本單元修改，位置登錄 `P8-PROP-004`。
- 全程不連正式組織、不執行寫入或實機驗收、不保存或輸出任何憑證、會友資料或連線資訊。

## 驗收條件

- 08A §5 P8-02 列出的 9 個測試以同名存在並通過：GoodShepherd.Core 兩目標 6 個、ChurchReport.MemberInfo.Tests 3 個。
- I18nCheck 產生器能確定性產生 11 個 zh-TW 角色資源及 en 空殼；`verify` 成功。
- 兩個 lookup 使用可見文字欄位，但提交值仍為原 D365 標籤；既有 `data` 相容欄位保留。
- 兩個 ChurchReport 指定來源檔不含 11 個角色中文字面值，角色權限不依賴顯示文字。
- 變更過的 C#／Razor 檔均有符合實際用途的繁體中文檔案整體說明、成員文件，並以 UTF-8 無 BOM、CRLF、末尾 CRLF 儲存。
- 完成指定測試、適用建置、I18nCheck、文件說明檢查、位元組檢查及 `C:\smv\guard-p8` 完整守門；提交、單頁回報、驗收後推送 P8 分支並以 `git ls-remote` 核對，再只更新進度頁 DATA。

## 範圍外

- 不改 ChurchReport 其他約 40 處既有中文標籤業務判斷。
- 不在 P8-02 實作教會文字覆寫資料來源、輪詢、設定頁或任何資料寫入。
- 不改 08A 以外計畫文件、不調整既有測試期望以外的行為、不新增專案或依賴。

## 未能執行之步驟

若本機無法在不洩漏秘密、且已確認只會連 goodshepherdback 的條件下完成一次唯讀 metadata 讀取，依 08A §7.2 跳過該步驟；不得從先前推論或其他組織資料捏造黃金檔。保留未知角色的 fail-closed 行為，記錄需要的授權／metadata 證據，並按固定順序繼續其他單元；不可宣稱 P8-02 完成，除非其驗收條件皆有證據。
