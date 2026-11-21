# P8-03b 教會設定網頁

## Goal

依 08A §4.4、§5 P8-03b 建立 ChurchSettings 三頁、伺服器權限判定、CSRF/XSS 防護、偽語系資源與導覽入口；指定十個 ChurchReport.MemberInfo.Tests 必須同名、先紅後綠，離線實作。

## Requirements

- 依 D8-12，所有頁面動作（包含 GET）均須在伺服器以本次登入 contact 的資料重新判定管理員資格；管理員判定沿用 `MemberInfoAccessResolver.Resolve`，教會識別只取登入 contact 的 `parentcustomerid`。request 中任何教會識別值都忽略。非管理員或沒有有效教會的登入者一律 HTTP 403。
- 新增 `SpeechMessageProducts.ChurchReport/Areas/ChurchSettings/ChurchSettingsAccess.cs`、`ChurchSettingsController.cs` 與 `Resources/ChurchSettingsText.resx`，不新增 csproj。三頁固定路徑為 `/ChurchSettings/Terms`、`/ChurchSettings/OfferingCategories`、`/ChurchSettings/Audit`。
- 名詞頁只允許 `ChurchTermWhitelist` 的鍵；顯示系統原文、教會覆寫及預覽，支援還原預設。長度與模板參數驗證沿用既有 I18nCheck 限制及 `ChurchTextOverrideValidator`／`ChurchSettingsService`，不可複製第二份規則。
- 奉獻類別頁只讀寫目前授權教會的類別；支援新增、改名、停用／啟用、排序、會計科目及舊選項值。新代碼由核心服務產生 `custom-` 加 8 位小寫十六進位，並保持教會範圍唯一。
- 稽核頁只呈現目前授權教會最近 200 筆紀錄，修改者只顯示 D365 使用者顯示名稱；資料來源必須先以教會範圍查詢。
- 所有寫入 POST 使用 antiforgery 驗證；Razor 使用預設 HTML 編碼，不以 `Html.Raw` 輸出輸入值；JSON 以標準序列化器輸出並保留編碼。
- `ChurchSettingsText.resx` 為 zh-TW 唯一來源，英文空殼與 qps-ploc 由 I18nCheck 規則產生；語系解析依既有 `MemberCultureResolver` 與教會啟用語系。Production 必須拒絕 qps-ploc。ChurchReport CJK literal ratchet 不得增加。
- 在既有共用導覽加入本地化「教會設定」入口，只有伺服器判定為管理員時顯示；導覽隱藏不是端點授權。
- 所有新增或實質修改的 C#、Razor 檔須含完整繁中檔案整體說明與成員文件，UTF-8 無 BOM、CRLF、檔尾 CRLF。

## Acceptance Criteria

- [ ] 指定十個測試以原名存在於 `ChurchReport.MemberInfo.Tests`，先證明功能缺少時紅燈，再實作為綠燈：
  `Save_NonAdmin_Forbidden`、`Save_ChurchIdFromRequestIgnored`、`Save_ScriptInput_EncodedOnOutput`、`Get_NonAdmin_Forbidden`、`Get_LoginWithoutChurch_Forbidden`、`Post_WithoutAntiforgery_Rejected`、`ChurchSettings_QpsPloc_AllVisibleTextFromResource`、`ChurchSettings_ProductionRejectsQpsPloc`、`OfferingCategories_PageShowsOnlyThisChurch`、`Audit_PageShowsOnlyThisChurch`。
- [ ] 三頁面、伺服器授權、教會範圍隔離、CSRF/XSS 防護、本地化資源及管理員專屬導覽均符合 §4.4。
- [ ] 執行 08A §5 P8-03b 列出的 ChurchReport.MemberInfo.Tests、GoodShepherd.Core.Tests、LineWebhook.Tests、Hosting Architecture 測試與 I18nCheck；程式碼、文件與編碼檢查通過。
- [ ] 完成 P8-03b 一頁回報、驗收及進度頁 DATA 更新；以單元編號開頭提交，在唯一 `C:\smv\guard-p8` 完整守門，推送 P8 分支並用 `git ls-remote` 核對 SHA。
- [ ] 全程離線，不連 D365／LINE；不增加 ChurchReport CJK literal ratchet，不新增專案，不執行單元級 CCG 或外審。

## Notes

- Keep `prd.md` focused on requirements, constraints, and acceptance criteria.
- 此為跨 Controller、服務、Razor、資源與測試的複雜單元；依 repo 已核准的 P8 執行授權直接完成 planning artifacts 並啟動，不另詢問。
