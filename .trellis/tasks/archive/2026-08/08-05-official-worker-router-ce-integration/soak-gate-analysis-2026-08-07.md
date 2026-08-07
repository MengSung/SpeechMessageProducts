# P6 WorkerTestHost Soak Gate Analysis（2026-08-07）

## 目前結論

本文件保留早期 soak gate 阻塞的完整去識別化證據與根因分析；該阻塞已由下方的
長壽命 measured-window 測試設計修復並通過。P6.1 的 Router／Pool／Lease／IPC／drain
與其他離線品質證據維持有效，P6.2 的真機 CE 相容性仍是未來獨立的 `evidence-pending`
工作，不是 Data8-first P7 的前置條件。

這個結果不會啟動 Official CRM Worker、不會連線 CE，也不會變更 ChurchReport 流量、
feature flag、profile 或 credential。

## 已觀測證據

- `OfficialWorkerSoakAndPerformanceTests.WorkerSoak_repeated_package01_recycle_returns_all_owners_to_zero_without_unbounded_trends`
  連續失敗兩次，失敗位置固定在 `AssertPostWarmUpResourceTrend` 的
  `within-generation private bytes` 判斷。
- 失敗時的去識別化 resource sample 為
  `7,020,544 → 7,348,224 → 7,610,368 → 11,358,208` bytes；第三個區間有一次
  `3,747,840` bytes 成長，超過原本 50% 的單調成長門檻。
- 試行 16 次相同負載的暖機後仍出現
  `8,511,488 → 12,382,208 → 12,763,136 → 13,094,912` bytes；因此不能把「增加固定
  暖機次數」當作修復，也已撤回該未證實的變更。
- 每一 generation 是新的 `SpeechMessage.Dynamics.WorkerTestHost` 測試程序；它不載入
  CRM SDK、不使用 credential、不連線 CE。測試程序以 completed-operation policy recycle，
  並在 `finally` 檢查 process、pipe、gate、reader、task、CTS 與 admission owner 都已退休。
- Lenovo 沒有殘留 `testhost`、WorkerTestHost、CRM Worker 或 7244 listener。TCP
  exclusion range `7171-7270` 仍涵蓋 7244，但與本次失敗無關。

## 根因判斷

**分類：D（Test Coverage Gap）與 E（Implicit Assumption）。**

測試把一個短命測試程序的 OS private-bytes 曲線當作長期 retention 的直接證據；但測試
負載會在每次 operation 建立 30-row Package01 payload，且 .NET GC heap segment 的按需配置
會造成階梯式 private-bytes 成長。現有資料支持此假設，但尚不足以排除 WorkerTestHost 或
Supervisor 在同一 generation 內保留 payload 的可能性。

信心評估：測試量測模型不足 70%；實際同代 retention 30%。不得把此估計當作已修復結論。

## 已拒絕的修復

1. 在原測試前加入固定 16 次暖機，並把 recycle limit 提升到 32 次。
   結果：量測區第一個 sample 仍增加約 3.9 MB，未消除失敗。此變更已撤回。
2. 放寬 50% 門檻、強制 GC、略過 assertion 或把此測試標記 skipped。
   結果：未採用；任何一種都會掩蓋真實 retention，違反 P6 lifecycle quality contract。

## 已完成的有界修復工作

在 P6 scope 內建立不接觸 CE 的長壽命 WorkerTestHost lifecycle test：

1. 明確區分相同負載的 warm-up window 與 measured window，並以足夠的
   `maximumCompletedOperations` 容納兩者。
2. 不強制 GC、不放寬 50% 門檻；只量測 warm-up 完成後的固定 window。
3. 保留 worker reuse、IPC payload、resource trend、recycle、drain 與所有 owner 歸零的
   assertion，讓它能分辨真實 retention 與 CLR 啟動配置。
4. measured window 若仍出現相同趨勢才會視為 P6 lifecycle defect，從
   WorkerTestHost／WorkerProcessHost／Supervisor 的 request-result ownership 反向追查；
   本次 measured window 未重現該趨勢。

此工作不需要使用者提供任何資料。實作後核心 soak 連續三次通過，每次 6 個 generation、
每代 64 warm-up＋64 measured request，共 384 measured requests；recycle、drain 與所有
process／pipe／gate／reader／task／CTS／admission owner 均回到零。完整 Dynamics suite
為 466 passed／7 skipped，Kestrel Negotiate 為 7/7，Release build 為 0 warnings／0 errors。

## P6 closure disposition

P6.1 soak gate 已綠，可進入 Trellis 結案流程；P6.2 仍記為 `evidence-pending`，不啟動
Official Worker 真機、CE operation、feature flag 或 ChurchReport 流量。正式 P6 close/archive
完成後，才可啟動既有 P7.0 task。
