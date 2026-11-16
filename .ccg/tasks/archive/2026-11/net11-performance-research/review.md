# 審查結果

## 狀態

- 研究結論：支持「目前維持 .NET 10；未來若升級 .NET 11，必須另做 `net11.0` retarget、套件相容性與正式流量形狀 benchmark」。
- 審查結論不是「證明 .NET 11 沒有改善」，而是「在目前 workload、硬體、輸入與樣本數下，沒有足夠證據宣稱實務改善」。
- 外部模型流程透過 `Start-CcgDualModelRun.ps1` 執行。Claude 產生可用審查；Gemini 兩次回傳 403／503，`dualModelComplete=false`，故本次為單模型 fallback，不是雙模型完成。

## 已修正

1. `run_research.py` 現在除 runtime version、Server GC、affinity 外，也驗證 `System.Private.CoreLib` 路徑包含預期 shared framework 版本。
2. `analyze_results.py` 對 `n<10` 的結果不再標為「改善／退步」，改為「方向性、低檢定力、未定論」。
3. 報告說明 bootstrap CI 在小樣本下僅為描述性區間，並揭露 micro 單輪約 `0.2–1.8` 的大變異。
4. 報告揭露 `Task.Delay(10)` 在 Windows 實測 p50 約 `15.6 ms`，不把它當成精準 10 ms 模型。
5. 報告區分正式批次與被後續批次取代的 pilot 證據。
6. 報告說明 process-wide `TotalProcessorTime` 可能包含 GC／JIT／背景執行緒，不把它當成案例本身 CPU 證明。

## Warning／限制

- 目前樣本數不足以對小於約 5% 的差異做高檢定力結論。
- Balanced 電源、IDE／其他工作樹、熱降頻及本機共享資源可能造成 outlier。
- HTTP synthetic sink 不代表 SQL 去重、可靠 queue、背景 worker 或外部 LINE／Dataverse latency。
- runtime-only 對照不包含完整 `net11.0` target 才能取得的 ASP.NET Core runtime-async code generation effect。

## 驗證

- SDK 10.0.403 csc 編譯成功，研究 IL hash：
  `9dc6644f8e05fc26f3e436426435a1e5b3057f509bc3ffa630bb9f16324eef4c`
- .NET 10.0.15／11.0.0 micro 與 HTTP 程序均驗證成功。
- `python -m py_compile` 通過 `run_research.py`、`analyze_results.py`。
- 原始輸出中的 correctness hash、channel isolation、bad signature、duplicate stable ID、registration drain、process shutdown 皆通過。
