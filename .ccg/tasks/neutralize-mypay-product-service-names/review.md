# Review: neutralize-mypay-product-service-names

## Scope

本次審查範圍只涵蓋 ChurchReport 付款後產品流程服務命名重構：

- 移除 `ChurchReport.Services` 底下 `MyPay*` 產品服務型別與檔名。
- 新增 provider-neutral 的 `Payment*` 服務型別。
- 更新 `MyPayController`、`Startup`、付款後 workflow handler 與相關測試引用。
- 保留 CRM 更新、LINE 通知、收費單分類在 ChurchReport 產品層，不移入 `SpeechMessage.Payments`。

## Local Review Findings

### Critical

無。

### Warning

無。

### Info

- `MyPayController` 路由與 controller 名稱仍保留 `MyPay`，因為它是既有高鉅 callback 路由入口；本次只處理產品服務命名，不改公開路由契約。
- 搜尋 `MyPayCrmService|MyPayFeeTypeHelper|MyPayLogger|MyPayMessageBuilder|MyPayNotificationService` 後，剩餘命中只在 `PaymentProductServiceNamingTests` 的 legacy type-name 清單中，用來驗證舊型別不存在。
- `ChurchReport\Services` 底下已無 `MyPay*.cs` 檔案。

## External Review Attempt

已依 CCG 規則嘗試呼叫雙模型 reviewer，但本機缺少 backend CLI：

- Gemini reviewer: `gemini command not found in PATH`
- Claude reviewer: `claude command not found in PATH`

wrapper 可執行檔存在於 `C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe`，但 `gemini` 與 `claude` 命令本身無法解析，因此無法取得外部模型審查報告。原始失敗輸出已保存在：

- `.ccg/tasks/neutralize-mypay-product-service-names/review-gemini.md`
- `.ccg/tasks/neutralize-mypay-product-service-names/review-claude.md`

## Verification

已重新執行以下驗證：

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter FullyQualifiedName~Payments --no-restore
```

結果：通過 63，失敗 0，略過 0。

```powershell
dotnet build ChurchReport\ChurchReport.csproj --no-restore
```

結果：建置成功，0 warnings，0 errors。

## Boundary Checks

已執行舊服務名稱搜尋：

```powershell
Get-ChildItem -Path 'ChurchReport','ChurchReport.MemberInfo.Tests' -Recurse -Include *.cs -File |
  Where-Object { $_.FullName -notmatch '\\(bin|obj|artifacts)\\' } |
  Select-String -Pattern 'MyPayCrmService|MyPayFeeTypeHelper|MyPayLogger|MyPayMessageBuilder|MyPayNotificationService'
```

結果：只剩 `PaymentProductServiceNamingTests.cs` 的 legacy type-name 測試資料命中。

已檢查檔案編碼與可疑亂碼字元：

- 本次修改/新增的付款服務與測試檔均為 UTF-8 無 BOM。
- 無 `ReplacementChar`。
- 無 `嚗/瘚/撱/隤/蝔/銝/憟` 等常見 Big5/UTF-8 mojibake 字元。
