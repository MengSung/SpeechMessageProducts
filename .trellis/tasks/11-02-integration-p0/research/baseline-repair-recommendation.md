# P0-01 既有測試失敗：修復建議（尚未執行）

## 決定與範圍
使用者於 2026-11-02 選擇「先停在 P0-01，另行處理既有測試失敗」。
本文件僅提供下一次修復工作的可審閱範圍，不構成已開始修復或已變更原計畫。
建議先讓原有測試恢復有效守門，再繼續 P0-02；不以刪除測試、略過測試或放寬契約消除紅燈。

## 目前定位

| 分類 | 失敗案例 | 證據 | 建議 |
|---|---:|---|---|
| 舊組件名稱 | 12 | 測試仍以 Type.GetType 的 ChurchReport 組件名稱尋找型別，或斷言 AssemblyName=ChurchReport；現有 csproj 的 AssemblyName 已是 SpeechMessageProducts.ChurchReport，RootNamespace 仍是 ChurchReport | 從可信任產品型別取得實際組件，再驗證原有型別／架構契約；同時查驗原本應不存在的型別，避免錯誤組件名造成假綠燈 |
| 舊方案／產品路徑 | 10 | 2 個 ChurchReport 案例與 1 個 RichMenus 案例仍找 ChurchReport.sln；7 個服務抽取案例仍讀 ChurchReport/Models/DonationPaymentManager.cs | 對齊 SpeechMessageProducts.sln 與 SpeechMessageProducts.ChurchReport 路徑，保留架構邊界與委派流程斷言 |
| Debug／Release 契約 | 2 | FileToolUtilityTracer.IsCompileTimeTraceEnabled 在 Release 回傳 false；測試仍直接開啟預期產生的追蹤檔 | 分別驗證 Debug 的格式／釋放與 Release 的不建檔／無副作用；保留資源釋放及全域 listener 不成長斷言 |

上述是目前紅燈的直接原因；尚未修正前置定位，因此後續斷言是否還會暴露其他問題仍待實跑。
目前沒有證據支持為了這些錯誤變更產品行為、重命名產品組件，或啟用 Release legacy 追蹤。
legacy 追蹤停用契約與 Exception.log 在 Debug／Release 都支援的例外通知契約是不同功能，不可混淆。

## 預計需要審閱的測試檔（只列範圍，未修改）
- ChurchReport.MemberInfo.Tests/Payments/DonationPaymentManagerNamingTests.cs
- ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorNamingTests.cs
- ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnProcessorNamingTests.cs
- ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnWorkflowNamingTests.cs
- ChurchReport.MemberInfo.Tests/Payments/DonationPaymentFormModelNamingTests.cs
- ChurchReport.MemberInfo.Tests/Payments/DonationPaymentNamingCompatibilityTests.cs
- ChurchReport.MemberInfo.Tests/Payments/PaymentProductServiceNamingTests.cs
- ChurchReport.MemberInfo.Tests/Payments/PaymentPostPaymentArchitectureTests.cs
- ChurchReport.MemberInfo.Tests/Payments/DonationPaymentServiceExtractionTests.cs
- LineMessagingProcessor.RichMenus.Tests/Boundary/RichMenuProjectBoundaryTests.cs
- ToolUtility.Dataverse.Tests/FileToolUtilityTracerTests.cs

## 後續修復的守門建議
1. 先確認並授權將測試修復列為獨立前置工作；階段 0 仍停在 P0-01。
2. 保存本次原始 24 個失敗；逐組重現紅燈，修復測試的定位與模式判斷。
3. 不刪既有案例、不降低既有斷言；若有真正產品契約問題，停止並列出差異。
4. 對實質修改的測試檔補完整繁中 XML 註解，逐位元組檢查 UTF-8 無 BOM、CRLF 與檔尾 CRLF。
5. 驗證 Debug／Release 相關測試，以及完整 Release 方案測試；以原有 738 個案例全部通過、略過 0 為目標，新增案例另列。
6. 依規定經雙模型分析／審查與本機驗證後，再由使用者確認是否恢復原階段 0 工作。
