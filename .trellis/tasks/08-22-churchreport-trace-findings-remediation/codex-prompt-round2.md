# 已作廢 — 請改用下列兩份

本檔（原「第二輪提示詞」）已依上線優先級拆成兩份，**請勿再貼這一份給 Codex**。

| 檔案 | 用途 | 何時執行 |
|---|---|---|
| `codex-prompt-2a-release-gate.md` | 上線前把關：修正一處矛盾註解 + 實際跑一次 `SaveIntegrate` 煙霧測試 | **先做。這是唯一的上線阻斷項。** |
| `codex-prompt-2b-cleanup.md` | 上線後清理：提交重整、`requiresRefresh` 決策、`SyncRoot` 採用、例外摘要、Trellis 收尾 | 2A 通過後，可分次做 |

拆分理由：原本的 R1–R6 混合了「不做就不能上線」與「工程品質債」兩類事項，
容易讓人誤以為六項都是上線條件。實際評估後，六項之中只有「實際執行一次
`SaveIntegrate`」是真正的阻斷項——因為該功能的資料流被大幅改寫，
卻沒有任何測試覆蓋整條路徑，也從未被真正執行過。

其餘各項的殘留風險都**嚴格小於**修改前的狀態，可以帶著上線並排入後續。

相關輔助腳本：

- `.trellis/scripts/verify_trace_invariants.py` — 逐條驗證 trace 生命週期不變量
- `.trellis/scripts/check_encoding.py` — 逐位元組驗證變更檔案的編碼契約
