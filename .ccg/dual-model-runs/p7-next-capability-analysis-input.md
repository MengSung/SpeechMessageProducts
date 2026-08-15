# P7 下一個 capability 的唯讀架構分析

請只做唯讀分析，不能修改檔案、執行 CE 操作、啟用 feature gate、切換流量或建議跳過 P7.5/P8 gate。

目前的權威狀態：70-row matrix 中 Registry 28 declared / 13 local-only / 29 not-declared；
Data8 27 implemented / 13 local-only-rejected / 30 not-implemented；ProductClient 27 implemented /
43 not-implemented；Consumer 3 migrated-disabled / 67 not-migrated；CE 9.1 為 6 succeeded /
50 evidence-pending / 13 not-executed / 1 historical no-go-closed；所有 row 都是 temporary-legacy。
歷史 P7.2 Slice C 已 closed 且 exact cleanup，絕不可重播。

已完成的最新 child 是 MemberInfo request-local target authorization scope：它只提供 immutable、
fail-closed 的 authorization contract，尚未接上 consumer 或 CE。

請根據現行 workspace 的 matrix、ChurchReport call sites 與 parent artifacts：
1. 找出最多三個最適合下一步的獨立 P7 capability，按優先次序列出 operation ID、來源位置與理由。
2. 只有 DTO-only、server-derived authorization、沒有 Session/InMemoryContext、credential-bearing
   ListManager、shared mutable state、stored query、unbounded response 或 write adjacency 的候選才可推薦。
3. 對其他看似相近但不安全的候選，精確說明必須 fail closed 的原因。
4. 說明 parent task 需要校正的 stale checkpoint/nextAction，但不要提早宣稱 P7.5 或 P8 可做。

輸出格式：Critical / Warning / Info，並附上可驗證的檔案路徑與符號名稱。
