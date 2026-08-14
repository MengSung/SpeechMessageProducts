# P7.4 MemberInfo 上課紀錄讀取授權邊界稽核

## 目標與使用者價值

針對權威矩陣的 `ORG-CALL-00027`（`memberinfo.storlessons.retrieve.by.contact`）完成來源、
授權與生命週期稽核，判定它能否在不改變既有 ChurchReport consumer 的前提下，成為獨立、
預設關閉、強型別且 request-local 的 ProductClient 唯讀 capability。

本 child 的正確交付物是安全設計決策。若不能證明 Gateway 所需的 immutable authorization
boundary，必須以精確的 source-only local design no-go 結案；不可用局部 Church path、既有
Session 或 legacy 授權結果製造看似可交付、實則跨使用者不安全的遷移。

## 已確認的來源事實

- 既有 `IPackage01FeeReadClient.RetrieveStorLessonsByContactAsync`、
  `lessons.stor.retrieve.by.contact` 及 DTO mapper 已存在；本 child 不建立第二個 executor、
  registry、ProductClient 或 raw CRM bridge。
- `LoadContactStorLessons` 在進行 typed composition 前會呼叫 `EnsureCorrectUserData()`，再以
  browser `contactId` 轉成 GUID，並呼叫 `CanViewContact(contactGuid)`。
- `CanViewContact` 會呼叫 `GetAccess()`；後者優先信任 Session `_MemberInfoAccess`，cache miss
  時從 shared `InMemoryContext.PersonalInfomationModel` 與 `InMemoryContext.ListManager` 推導並
  回寫 Session。
- Shepherd branch 會經 `GetShepherdContactIds()` 呼叫 `EnsureShepherdListsLoaded()`。當共享
  `ListManager` 尚未載入資料時，該方法以保存的 account/password 呼叫 `SetupListManager()`。
  因此 legacy credential-backed load 和 shared mutable state 出現在新 Gateway 授權決定之前。
- `EnsureCorrectUserData()` 本身也可能依 Session 密碼重設 shared `ListManager`，並使用 static
  validation cache。這不是 server-derived、immutable、request-local 的前置授權邊界。
- `StorLessonQueryService` 現有 trace 會輸出 contact GUID 與姓名；這不符合未來 Gateway 路徑的
  去識別化 diagnostics 要求，但本 child 不修改 runtime。

## 需求與約束

1. 完整稽核 `LoadContactStorLessons`、`EnsureCorrectUserData`、`GetAccess`、`CanViewContact`、
   Shepherd contact scope 與 typed StorLesson service，不能只以 controller 的呼叫順序判定安全。
2. 禁止將 Session、`InMemoryContext`、`ListManager`、保存帳密、static validation cache、
   ToolUtility、browser locator 或 caller-supplied profile/workload/endpoint/credential 當 Gateway
   authorization 或 routing authority。
3. 若 immutable server-derived MemberInfo scope 未在 Session、`InMemoryContext`、cache、
   legacy loader、client composition 與 CRM I/O 前建立，必須判定 local-design-no-go；不得新增
   sub-gate、runtime wiring、partial Church workaround、fallback 或 retry。
4. 只允許 task／CCG 記錄、來源稽核、限時外部審查、本機檢查、scope-only commit 與 archive；
   不得修改 runtime、matrix、feature gate、CE、fixture、traffic、P7.5 或 P8。

## 驗收條件

- [x] `source-audit.md` 能對應 matrix row、controller、base controller、access、Shepherd loader
      與 legacy／typed path 的因果關係。
- [x] `design.md` 精確決定可否進入 runtime implementation；若 no-go，必須列出禁止事項與最小
      恢復條件。
- [x] `implement.md` 僅包含 task record、審查、驗證、commit、archive，不包含 runtime、CE 或
      feature enablement 行動。
- [x] CCG task 記錄、限時雙模型狀態與本機 check 完成；scope-only commit 與 Trellis／CCG archive
      在本次 check 後執行。
- [x] 本機檢查證明沒有 runtime、matrix、gate、CE、traffic、P7.5 或 P8 變更。

## 非目標

- 不建立 `ORG-CALL-00027` 的新 registry、Data8 executor、ProductClient、feature gate 或 consumer route。
- 不修改 `MemberInfoController`、`BaseChurchController`、`InMemoryContext`、`ListManager`、
  `StorLessonQueryService`、ToolUtility、Session 或 cache。
- 不建立或執行 CE fixture、nonce、ledger、preflight、mutation、read-back、reconcile 或 cleanup。
- 不啟用 feature gate、不切換 ChurchReport 流量、不宣稱 consumer migration、ToolUtility removal、
  P7.5-ready 或 P8 readiness。

## 恢復條件

先完成獨立的 MemberInfo authorization-boundary child：由已驗證 principal 在伺服器端建立
不可變、request-local 的 Church／Shepherd authorization scope，而且 scope 建立必須早於任何
Session、`InMemoryContext`、cache、legacy `ListManager`、profile/client composition 與 CRM I/O。
Shepherd assignment 不可使用保存帳密或 `SetupListManager()` loader。完成後，才可重新規劃
`ORG-CALL-00027` 的固定、bounded DTO-only capability；其後仍須另有 A/B isolation、cancellation／
lease cleanup、CE 9.1、Embedded／Dedicated parity、rollback 與 traffic evidence。
