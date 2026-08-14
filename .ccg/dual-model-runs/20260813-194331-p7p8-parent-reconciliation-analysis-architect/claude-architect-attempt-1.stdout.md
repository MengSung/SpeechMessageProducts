# P7/P8 Parent 現況校正分析（唯讀）

## 範圍與方法
比對 `.trellis/tasks/08-05-gateway-purpose-and-positioning/{prd,design,implement,roadmap-p5-p7,task}.{md,json}` 與 repository 目前真實狀態（`.trellis/tasks/` 現存目錄、`.trellis/tasks/archive/2026-08/` 封存目錄、`git log`）。已確認：P3–P6、P7.0、P7.3 封存；P7.1 僅部分 typed read + CE 9.1 唯讀 evidence；P7.2 舊 Slice C write-not-committed 且 cleanup 完成、不可重試；P7.4 可繼續 disabled local-only child；P7.5 no-go；P8 尚未建立。五份文件在「不可重試 P7.2 舊 cycle」「P7.5/P8 gate 未解除」「feature gate 維持 false」三項上完全一致，**未發現會導致誤啟動 P7.5/P8、變更 gate 或假稱 CE evidence 的內容**。以下為精確度／時效性層級的校正建議。

---

## Critical
無。（五份文件皆未含會誤導後續執行去啟動 P7.5/P8、開啟 feature gate 或宣稱 CE/traffic evidence 的敘述。）

---

## Warning

### W1. `task.json` 的 `nextAction` 描述已完成的動作，未反映其後多個已封存的 P7.4 child
`task.json:21` 目前寫：
> "Archive the completed P7.1 dedication-booking typed-read child, then create and start the next independently verifiable local-only P7.4 capability child from the authoritative 70-row matrix backlog."

但 `git log` 顯示該 child（`08-13-08-13-p71-dedication-booking-typed-read`）已在 commit `cdc00e0f`／`7217832d` 封存，且封存**之後**又新增並封存了三個 P7.4 child：`08-13-08-13-p74-dedication-booking-read-boundary`（封存於 `36dd807c`／`cd64b7a6`）、`08-13-p74-auth-contact-lookup-boundary`（封存於 `079c5c8f`）、`08-13-p74-authentication-credential-policy-boundary`（新增 `docs(auth): define credential verification boundary`，封存於 `4e1d2636`／`99be6aea`）。`currentBaseline`（`task.json:6`）與 `notes`（`task.json:40`）也只停在 ORG-CALL-00041 的驗證結果，未提及後續三個 child。

**風險**：後續代理讀到 nextAction 會重複去做「封存已封存的 child」，且不知道 P7.4 已新增一份 credential verification boundary 決策（已寫入 `.trellis/spec/backend/cross-user-isolation-and-performance.md`），可能重工或忽略最新邊界決策。

**建議校正（繁中）**：
> `nextAction`：08-13-08-13-p71-dedication-booking-typed-read 及後續三個 P7.4 child（dedication-booking-read-boundary、auth-contact-lookup-boundary、authentication-credential-policy-boundary）均已封存。下一步請至 `08-12-churchreport-productclient-cutover` 之 task.json 讀取其現行 `nextAction`，依 authoritative 70-row matrix backlog 建立下一個獨立可驗收、disabled-by-default 的 P7.4 local-only child；不得重新建立已封存項目，亦不得因本次校正啟動 P7.5/P8 或變更 feature gate。

### W2. `roadmap-p5-p7.md` 第 3 節「目前真實狀態」表格 P7.1／P7.4 列未涵蓋後續已封存證據
`roadmap-p5-p7.md:44` P7.1 列只列「六項 Package01 typed Data8 read」，未提及已封存的 ORG-CALL-00041（認獻單 dedication-booking typed read，P7.1 追加項目）。`roadmap-p5-p7.md:47` P7.4 列只列 legacy admission boundary，未涵蓋其後封存的 dedication-booking-read-boundary、auth-contact-lookup-boundary、authentication-credential-policy-boundary 等 child。

**風險**：該表格標示為「目前真實狀態」，若讀者將其當作最新盤點依據，會低估已完成的 local-only 證據範圍，可能導致重複研究或誤判 backlog 覆蓋率。

**建議校正（繁中，可附於表格下方既有 checkpoint 段落之後）**：
> ### 2026-08-13 P7.1／P7.4 追加封存 checkpoint
> P7.1 除六項 Package01 typed read 外，另有 ORG-CALL-00041（認獻單 dedication-booking typed read）已完成 registry／Data8 fixed-query／closed-response／ProductClient 本機驗證並封存；仍非 consumer／CE／traffic／feature-gate evidence。P7.4 除 legacy admission boundary 外，另有 dedication-booking-read-boundary、auth-contact-lookup-boundary、authentication-credential-policy-boundary 等 disabled-by-default local child 已完成並封存；所有 checked-in feature gate 維持 false，P7.5/P8 gate 不變。

---

## Info

### I1. `prd.md` 與 `design.md` 的歷史補充段落均停在 2026-08-13 P7.5 前置 evidence checkpoint，未包含其後的 P7.1／P7.4 封存
與 W2 同類問題，但 `prd.md`／`design.md` 中的段落屬「一次性歷史紀錄」而非即時狀態表，誤導風險較低。建議日後每完成一個 P7.4/P7.1 里程碑時，比照既有格式（例如 `design.md` 的「2026-08-13 P7.4 admission boundary 完成後的設計狀態」段）於本 parent 追加一段簡短 checkpoint，避免長期漂移累積成需要一次大改的落差。

### I2. `task.json` 的 `relatedFiles`／`children` 引用路徑均已核實存在
`authoritative-gap-matrix.json`、`capacity-enablement-audit.md` 均可在對應路徑找到，`children` 中列出的 `08-12-churchreport-productclient-cutover` 仍為現行未封存的 active parent，`parent: null` 正確。此部分無需校正，僅供記錄本次核對範圍已涵蓋。

---

## 結論
本次分析未發現需要 Critical 等級介入的過期內容；核心 gate（P7.2 舊 cycle 不可重試、P7.5/P8 未啟動、feature gate=false）在五份文件中保持一致且正確。主要落差是 W1／W2 兩處「現況快照」未跟上其後已封存的 P7.1 追加項目與三個 P7.4 child，建議依上方文字做最小範圍補充，不變更任何決策、gate 或範圍界線。

---
SESSION_ID: 17efae55-8fc7-455b-8a4e-36a81379d55c
