# P8-03b 技術設計

## 邊界與入口

在既有 ChurchReport MVC Host 加入 `ChurchSettings` Area；新增 `ChurchSettingsAccess` 作為每次 action 的授權與教會解析入口，新增 `ChurchSettingsController` 作為頁面／POST 邊界，核心規則全部委派給 P8-03a 的 `ChurchSettingsService`、`ChurchTermWhitelist`、`ChurchTextOverrideValidator`。不新增 csproj、不建立第二套驗證器、不建立控制器級共享使用者或教會狀態。

路由固定為 `/ChurchSettings/Terms`、`/ChurchSettings/OfferingCategories`、`/ChurchSettings/Audit`。Area 的 Razor Views 與中性資源檔放在 `Areas/ChurchSettings/`；資源產生碼只由 I18nCheck 產生及 deterministic check，不手改 `.g.cs`。

## 授權與資料流

1. 每次 action 由當前 authenticated principal 取得登入 contact 的伺服器可信識別，再以既有 ChurchReport 資料存取方式載入 contact 欄位；不可採用 query、form、route、JSON 或 ViewModel 內的 church ID。
2. 由伺服器載入的 `new_church_jobtitle` 與既有登入類型呼叫 `MemberInfoAccessResolver.Resolve`。只有結果與 `MemberInfoAccess.Church` ordinal 相同才是管理員；其他結果 fail closed。
3. 同一筆伺服器載入的 contact 必須有 `parentcustomerid`，LogicalName 必須為 `account` 且 ID 非空，否則回 403。以該值建立已授權教會與完整 `TextContext`；這些物件只在本次 request 存活。
4. 管理員確定後才讀取本教會覆寫、類別或稽核。傳入核心服務的教會取自上述伺服器解析結果；修改表單不含可授權教會欄位。`ChurchSettingsService` 再驗證完整 scope 和正式記錄 ID。
5. DI 只借用既有 ChurchReport manager、cache、store 及服務生命週期；controller 不持有 static/request 外狀態，不建立長期連線、背景工作或另一個快取。取消沿用 request token。

## 頁面與輸出

- Terms：從白名單建立有限候選，只顯示系統原文、該教會原始覆寫與解析預覽；文字以既有 catalog 和授權 context 取得。Save/Reset 再授權，將 key 與內容送入既有服務。超長字串、非法語系或模板參數錯誤由既有 validator 拒絕。
- OfferingCategories：只讀服務回傳的該教會最多 200 筆純值；以正式 category ID 作表單識別，不以名稱或索引定位。新增與更新均重新授權；服務產生唯一代碼並確認 category 歸屬。
- Audit：只顯示既有服務回傳的最近 200 筆同教會稽核純值；actor 僅投影 D365 顯示名稱，不輸出帳號、contact ID 或 SDK Entity。
- POST 標註 antiforgery 驗證；Razor 動態文字維持自動編碼且不使用 `Html.Raw`。若頁面需要 JSON，使用 MVC JSON result 及預設 encoder，不手工拼 JSON 或停用 HTML escaping。
- 本地化由 `MemberCultureResolver` 依登入偏好及教會啟用語系解析；Area 全部可見固定文字都來自 `ChurchSettingsText`。qps-ploc 僅限非 Production，Production 啟動驗證沿用現有 localization 啟動政策。
- 共用導覽只以 `ChurchSettingsAccess` 的伺服器結果控制連結可見性；Controller 每次 request 獨立授權，因此導覽快取、隱藏或偽造路徑不會成為安全邊界。

## 失敗、隔離與相容性

未登入、無有效教會、權限非 Church 或伺服器身分載入失敗均不得執行設定查詢／寫入，回 403。若資料來源故障，保留真實失敗路徑並交既有共用錯誤處理；不能把錯誤轉成另一教會或成功空結果。所有讀寫以登入 contact 的 account 關聯為唯一教會範圍；A/B 教會的頁面和修改紀錄不共用 mutable model 或快取項目。

頁面樣式屬可延後項目；路由、權限、資料隔離、CSRF/XSS、本地化與三頁功能皆為必做。全程以合成測試資料離線驗證，不建立 D365 實機宣稱。
