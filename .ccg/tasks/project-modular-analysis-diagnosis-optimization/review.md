# 模組邊界文件審查

## 審查標的

`docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`

## 唯讀 Subagent

初稿被判定不適合直接作為獨立優化控制圖。主要有效發現：

- LINE processor core 與 ASP.NET Core adapter 必須拆分。
- ToolUtility CRM、LINE adapter 與混合 facade 必須拆分。
- B04、B06、X02、F01、X04 過粗。
- 測試必須跟隨直接受測主體。
- `MapData` 有重複且錯誤的擁有權。
- 缺少明確的 consumer gate 與 gate-blocked 狀態。

以上均已修訂，文件改為 35 個葉節點，並新增 F03Q、X02Q、X05Q
分析隔離節點。

## CCG 外部 Review

Run:
`20260710-170510-project-module-boundary-document-review-reviewer`

結果：

- Claude 成功並產出可用 findings。
- Gemini 因 provider quota/billing 403「餘額不足」未產出結果。
- `degradedFallback=true`，因此不是完整雙模型 review。

有效 findings：

- `ChurchReport.MemberInfo.Tests/Payments/PushUtilityTests.cs` 直接測試 B07
  的 `ChurchReport.Tools.PushUtility`，已從 B05 改歸 B07。
- `ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs` 直接測試 F03Q
  的混合 facade，已從 F03A 改歸 F03Q。

經本機核對不成立的 finding：

- Reviewer 宣稱 DevExtreme vendor 檔位於 `wwwroot/css`、`wwwroot/js`
  根目錄。實際根目錄分別只有 16 個 CSS 與 10 個 custom JS；vendor
  檔位於 `wwwroot/css/devextreme/**` 140 個及
  `wwwroot/js/devextreme/**` 161 個，原 X03 wildcard 已完整涵蓋。

## 結論

- 分析與診斷：文件可用。
- 優化：只有在第 10.1 節 provider baseline、consumer gate 與 rollback
  point 完成後才可進入。
- F03Q、X02Q、X05Q：只能分析、拆分、移交或淘汰，不能整包優化。
