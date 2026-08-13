# P7.4 MemberInfo 個人出席紀錄 typed read boundary 實作計畫

> 此計畫只實作 ORG-CALL-00026 的 disabled-by-default local candidate。不得執行 CE、fixture、
> traffic、P7.5、P8、push 或 PR；歷史 P7.2 Slice C 保持 closed。

## 1. 規劃與啟動

- [x] 讀取目標、AGENTS.md、parent/archived evidence、immutable matrix、現有 controller/Package02 patterns
      和 mandatory backend/guides specs。
- [x] 確定不擴張既有 contact-profile write client，採獨立 present-record read client，並記錄 fixed query、
      bounded DTO、authorization、rollback 與 local-only 邊界。
- [x] 發起過一次限時 CCG architect analysis；45 秒後沒有可採用的完成結果，記錄「雙模型未完成」，
      不再次等待同一輪，改採本機 evidence。
- [x] child 已進入 in-progress，並建立 CCG task record 與 JSONL context；本 session 的 current-task pointer
      已由 Trellis session context 指向此 child，無須另行建立重複 task。

## 2. RED：最小跨層契約測試

- [x] 已新增 registry/wire、Data8、ProductClient、ChurchReport service、bootstrap 與 controller source-contract
      tests；既有 task notes 記錄 controller/bootstrap 兩組 contract test 在 implementation 前分別為 0/3 與 0/2
      預期 RED，避免把現有實作誤當成未被驗證的行為。
- [x] 已測試固定 operation/response branch、allowed parameters、single-page and bounded query、defensive copies、
      profile/workload isolation、cancellation/no retry、false-gate no composition、profile-before-host、
      authorization-before-typed-dispatch 和 legacy false branch。

## 3. GREEN：最小受控實作

- [x] 在 abstractions 新增 operation ID、registry definition、response union branch 和 immutable wire record。
- [x] 在 Data8 新增唯一 fixed-query executor branch、strict parameter/query/page/schema/text/byte validation，並在
      factory dispatch allowlist 接線；contact fullname 由同一固定 inner link 投影，沒有第二次 CRM Retrieve。
- [x] 在 ProductClient 新增獨立 interface/client/DTO，僅接受 bounded request、驗證 exact response contract，
      並以 request-local defensive copy 回傳。
- [x] 在 ChurchReport 新增 present read service、base/sub gate predicate/factory 與 false settings；controller 的
      true branch 為 async typed DTO projection，false branch 保留 legacy source 與 DataSourceLoader contract。
- [x] 每個 production edit 後已跑對應 RED/GREEN focused tests；沒有不相關重構、feature enablement、CE 或流量操作。

## 4. Check、提交與封存

- [x] 已完成 focused Dynamics 19/19、ChurchReport 9/9、完整 solution Release test（Dynamics 826 passed、7 skipped，
      兩個 .NET Framework worker suite 各 19 passed）、Release build（0 warning／0 error）、byte-level UTF-8 no-BOM/
      CRLF/final-CRLF、`git diff --check`、scope/forbidden API/isolation/lifecycle scans。
- [x] 已透過 CCG self-healing runner 執行 final review。Gemini 有可用 PASS 結果；Claude 因 provider session limit
      未完成，已依 45 秒規則記錄為「雙模型未完成／single-model degraded fallback」，未重試等待。
- [x] 已更新 matrix row 和 P7.4 parent，僅標示 local disabled candidate；接續完成 Trellis Check、scope-only commit
      和 archive。下一個 child 必須從 matrix 選取，且不得啟動 P7.5/P8。
