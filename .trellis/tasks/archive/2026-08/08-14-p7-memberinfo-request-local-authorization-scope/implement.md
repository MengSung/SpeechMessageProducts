# P7 MemberInfo request-local target authorization scope 實作計畫

## Phase 1：規格與 CCG analysis

1. [x] 讀取目標、active task、parent matrix、MemberInfo contract、現有 request scope、
   login claims、controller 與 legacy assignment source。
2. [x] 建立 CCG M/high-risk task record 與 dual-model architect prompt。
3. [x] 執行一次 45 秒 bounded dual-model architect run；逾時後終止 process tree，
   記錄「雙模型未完成」，不重送。
4. [x] 將 source audit 與 fail-closed design 寫入 PRD/design；不把 partial typed catalog
   誤認為完整 assignment authority。

## Phase 2：TDD 與 local-only seam

1. [x] 在 `ChurchReport.MemberInfo.Tests/Security/MemberInfoTargetAuthorizationScopeTests.cs`
   先寫失敗測試：Church scope、Shepherd bounded list IDs、subject mismatch、source unavailable、
   incomplete evidence、empty/duplicate/invalid ID、A/B interleaving、cancellation 與 retained-state reflection。
2. [x] 執行 focused test，確認新型別不存在而失敗。
3. [x] 在 `SpeechMessageProducts.ChurchReport/Security/MemberInfoTargetAuthorizationScope.cs`
   建立 immutable enums、evidence/result/scope DTO 與純 resolver；不得加入 controller、DI、
   cache、Session、legacy manager、CRM SDK、profile、connector 或 I/O。
4. [x] 再跑 focused tests，確認所有 failure 固定分類且 scope defensive-copy。
5. [x] 加入 source contract test，證明現有 controller 未被接線、typed catalog 不會自動成為
   assignment source、future provider interface 不接受 browser selector／credential。

## Phase 3：品質與持久化

1. [x] 執行 MemberInfo focused suite、Release build、必要 full solution tests、UTF-8 no-BOM、
   CRLF/final-CRLF、`git diff --check` 與 scope check。
2. [x] 執行一次 45 秒 bounded CCG reviewer run；若無 usable output，記錄「雙模型未完成」並完成本機 review。
3. [x] 更新 parent PRD/design/implement/roadmap/task metadata，以最新 70-row post-runtime-health
   matrix 的 27 ProductClient implemented、67 unmigrated consumer 與 P7.5/P8 gates 為準。
4. [x] 更新 CCG task/review 記錄、完成 scope-only commit/archive；不得 stage 既有 user change 或
   `.ccg/dual-model-runs/`。
5. [ ] 用最新 matrix 選取下一個不依賴未證明 MemberInfo target source 的 P7 child；若不存在，
   記錄精確 source prerequisite，不重播 Slice C，也不啟動 P7.5/P8。

## 本次檢查證據（2026-08-14）

- RED：`Evidence_factory_is_not_publicly_callable` 先失敗，證明 public `Create` 可供任意
  consumer 偽造 evidence。
- GREEN：將 factory 限為 internal，僅以 `InternalsVisibleTo("ChurchReport.MemberInfo.Tests")`
  提供測試 fixture seam；focused 9/9 passed。
- 完整 MemberInfo：652 passed／14 skipped；Release build 0 warnings／0 errors。
- 第一輪 solution test 的不相關 Dynamics Kestrel test 出現一次 `ResponseEnded`；獨立
  Dynamics suite 885 passed／7 skipped，第二輪完整 solution test 亦全部通過，無程式修正。
- 最終 CCG reviewer：Gemini 45 秒 timeout、Claude 無 usable output，記錄「雙模型未完成」；
  已完成本機安全／scope／編碼檢閱。
