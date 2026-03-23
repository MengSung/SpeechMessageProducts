# Theme 色系切換提示詞（appsettings.json 驅動）

## 一、5 段式提示詞寫法（可複用）

1. **目標**：要做什麼
   - 例如：在 `appsettings.json` 設定 Theme，切換整站色系。
2. **範圍**：只改哪些檔案
   - 例如：`appsettings.json`、`Startup.cs`、`Views/Shared/_Layout.cshtml`、`wwwroot/css/*.css`。
3. **限制**：哪些不能動
   - 不改 API 路由、不改資料欄位、不新增外部套件。
4. **驗收標準**：如何算完成
   - 切換 `Theme:Current` 後，首頁與主要頁面顏色明顯切換；無 JS/C# 錯誤。
5. **輸出格式**：要 AI 回什麼
   - 直接改檔 + 條列「修改檔案/UX原因/相容性風險」。

---

## 二、可直接複製的完整提示詞

請實作「**appsettings.json 控制整站 Theme 色系切換**」，需求如下：

### 目標
- 目前色系定義為「藍色系」。
- 新增「橘色系」、「綠色系」。
- 當我在 `appsettings.json` 選擇色系時，整體網頁會切換到該色系。

### 範圍
- 僅修改與 Theme 相關檔案（例如）：
  - `appsettings.json`
  - `Startup.cs`（或相對應啟動設定）
  - `Views/Shared/_Layout.cshtml`
  - `wwwroot/css` 內既有樣式檔（必要時新增一個 theme css 檔）

### 限制
1. 不可修改 Controller/API 路由/資料欄位名稱/DataSource 行為。
2. 不可新增外部套件。
3. 保留既有功能流程。
4. 僅做 Theme 色彩切換，不改業務邏輯。

### 實作要求
1. 在 `appsettings.json` 增加設定，例如：
   - `Theme:Current` = `blue | orange | green`
   - `Theme:Presets` 定義三組主色/輔色/背景/邊框/文字色。
2. 在啟動流程讀取 `Theme:Current`，傳到 Layout（ViewData / ViewBag / 設定服務皆可，沿用專案慣例）。
3. 在 `_Layout.cshtml` 於 `<body>` 或根容器加上對應 class（例：`theme-blue` / `theme-orange` / `theme-green`）。
4. 在 CSS 使用變數或 class 覆寫主題色（至少包含：主按鈕、標題、卡片、DataGrid 表頭/hover/選取）。
5. Theme 設定值不合法時，回退到 `blue`。

### 驗收標準
1. 將 `Theme:Current` 設成 `blue/orange/green` 時，頁面主色正確切換。
2. 不影響既有資料載入、編輯、儲存、API 呼叫。
3. 無新增編譯錯誤與 JS 錯誤。
4. 手機寬度 360~430px 仍可讀可用。

### 輸出格式
- 直接套用修改。
- 最後條列：
  1) 修改檔案清單
  2) 每項改動提升了什麼 UX
  3) 相容性風險

---

## 三、Theme 色系切換（appsettings.json 驅動）詳細實作步驟

> 說明：以下步驟支援使用中文值，例如 `Theme:Current = "藍色" | "橘色" | "綠色"`。

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

