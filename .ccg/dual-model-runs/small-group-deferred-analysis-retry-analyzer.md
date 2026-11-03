# CCG analyzer Task: small-group-deferred-analysis-retry

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P0.Worktree

## Request
# 小組回報前端錯誤：唯讀分析

僅分析本提示提供的程式與契約，不使用工具、不讀 repo、設定、日誌、User Secrets 或任何執行中頁面；不要修改檔案、不要連線。回覆繁體中文。

截圖實際錯誤：this.store(...).load(...).done is not a function，出現兩次；兩個 Grid 共用下列 adapter。產品現有 jQuery 2.2.4、DevExtreme client 22.1.6，不可改版本。store.load 原回傳 jQuery promise，可 resolve(data, extra)，extra 含 totalCount/summary/groupCount；原生 Promise 只保留第一參數。

adapter 目前：store.load = function(loadOptions) { return coordinator.runLoad(function() { return originalLoad(loadOptions); }); }。
coordinator 目前 runLoad 用 new Promise，內部 Promise.resolve(transport).then，檢查 generation/disposed，過期以 StaleGenerationError 拒絕；當前 success resolve(value)，當前 failure reject(error)，再清除 activeRequest。owner WeakMap；重複 mount dispose 舊 owner，dispose abort transport 並清 pending refresh。日期切換也用 runLoad().then().catch()。

擬議最小修正：runLoad 新增可選 Deferred factory，Grid 注入既有 $.Deferred；此分支完整轉送傳輸 done/fail 的所有參數與 context、保留相同世代檢查；default 原生 Promise 仍給日期切換使用。所有資料限單次載入，完成/失敗/取消不留 active transport。成功/錯誤 callback 拋例外時也必須清理。以 repo 真實 jQuery Deferred 程式及真實 Razor adapter 做無網路 Node 測試，涵蓋 .done/.fail、多參數、錯誤、亂序、dispose、獨立 owner。

請評估此最小修正是否正確；指出生命週期、原始例外、回傳型別、jQuery 2.2.4 與 metadata 注意事項；按 Critical/Warning/Info。圖表另有自己的 DataSource，不要假設同一錯誤能解釋其空白。禁止建議移除世代防護或放寬安全守門。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.