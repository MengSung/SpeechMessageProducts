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
