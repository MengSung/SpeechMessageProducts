## P7.2 Slice C ExecuteFixture 診斷分類審查報告

### Critical 🔴

**`docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1:2175-2179` 與 `:297-309` 的 allowlist 不同步，導致 `live-evidence-incomplete` 分類會使 parent 把結果從 `child-process-failed` 降級成 `handoff-failed`**

- **證據**：
  - 新增的 `Get-StrictSliceCChildFailureDiagnosticCategory`（第 2130-2188 行）在第 2175-2179 行允許四個 no-go reason 投影為 `diagnosticCategory`：
    ```
    'runtime-failure', 'cleanup-failure', 'fixture-precondition-failed', 'live-evidence-incomplete'
    ```
    其中 `live-evidence-incomplete` 確實是 `Get-StrictSliceCEvidenceFile` 既有 `$topReasons`（第 2037 行，非本次 diff 新增）允許的合法頂層 no-go reason，因此 child 完全可能寫出這個 schema 合法的 no-go evidence。
  - 但既有共用函式 `New-HandoffResult` 的 `DiagnosticCategory` allowlist（第 297-309 行）只列了：
    ```
    'fixture-precondition-failed', 'baseline-owner-unavailable', 'fresh-source-readback-failed',
    'fresh-leader-readback-failed', 'fresh-relationship-readback-failed', 'remove-membership-readback-failed',
    'transfer-source-membership-readback-failed', 'baseline-owner-readback-failed', 'fresh-graph-unproven',
    'provisioning-ambiguous', 'runtime-failure', 'cleanup-failure'
    ```
    **未包含 `live-evidence-incomplete`**。第 310 行對非 allowlist 值會 `throw 'fresh-fixture-diagnostic-invalid'`。
  - 呼叫路徑（第 3284-3295 行）：
    ```powershell
    elseif ($ExecuteFixture) {
        $diagnosticCategory = Get-StrictSliceCChildFailureDiagnosticCategory ...
    }
    Complete-HandoffResult (New-HandoffResult ... -DiagnosticCategory $diagnosticCategory)
    ```
    當 `$diagnosticCategory -eq 'live-evidence-incomplete'` 時，`New-HandoffResult` 會在 `Complete-HandoffResult` 執行前拋例外，因此本應寫出的 `child-process-failed` 結果**根本沒有被寫出**。例外會被外層 catch（第 3461-3468 行）攔截，因為 `$childProcessStarted -eq $true` 且例外訊息不等於 `'evidence-result-unavailable'`，最終改以 `Reason 'handoff-failed'` 收尾。
- **失敗情境**：一個受控 CE Slice C ExecuteFixture child 寫入完整、schema 合法的 no-go evidence（`outcome='no-go'`, `reason='live-evidence-incomplete'`），接著因 xUnit 最終斷言以非零結束——這正是本次任務背景所描述、需要被診斷分類的典型場景之一。實際跑出來的 parent 結果會是 `no-go / handoff-failed`（且完全沒有 `diagnosticCategory` 欄位），而不是 Required Behavior #1 明訂的「nonzero child exit always remains `no-go / child-process-failed`」。這直接違反了本次變更要滿足的核心契約。
- **建議修復**：在 `New-HandoffResult` 第 297-309 行的 allowlist 加入 `'live-evidence-incomplete'`，讓兩個 allowlist 恢復同步（函式註解本身也宣稱「僅投影既有 allowlist 中的 no-go reason」，顯示這是同步遺漏而非刻意排除）。

### Warning 🟡

**測試完全沒有覆蓋 `live-evidence-incomplete` 這個分類，讓上述 Critical 缺陷得以通過現有測試**

- **證據**：`docs/scripts/Invoke-Package02Data8ListManagementEvidence.Tests.ps1` 新增的 `-WriteNoGoEvidence` 情境（第 267-278 行）把 `synthetic evidence.reason` 固定寫死為 `'runtime-failure'`（第 269 行），且全檔搜尋 `live-evidence-incomplete` 完全沒有出現。新增斷言（Tests.ps1 第 1206-1211 行左右）只驗證了 `'runtime-failure'` 這一個、恰好同時存在於兩個 allowlist 交集內的值，因此無法偵測到 allowlist 不同步的問題。
- **建議**：至少再補一組 `-WriteNoGoEvidence` 使用 `reason='live-evidence-incomplete'` 的案例，斷言最終仍是 `outcome='no-go' / reason='child-process-failed' / diagnosticCategory='live-evidence-incomplete'`，這樣才能真正驗證 Required Behavior #1 對所有四個 allowlisted reason 都成立，而不只是其中一個。

### Info 🟢

- `Get-StrictSliceCChildFailureDiagnosticCategory`（ps1:2130-2188）本身的邊界檢查（parent-owned 非 reparse temporary root、精確檔名 `P72Data8ListManagementEvidence.json`、完整 strict schema 解析、no-go-only 且 allowlist-only 投影、任何例外一律回傳 `null`）與 Required Behavior #2、#3、#4 的要求逐條比對均一致，且只在 `elseif ($ExecuteFixture)`（純 ExecuteFixture 單一模式）分支被呼叫，不會影響 go/其他 lane，實作方向正確。
- Tests.ps1 中同時新增的 `BootstrapFreshSeed` 相關測試與 helper（`Invoke-RunnerWithSyntheticBootstrapChild`、`Write-TestFreshSeed` 等）不在本次審查要求的檔案行為範圍內（該功能與 diagnosticCategory 無關），故未深入審查；若後續需要，建議另開一輪針對 seed bootstrap 契約（current-user 綁定、legacy candidate 縮減、原子發布）的審查。

## Summary
發現 1 個 Critical：`live-evidence-incomplete` 分類因兩個 allowlist 不同步而使 parent 把應為 `no-go / child-process-failed` 的結果誤報為 `no-go / handoff-failed`，直接違反 Required Behavior #1，且現有測試未覆蓋此路徑（Warning）。建議先修復 `New-HandoffResult` 的 allowlist 並補上對應測試後再合併。

---
SESSION_ID: 71d482d8-4eba-44b7-83c7-6a9f36f85243
