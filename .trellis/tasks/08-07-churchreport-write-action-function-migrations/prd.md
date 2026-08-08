# P7.2 ChurchReport 寫入、Action 與 Function 能力遷移

## 目標

將 P7.0 coverage matrix 指派給 `P7.2-write-action-function` 的 24 個 ChurchReport CRM 寫入、Action 與 Function call-site，逐一改為 Data8-first 的具名能力。ChurchReport 必須只透過 ProductClient／Gateway contract 執行經允許的能力；不得暴露通用 CRUD、任意 FetchXML、任意 `OrganizationRequest`、呼叫端自選 CE／Profile／Connector 或直接 CRM SDK 存取。24 個 call-site 中的兩個 `entityimage` operation 保留固定商業 operation ID 與寫入語意，但其最多 5 MiB 的 binary transport 必須由 P7.3 bounded media／stream contract 實作；P7.2 不得為了提前完成而放寬既有 64 KiB Gateway JSON／dispatch envelope，或把圖片改成 Base64 scalar。

首個實作與真實 CE 9.1 證據切片是 `memberinfo.contact.update.basic.info`：只允許對 P7.2 task-owned 的 sunnyvalechback 測試會員更新 `mobilephone` 與 `address2_line1`，並在同一次受控流程中讀回驗證、復原原值與輸出去識別化證據。它建立寫入路徑的 idempotency、ambiguous-timeout、reconciliation、cleanup 與資源生命週期樣板；不是把其餘 23 項自動視為可寫。

## 已確認事實

- P7.0 已封存，coverage matrix 有 70 個 call-site，其中 24 個歸屬 P7.2：21 個 write、2 個 action、1 個 function；它們分屬 list membership、member profile、donation lifecycle、appointments、contact onboarding、fee lessons 與 attendance。
- `sunnyvalechback`（CE 9.1）與 `jesus`（CE 8.2）均為與正式組織隔離的開發 Organization；使用者於 2026-08-08 明確確認兩者的所有會員、收費單、課程、出席及其他資料均為虛構研發資料，授權 P7 在 coverage matrix 範圍內任意新增、修改或刪除。這項授權不得延伸到正式環境、P8 或 Central Gateway；CE 8.2 operation 仍只有在 matrix 明確標為 `required` 時才可執行。
- CE 8.2 寫入證據目前沒有 matrix `required` row；P7.2 對 CE 8.2 的寫入 dispatch 必須 fail closed，直到未來任務明確新增 required row 與其獨立 fixture。
- P7.1 已完成並封存，維持 `Embedded + Data8` 與 `DedicatedGateway + Data8` 的 Lenovo 路線；`Package01FeeReadsEnabled` 仍為 `false`，P7.2 不啟用 ChurchReport 流量、feature flag 或 P6.2／Official Worker。
- 現有 `OperationExecutionRequest` 已有 `IdempotencyKey` 欄位，但 Data8 executor 目前只允許 WhoAmI 與 P7.1 Package01 read；任何寫入路徑都必須先新增封閉 DTO、registry definition、connector template、回應 discriminator、ProductClient 與測試，不能擴張為 generic API。
- Dedicated Gateway 的 operation body 預設硬限制為 64 KiB，canonical dispatch envelope 同樣受 deployment-owned byte ceiling 約束，且目前只接受 bounded scalar。`memberinfo.contact.update.image` 與 `newperson.contact.update.image` 的既有產品契約允許 5 MiB 上傳，因此兩個 image row 必須移交 P7.3 建立串流、buffer、暫存 handle、取消與清理所有權；這是 coverage ownership 重校，不是刪除能力或縮小 ChurchReport 功能。

## 範圍

### 這個 P7.2 task 必須完成

1. 以 machine-readable activation matrix 將 24 項候選能力分成有獨立 transaction／rollback owner 的受控切片。
2. 對每個要實作的切片，在寫入前定義 fixture owner、允許 mutation、前置條件、CE version、idempotency、ambiguous-timeout、cleanup 與 reconciliation。
3. 先以測試驅動方式建立 contact basic-info 的封閉 Data8／ProductClient 能力、Embedded／Dedicated 路由及本機 lifecycle 測試；僅在 fixture preflight 通過後取得 CE 9.1 真實證據。
4. 以相同規則完成其餘 P7.2 matrix-required、可透過 bounded canonical operation envelope 表達的切片；金融、預約、會員清單、到會與新人流程各自擁有 rollback 邊界，不能與 contact profile 寫入混成同一個 transaction。
5. 未具備 approved fixture 的候選能力維持 `evidence-pending` 並在 dispatch 前 fail closed；不得因為 sunnyvalechback 可用便自動升格為可執行。
6. 對兩個 image row 產出可被 P7.3 接續的固定 operation ID、產品授權語意、5 MiB 輸入上限、格式驗證、內容生命週期、read-back／cleanup 與 sanitized evidence 要求；P7.2 關閉時它們必須標記為 `p7.3-media-dependency`，不得偽裝成已執行或從 coverage 消失。

### 明確不在本 task 範圍

- P6.2／Official Worker 啟動或 CE live compatibility 重試。
- CE 8.2 寫入、正式環境、雲端 Central Gateway、P8、push、PR 或部署。
- 啟用 ChurchReport feature flag、切換產品流量，或移除 ToolUtility／CRM SDK；這些屬 P7.4／P7.5。
- 對正式環境或 coverage matrix 未核准的 CE 記錄寫入、刪除，或以「寫了再刪」取代各切片已定義的 cleanup／reconciliation。
- 5 MiB 圖片的 Gateway binary upload、跨程序 media handle、串流 copy、暫存檔／buffer owner 與真實 CE image write evidence；這些屬 P7.3 special-resource，P7.2 只保存其具名商業語意與 fail-closed handoff。

## 受控切片與順序

| 切片 | Matrix operation ID | 數量 | CE 9.1 fixture／rollback 邊界 | 初始狀態 |
|---|---|---:|---|---|
| A. Contact basic info | `memberinfo.contact.update.basic.info` | 1 | 單一 task-owned contact；恢復兩個字串欄位原值 | planning-ready |
| B1. Contact LINE profile | `memberinfo.contact.update.line.profile` | 1 | 單一 contact；只復原三個 LINE profile 欄位 | planning-ready |
| B2. Ungrouped commitment aggregate | `memberinfo.contact.count.ungrouped.commitment` | 1 | 唯讀、固定 query semantics；不接受任意 FetchXML 或 caller-supplied grouped graph | planning-ready |
| B3. Contact image media handoff | `memberinfo.contact.update.image`、`newperson.contact.update.image` | 2 | P7.3 擁有 5 MiB bounded media transport、read-back、cleanup 與 evidence；P7.2 保持 dispatch fail closed | p7.3-media-dependency |
| C. List membership | 6 個 `list.*`／`contact.assign.owner`／transfer operation | 6 | task-owned contact、list、membership 與 owner；關聯復原 | fixture-pending |
| D. Donation lifecycle | 6 個 `payments.*` operation | 6 | task-owned donation／booking／fee graph；不可對真實付款資料操作 | fixture-pending |
| E. Appointments | `appointments.entity.create.or.update` | 1 | task-owned appointment 與 owner assignment 復原 | fixture-pending |
| F. Contact onboarding | `newperson.contact.create.full.onboarding` | 1 | task-owned contact graph；建立後以已知 ID 清理 | fixture-pending |
| G. Fee lessons | 3 個 `fees.*` operation | 3 | task-owned stor-lesson／fee graph；金額與狀態對帳 | fixture-pending |
| H. Attendance | 2 個 `presentrecord.*` operation | 2 | task-owned attendance graph；create/upsert 以 key 對帳 | fixture-pending |

## 驗收條件

- [ ] `p7.2-fixture-activation-matrix.json` 覆蓋全部 24 個 P7.2 candidate；每個可執行切片都有單一 owner、預設拒絕狀態與不可混用的 rollback 邊界，兩個 5 MiB image row 則有明確且可驗證的 P7.3 media dependency，不能被誤算為 P7.2 live evidence。
- [ ] Contact basic-info capability 的 API、DTO、registry、Data8 executor、ProductClient 與 ChurchReport adapter 均為具名且 bounded；不接受 `Entity`、`QueryBase`、`OrganizationRequest`、endpoint、credential、任意欄位 map 或 caller-selected routing。
- [ ] 寫入的 request、lease、client、buffer、CTS、cancellation registration、stream 與暫存資料有唯一 owner、大小／時間上限與成功、取消、例外、timeout、drain 的確定釋放路徑；不得出現 session、profile、tenant、credential、memory 或 resource leakage。
- [ ] 每一個 live CE 9.1 write 在執行前通過 fixture preflight，在執行後完成 read-back、reconciliation、cleanup 與 sanitized evidence；ambiguous timeout 不做盲目重試。
- [ ] CE 8.2 及任何未核准切片一律 fail closed，且測試可證明它們不會取得 connector lease 或送出 CRM operation。
- [ ] Operation JSON／canonical envelope 不接受 binary、Base64 image 或 caller-supplied media path；image operations 在 P7.3 完成 bounded upload／handle contract 前保持 fail closed，且既有 64 KiB Gateway operation body ceiling 不被放寬。
- [ ] 完整相關 unit／integration／lifecycle／stress／rollback 驗證、Release build、UTF-8 no-BOM、CRLF-only、final CRLF、`git diff --check`、Trellis check 與 task-owned commit/archive 均通過。

## 未決事項與判斷規則

不需要使用者現在手動提供 GUID、密碼或欄位值。第一個實際 CE write 前，P7.2 會先完成 task-specific、Windows PowerShell 5.1-compatible 的受限 preflight／fixture bridge；它只建立或選取一筆帶本機 fixture marker 的測試 contact，且只回傳去識別化 JSON。若該 bridge 無法證明 owner、基線讀取或 cleanup 能力，僅暫停切片 A 的 live evidence，不倒退 P6／P7.0／P7.1，也不開放其他寫入切片。
