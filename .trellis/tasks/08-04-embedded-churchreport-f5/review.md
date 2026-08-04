# P4 最終品質稽核

稽核日期：2026-08-05
稽核範圍：`embedded-churchreport-f5` 的完整程式、測試、規格一致性與資源生命週期。

## 結論

P4 的離線交付條件全部通過，可以封存。P4 不宣稱已完成外部 CE 真機量測；該項工作已明確移交為 P6 程式與離線驗證完成後的一次跨模式整合閘門。

## 需求逐項稽核

| P4 要求 | 稽核結果 | 證據 |
| --- | --- | --- |
| ChurchReport 可在 `ConnectionMode=Embedded` 與 ProfileAlias 下經由既有安全管線執行 | 通過 | `EmbeddedHostAdapter`、Embedded DI 與 ChurchReport mapper focused tests 通過。 |
| Embedded 不讀取或依賴 `Gateway.Endpoint` | 通過 | Embedded focused tests 與 ChurchReport 全套測試通過。 |
| Guard → ProfileResolver → Admission → Router → Data8 generation-owned pool 的順序與 fail-closed 邊界維持 | 通過 | Embedded focused tests 9/9 通過；完整 Dynamics tests 442/442 通過。 |
| CE 8.2 / 9.1 Catalog 可依 Alias 選取，Disabled 或缺少 ServiceUri 時在配置階段 fail closed | 通過 | ChurchReport 全套 395 passed；P4 mapper tests 受全套覆蓋。 |
| permit、client、pool、deadline、drain、dispose 的釋放與隔離 | 通過 | P3 lifecycle contracts 持續由完整 Dynamics suite 驗證；P4 adapter tests 覆蓋使用端 `await using` 與失敗釋放。 |
| C# 檔案繁體中文註解、UTF-8 無 BOM、僅 CRLF、末尾 CRLF | 通過 | 18 個 P4 新增或實質修改的 `.cs` 檔案逐位元檢查均通過。 |
| Release 可建置 | 通過 | `dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore --nologo`：0 warnings / 0 errors。 |
| 外部 CE 真機量測 | 延後 | opt-in `LiveEmbeddedDynamicsComparisonTests` 在未提供明確啟用條件時預期略過；將在 P6 後進行 legacy、Embedded、Dedicated 三模式整合量測。 |

## 最終驗證紀錄

```text
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --filter "FullyQualifiedName~Embedded" --no-restore --nologo
9 passed / 0 failed / 0 skipped

dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore --nologo
395 passed / 0 failed / 1 skipped

dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --no-restore --nologo
442 passed / 0 failed / 7 skipped

dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore --nologo
0 warnings / 0 errors
```

七個 Dynamics 略過項目都是未明確啟用的 Live SQL contract 測試，並非失敗；它們不屬於 P4 的 Embedded 離線交付範圍。

`git diff --check` 已通過，沒有空白或補丁格式錯誤。

## P6 交接閘門

外部 CE 真機量測須在 P6 程式和離線測試完成後只執行一次，且必須同時驗證：

- legacy、Embedded、Dedicated 的結果一致性；
- p50、p95、p99 延遲；
- 至少 200 次 borrow / use / return 循環；
- 故障淘汰與代際 drain；
- permit、client、timer、task、handle 與 session 全數回到量測前基線。

未完成上述真機量測前，不得將離線綠燈描述為外部 CE 生產相容性成功。
