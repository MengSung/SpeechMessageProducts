# P7 現況重新基準化

## 目標與使用者價值

以目前工作樹、封存 P7 證據與既有靜態分析器重新產生可重複驗證的 P7 權威差距矩陣，校正 P7 parent 的過期現況敘述與下一步。這讓後續 child 能依真正的 capability 缺口推進，而不是重做封存工作、重播歷史 CE cycle 或把本機 contract 誤稱為上線證據。

## 已確認事實

- P3–P6、P7.0、P7.1、P7.2、P7.3 的既有任務均為唯讀封存證據；不得修改或重做。
- 歷史 P7.2 Slice C 是 `write-not-committed` no-go，exact cleanup 已完成；舊 nonce、ledger、fixture、descriptor 與 cycle 永不可重播。
- P7.4 parent 仍在進行 default-disabled 本機能力遷移。最新 ORG-CALL-00057 已完成 registry、Data8 與 ProductClient 資料平面及本機驗證，但沒有 consumer、CE、host 或 traffic 證據。
- 現有離線 analyzer 對目前程式碼產生 70 rows：registry declared 28、Data8 executor implemented 27、ProductClient implemented 26、consumer migrated-disabled 3、CE 9.1 succeeded 6；所有 rows 仍為 temporary-legacy。
- P7.5 prerequisite report 仍為 deterministic `no-go`：70 temporary-legacy、67 consumer-not-migrated，且 CE／host／parity／soak／drain／rollback 與 legacy reference 缺口尚存。

## 範圍

1. 以封存 70-row source identity、現行 registry／Data8 executor／ProductClient／ChurchReport source 與 P7.5 report 產出 task-owned、去識別化權威矩陣。
2. 為矩陣提供可重複執行的 task-owned rebuild／validate 入口與 focused contract tests。
3. 更新 `08-05-gateway-purpose-and-positioning` 的 PRD、design、implement、roadmap、task metadata，使其正確反映已封存基線、P7.4 現況、P7.5 no-go、P8 gate 與下一個 child 選擇條件。
4. 在 parent 中持久化這次雙模型 45 秒上限到期、未取得 usable output 的降級結果。
5. 根據矩陣和 source audit，選擇或精確排除下一個獨立 P7 child；不以單一 no-go 停止其他獨立 P7 工作。

## 不在範圍

- 不執行 CE Create、Update、Assign、Delete、Associate、Disassociate、feature flag、traffic、CE 8.2、Official Worker 或雲端部署。
- 不建立 P7.5 removal child、P8 parent／child，或宣稱已有 immutable handoff。
- 不讀取或輸出 endpoint、帳號、CRM ID、credential、token、cookie、原始 CRM 回應、原始例外或任何 baseline 值。
- 不修改其他 task 的既有工作區變更或未受本 child 擁有的檔案。

## 驗收條件

- [ ] 新 matrix 恰有 immutable source 的 70 個唯一 call-site rows，且 checksum、schema、固定分類與排序均可驗證。
- [ ] matrix 分別記錄 registry、executor、ProductClient、consumer、CE 8.2／9.1、Embedded／Dedicated、rollout、rollback、temporary legacy、special-resource、P7.5 blocker；任一欄位不得推導另一欄位成功。
- [ ] matrix／validator 將 historical Slice C 維持 `no-go-closed`、D–H 維持 local-only／not-executed，且拒絕將 local-only、disabled gate、靜態宣告或 unit-test pass 升格為 CE／consumer evidence。
- [ ] parent 文件與 task metadata 不再要求重做封存 P6／P7.0 或封存 P7.2 cycle，並明確維持 P7.5／P8 fail-closed gate。
- [ ] 完成 targeted tests、matrix validation、JSON parsing、UTF-8 無 BOM、CRLF、final CRLF、`git diff --check`、scope check 與相稱的 review 記錄。
