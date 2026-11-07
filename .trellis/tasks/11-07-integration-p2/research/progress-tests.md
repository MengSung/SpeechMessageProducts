# 進度頁階段演進測試修復紀錄

## 範圍與重現（2026-11-07，Asia/Taipei）

僅修改指定測試與本紀錄，不修改進度 HTML、Host 或其他人的變更，不提交。

### 修改前原始測試輸出

命令：node --test tools/RepoQuality/Tests/Test-ProgressPage.cjs；結束碼：1。

```text
✔ Every work package has a supported status and renders without undefined labels or icons (9.0099ms)
✖ No alert hides the warning region and still renders the page (5.4967ms)
✔ Alert without prompt omits copy button and full prompt area (5.3965ms)
✔ Alert with prompt preserves escaped prompt and copy handler (4.9681ms)
✔ Stage 1 progress lists all twelve packages and preserves the live acceptance boundary (11.7586ms)
✖ Stages 2 through 8 render every planned package once and keep all packages not started (0.5588ms)
✔ Planned phase disclosures show a visible expanded-state indicator (3.5337ms)
✔ All nine stages share one closed work-package disclosure list without standalone P0/P1 sections (4.8288ms)
✖ Unified summaries exclude review milestones and account for user-operated work (4.7378ms)
✔ Integrated P0/P1 entries preserve every evidence field and never merge same-name records (11.0698ms)
✔ Future work packages preserve external-operation and isolation stop boundaries (0.5158ms)
✔ Summary cards and phase meters agree with accepted work packages (0.2181ms)
✔ Informational history defaults to a closed neutral disclosure (5.1871ms)
✔ Current warnings and errors remain separate from collapsed history (5.8245ms)
✔ Empty notices hide their region and unknown severity cannot inject markup (7.0714ms)
ℹ tests 15
ℹ suites 0
ℹ pass 12
ℹ fail 3
ℹ cancelled 0
ℹ skipped 0
ℹ todo 0
ℹ duration_ms 190.7598

✖ failing tests:

test at tools\RepoQuality\Tests\Test-ProgressPage.cjs:87:1
✖ No alert hides the warning region and still renders the page (5.4967ms)
  AssertionError [ERR_ASSERTION]: The input did not match the regular expression /1\.0\.0\.11\.Integration\.P1\.Worktree/. Input:
  
  '<span>資料快照：2026-11-07T08:18:17+08:00</span><span>分支 1.0.0.11.Integration.P2.Worktree</span><span>主會話inline實作／驗證／自審；依使用者目標取消Gemini／Claude，不宣稱外部審查完成</span>'
  
      at TestContext.<anonymous> (D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P2.Worktree\tools\RepoQuality\Tests\Test-ProgressPage.cjs:91:10)
      at Test.runInAsyncScope (node:async_hooks:214:14)
      at Test.run (node:internal/test_runner/test:1106:25)
      at Test.processPendingSubtests (node:internal/test_runner/test:788:18)
      at Test.postRun (node:internal/test_runner/test:1235:19)
      at Test.run (node:internal/test_runner/test:1163:12)
      at async startSubtestAfterBootstrap (node:internal/test_runner/harness:358:3) {
    generatedMessage: true,
    code: 'ERR_ASSERTION',
    actual: '<span>資料快照：2026-11-07T08:18:17+08:00</span><span>分支 1.0.0.11.Integration.P2.Worktree</span><span>主會話inline實作／驗證／自審；依使用者目標取消Gemini／Claude，不宣稱外部審查完成</span>',
    expected: /1\.0\.0\.11\.Integration\.P1\.Worktree/,
    operator: 'match',
    diff: 'simple'
  }

test at tools\RepoQuality\Tests\Test-ProgressPage.cjs:190:1
✖ Stages 2 through 8 render every planned package once and keep all packages not started (0.5588ms)
  AssertionError [ERR_ASSERTION]: 階段 2 must remain not started
      at TestContext.<anonymous> (D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P2.Worktree\tools\RepoQuality\Tests\Test-ProgressPage.cjs:265:12)
      at Test.runInAsyncScope (node:async_hooks:214:14)
      at Test.run (node:internal/test_runner/test:1106:25)
      at Test.processPendingSubtests (node:internal/test_runner/test:788:18)
      at Test.postRun (node:internal/test_runner/test:1235:19)
      at Test.run (node:internal/test_runner/test:1163:12)
      at async Test.processPendingSubtests (node:internal/test_runner/test:788:7) {
    generatedMessage: false,
    code: 'ERR_ASSERTION',
    actual: false,
    expected: true,
    operator: '==',
    diff: 'simple'
  }

test at tools\RepoQuality\Tests\Test-ProgressPage.cjs:312:1
✖ Unified summaries exclude review milestones and account for user-operated work (4.7378ms)
  AssertionError [ERR_ASSERTION]: The input did not match the regular expression /目前階段/. Input:
  
  '\n' +
    '        <span class="planned-phase-toggle" aria-hidden="true"></span>\n' +
    '        <span class="planned-phase-id">階段 1</span>\n' +
    '        <span class="planned-phase-name">基礎層與國際化地基</span>\n' +
    '        <span class="planned-phase-count">12/12 驗收、0 進行中、0 未開始</span>\n' +
    '        <span class="planned-phase-est">預估 12～16 天</span>\n' +
    '      '
  
      at TestContext.<anonymous> (D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P2.Worktree\tools\RepoQuality\Tests\Test-ProgressPage.cjs:330:10)
      at Test.runInAsyncScope (node:async_hooks:214:14)
      at Test.run (node:internal/test_runner/test:1106:25)
      at Test.processPendingSubtests (node:internal/test_runner/test:788:18)
      at Test.postRun (node:internal/test_runner/test:1235:19)
      at Test.run (node:internal/test_runner/test:1163:12)
      at async Test.processPendingSubtests (node:internal/test_runner/test:788:7) {
    generatedMessage: true,
    code: 'ERR_ASSERTION',
    actual: '\n        <span class="planned-phase-toggle" aria-hidden="true"></span>\n        <span class="planned-phase-id">階段 1</span>\n        <span class="planned-phase-name">基礎層與國際化地基</span>\n        <span class="planned-phase-count">12/12 驗收、0 進行中、0 未開始</span>\n        <span class="planned-phase-est">預估 12～16 天</span>\n      ',
    expected: /目前階段/,
    operator: 'match',
    diff: 'simple'
  }

```
