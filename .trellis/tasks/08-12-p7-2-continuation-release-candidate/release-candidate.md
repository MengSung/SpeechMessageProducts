# P7.2 continuation 第一版 Release candidate

## 識別與用途

- 產物狀態：**本機驗證候選版；不得作為 P7.4 流量切換或 P7.5 ToolUtility 移除依據。**
- 適用部署：既有 deployment-owned `crm91 + Data8 + CE 9.1` 的本機建置與測試。
- 不包含：CE 8.2、Official Worker、feature flag、ChurchReport 流量切換、共享或正式資料 mutation。
- 歷史限制：已歸檔 P7.2 Slice C cycle 維持 closed，絕不可重試。

## 各 Slice 狀態

| Slice | 本機狀態 | CE 實證 | Runtime / rollout 狀態 | cleanup 狀態 |
| --- | --- | --- | --- | --- |
| C | 已修正 operation-scoped `IOrganizationService` 不再寫入共享 ToolUtility；group-leader 唯讀路徑使用 explicit service 與 operation-local report，A/B、fault、Dispose、lazy connector 及 session-state overload fail-closed 均有回歸測試。 | 新 fresh cycle 的 read-only preflight=go、provision=go；single ExecuteFixture=no-go（`write-not-committed`），故沒有完整可發布 CE evidence。 | `ListManager` 過渡 overload 與所有未遷移 legacy flow 固定 fail closed；P7.4/P7.5 保持禁止。 | 本 cycle strict ledger 的 exact cleanup=go（`fresh-fixture-cleaned`）；沒有 pending fixture。 |
| D | 付款成功防重播、失敗僅 reconciliation、unknown／pending fail-closed 的 local-only reducer/plan 與 A/B 測試已完成。 | 未執行。 | `CeExecutorEnabled=false`、`ConsumerEnabled=false`。 | 僅定義 reverse-known-keys contract，未建立 CE fixture。 |
| E | appointment create-zero／update-one、duplicate／missing no-go、already-applied no-replay 的 local-only reducer/plan 與 A/B 測試已完成。 | 未執行。 | `CeExecutorEnabled=false`、`ConsumerEnabled=false`。 | 僅定義 reverse-known-keys contract，未建立 CE fixture。 |
| F | onboarding 僅接受 fresh graph；partial／notification／uncertain no-go，並明定 present record → membership → contact 的 reverse-known-key cleanup；TDD 與 A/B 測試完成。 | 未執行。 | `CeExecutorEnabled=false`、`ConsumerEnabled=false`。 | 僅定義 reverse-known-keys contract，未建立 CE fixture。 |
| G | per-operation immutable draft 僅可 discard；fee/stor-lesson create/update 的 partial、timeout、owner-ready 不足均 no-replay，TDD 與 A/B 測試完成。 | 未執行。 | `CeExecutorEnabled=false`、`ConsumerEnabled=false`。 | draft 僅能 discard；其他 contract 僅定義 reverse-known-keys，未建立 CE fixture。 |
| H | attendance cardinality／create-vs-update、zero-active 不關聯、exactly-one 精確關聯、duplicate／unavailable fail-closed 的 reducer/plan 與 A/B 測試完成；不含 contact/owner/group/follow-up mutation。 | 未執行。 | `CeExecutorEnabled=false`、`ConsumerEnabled=false`。 | 僅定義 reverse-known-keys contract，未建立 CE fixture。 |

## 已驗證的安全邊界

1. D–H 共有 13 個固定 operation ID；catalog 是 process-static immutable metadata，回傳的 collection snapshot 互不共享，輸入與輸出 name collection 不能由呼叫端修改。
2. catalog 不含 Owner、entity、endpoint、credential、token、FetchXML、organization 或 profile 作為輸入；輸出僅為固定去識別化分類。
3. 所有 D–H catalog entry 的 CE executor 與產品 consumer 均為 `false`。
4. Data8 executor 對全部 13 個 D–H ID 在 resolver / admission / connector pool 之前回傳 `operation.not-supported`；回歸測試證明 admission acquire/release 與 connector client create/dispose 都是零。
5. Slice C 修正將借用的 CRM service 限制於當前操作呼叫鏈；靜態／動態名單 façade overload 均把傳入 service 原樣轉送。service 所有權、lease release、timeout eviction 與 Dispose 仍由外層 owner 管理，不能回寫 Factory 共用 ToolUtility。
6. local-only catalog 拒絕 `token`、`organization` 與 `profile` authority fragments；其輸入仍不能成為 connector／credential／profile 選路來源。
7. `ListManager.SetupIntegrateData(string, IOrganizationService)` 由於會讀取 session instance state，已在 CRM I/O 前固定拒絕；產品尚未接入具有完整 immutable context 的新入口。未遷移 legacy flow 仍直接依賴 Factory ToolUtility，因此是 P7.4/P7.5 的明確切流 blocker。

## 建置與測試方式

```powershell
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --no-restore --verbosity minimal
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore --filter FullyQualifiedName~LivePackage02Data8ListManagementEvidenceTests --verbosity minimal
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore --filter FullyQualifiedName~DownloadListManagerIsolationTests --verbosity minimal
dotnet build .\SpeechMessageProducts.ChurchReport\SpeechMessageProducts.ChurchReport.csproj -c Release --no-restore
```

`ToolUtility.Tests` 目前仍以 `net8.0` reference `net10.0` 的 ToolUtility，restore 會產生 `NU1201`；這是既有測試專案 target-framework 相容性問題，不能把它視為此候選版的通過結果，也不能用降 target 或略過隔離測試掩蓋。

## 2026-08-12 本機候選版驗證結果

- Slice F、G、H 均依 TDD 完成：先執行缺少 production API 的 RED，再以最小純本機 reducer／plan implementation 轉綠。三個新 Slice targeted suite 共 **62 passed、0 failed**。
- 完整 `SpeechMessage.Dynamics.Tests`：**660 passed、0 failed、7 skipped**；skip 均為明示的 live SQL coordinator tests，沒有被當成 CE evidence 或實機成功。
- `DownloadListManagerIsolationTests`、`DownloadIntegrateDataOperationServiceIntegrationTests` 與 `DownloadIntegrateDataPresentRecordIsolationTests`：**15 passed、0 failed**。
- `SpeechMessageProducts.ChurchReport` Release build：**0 warnings、0 errors**。
- byte-level check：本輪 16 個 task-owned C# 檔皆為 UTF-8 無 BOM、CRLF-only、final CRLF；`git diff --check` 通過。
- CCG final reviewer 在 45 秒預算內：Gemini 有可讀輸出且無 Critical／Warning；Claude session quota 無可用輸出，故本輪是「Gemini 單模型降級＋本機驗證」，不是完整雙模型審查。不得因此解除任何 CE、P7.4 或 P7.5 gate。
- 最後 solution quality gate：`dotnet test .\SpeechMessageProducts.sln --no-restore -m:1` 全數通過。
  Kestrel request-body 邊界案例在獨立重複 8 次、Dynamics 全套與 ChurchReport 真實程序邊界案例
  均通過；沒有以忽略 transport reset、關閉全域平行化或放寬 HTTP 413 assertion 來換取綠燈。

## 進入下一個 CE cycle 的唯一條件

只可在所有本機品質閘門通過後，以新的 nonce、新 ledger 與新的 task-owned fresh fixture 執行一次：

```text
bootstrap → read-only preflight → provision → single ExecuteFixture
→ exact read-back / reconcile → exact cleanup
```

任何 no-go、timeout、ambiguous、read-back mismatch 或 cleanup uncertainty 都立即停止該 CE mutation family；不重試、不切流、不移除 ToolUtility。本輪的唯一 ExecuteFixture 已發生 `write-not-committed` no-go，且 strict fresh-fixture cleanup 已完成；因此 CE 軌道目前 closed。
