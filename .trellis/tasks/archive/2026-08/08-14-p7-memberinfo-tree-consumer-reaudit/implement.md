# 執行計畫：MemberInfo tree consumer 重新稽核

## 順序與停止點

1. 對 authoritative matrix 的 00031／00032／00033 讀取 row contract、legacy call sites、既有 assignment source 與封存 source audits；不掃描 CRM、不啟動 CE。
2. 補齊本 PRD、design、CCG task requirements/plan/review 與可讀 metadata，將三個 row 的決策分開。
3. 以 `Start-CcgDualModelRun.ps1` 執行一次 architecture analysis。總等待時間最多 45 秒；逾時、quota 或無輸出時，記錄「雙模型未完成」並繼續本機稽核。
4. 以 source code、matrix、既有 tests 與已封存 audit 建立 `audit.md`：為每個 row 記錄 call chain、trust boundary、資料／資源生命周期、可行 child 或 no-go 依據。
5. 檢查 planning artifacts、metadata、git scope 與 Markdown encoding；再啟動本 child。只有 00031／00032 同時滿足設計條件，才建立另一個不重疊 implementation child；00033 維持 no-go。

## 驗證

```powershell
& 'C:\Users\Administrator\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' .\.trellis\scripts\task.py validate 08-14-p7-memberinfo-tree-consumer-reaudit
git diff --check
git status --short
```

此 planning child 不執行產品 build、CE request、fixture、Controller integration 或 feature gate，因為它不變更產品程式碼。若建立後續 implementation child，該 child 才必須在修改前執行 `trellis-before-dev`、TDD、targeted test、Release build、byte-level encoding/CRLF 檢查與完整 Trellis Check。

## 風險與回復

- 若 evidence 無法完整支持一個新 capability，停止在稽核結論，不猜選 scope、不加入 fallback、不接線 consumer。
- 若雙模型逾時或配額不足，保留 runner artifacts，標記降級，不重複等待。
- 若工作區出現非本 task 的變更，保持不修改、不 stage、不 commit；scope-only task artifacts 可獨立提交或封存。
