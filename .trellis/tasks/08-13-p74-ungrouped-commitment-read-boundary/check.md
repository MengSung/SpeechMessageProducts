# P7.4 ORG-CALL-00024 最終品質檢查

## 範圍

本次檢查僅涵蓋 `ORG-CALL-00024` 的非空未分組承諾 aggregate count 本機 ProductClient
邊界，以及其 task-owned 測試、設定與 parent metadata。沒有 CE request 或 mutation、沒有
feature gate enablement、沒有 ChurchReport 流量切換、沒有 ToolUtility／CRM SDK 移除，亦沒有
重試已關閉的 P7.2 Slice C cycle。

## 修正與驗證

- `TryCreatePackage02UngroupedCommitmentReadClient` 的 base/sub-gate 測試已直接呼叫專用 factory，並
  提供 deployment-owned `ProfileAlias=crm91`；gate=false 與 sub-gate-only 均在 host resolution 前回傳 null。
- 專用 factory 在建立 process host、provider、handler、Data8 pool 或 outbound I/O 前驗證非空 ProfileAlias。
- typed branch 使用固定 workload、deployment-owned profile 與 `RequestAborted`，不 retry、不 fallback 至
  legacy aggregate；typed DTO 會驗證 null／duplicate／negative count 並建立 request-local defensive copy。
- typed count 與既有 page exclusion set 同時使用時 bypass 三分鐘 grouped-id cache，避免同一頁使用新舊 snapshot；
  bypass 僅為本次 request，沒有新增 cache、session、timer、queue 或其他長生命週期 owner。
- public action、修改的 contract test 與新 service/test 均有繁體中文邊界與 lifecycle 文件；設定檔 gate 維持 false。

## 測試與建置

- `dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~DonationDynamicsAccessBootstrapLifecycleTests`：18 passed。
- `dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~Package02UngroupedCommitment`：9 passed。
- `dotnet test .\SpeechMessageProducts.sln --configuration Release --no-restore --nologo`：ChurchReport 589 passed／14 skipped；Dynamics 739 passed／7 skipped；其餘 solution test projects 均通過。
- `dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore --nologo`：0 warnings、0 errors。
- `git diff --check`：通過。
- 受影響 `.cs`／`.json`／task artifacts 已完成 UTF-8、無 BOM、CRLF、final CRLF byte-level 檢查。

## 結論

本 child 的本機品質閘門通過，可進入 scope-only commit 與 archive。這只是預設關閉的 local-only
候選，不構成 CE evidence、P7.4 cutover、P7.5 prerequisite-ready 或 P8 deployment evidence。
