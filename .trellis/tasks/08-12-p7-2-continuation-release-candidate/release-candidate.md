# P7.2 continuation 第一版 Release candidate

## 識別與用途

- 產物狀態：**本機驗證候選版；不得作為 P7.4 流量切換或 P7.5 ToolUtility 移除依據。**
- 適用部署：既有 deployment-owned `crm91 + Data8 + CE 9.1` 的本機建置與測試。
- 不包含：CE 8.2、Official Worker、feature flag、ChurchReport 流量切換、共享或正式資料 mutation。
- 歷史限制：已歸檔 P7.2 Slice C cycle 維持 closed，絕不可重試。

## 各 Slice 狀態

| Slice | 本機狀態 | CE 實證 | Runtime / rollout 狀態 | cleanup 狀態 |
| --- | --- | --- | --- | --- |
| C | 已修正 operation-scoped `IOrganizationService` 不再寫入共享 ToolUtility；三個 dynamic-list façade overload 亦已驗證只使用呼叫端 service；child no-go 分類與 stack preservation 已有回歸測試。 | 尚未取得新的獨立 cycle evidence。 | `DownloadIntegrateData` 仍直接持有 Factory ToolUtility，P7.4/P7.5 保持 fail closed。 | 舊 cycle 已完成紀錄中的 cleanup；新 cycle 尚未開始。 |
| D | 6 個 donation lifecycle ID 與 local-only contract 已建立。 | 未執行。 | `CeExecutorEnabled=false`、`ConsumerEnabled=false`。 | 僅定義 reverse-known-keys contract，未建立 CE fixture。 |
| E | appointment contract 與 operation ID 已建立。 | 未執行。 | `CeExecutorEnabled=false`、`ConsumerEnabled=false`。 | 僅定義 reverse-known-keys contract，未建立 CE fixture。 |
| F | contact onboarding contract 與 operation ID 已建立。 | 未執行。 | `CeExecutorEnabled=false`、`ConsumerEnabled=false`。 | 僅定義 reverse-known-keys contract，未建立 CE fixture。 |
| G | fee lessons 的 3 個 local-only contracts 已建立。 | 未執行。 | `CeExecutorEnabled=false`、`ConsumerEnabled=false`。 | in-memory draft 僅能 discard；其他 contract 僅定義 reverse-known-keys，未建立 CE fixture。 |
| H | attendance 的 2 個 local-only contracts 已建立。zero-active 不關聯週報；exactly-one 精確關聯；duplicate/unavailable fail closed。 | 未執行。 | `CeExecutorEnabled=false`、`ConsumerEnabled=false`。 | 僅定義 reverse-known-keys contract，未建立 CE fixture。 |

## 已驗證的安全邊界

1. D–H 共有 13 個固定 operation ID；catalog 是 process-static immutable metadata，回傳的 collection snapshot 互不共享，輸入與輸出 name collection 不能由呼叫端修改。
2. catalog 不含 Owner、entity、endpoint、credential、token、FetchXML、organization 或 profile 作為輸入；輸出僅為固定去識別化分類。
3. 所有 D–H catalog entry 的 CE executor 與產品 consumer 均為 `false`。
4. Data8 executor 對全部 13 個 D–H ID 在 resolver / admission / connector pool 之前回傳 `operation.not-supported`；回歸測試證明 admission acquire/release 與 connector client create/dispose 都是零。
5. Slice C 修正將借用的 CRM service 限制於當前操作呼叫鏈；靜態／動態名單 façade overload 均把傳入 service 原樣轉送。service 所有權、lease release、timeout eviction 與 Dispose 仍由外層 owner 管理，不能回寫 Factory 共用 ToolUtility。
6. local-only catalog 拒絕 `token`、`organization` 與 `profile` authority fragments；其輸入仍不能成為 connector／credential／profile 選路來源。
7. `ListManager.SetupIntegrateData` → `DownloadIntegrateData` 仍有 Factory ToolUtility service、converter 與 partial-flow 依賴；尚未完成 request-local propagation、fault eviction 與 interleaved A/B lifecycle 證據前，它是 P7.4/P7.5 的明確切流 blocker。

## 建置與測試方式

```powershell
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --no-restore --verbosity minimal
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore --filter FullyQualifiedName~LivePackage02Data8ListManagementEvidenceTests --verbosity minimal
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore --filter FullyQualifiedName~DownloadListManagerIsolationTests --verbosity minimal
dotnet build .\SpeechMessageProducts.ChurchReport\SpeechMessageProducts.ChurchReport.csproj -c Release --no-restore
```

`ToolUtility.Tests` 目前仍以 `net8.0` reference `net10.0` 的 ToolUtility，restore 會產生 `NU1201`；這是既有測試專案 target-framework 相容性問題，不能把它視為此候選版的通過結果，也不能用降 target 或略過隔離測試掩蓋。

## 進入下一個 CE cycle 的唯一條件

只可在所有本機品質閘門通過後，以新的 nonce、新 ledger 與新的 task-owned fresh fixture 執行一次：

```text
bootstrap → read-only preflight → provision → single ExecuteFixture
→ exact read-back / reconcile → exact cleanup
```

任何 no-go、timeout、ambiguous、read-back mismatch 或 cleanup uncertainty 都立即停止該 CE mutation family；不重試、不切流、不移除 ToolUtility。
