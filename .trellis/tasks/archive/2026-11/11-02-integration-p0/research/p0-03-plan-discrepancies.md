# P0-03 計畫與現況差異

## 停止依據

依 00 §9 第 6 點，發現計畫與程式現況有矛盾必須停止，不能自行猜測範圍或啟用停用中的依賴。
P0-01 已提交 90799969；P0-02 已提交 bc13efe7。此紀錄保留最初停止時的差異；使用者已核定下方修正，P0-03 已據此執行。

## 差異一：ASP.NET Core 2.2.x 並非有效套件

P0-03 寫明「其他套件維持現有版本，包括 ChurchReport 的 Microsoft.AspNetCore.* 2.2.x」。
實際 SpeechMessageProducts.ChurchReport.csproj 中，Microsoft.AspNetCore 2.2.0、Authentication.Cookies 2.2.0、Mvc 2.2.0、Routing 2.2.2、IIS 2.2.6、IISIntegration 2.2.1、Kestrel 2.2.0、Session 2.2.0、StaticFiles 2.2.0 都位於 XML 註解內。
目前有效的是 Microsoft.AspNetCore.Mvc.NewtonsoftJson 10.0.0；不應把歷史註解誤當成現役套件加入 CPM。

## 差異二：「所有 csproj」的邊界未涵蓋歷史快照

SpeechMessageProducts.sln 內有 19 個專案；Git 追蹤的 csproj 共 29 個。
其中兩個明確是文件／封存資料：

- .ccg/tasks/archive/2026-11/subagent-goal-word-tutorial/docx-generator/SubagentGoalDocx.csproj
- docs/portable/member-info-portable-kit/reference-implementation/tests/ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj

後者保留 PackageReference Version 與 NoWarn=NU1605，但不是目前方案的建置專案；reference-implementation/README.md 將它列為行為契約參考，不是目前 repo 的直接建置設定。
若依原文字面檢查任何 csproj，將要求改動文件快照；若只掃方案，則會漏掉原計畫已提到、但尚未納入方案的 ToolUtility.Tests 及其他專案。不能自行選擇其中一種。

## 建議核定的修正文案

1. P0-03 的套件盤點、CPM 與套件守門範圍為 repo 的實際程式專案；明確排除 .ccg/tasks/ 的任務／封存內容及 docs/ 的文件／參考快照。方案外的實際專案仍須盤點，原計畫列出的孤兒專案不在此階段刪除。
2. ASP.NET Core 2.2.x 維持註解停用，不新增、不啟用、不納入中央 PackageVersion；目前有效的 Microsoft.AspNetCore.Mvc.NewtonsoftJson 10.0.0 維持原版本。
3. Test-PackageVersions.ps1 以 XML 的有效 PackageReference／NoWarn 與上述明確範圍判斷，不以文字搜尋把註解當成有效依賴。

使用者已回覆「核定建議，更新計畫後繼續 P0-03」。已先更新原實作計畫，再執行套件守門紅燈、CPM 與完整驗證；完成結果見 wp-reports/P0-03.md。
