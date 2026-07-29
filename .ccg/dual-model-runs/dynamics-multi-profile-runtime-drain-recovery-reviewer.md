# CCG reviewer Task: dynamics-multi-profile-runtime-drain-recovery

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Dynamics Multi-Profile Runtime Drain Recovery 修正審查

## 審查背景

前一次正式雙模型審查：

```text
20260729-161900-dynamics-multi-profile-runtime-reviewer
```

Claude 找到一項有效 Critical：`DynamicsProfileRuntimeManager.ReplaceCoreAsync` 在新 Generation 已發布後，若舊 Runtime 的 `DrainAndDisposeAsync` 拋錯，`slot.Draining` 可能永遠不清除，後續 Replace 永久失效；若直接無條件清除，又可能遺失尚未 Disposed 的舊 Runtime ownership。

修正前已完成正式雙模型分析：

```text
20260729-163834-dynamics-multi-profile-runtime-drain-recovery-analyzer
ok=true
degradedFallback=false
completedBackends=[gemini, claude]
```

## 必讀檔案

這批 Runtime 檔目前仍是未追蹤新檔，單純 `git diff` 不會顯示其內容。請務必直接讀取：

- `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntimeManager.cs`
- `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntime.cs`
- `SpeechMessage.Dynamics.WebApi/Runtime/IDynamicsProfileRuntime.cs`
- `SpeechMessage.Dynamics.Tests/MultiProfileRuntimeTests.cs`
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-multi-profile-runtime-verification.md`
- `docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md`

並查看：

```powershell
git status --short
git diff --check
```

## 修正內容

1. `ReplaceAsync` 仍用 `ReplacementInProgress` 保證同 Alias 只有一個非同步 replacement owner；平行 Replace 在 Factory 前拒絕。
2. 若前次 owner 已離開、Slot 仍有 Draining Runtime，下一個 Replace 先重試該精確 Runtime 的 cleanup。
3. Pending Draining 完成前不遞增 Generation、不呼叫 Factory，因此不會配置第三套 Client／Token／Handler／CTS／Admission Registration。
4. 新增 `DrainOwnedRuntimeAsync`：在鎖外等待，`finally` 只在 `runtime.State == Disposed` 且 reference identity 仍等於 `slot.Draining` 時清除 Catalog reference。
5. cleanup failure 仍向上傳遞；已 Disposed 的幽靈 reference 不再永久阻塞 Slot。
6. caller cancellation／timeout 後若 Runtime 仍為 Draining，Slot 保留 reference，後續 Replace 或 Manager Shutdown 可重新接管。
7. Replacement drain 使用 caller＋Manager `_shutdownCts` linked token；Shutdown 先結束 replacement lifecycle owner，最終 `DisposeCoreAsync` 再接管 Active／Draining cleanup。
8. Generation 編號延後到 Pending Draining 收斂後才遞增，失敗重試不製造沒有 Runtime 的 gap。

## RED→GREEN regression

```text
Disposed_draining_cleanup_failure_is_reported_and_does_not_block_later_replacement
Unfinished_draining_runtime_is_retried_before_allocating_the_next_generation
Manager_shutdown_cancels_the_published_replacement_drain_owner
```

RED 證據：

- 已 Disposed cleanup failure 後，舊實作快照保留 Generation 1 Disposed＋Generation 2 Active。
- 未完成 Draining 的第二次 Replace 在入口同步拋出 `InvalidOperationException`，沒有 recovery path。
- 發布後 drain 使用裸 caller token 時，Manager Shutdown 後兩秒觀察窗得到 `TimeoutException`，Replace owner 未停止。

GREEN 證據：

```text
三個新增 regression：3 passed
MultiProfileRuntimeTests：15 passed
Focused MultiProfile／Registry／Factory／Readiness／Phase4 Soak：35 passed
SpeechMessage.Dynamics.Tests：158 passed
SpeechMessageProducts.sln Release Build：0 warnings / 0 errors
NuGet vulnerability audit：未發現已知易受攻擊套件
Changed WebApi／Gateway／Tests scoped dotnet format：通過
Changed text files：56 個，strict UTF-8、無 BOM、CRLF
New C# files：15 個，有中文文件標記且無 <inheritdoc /> 佔位
git diff --check：通過
```

## 強制審查問題

請逐項確認：

1. 已 Disposed cleanup failure 是否會正確清除精確的 Draining reference，同時不吞例外？
2. caller cancellation／timeout 後尚未 Disposed 的 Runtime 是否仍被 Manager 強引用並可重試？
3. Pending Draining cleanup 前是否確實不會配置第三套 Runtime resource graph？
4. `ReplacementInProgress`、`_activeLifecycleOperations`、Manager Dispose 與 linked cancellation 是否存在 lock race、underflow、deadlock 或雙重 Dispose？
5. `DrainOwnedRuntimeAsync` 在 success、fault、cancellation、shutdown 與 state transition 下是否都遵守 exact-reference cleanup？
6. Generation numbering、Active＋最多一個 Draining、Queue-after-admission current-active resolution 是否保持原契約？
7. 新測試是否真正測到 Production Manager 行為，而不是只測 Fake 自己？
8. 是否仍有 Runtime／Client／Handler／Token／CTS／Admission Registration／Task 強引用或 lifecycle leak？
9. 新增 Production／Test 程式的繁體中文註解是否足以說明責任、ownership、併發、錯誤結果與 Drain／Dispose 順序？

## 輸出格式

請輸出 Critical／Warning／Info 分級報告。若沒有 Critical 或 Warning，明確寫 `PASS`。任何 finding 都要附檔案、方法／測試、具體失敗路徑與建議修正；不要把尚未完成的 real-server／跨 Process Phase 4 Gate 誤當成本次本地增量的程式缺陷。



## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.