# 會友資訊手機響應式字體與操作尺寸設計

## 目標

在 320–640 CSS px 的手機與窄螢幕上，依可用寬度連續調整會友資訊頁的字體、行高、內距與觸控目標。主要成功條件是文字容易閱讀、按鈕與展開列容易點擊，且不破壞既有完整表格、單一水平卷軸與手指左右滑動。

桌機版不變；所有新增規則限制在既有 `max-width: 640px` 行動版 media query 內。

## 研究依據

- [Material Web Type Scale](https://unpkg.com/@material/web@2.4.0/typography/md-typescale-styles.css)：Body Large 為 `1rem/1.5rem`，Label Large 為 `.875rem/1.25rem`。
- [W3C WCAG 2.2 Target Size Minimum](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html)：指標輸入目標至少 `24×24 CSS px`，並建議重要控制項採用更大的目標。
- [Android Accessibility Touch Target](https://support.google.com/accessibility/android/answer/7101858?hl=en)：互動元素建議至少 `48dp` 寬高。
- [web.dev Accessible Responsive Design](https://web.dev/articles/accessible-responsive-design)：觸控裝置的可點擊元素建議採用 `48px` 目標。
- [W3C WCAG Text Spacing](https://www.w3.org/WAI/WCAG22/Understanding/text-spacing.html)：在 1.5 倍行高等文字調整下，不得遺失內容或功能。

## 響應式策略

採用純 CSS `clamp()`，不以 JavaScript 監聽 resize，也不重新設定 DevExtreme 欄位。`rem` 作為上下限，搭配少量 `vw` 形成 320–640px 間的平滑縮放，並保留瀏覽器文字縮放能力。

### 字級與行高

| 元素 | 320px 附近 | 640px 附近 | 行高 |
|---|---:|---:|---:|
| 區長文字 | 18px | 20px | 1.4 |
| 小組名稱、小組長 | 16px | 18px | 1.5 |
| 數量徽章 | 14px | 15px | 20–22px |
| 搜尋／同步／返回按鈕 | 14px | 15px | 20–22px |
| DataGrid 表頭 | 16px | 17px | 1.4 |
| DataGrid 資料列 | 15px | 16px | 1.5 |
| 搜尋輸入框 | 固定至少 16px | 固定至少 16px | 1.5 |

建議 CSS 變數：

```css
--mi-mobile-district-font: clamp(1.125rem, calc(1rem + .65vw), 1.25rem);
--mi-mobile-tree-font: clamp(1rem, calc(.875rem + .65vw), 1.125rem);
--mi-mobile-label-font: clamp(.875rem, calc(.8125rem + .3vw), .9375rem);
--mi-mobile-grid-font: clamp(.9375rem, calc(.875rem + .3vw), 1rem);
--mi-mobile-grid-header-font: clamp(1rem, calc(.9375rem + .32vw), 1.0625rem);
```

### 觸控與間距

- 搜尋、同步與返回按鈕：`min-height: 48px`，維持單列 `flex-wrap: nowrap`；透過較小的流動水平 padding 讓 320px 寬仍可容納。
- 區長列：`min-height` 64–72px。
- 小組列：因包含名稱與小組長兩行，`min-height` 72–84px。
- 展開箭頭：建立 `44×44px` 可點擊／對齊區域；整列本身仍是按鈕，因此實際觸控目標大於 48px。
- 文字容器只使用 `min-height`，不設定固定高度，讓 200% 文字縮放或較長姓名可以自然換行。
- 數量徽章增加垂直 padding，但不固定寬度，避免三位數人數被截斷。

## DataGrid 行動版

- 保留既有固定欄位寬度、`columnHidingEnabled: false` 與無 `hidingPriority`；不得重新出現 adaptive 三點欄位。
- 保留唯一的 DevExtreme 水平卷軸、`useNative: true` 與 `scrollByContent: true`，讓手機可在資料列上左右滑動。
- 只調整表頭／資料列字級、行高與儲存格 padding；不壓縮欄位到難以閱讀，也不隱藏任何欄位。
- 長地址與關係目標可依既有 `wordWrapEnabled: true` 增加列高，不截斷內容。
- 姓名連結與頭像維持可點擊；資料列的垂直尺寸至少接近 48px。

## 工具列窄螢幕保護

- 搜尋框維持 `min-width: 0`，由它承擔主要縮減空間。
- 搜尋與「重新同步LINE」按鈕維持不換行，字級最低 14px、最小高度 48px。
- 320px 寬時縮小按鈕水平 padding 與圖示間距，不讓「重新同步LINE」掉到下一列。
- 不縮小搜尋輸入文字到 16px 以下，避免 iOS Safari 聚焦時自動放大頁面。
- Bootstrap 3 將 `html` 根字級設為 10px，因此原生搜尋輸入框不可用 `1rem` 表示安全字級；手機規則與後置防護 selector 都必須明確使用 `16px`。

## 測試與驗收

- 擴充 `MemberInfoTreeViewContractTests`，要求 mobile media query 具有 `clamp()` 字級、48px 操作高度、44px 展開區與 DataGrid 行動字級。
- 測試繼續要求 `flex-wrap: nowrap`、`columnHidingEnabled: false`、不存在 `hidingPriority`、原生水平滑動設定存在。
- 執行完整 `ChurchReport.MemberInfo.Tests`、Razor JavaScript 語法檢查與 Debug solution build。
- 使用者在 320、390／430、640px 寬度實測：工具列同列、區長／小組易讀、按鈕易點、DataGrid 只有一條卷軸且能以手指左右滑動。

## 範圍外

- 不修改桌機版字級。
- 不改資料、排序、搜尋、權限或 API。
- 不改會友細節 Popup 的既有內容排版。
- 不 Commit；待使用者實測確認後由使用者處理。
