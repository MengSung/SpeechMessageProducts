# P7/P8 Parent 現況校正實施計畫

## 執行順序

1. [x] 讀取 goal objective、AGENTS.md、Trellis workflow、active P7/P8 task、authoritative matrix、
       P7.5 prerequisite report、封存 P7.2/P7.4 evidence 與目前 git state。
2. [x] 稽核 ORG-CALL-00066 的 source、tests 與封存 child；確認它已完成 local-only fee-editor
       boundary，不能重做或接入 editable FeeList chain。
3. [x] 建立本 task 的 PRD、design、implementation plan 及 context manifests，明確定義 evidence
       hierarchy、no-go boundaries 與 parent 文件最小校正範圍。
4. [x] 使用 CCG self-healing entrypoint 執行 P7/P8 documentation analysis；43 秒時沒有 usable output，
       已停止 bounded child process 並將「雙模型未完成」寫入 task record；不重試等待。
5. [x] 依 current evidence 以 append-only checkpoint 更新 parent PRD、design、implement、roadmap 與
       task metadata；不修改 matrix row、C#、settings 或 CE state。
6. [x] 驗證 JSON／Markdown UTF-8 no-BOM、CRLF、final CRLF、task validation、`git diff --check`、
       scope diff 與禁止變更 scan。
7. [x] 更新 check record，執行 Trellis spec-update judgment；接著完成 scope-only work commit 與 archive。

## 驗證命令

```powershell
python .\.trellis\scripts\task.py validate 08-14-p7p8-parent-current-state-reconciliation
git diff --check
git diff --name-only
```

另以 byte-level PowerShell 檢查本 task 與 parent 的變更檔案為 UTF-8 無 BOM、僅 CRLF、final CRLF，並確認
diff 沒有 `appsettings`、`.cs`、CE fixture 或 non-task production path。

## 停止與回復

- CCG 超時／quota：停止等待，記錄「雙模型未完成」，以本機 source evidence 繼續；不得重試等待。
  本輪 final reviewer 在 45 秒限制內未形成完整雙模型結果，後續才落下的單一 backend
  輸出不作為 accepted review；僅作為非決策性參考。
- 發現 parent 文件與 matrix／封存 child 衝突：只修正文件，將差異寫入 check record。
- 發現下一 capability 有 write adjacency、shared Entity bridge、credential/session ambiguity 或 special-resource
  ownership gap：記錄 no-go；不建立假的 read child，不改 gate、CE、P7.5 或 P8。
