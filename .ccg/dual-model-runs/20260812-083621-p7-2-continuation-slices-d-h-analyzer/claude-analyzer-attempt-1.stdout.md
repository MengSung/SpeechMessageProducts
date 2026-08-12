## P7.2 continuation：Slice D–H 本機 capability 分析（已完成）

我以 `.trellis/tasks/archive/2026-08/08-05-gateway-capability-inventory/coverage-matrix.json` 全量比對（5259 行）搭配實際開檔驗證（DonationContactCreationService、AppointmentsDownUpLoader、UploadIntegrateData.PresentRecord、FeeService、Package01/02 Data8 connector 範本、既有測試如 `DownloadListManagerIsolationTests.cs`），完成分析並寫入 `.ccg/dual-model-runs/20260812-083621-p7-2-continuation-slices-d-h-analyzer/claude-analyzer-attempt-1.stdout.md`。

重點結論：

- **Slice D（donation lifecycle）**：`DonationContactCreationService.SetDedicationNumber` 有無併發保護的查最大編號→+1→寫回競態，且與 contact 建立之間無補償機制——這是本次分析中最重要的新發現（既有 gemini 分析未涵蓋），列為 Critical。
- **Slice E（appointments）**：owner 指派已驗證為伺服端解析、非 caller 指定，符合安全約束；但 coverage matrix 只登記 1 個 call site，而檔案有 1878 行，實際呼叫面可能被低估（Warning）。
- **Slice F（contact onboarding）**：語意上編排 Slice C 尚未取得完整 CE evidence 的 list/present-record 能力，不能作為 Slice C gate 已滿足的證據，必須排在實作順序最後（Critical）。
- **Slice G（fee lessons）**：讀取路徑已有 registry 骨架，是風險最低、可最先落地的切入點。
- **Slice H（attendance）**：批次出席寫入完全無交易邊界與結構化部分失敗回報，新 capability 必須訂出明確 per-record disposition 契約（Critical）。

建議實作順序：G 讀 → H 讀 → E 單筆寫入 → H 批次寫入 → G 寫入 → D → F，並沿用既有 `Package02Data8ContactBasicInfoWriteOperations`／`DownloadListManagerIsolationTests`／`P72Data8ListManagementFixtureReconciler` 三組已審查過的樣式作為測試與 cleanup 骨架。完整的檔案對照表、測試清單與 P7.4/P7.5 阻斷證據清單已寫入上述輸出檔。

---
SESSION_ID: 5a3f8ac3-cd27-47f5-9651-b8478695f69d
