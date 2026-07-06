> 說明：以下步驟支援使用中文值，例如 `Theme:Current = "藍色" | "橘色" | "綠色"`。

### Step 0：實際編寫順序（先做這個）

1. 先改 `appsettings.json`，把 Theme 設定值補齊。
2. 再改 `Startup.cs`，完成讀取、驗證、回退預設值。
3. 再改 `Views/Shared/_Layout.cshtml`，將 Theme class 套到 `<body>`。
4. 最後改全域 CSS（例如 `wwwroot/css/site.css` 或既有 theme 檔）讓元件吃變數。
5. 完成後依 Step 6 做切換驗證與回歸測試。

### Step 1：在 `appsettings.json` 建立 Theme 設定結構

1. 新增根節點 `Theme`。
2. 新增 `Theme:Current` 作為目前啟用色系（建議先設為 `藍色`）。
3. 新增 `Theme:Presets`，定義三組色系（藍色、橘色、綠色）。
4. 每組至少包含以下欄位：
   - `Primary`（主色）
   - `Secondary`（輔色）
   - `Background`（頁面背景）
   - `Border`（邊框色）
   - `Text`（主要文字色）

### Step 2：在啟動流程讀取 Theme 設定

1. 於 `Startup.cs`（或專案現行啟動位置）讀取 `Theme:Current`。
2. 加入合法值驗證（僅允許：`藍色`、`橘色`、`綠色`）。
3. 若值無效、空值或缺漏，回退到 `藍色`。
4. 將最終 Theme 值提供給 View 層（沿用專案慣例）：
   - `ViewBag` / `ViewData` / 全域服務注入皆可。

補充（程式實作重點）：
- 建立允許清單：`藍色`、`橘色`、`綠色`。
- 讀到的值先 `Trim()`，再比對允許清單。
- 不合法時統一改成 `藍色`，避免 View 層出現未知 class。
- 最終輸出 View 值時，建議一併轉為 CSS class 字串（例如 `theme-blue`），減少 View 判斷。

### Step 3：在 `Views/Shared/_Layout.cshtml` 套用 Theme class

1. 在 `<body>` 或最外層根容器加上 class。
2. 對應規則建議：
   - `藍色` => `theme-blue`
   - `橘色` => `theme-orange`
   - `綠色` => `theme-green`
3. 若未取得值，預設套用 `theme-blue`。

### Step 4：在 CSS 建立主題變數與覆寫規則

1. 在既有全域樣式或單一 theme 檔案中建立三組主題 class。
2. 每個 class 以 CSS 變數定義色票（`--color-primary` 等）。
3. 讓核心元件吃變數，至少涵蓋：
   - 主按鈕
   - 區塊標題
   - 卡片容器
   - DataGrid（表頭/hover/選取）
4. 保留現有元件功能，只改視覺層。

補充（程式實作重點）：
- 三組 class 都要完整定義同一批變數名稱，避免缺值。
- 既有元件若直接寫死色碼，改成變數引用（例如按鈕、卡片、DataGrid）。
- `:hover`、`selected`、`active` 狀態也要改為同一套變數系統。
- 不改動 JS/資料邏輯，只調整樣式來源。

### Step 5：頁面級樣式對齊（避免局部寫死色碼）

1. 針對近期待改過的頁面，優先把硬編碼主色改為變數。
2. 若保留部分固定色，需確認不會與 Theme 衝突。
3. 手機版（360–430px）同步檢查按鈕、表格、卡片可讀性。

### Step 6：驗證與回歸測試

1. 依序切換 `Theme:Current`：`藍色` → `橘色` → `綠色`。
2. 每次切換後檢查：
   - 首頁與主要功能頁主色是否一致更新
   - DataGrid 表頭/hover/選取是否正確換色
   - 按鈕與重點提示色是否一致
3. 確認無新增 C# 編譯錯誤與 JS 錯誤。
4. 驗證既有資料流程（載入/編輯/儲存/API）不受影響。

### Step 7：交付格式（給 AI 或給工程師）

請固定要求輸出：

1. 修改檔案清單
2. 每項改動帶來的 UX 改善原因
3. 相容性風險（例如：舊頁面硬編碼顏色可能導致局部不一致）