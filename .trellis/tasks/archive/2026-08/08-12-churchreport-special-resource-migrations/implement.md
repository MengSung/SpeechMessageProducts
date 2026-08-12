# P7.3 ChurchReport 特殊資源能力遷移：執行計畫

## 執行順序

1. 建立抽象層 RED tests，確認五項 special-resource operation 尚未被 executor 接受，並鎖定
   registry、request/response discriminator 與有限 policy。
2. 新增 Operations abstraction：operation IDs、registry definitions、closed image/metadata/meeting
   DTO 與 `OperationResponseData` one-branch union。更新 manifest/schema agreement tests。
3. 新增 Data8 connector special-resource helper：固定 image retrieve/update/read-back、metadata
   projection 與 meeting statistics bounded query/paging；擴充 executor normalizer、estimated bytes、
   supported dispatch 及 connector switch。
4. 新增 ProductClient special-resource interface/implementation/DTO/DI，建立純值 request/response
   validation 與 defensive-copy tests。不得修改 ChurchReport consumer 或 feature gate。
5. 逐層執行 targeted tests；若出現 contract/cleanup fault，先修正該層並保留 fail-closed。
6. 跑完整 Release tests/build、encoding/CRLF、`git diff --check`、scope/isolation/lifecycle review。
7. 以規定 self-healing runner 做一次不超過 45 秒的 review 嘗試；若無可用 output，記錄
   「雙模型未完成」並以本機 review 完成。scope-only commit、archive 後才能評估 P7.4。

## 檔案與所有權

- Abstractions：`OperationIds.cs`、`Package01OperationRegistry.cs`、`OperationResponseData.cs` 和
  新增封閉 DTO 檔案。
- Data8：`Data8ProfileOperationExecutor.cs`、`OnPremiseData8ConnectorClientFactory.cs`、新增
  `Package03Data8SpecialResourceOperations.cs`。
- ProductClient：新增 `SpecialResources/` interface、implementation、DTO 和 DI registration。
- Tests：registry/response agreement、executor、connector factory、ProductClient focused tests。
- 不可變範圍：ChurchReport consumer、ToolUtility、設定 feature gate、CE fixture/ledger。

## 驗證命令

```powershell
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --configuration Release --no-restore
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --configuration Release --no-restore
dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore
git diff --check
```

encoding/CRLF 檢查採 repository 現有 scripts；檢查範圍包含所有本 task 新增或修改的 `.cs`/`.cshtml`。
任何 full test/build/encoding/scope gate 不通過都不可 commit/archive。

## 回復點

本 task 沒有 CE mutation 或 traffic change。若本機 contract 不成立，回復點是尚未提交的 task-owned
source/test/artifact changes；不得用 CE retry、舊 fixture 或 consumer fallback 掩蓋失敗。

## 2026-08-12 完成紀錄

- 已完成五項封閉 capability 的本機實作：兩項 contact image update、一項 image retrieve、一項
  option-set metadata projection/cache，以及一項固定 Sunday meeting statistics paging result。
- metadata cache 僅以 server-resolved locale 與已解析的 `(ProfileAlias, GenerationId, target)` 建鍵；
  無法證實單一 server locale 時維持 request-local projection，絕不猜測或快取。
- weekly paging 在每次同步 CRM page I/O 前後觀察 request-owned cancellation；取消、cookie 缺失、
  超頁、超列、超 byte 或 schema 不符均不產生 partial success，lease 走 fault/dispose。
- 回歸測試發現 connector 以 `Succeeded=false` 回覆時原本未標記 lease faulted，可能讓未知 Data8/WCF
  session 回到 idle pool。已改為回傳 bounded failure 前呼叫 `MarkFaulted()`，並以 focused test 驗證
  client dispose 與 admission permit exactly-once release。
- 本 task 未執行任何 CE mutation、feature gate、ChurchReport consumer migration、traffic switch、
  Official Worker、P7.4/P7.5/P8 或雲端部署；matrix 維持 `consumer-not-migrated`／`evidence-pending`。

### 品質證據

| 檢查 | 結果 |
| --- | --- |
| `dotnet test .\SpeechMessageProducts.sln --configuration Release --no-restore` | 通過；Dynamics 722 passed／7 skipped，ChurchReport 528 passed／14 skipped；skip 均為既有環境或 live fixture gate。 |
| `dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore` | 通過，0 warnings，0 errors。 |
| P7.3 focused tests | 通過 92 tests；涵蓋 executor、factory、ProductClient、metadata cache。 |
| `.cs` byte-level check | 23 個 task-owned C# 均為 UTF-8 無 BOM、CRLF-only、final CRLF。 |
| `git diff --check` | 通過。 |
| scope／isolation／lifecycle review | 通過本機人工審查；沒有 ProductClient/Gateway/Abstractions 的 SDK、raw stream、cookie 或 Entity 邊界外洩，也未改 ChurchReport consumer。 |

### 審查降級紀錄

已透過 `Start-CcgDualModelRun.ps1` 啟動 Gemini／Claude reviewer，設定 40 秒且只允許一次嘗試；
45 秒內只產生 health 與 prompt artifact，未產生任何 backend finding/summary。依核准規則停止等待，
本次紀錄為「雙模型未完成」，不可稱為完整雙模型審查；後續以完整本機測試、diff、encoding 與
cross-layer isolation/lifecycle review 完成本機 quality gate。
