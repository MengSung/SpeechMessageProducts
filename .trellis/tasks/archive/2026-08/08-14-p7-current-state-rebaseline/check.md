# P7 current-state rebaseline 檢查紀錄

## 2026-08-14 規劃與分析

- 以 project self-healing entrypoint 執行一次 architect run：`TimeoutSeconds=45`、`MaxAttempts=1`。
  期限內無 usable backend output；記錄「雙模型未完成」，不重送。
- 完成 matrix authority、parent drift、candidate audit。current source hash 與 archived matrix 不同；
  P7.4 direct safe candidate 為零，P7.5／P8 維持 fail closed。

## 2026-08-14 實作與本機驗證

- task-owned wrapper 只允許 child 目錄中的 matrix output，重用 immutable archived offline analyzer，
  不接受 network、credential、CE、feature 或 traffic 輸入。
- matrix 產生／validate 成功，70 rows、schema valid；summary 由同次 matrix 計算。
- Python focused tests 涵蓋 70-row、Slice C no-go、local-only、output containment、tamper rejection、
  UTF-8 PowerShell stderr decoding 與 summary regression。
- 最終 reviewer run 同樣依 45 秒／單次上限執行；沒有 usable output，記錄「雙模型未完成」，不重送。
- 最終品質檢查發現：以 repository-root 相對 `-File` 路徑執行 wrapper 時，PowerShell 在參數預設值求值階段尚未提供 `$PSScriptRoot`，使預設 matrix 路徑解析失敗。先新增實際相對呼叫的 regression test，確認失敗後，才將預設 output 選擇移至已解析 task root 的 script body；wrapper 仍拒絕 task root 外的路徑。
- 修正後重新執行 focused wrapper tests：6 passed；matrix Build／Validate、JSON parse 與 `task.py validate` 皆通過。
- byte-level encoding scan 已確認本 child、CCG task 與本次修改的 parent 檔案皆為 UTF-8 無 BOM、CRLF-only、final CRLF。
- `dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore`：成功，0 warnings、0 errors。
- `dotnet test .\SpeechMessageProducts.sln --configuration Release --no-restore`：成功，0 failures。先前一次 Kestrel chunked-transport failure 未重現；先前 focused test 與本次完整 suite 均通過，故本 child 沒有修改不相關的 C# transport code。
- `git diff --check` 與 task scope review 通過；未 stage `.ccg/dual-model-runs/`、既有 P7.4 CCG artifact 或其他不屬於本 child 的變更。

## Parent 校正

- parent 記錄 P7.4 已封存 20 capability children、00057 local-only data plane、00011／00012 action no-go，
  並移除已完成 00052 archive action 作為 nextAction。
- archived P7.5 report 被明確標示為舊 hash 的歷史 no-go snapshot；current matrix 本身足以保持
  P7.5 no-go，但 P7.5 前仍需用 current matrix 重建完整 source/project/settings successor report。
