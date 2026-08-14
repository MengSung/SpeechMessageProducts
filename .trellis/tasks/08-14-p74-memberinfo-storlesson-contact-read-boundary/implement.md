# 執行紀錄：完成 MemberInfo 上課紀錄授權邊界安全決策

## 執行範圍

本 child 僅有文件與 source audit。它不修改 `.cs`、`.cshtml`、registry、executor、
ProductClient、appsettings、matrix、feature gate、CE 或產品流量。

1. [x] 讀取 active goal、AGENTS.md、P7/P8 parent、P7.4 parent、權威 matrix、相關 controller／
   service 與 cross-user isolation contract。
2. [x] 對照 `ORG-CALL-00027`、`LoadContactStorLessons`、`EnsureCorrectUserData`、`GetAccess`、
   `CanViewContact`、`GetShepherdContactIds`、`EnsureShepherdListsLoaded` 及 existing typed service。
3. [x] 修正 PRD／design／本文件，使原先假設可安全接線的 sub-gate plan 轉為 source-only local
   design no-go，並記錄完整禁止事項與恢復條件。
4. [x] 透過 CCG self-healing runner 發起一次 bounded architect analysis；每個 backend 最多等待 45 秒。
   Gemini 有 output、Claude 無 usable output；記錄「雙模型未完成」，採本機完整 source trace，
   不重試等待。
5. [x] 建立 CCG task／review record，執行 task JSON、manifest、UTF-8／CRLF／final-CRLF、
   `git diff --check`、scope check 與限時 final review。
6. [x] 更新 P7.4 parent checkpoint；下一步執行 scope-only commit，封存 Trellis／CCG child；不改動既有 dirty files。
7. [ ] 從 authoritative gap matrix 選擇下一個不依賴此 MemberInfo authorization chain 的 safe local child；
   此項屬 parent 的後續 scheduling，不擴大本 child scope。

## 明確停止點

- 此 child 不得進入 runtime implementation。任何「僅保留 legacy 或僅 Church route」的接線都不能
  修復 gateway authorization boundary，且禁止作為 workaround。
- 不發出 CE request，不建立 fixture、nonce、ledger、preflight、mutation、read-back、reconcile 或 cleanup。
- 不重播歷史 P7.2 Slice C；不啟用 gate、不切流量、不修改 ToolUtility；不開始 P7.5 或 P8。

## 驗證命令

```powershell
python ./.trellis/scripts/task.py validate .trellis/tasks/08-14-p74-memberinfo-storlesson-contact-read-boundary
python -m json.tool .trellis/tasks/08-14-p74-memberinfo-storlesson-contact-read-boundary/task.json > $null
python -m json.tool .ccg/tasks/p74-memberinfo-storlesson-contact-read-boundary/task.json > $null
git diff --check
git diff -- .trellis/tasks/08-14-p74-memberinfo-storlesson-contact-read-boundary .ccg/tasks/p74-memberinfo-storlesson-contact-read-boundary
```
