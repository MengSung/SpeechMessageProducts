# P7.3 ChurchReport 特殊資源能力遷移：CCG 執行計畫

1. 讀取 P7.3 Trellis PRD、權威 matrix、現行 registry/executor/ProductClient 與 legacy
   image/metadata/paging call site；以雙模型分析和本機檢查確認 bounded contract。
2. 先新增 abstraction/registry/response-union 的 RED test，確認目前五個 capability
   尚未被 Data8 executor 接受。
3. 以 TDD 實作 image payload、metadata option-set 與 weekly statistic result 的 typed
   operation contract，包含 immutable defensive copy 與 fixed limits。
4. 以 TDD 擴充 Data8 executor/connector，讓它只接受 server-owned schema、固定 query，
   並在 cancellation、over-limit、cookie/fault 時 fail closed 且釋放 lease。
5. 以 TDD 建立 typed ProductClient 與 profile-generation-isolated metadata cache；不得
   修改 ChurchReport consumer 或 feature gate。
6. 執行 focused tests、完整 tests/build、encoding/CRLF、diff/scope 檢查與雙模型 review。
7. 將結果寫回 Trellis/CCG，scope-only commit 並 archive；只有 evidence 證明 P7.3
   完整後，才評估建立 P7.4 child。
