# 執行計畫：完成安全 no-go 紀錄

1. [x] 讀取目標、AGENTS.md、P7 parent、P7.4 parent、權威矩陣與 legacy source。
2. [x] 對照 `ORG-CALL-00047` 的 caller、static/dynamic 分支、stored FetchXML 與 shared service fallback。
3. [x] 寫入 PRD、source audit、design、context manifest 與本文件；範圍限定 task／CCG 文件。
4. [x] 以 CCG self-healing runner 執行一次 bounded architecture analysis；每個 backend 最多等待 45 秒。
      Gemini 逾時後留下可用輸出；Claude 未產生可用輸出。依 45 秒上限記錄「雙模型未完成」，不重試等待。
5. [x] 依分析與本機 evidence 撰寫 planning／review 結果。Gemini 未發現 Critical；其「亂碼」Warning 經原始 bytes
      驗證為誤判：本 child 新增檔案皆為 UTF-8 無 BOM、無 replacement character、CRLF 與 final CRLF。
6. [x] 執行 JSON／UTF-8／CRLF、`git diff --check` 與 scope 檢查；沒有 runtime、matrix、gate、CE、traffic、P7.5 或 P8 變更。
7. [x] 更新 P7 parent 的 current state／next action，將 child scope-only commit 後 archive。

## 驗證命令

```powershell
python -m json.tool .trellis/tasks/08-14-p71-list-member-count-typed-read/task.json > $null
python -m json.tool .ccg/tasks/p71-list-member-count-typed-read/task.json > $null
git diff --check
git diff -- .trellis/tasks/08-14-p71-list-member-count-typed-read .ccg/tasks/p71-list-member-count-typed-read
```

## 風險與停止點

- 若分析發現先前記錄遺漏 dynamic query、shared state 或 authorization fact，僅修正 task 文件；不擴大為 runtime implementation。
- 若 CCG provider 在 45 秒內未完成，記錄「雙模型未完成」並以本機來源與檢查繼續；不得重複等待。
- 此 child 不授權任何 CE、feature gate、traffic、P7.5 或 P8 行動。
