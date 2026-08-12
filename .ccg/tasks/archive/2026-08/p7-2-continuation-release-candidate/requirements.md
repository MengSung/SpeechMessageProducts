# P7.2 continuation 需求摘要

本任務承接已歸檔 P7.2 Slice C 的終態紀錄，但不會重新開啟或重試該任務的任何 CE cycle。目標是先以可重現的本機證據處理 child-process、原始 CRM 例外傳遞、共享 `IOrganizationService` 與資源生命週期風險；同時將尚未開始的 Slice D-H 拆成可本機驗證的 capability 範圍，並建立清楚標示 CE evidence 狀態的第一版 Release candidate。

既有 `harden-churchreport-error-recovery` 是本任務的根因前置工作，不得建立相同問題的重複 CCG task。新任務必須將其 findings、程式變更、測試與釋放保證納入軌道 A 的證據。

受控 CE 9.1 測試環境允許本次 task-owned fresh fixture 的 Create、Update、Assign、Delete、Associate 與 Disassociate；這是 mutation 授權，不是成功證據。任何新 CE cycle 都必須以新的 nonce、ledger、fixture、精確 read-back、reconciliation 與 cleanup 證明結果，且不得動到共享、stale、週報或正式資料。
# P7.2 continuation 與既有錯誤復原工作分工

`harden-churchreport-error-recovery` 是 DownloadListManager／例外復原根因的唯一 CCG 實作 owner。本 CCG continuation task 負責承接其驗證結果、Slice C 新 cycle gate、Slice D–H 本機 capability 與 Release candidate；不得對相同 service lifecycle 問題建立第二個獨立修正。

Track A 在該 predecessor 完成前，只可做根因研究、測試設計與安全契約；Track B 可做不含 CE mutation 的本機工作。所有修改仍必須寫入對應 Trellis task 與 CCG context。
