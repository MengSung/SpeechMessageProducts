# 審查結果

## 根因

Gemini 確認根因成立：前端先顯示 FileReader 本地預覽，但上傳成功後會改載伺服器影像；伺服器端 `IMemoryCache` 仍保留舊照片，因此舊圖覆蓋新預覽。

## 修正

- CRM 更新成功後清除該 Contact 的完整圖快取。
- 清除 32–256 像素全部縮圖快取。
- 讀取端與批次讀取端共用個人照片縮圖上下限常數。
- 新增快取失效回歸測試。

## 分級結果

- Critical：無。
- Warning：測試透過反射呼叫私有快取失效方法，重命名時只能由測試執行期發現；這是避免暴露正式 API 的取捨。
- Info：前端既有 timestamp cache-busting 與本地預覽流程不需修改。

## 外部審查狀態

- Gemini：完成，PASS，無 Critical。
- Claude：透過專案自動復原入口重試兩次，均因 provider 無可用輸出而失敗；不可宣稱完整雙模型審查成功。

## 驗證

- `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter FullyQualifiedName~PersonalContactImageCacheTests --no-restore`：通過。
- `dotnet build SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj --no-restore --no-incremental`：通過，0 警告、0 錯誤。
- 完整 `ChurchReport.MemberInfo.Tests`：既有付款／原始碼路徑測試 22 項失敗，與本次變更無關；其餘 305 項通過。
