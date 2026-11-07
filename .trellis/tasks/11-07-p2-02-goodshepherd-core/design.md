# P2-02 技術設計

## 專案邊界

`SpeechMessageProducts.GoodShepherd.Core` 是 Plugin 與 .NET 10 產品主機共用的產品規則層，target 為 `net462;net10.0`。它只向下參考既有雙目標共用核心及 LINE SDK，不依賴 Hosting、Tenancy、ToolUtility 主機層、Dataverse.Client 或 ASP.NET。

測試專案 target 為 `net48;net10.0`。net48 是測試宿主，不是產品 target；其 ProjectReference 必須由 NuGet/MSBuild nearest framework 選到產品 `net462` 輸出。

## 模組與資源

- 建立十個模組目錄及繁中 README，記錄正式計畫 P5 搬移來源及邊界；不複製舊程式。
- `Resources/GoodShepherdText.resx` 是未來產品文字的唯一來源；初始空表只建立生成管線，不新增虛構文案。
- `GoodShepherdText.g.cs` 由 I18nCheck 產生。MSBuild 不將 resx 編成衛星資源；生成資料只包含固定資源列。
- `Notifications/MemberNotificationService` 先建立明確的內部骨架型別。P2-04 才增加查詢、Channel 解密、教會授權、語系、訊息組裝、Retry Key 與傳輸的正式 API。

## 測試與資源生命週期

一個離線 smoke 測試載入產品組件，核對 `TargetFrameworkAttribute` 與服務型別，分別防止 net48 測試宿主誤指到不存在或錯誤的產品 target。測試不建構 client、不開網路、不持有外部資源。更完整的建置與架構檢查由方案及 Hosting 架構測試負責。

## 方案整合與回復

兩個 `.csproj` 一經建立即加入 `SpeechMessageProducts.sln`，由 `dotnet sln list` 與方案 GUID 組態映射驗證。若新增參考造成 target 不相容，應修正新專案參考，不改變既有專案 target 或關閉嚴格分析器。若需回復，移除本包新增專案及其方案項目，不碰其他工作樹修改；本任務不執行 D365／LINE 外部操作。
