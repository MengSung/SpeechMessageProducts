# P4 Embedded 驗證紀錄

> 最後更新：2026-08-04
>
> 本紀錄刻意把「離線行為證據」和「真實 CE 證據」分開。離線測試不能替代真實
> Dynamics 365 連線、結果一致性或延遲量測；使用者已決定將後者統一延後至 P6 後的整合驗收，
> 因此它不再阻擋 P4 的程式與離線驗收完成，也不得被誤標為已取得。

## 已取得的離線證據

| 項目 | 指令／範圍 | 結果 |
| --- | --- | --- |
| Embedded Adapter、Data8 executor 與 client lifecycle | `SpeechMessage.Dynamics.Tests` 的 P4 focused filter | 11 passed / 0 failed |
| ChurchReport mapper、runtime、preflight 與 process-host lifecycle | `ChurchReport.MemberInfo.Tests` 的 P4 focused filter | 24 passed / 0 failed |
| Release 編譯 | `dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore --nologo` | 0 warnings / 0 errors |

這些測試已覆蓋：Embedded 不需要 `Gateway.Endpoint`、固定 `ProfileAlias`、保留參數在取得
permit/client 前 fail closed、Profile/Organization 隔離、取消/逾時/Drain/Dispose 的 pool/permit/client
確定性釋放，以及 Development 設定的 `Embedded` + `sunnyvalechback` 選擇。

## 完整 Dynamics 測試的獨立結果

`dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --no-restore --nologo`
於 2026-08-04 得到：441 passed / 7 skipped / 1 failed / 449 total。

唯一失敗為 `OfficialWorkerSoakAndPerformanceTests.WorkerSoak_repeated_package01_recycle_returns_all_owners_to_zero_without_unbounded_trends` 的 Official Worker private-bytes 趨勢。P4
沒有啟用、修改或使用 Official Worker；此失敗不得被重寫成 P4 成功，也不得藉由放寬門檻掩蓋。

## 延後至 P6 後整合驗收的真實 CE 證據

1. Development 組態的 `CrmConnection:Password` 是安全 placeholder，且目前沒有使用者層級
   `CRM_PASSWORD` 覆寫；host 會在建立 Data8 client 前 fail closed。因此尚未執行真實 CE WhoAmI。
2. `Package01FeeReadsEnabled` 依設計維持 `false`，收費清單仍走 ToolUtility legacy 路徑。沒有一個
   已啟用的 Embedded 收費查詢可與 legacy 收費查詢做結果一致性比較；不得用替身測試冒充真實結果一致性。
3. 尚未在同一 CE、同一安全讀取工作負載、相同暖機與樣本數條件下取得 legacy/Embedded 的 p50、p95、p99。
   因此目前沒有可誠實宣告的「Embedded p95 不劣於 legacy」結論。

## 已準備、但僅於 P6 後執行的真機對照量測

`ChurchReport.MemberInfo.Tests/LiveEmbeddedDynamicsComparisonTests.cs` 提供 opt-in 的 xUnit case。它預設略過；只有
`SPEECHMESSAGE_DYNAMICS_P4_LIVE=1` 與目前測試程序的 `CRM_PASSWORD` 同時存在才會執行。測試不使用 Gateway
endpoint，且仍保持 `Package01FeeReadsEnabled=false`：先對 legacy ToolUtility pool 與 Embedded pipeline 各 warm-up
一次，再以相同帳密做 21 次循序 WhoAmI，檢查 UserId／BusinessUnitId／OrganizationId 及 catalog 預期組織相同，
最後輸出兩邊 p50／p95／p99 並嚴格判定 Embedded p95 <= legacy p95。所有 legacy service、pool timer、Data8 runtime、
permit/client/pool 與 logger factory 都在 `finally` 中釋放。

## P6 後真實驗證的最小判定

真實 F5 必須只驗證 `runtime.health.whoami`：它會依
`RequestGuard → ProfileResolver → Organization Admission → IConnectorRouter → Data8ConnectorPool`
取得一個短生命週期 lease，並在 `await using` 結束時歸還 permit/client。成功條件為 host 正常啟動且
preflight 的回傳 OrganizationId 等於 `sunnyvalechback` catalog 的預期 ID；失敗、逾時或組織不符均為
fail closed，且不得繼續宣告 P4 已通過。

完成此最小 F5 驗證後，結果一致性與 p50/p95/p99 仍需以相同的非破壞性讀取 workload 另行量測並記錄。
在功能旗標仍為 false 的 P4 範圍內，該量測不得改變收費清單的實際路由。P6 後的整合閘門還必須包含 Dedicated
Gateway；Embedded、Dedicated 與 legacy 的任一結果或資源基線不一致均為失敗。

---

## P4.1：CE 8.2／9.1 Organization Catalog 登錄（2026-08-04）

- `CrmConnection:OrganizationCatalog` 已登錄 **27** 個 CE 8.2 與 **5** 個 CE 9.1 組織；同名
  `speechmessage` 以 `speechmessage-ce82`／`speechmessage-ce91` 區分。
- `CrmConnectionEmbeddedProfileMapperTests`：**7 passed / 0 failed**。其中包含 CE 8.2 alias 解析、Disabled
  organization 在 profile／permit／client 建立前 fail-closed，以及 Embedded factory 只使用 selected Catalog URI。
- ChurchReport 全套：**395 passed / 1 skipped**；唯一 skipped 為未提供 opt-in 與密碼時的真機量測。
- Release build：**0 warnings / 0 errors**；所有本次修改的 C# 檔已位元組驗證為 UTF-8 無 BOM、CRLF-only、final CRLF。

目前使用者只提供 CE 8.2 Organization identity，未提供每個 CE 8.2 的 HTTPS Organization Service URI。因此除了既有
`sunnyvalechback` 外，其他 alias 仍是「可選、已登錄但未配置連線目標」；選取後會安全拒絕，不會猜測端點、重用
9.1 credential，或建立跨組織 session／connection。補上單一 entry 的核准 `ServiceUri`，才可對該 alias 進行後續
受控真機量測。
