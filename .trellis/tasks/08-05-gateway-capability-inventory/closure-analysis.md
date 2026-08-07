# P7.0 Closure Analysis

## 結果

P7.0 已在 P6 正式封存後完成。交付物僅位於本 task 目錄：70-row
machine-readable coverage matrix、source-derived manifests、P7.2 activation input、
ChurchReport legacy dependency baseline，以及完全離線的 deterministic validator 和其測試。
未修改產品 runtime、專案引用、Registry、Data8 executor、Official Worker、ProductClient、
ChurchReport 設定或 feature flag；未執行 CE operation、Official Worker startup、瀏覽器或雲端部署。

## 已驗證的現況

- Phase 0 matrix 仍是 70 個 call site、12 個 capability family；70 rows 不被誤報為 70 operations。
- Source manifest 將 9 個 Registry declarations、1 個 Data8 implementation、3 個 Official Worker
  protocol allowlist、P6.1 offline Router implementation、6 個 ProductClient methods、consumer disabled
  與 CE real evidence 分開記錄。
- `Package01FeeReadsEnabled` 的 base 與 Development 值皆為 `false`，所以 consumer 仍未啟用。
- P7.0 default validator 為 green，並將 661 個 ChurchReport production legacy dependency reference
  作為 P7.5 warning；`--enforce-p7-5` 正確以
  `P75-PRODUCTION-LEGACY-DEPENDENCY` fail closed。
- 六個 validator tests、Python compile、JSON parser、deterministic-output hash comparison、
  scope、UTF-8 without BOM、CRLF-only/final-CRLF 和 `git diff --check` 均已通過。

## Phase 3.3 Spec Update Judgment

**無 spec update 必要。**

本次新增的是 P7.0 專屬的離線盤點工具與資料基線，沒有改變任何產品 API、部署 profile、
Data8/Worker lifecycle、ConnectionMode、ConnectorKind 或 ChurchReport 行為。既有 backend spec
已涵蓋「Registry／executor／consumer／real CE evidence 必須獨立」及「不得建立 generic CRUD／
arbitrary FetchXML」等可重用契約。

實作期間發現的「source scanner 不可因註解文字而將 Registry operation 誤列為 Worker protocol
allowlist」只適用於本 task 的靜態掃描器；已由具名回歸測試與 task-local source manifest 直接保存，
尚不足以形成跨產品或 runtime 的 `.trellis/spec/` 規則。

## 下一個 Gate

P7.1 只能依 P7.0 matrix 建立第一個 typed read capability slice。它必須維持
`Embedded + Data8` 與 `DedicatedGateway + Data8` 的可配置性，先完成 typed contract、
authorization、bounded response/lifecycle、Data8 real CE evidence 與 rollback shape，再考慮任何
consumer enablement；P7.4 前不得開啟 ChurchReport feature flag。
