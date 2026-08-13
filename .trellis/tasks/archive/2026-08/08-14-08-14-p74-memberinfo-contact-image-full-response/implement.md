# P7.4 MemberInfo 完整聯絡人頭像回應邊界實作計畫

## 前置與禁止事項

- 已讀取 parent P7.4、P7.3 archived special-resource contracts、matrix、MemberInfo image routes 與
  cross-user isolation／Gateway hosting specs。
- 保持 Package03 base gate 和新 display sub-gate 為 false；不執行 CE、fixture、traffic、P7.5、P8、
  Official Worker、push 或 PR。
- 不修改舊 `GetContactImage`／batch／Personal image route；不以 ToolUtility、SDK Entity 或 fallback
  彌補新 typed route。

## TDD 執行順序

1. **RED：封閉 abstraction**
   - 在 `SpeechMessage.Dynamics.Tests` 新增 display operation registry/response union 測試。
   - 驗證 image、redirect、avatar 三 branch 是唯一選項，且 bytes／URL 的 mutable input 在 factory 後
     不會污染 response；多 branch、空 image、無效 URL、超長值都失敗。
   - 執行 focused test，預期因 operation／response branch 尚不存在而失敗。
2. **GREEN：Data8 和 ProductClient**
   - 在 Abstractions 加入 operation ID、registry definition、closed display request/response types。
   - Data8 新增固定 `Retrieve` 三欄 projection、image-first display union、URL/gender projection；在
     `OnPremiseData8ConnectorClientFactory`／executor allowlist 接線。
   - ProductClient 新增固定 request/result method；不得修改既有 image-only method。
   - 擴增 Dynamics tests，證明固定 operation、single projection、response kind、cancel/fault handling。
3. **RED：ChurchReport display service／route contract**
   - 先新增 service 和 controller contract tests，指定 base/sub false 短路、scope→parse→target auth
     順序、fixed profile/workload、三種輸出、取消傳遞、A/B isolation、無 cache／legacy fallback，及
     existing `GetContactImage` source 不變。
   - 執行 focused tests，預期 production service／route／gate 不存在而失敗。
4. **GREEN：disabled full display candidate**
   - 新增 request-local display service、獨立 route、bootstrap predicate/factory 和兩份 false setting。
   - image branch 僅做 bounded local thumbnail transformation；redirect/avatar branch 不建立任何
     transport 或 server cache。所有 generic catch 排除 `OperationCanceledException`。
   - 測試 GREEN；重新執行 abstraction/Data8/ProductClient focused suites。
5. **Check**
   - 執行 impacted Dynamics、ChurchReport focused suites、兩個 project tests、solution Release tests 和
     Release build。
   - byte-level 驗證所有本 child 實質變更 `.cs`／`.cshtml` 為 UTF-8 無 BOM、CRLF-only、final CRLF；
     執行 `git diff --check`、forbidden API／gate=false／scope scan、Trellis task validation。
   - 以 `Start-CcgDualModelRun.ps1` 進行 architect/reviewer（每次最多等待 45 秒）。修正可證實的
     Critical finding；quota/session/timeout 則記錄「雙模型未完成」，不等待或重試拖延。
6. **Finish**
   - 更新 child/parent check 記錄與 nextAction，只 stage 本 child 及其 task-owned implementation。
   - scope-only commit 並 archive；parent P7.4 保持 active，matrix 不升格為 migrated／CE complete。

## 回復點

- 任一 TDD test、A/B isolation、response union、encoding 或 review 顯示 SDK leakage、legacy fallback、
  partial response、cache retention 或取消處理錯誤時，撤回未提交 production change，保留 failure evidence
  並回到對應 RED checkpoint。
- 實機 enablement 永遠不在本 child；保持 display gate=false 即是 deterministic rollback，沒有
  fixture、CE mutation 或需回收的 server-side data。

## 實際完成紀錄（2026-08-14）

- [x] 完成 display Operation ID、closed union、Data8 fixed projection、identity mismatch fail-closed、
      ProductClient defensive copy/cancellation forwarding 與 ChurchReport request-local route/service。
- [x] 為 immutable 70-row source inventory 新增可追溯 derived mapping；schema 現在要求
      `derivedOperationMappings`，避免 matrix/registry agreement 因少一列而漂移。
- [x] 先確認 schema-required RED（4 tests 的 1 個預期失敗），再以最小 schema patch 轉為 GREEN（4/4）。
- [x] 完成 Dynamics、ChurchReport MemberInfo、solution Release tests/build、gate=false、forbidden API、
      UTF-8/no-BOM/CRLF/final-CRLF 與 diff scope checks；精確結果見 `check.md`。
- [x] 45 秒外部審查降級為「雙模型未完成」；reviewer 提醒的 non-default LINE port 與 C# CRLF 均已
      用 regression test／byte-level scan 修正並重新驗證。
- [x] 未執行 CE、fixture、traffic、P7.5、P8、Official Worker、push 或 PR；保持 sub-gate=false 即為
      deterministic rollback，無外部 cleanup 項目。
