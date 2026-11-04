# CCG reviewer Task: p013-live-acceptance-closeout

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P0.Worktree

## Request
請以 ClaudeOnly 審查本次 P0-13 實機驗收收尾及文件更新。只讀審查；禁止任何 D365／LINE／Hyper-V 存取、禁止讀取 appsettings、User Secrets、金鑰、授權或其他未提供的秘密資料。
這是單模型（使用者指定 ClaudeOnly），不是雙模型或配額降級。
使用者於 2026-11-04 回報 net462 apply／verify=0，並全權授權 Codex 自動執行本次 smoke、調查、清理及文件更新，取代先前只允許使用者線上操作的限制。舊 token／會友 LINE ID 依使用者本次 trace 例外保留。
已完成實機操作：既有 CLI trace smoke=0；限定本次時窗與完整型別的唯一 trace 五項全符合，FrameworkFileVersion=4.8.4795.0；新 plan 僅 UpdateStepState，停用 apply=0／verify=0；最後回讀追蹤設定=1、step disabled、smoke task 殘留 0。
本輪沒有產品程式變更；清單 true→false 已提交 f664c685。原 a4649e3b 前完整 §8 1125／1125、Debug7／7，非本輪重跑。本輪額外 Deployment56／56、進度頁3／3。
審查重點：證據是否足以支持 scoped completion、清理與資料邊界、機密輸出、暫存查核器的有限查詢與 Dispose、文件是否存在目前狀態矛盾或冒稱 GitHub／LINE／其他歷史作業已驗證、進度 DATA 是否18／18且未開P1。不要把歷史段落當成目前未完成；資料保留與自主操作皆有使用者明確核定。
暫存查核器只在 ignored artifacts，本次不新增通用產品工具。其搭建期間修正 WhoAmI 類型載入與 organization.name 非 URL 別名的假設；最終固定端點身分仍由原 connector 保證。原始追蹤不輸出或保存。請按 Critical／High／Medium／Low 報告具體缺陷（沒有則明言）。不得直接修改任何檔案。

## 本次差異（含已提交清單與未提交文件）
diff --git a/.trellis/spec/backend/deployment-contract.md b/.trellis/spec/backend/deployment-contract.md
index 5b1b31f0b..9afafda6e 100644
--- a/.trellis/spec/backend/deployment-contract.md
+++ b/.trellis/spec/backend/deployment-contract.md
@@ -3,9 +3,11 @@
 ## Scope and connection boundary
 
 P0-12 is an offline implementation and test package. No D365 or LINE connection
-is permitted until the user reports goodshepherdback cleanup complete. Afterwards,
-Codex may only run the read-only commands authorized by D24; apply, rollback and
-smoke remain user operations. Credentials come only from the Deployment User
+is permitted until the user reports goodshepherdback cleanup complete. Earlier P0-13 guidance reserved writes for the user. On 2026-11-04 the user
+explicitly authorized Codex to finish this goodshepherdback trace acceptance,
+including smoke, the reviewed disable plan/apply, verification and restoration of
+exception-only tracing. This authorization is scoped to that acceptance and does
+not authorize other organizations or future LINE pushes. Credentials come only from the Deployment User
 Secrets provider; Windows Credential Manager is a future option. Do not modify
 ToolUtility or accept passwords on the command line.
 
@@ -72,7 +74,7 @@ scope restored in finally; every command releases its injected fake lease.
 
 Primary references: Microsoft Learn `sdkmessageprocessingstep` entity reference
 (`SdkMessageProcessingStepSecureConfigId`) and web-service error codes
-(`QueryBuilderNoAttribute`, `0x80041103`). Live retry is user-operated; successful
+(`QueryBuilderNoAttribute`, `0x80041103`). A live retry requires explicit user authorization; successful
 offline checks do not establish the current organization's state.
 
 ## Offline verification
@@ -88,10 +90,11 @@ acceptance, not a claim made by the offline suite.
 
 ## Minimal P0-13 acceptance (user approved 2026-11-04)
 
-P0-13 is user-operated trace-only sandbox acceptance. The Plugin deployment
+P0-13 is user-approved trace-only sandbox acceptance; the user explicitly
+authorized Codex to finish the remaining live operations on 2026-11-04. The Plugin deployment
 assembly targets .NET Framework 4.6.2 per the user's 2026-11-04 D21
-authorization; successful registration on the target D365 host remains to be
-confirmed by the user. The smoke field `FrameworkFileVersion=4.8.x` describes
+authorization. Registration, trace smoke and disabled-state verification on the
+target D365 host were confirmed on 2026-11-04. The smoke field `FrameworkFileVersion=4.8.x` describes
 the server's loaded `mscorlib` runtime file, not the Plugin target framework.
 Only OrganizationUrl,
 Username and Password under the Deployment organization User Secrets are needed;
@@ -109,4 +112,13 @@ the source-record-ID retry-key unit contract and LINE's 24-hour retry-key validi
 contract, as accepted by the user; this is not a claim of observed delivery or
 an assurance beyond the retry-key validity window. Transitional CI scope is
 accepted; prioritize DevExpress source resolution once phase 1 begins and retain
-full-solution local checks. Cleanup and real P0 acceptance remain unreported.
+full-solution local checks. The 2026-11-04 live run passed all five trace checks, reported runtime
+FrameworkFileVersion 4.8.4795.0, left zero run-scoped smoke tasks, disabled the
+diagnostic step with a single UpdateStepState plan and verify=0, and restored
+exception-only tracing (value 1). Keep only fixed results, counts and validated
+versions as evidence; never persist or print the raw message block. The temporary
+audit uses the existing fixed-endpoint connector, owns and disposes its
+configuration and connection, and queries only the exact diagnostic type, run
+time window and correlation. Old LINE data was retained under the user's explicit
+P0-only exception; do not describe it as cleared or extend that exception to later
+phases. This acceptance did not exercise LINE delivery or prove hosted CI success.
diff --git a/.trellis/tasks/11-02-integration-p0/task.json b/.trellis/tasks/11-02-integration-p0/task.json
index b108d0416..a8100f779 100644
--- a/.trellis/tasks/11-02-integration-p0/task.json
+++ b/.trellis/tasks/11-02-integration-p0/task.json
@@ -2,7 +2,7 @@
   "id": "integration-p0",
   "name": "integration-p0",
   "title": "階段 0 準備守門與可行性驗證",
-  "description": "依使用者 2026-11-04 核定正式改用 net462；完整離線驗證、簽章打包與第 10 份 ClaudeOnly 獨立複審通過，等待使用者重跑 P0-13。未執行 D365／LINE 連線。",
+  "description": "P0-13 net462 實機註冊、smoke、五項追蹤與停用清理全部通過，階段 0 完成；本次依使用者全權授權由 Codex 自動收尾。",
   "status": "in_progress",
   "dev_type": null,
   "scope": null,
@@ -21,16 +21,16 @@
   "children": [],
   "parent": "11-02-integration-v32",
   "relatedFiles": [],
-  "notes": "環境順序及 3.3 版本待決均已解除；不連線任何 D365／LINE。",
+  "notes": "本次 P0-13 已全權授權 Codex 自動操作 goodshepherdback；不發 LINE，不推送、不合併、不改寫歷史。",
   "meta": {
     "currentWorkPackage": "P0-13",
-    "userRequestedStop": true,
-    "gatePassed": false,
+    "userRequestedStop": false,
+    "gatePassed": true,
     "baselineSummary": "baseline/baseline-summary.md",
     "report": "wp-reports/P0-13.md",
     "repairRecommendation": "research/baseline-repair-recommendation.md",
     "reviewPending": false,
-    "waitingFor": "使用者以 net462 重新打包、plan／apply／verify／smoke 及停用收尾；舊 token／LINE ID 依核定保留",
+    "waitingFor": null,
     "completedWorkPackages": {
       "P0-01": "90799969",
       "P0-02": "bc13efe7",
@@ -65,9 +65,10 @@
       "P0-stage-review": "56f4e0ab",
       "P0-13-document": "cb137099（歷史初版文件；原 FileVersion 停止已由使用者 2026-11-04 核定解除）",
       "P0-13-document-minimal": "9f87187c（僅文件精簡，P0-13 實機驗收未完成）",
-      "P0-13-document-usage": "本次提交（僅操作說明補充，實機驗收未完成）"
+      "P0-13-document-usage": "本次提交（僅操作說明補充，實機驗收未完成）",
+      "P0-13": "f664c685（停用清單）；2026-11-04 實機 smoke／追蹤／停用 verify=0 通過"
     },
-    "stopReason": "net462 離線驗證與第 10 份獨立複審通過；實機驗收尚未完成，提交後交使用者重試，不開 P1、不連線 D365／LINE。",
+    "stopReason": null,
     "observedExternalCommit": "cf3595db",
     "observedExternalCommits": [
       "cf3595db",
@@ -100,14 +101,14 @@
       "quotaBlocked": false,
       "unresolvedCritical": 0,
       "unresolvedHigh": 0,
-      "serverRegistrationConfirmed": false,
+      "serverRegistrationConfirmed": true,
       "report": ".ccg/tasks/archive/2026-11/p013-net462-migration/review.md"
     }
   },
   "currentPhase": "review",
   "currentWorkPackage": "P0-13",
-  "nextAction": "由使用者從驗收文件第 2 步重建 net462 打包，接著重新 plan／apply／verify／smoke 及停用收尾；不得重用舊 net48 plan。",
-  "stopReason": "net462 離線驗證與第 10 份獨立複審通過；實機驗收尚未完成，提交後交使用者重試，不開 P1、不連線 D365／LINE。",
+  "nextAction": "核對驗收文件、進度 DATA 與本次獨立審查後提交並封存；不開 P1。",
+  "stopReason": null,
   "stageReview": {
     "mode": "claude-only",
     "state": "passed",
@@ -177,12 +178,12 @@
   "acceptance": {
     "document": "文件/規劃設計書/實作計畫-3.3/驗收/P0_Plugin骨架佈署步驟.md",
     "documentPrepared": true,
-    "prerequisitesChecked": false,
-    "realAcceptanceCompleted": false,
+    "prerequisitesChecked": true,
+    "realAcceptanceCompleted": true,
     "fileVersionTraceAvailable": false,
     "sameEventRetryVerified": false,
-    "cleanupReported": false,
-    "stage0Completed": false,
+    "cleanupReported": true,
+    "stage0Completed": true,
     "smallGroupArchiveCommit": "53df63f3",
     "scopeApproval": "使用者 2026-11-04 核定最小驗收；FileVersion 追蹤移至階段 5，實機重送改用單元測試與 LINE 官方重試契約。",
     "ciTransitionAccepted": true,
@@ -201,7 +202,19 @@
     "pushAcceptanceStage": "P2-04 前置驗收",
     "fileVersionAcceptanceStage": "階段 5 首個正式 Plugin 佈署 goodshepherdback 前",
     "sameEventRetryVerificationBasis": "使用者接受來源記錄 ID Retry Key 單元測試及 LINE 官方 24 小時有效期契約，取消 P0 實機重送；未聲稱實機驗證。",
-    "hostedCiSuccessVerified": false
+    "hostedCiSuccessVerified": false,
+    "userReportedRegistrationVerified": true,
+    "automatedAcceptanceAuthorized": true,
+    "smokePassed": true,
+    "fiveTraceChecksPassed": true,
+    "serverFrameworkFileVersion": "4.8.4795.0",
+    "diagnosticStepDisabled": true,
+    "finalVerifyExitCode": 0,
+    "traceSettingRestoredToExceptions": true,
+    "remainingSmokeTasks": 0,
+    "oldLineDataCleared": false,
+    "evidence": "wp-evidence/P0-13-live-acceptance.json",
+    "completedAt": "2026-11-04T12:41:18.2820756+00:00"
   },
   "futureWork": [
     {
diff --git a/.trellis/tasks/11-02-integration-p0/wp-reports/P0-13.md b/.trellis/tasks/11-02-integration-p0/wp-reports/P0-13.md
index 4c7dc4caf..e3da3b0bf 100644
--- a/.trellis/tasks/11-02-integration-p0/wp-reports/P0-13.md
+++ b/.trellis/tasks/11-02-integration-p0/wp-reports/P0-13.md
@@ -1,3 +1,41 @@
+# P0-13 自動實機驗收完成（2026-11-04）
+
+使用者先完成 net462 註冊並回報 verify=0，隨後全權授權 Codex 自動執行本次驗收、必要 D365-9.1 調查及停用清理；此授權取代前次僅由使用者操作的限制。
+
+## 本輪結果
+
+- 台灣時間 20:36:38～20:37:18：既有部署 CLI `p0-ping-trace` 回報 Smoke scenario passed，結束碼 0。
+- 以本次時窗及完整 DiagnosticsPingPlugin 型別查得唯一追蹤，五項全通過：P0-PING OK、Assembly 1.0.0.0、FrameworkFileVersion 4.8.4795.0、繁中及偽語系文字。Plugin 的編譯目標仍是 net462。
+- 同次 correlation 僅有診斷 Plugin；無 Plugin 例外或推播標記。這是限定查核，不是全組織歷史作業稽核。未執行 LINE 推播。
+- 停用清單後產生新 plan，審核只有同一步驟的一項 UpdateStepState；apply=0、收尾 verify=0。
+- 20:41:18 回讀：步驟已停用、trace 模式且無 secure 設定、Plugin 追蹤為「例外狀況」（1）、本次 smoke task 殘留 0。
+- false 已以新提交 f664c685 保存，提交後該清單 git status 無輸出；使用者的 true 提交與其他歷史保留。
+
+驗收查核沿用既有 DeploymentConfiguration 與唯一端點 connector，由程序正常使用 User Secrets；未人工讀取、顯示或另存憑證。查核只取本次必要欄位，追蹤內容在記憶體中比對後捨棄，連線及設定於 finally Dispose。臨時查核器的 WhoAmI 型別載入及組織顯示名稱假設已在本機搭建時修正：使用標準 WhoAmI 訊息，目標身分由既有固定端點 connector 保證，不以顯示名稱當組織別名。沒有修改產品程式。
+
+## 驗證與證據
+
+本輪 Deployment 回歸 56／56，無失敗或跳過；停用後實機 verify=0。完整本機 00 §8 沿用 a4649e3b 提交前的有效證據：Release 0 警告／0 錯誤、1125／1125（ChurchReport 596／596）、Debug tracer 7／7，編碼、重複碼與 I18n 通過。本輪只改清單停用值、文件與任務狀態，沒有重跑或冒稱重跑整套 §8。
+
+去識別化結果：[P0-13-live-acceptance.json](../wp-evidence/P0-13-live-acceptance.json)。原始工具輸出與臨時查核器留在 ignored artifacts/p013-live-acceptance，不提交原始 export 或追蹤。
+
+既有階段審查、第 09／10 份與 verify 欄位修正審查無未處理 Critical／High，模式均按原紀錄如實保留；本輪文件與收尾另做 ClaudeOnly 審查時單獨記錄，不宣稱既有審查已涵蓋新差異。
+
+## 完成判準及保留事項
+
+P0-13 最小實機驗收已通過；依使用者接受的 CI 過渡範圍，階段 0 為 18／18 完成。本次未建立 P1 分支或展開階段 1，不推送、不合併、不改寫歷史。
+
+- 舊 LINE token／會友 LINE ID 依使用者核定保留，未查詢或清空；此例外僅適用本次 trace 驗收。舊 Plugin／工作流程停用、三筆系統作業取消依使用者先前回報，未重新逐筆稽核。
+- GitHub 23／26 過渡範圍已接受；成功實跑及授權環境變數覆寫效果仍待使用者推送後確認。DevExpress 套件來源於階段 1 優先處理，本機 §8 繼續含全方案。
+- SmokeAsync 輪詢約兩秒及可控時鐘測試，仍是階段 1 第一批待辦，P2-04 前完成；本次沒有更改 100 毫秒輪詢。
+- P2-04 前另驗真實推播；Plugin 自身 FileVersion、更新與回復於階段 5 驗收。實機重送仍依使用者接受的單元測試及 LINE 重試契約替代，未實際推播或測試送達。
+- ImageSharp／Xml／Pkcs 修補仍須重新發佈 ChurchReport 到正式站才生效；本次未發佈。
+- 小組回報圖表未逐項驗證、正式線移植、既有 Excel 匯出與冷還原暫存清理維持原待辦。
+
+## 歷史紀錄
+
+下方保留先前文件交付及停止的當時紀錄；當時「未完成／不連線」由本節最新授權與實機結果取代，不表示本次仍待使用者操作。
+
 # P0-13 最小驗收文件交付與停止回報（2026-11-04）
 
 > 前半為 9f87187c 的精簡交付與完整本機驗證紀錄；最新操作補充見末節。本次只改文件，不重跑產品建置／測試及外部審查。
diff --git a/d365/registration/plugins.registration.json b/d365/registration/plugins.registration.json
index b91d5b0a3..0fa6f746e 100644
--- a/d365/registration/plugins.registration.json
+++ b/d365/registration/plugins.registration.json
@@ -14,7 +14,7 @@
       "unsecureConfig": "{\"mode\":\"trace\",\"culture\":\"zh-TW\"}",
       "secureConfig": "",
       "enabledByOrganization": {
-        "goodshepherdback": true
+        "goodshepherdback": false
       }
     }
   ]
diff --git "a/\346\226\207\344\273\266/\350\246\217\345\212\203\350\250\255\350\250\210\346\233\270/\345\257\246\344\275\234\350\250\210\347\225\253-3.3/01_\351\232\216\346\256\2650_\346\272\226\345\202\231\345\256\210\351\226\200\350\210\207\345\217\257\350\241\214\346\200\247\351\251\227\350\255\211.md" "b/\346\226\207\344\273\266/\350\246\217\345\212\203\350\250\255\350\250\210\346\233\270/\345\257\246\344\275\234\350\250\210\347\225\253-3.3/01_\351\232\216\346\256\2650_\346\272\226\345\202\231\345\256\210\351\226\200\350\210\207\345\217\257\350\241\214\346\200\247\351\251\227\350\255\211.md"
index 6dc3858da..0edc479dd 100644
--- "a/\346\226\207\344\273\266/\350\246\217\345\212\203\350\250\255\350\250\210\346\233\270/\345\257\246\344\275\234\350\250\210\347\225\253-3.3/01_\351\232\216\346\256\2650_\346\272\226\345\202\231\345\256\210\351\226\200\350\210\207\345\217\257\350\241\214\346\200\247\351\251\227\350\255\211.md"
+++ "b/\346\226\207\344\273\266/\350\246\217\345\212\203\350\250\255\350\250\210\346\233\270/\345\257\246\344\275\234\350\250\210\347\225\253-3.3/01_\351\232\216\346\256\2650_\346\272\226\345\202\231\345\256\210\351\226\200\350\210\207\345\217\257\350\241\214\346\200\247\351\251\227\350\255\211.md"
@@ -610,10 +610,10 @@
 
 ## P0-13　使用者最小驗收：以佈署專案在 goodshepherdback 註冊並觸發骨架
 
-**使用者 2026-11-04 核定：** 只保留核心沙箱可行性驗收，前置備妥後預留約 1 小時。完整操作見 `驗收\P0_Plugin骨架佈署步驟.md`；Codex 不連線 D365／LINE，依使用者回報提供逐步引導，所有線上操作由使用者執行。
+**使用者 2026-11-04 核定：** 只保留核心沙箱可行性驗收，前置備妥後預留約 1 小時。完整操作見 `驗收\P0_Plugin骨架佈署步驟.md`；原由使用者逐步操作；使用者 2026-11-04 最新核定在 verify=0 後全權授權 Codex 自動完成本次驗收及必要 D365-9.1 調查，取代原不連線限制；本次不發 LINE。
 
 0. **前置確認：** 停用 goodshepherdback 的舊 Plugin 步驟及自訂工作流程，取消等候中、可重試、排程中的舊自訂系統作業。**使用者 2026-11-04 追加核定：本次 P0-13 trace 保留資料中的舊 LINE token 與會友 `new_lineid`，免除這兩項清理前置，不記為已清理；此例外不自動放寬 P1-12 或後續推播階段。** `SPEECHMESSAGE_PLUGIN_KEYFILE` 可使用；Deployment User Secrets 只需 `OrganizationUrl`、`Username`、`Password`。trace 不要求 `TestLineUserId`、`TestChannelToken`，不需要測試 LINE channel。Plugins／Deployment 的直接及間接相依無 ImageSharp，不需要 Six Labors 授權。已確認項目依使用者回報更新，未做的實機驗收不勾選。
-1. **追蹤：** 在「設定 → 系統管理 → 系統設定 → 自訂」，把「啟用外掛程式追蹤記錄」設為「全部」。
+1. **追蹤：** 在「設定 → 管理 → 系統設定 → 自訂」，把「啟用外掛程式追蹤記錄」設為「全部」。
 2. **建置：** Release 明確指定 `SpeechMessagePackagePlugin=true` 打包，`Test-PluginAssembly.ps1` 通過，建置 Deployment。
 3. **plan：** 第一次 plan 前，清單即設為 trace、secureConfig 空白、`enabledByOrganization.goodshepherdback=true`。工具支援建立時直接啟用；新安裝只列 CreateAssembly／CreateType／CreateStep，已存在時核對預期差異。
 4. **apply → verify：** 套用剛審核的 plan，一輪完成註冊與啟用；trace 的 verify 必須為 0。清單、DLL 或組織現況變更就重新 plan。
@@ -626,7 +626,7 @@
 
 | 結果 | 後續 |
 |---|---|
-| 第 0～6 步由使用者回報全部成功 | 最小驗收通過，再依下方完整判準結束階段 0 |
+| 第 0～6 步實機全部成功（依使用者最新操作授權） | 最小驗收通過，再依下方完整判準結束階段 0 |
 | 第 3、4 步失敗（連線、權限、沙箱拒絕、簽章） | 回到 P0-12 或 P0-10 定位；若 net462 組件仍在目標框架驗證階段出現同一個 `FormatException`／`0x80040216`，表示本次框架推論未解決故障，停止並另行調查；連線方式不可行就回報，不改回手動註冊 |
 | 第 5 步未出現預期的 `FrameworkFileVersion=4.8.x` | 這是伺服器 mscorlib 執行環境檔案版本的觀察，不代表 Plugin TargetFramework；請核對伺服器環境與追蹤資料，不要因此把 Plugin 改回 net48 |
 | 第 5 步 smoke 逾時、沒有追蹤 | 請使用者確認伺服器沙箱處理服務、非同步服務及追蹤設定 |
@@ -646,7 +646,12 @@
 
 - P0-01～P0-12A（含 P0-SEC、P0-ENV）全部通過驗證並提交。
 - 階段 0 外部審查（00 第 6.4 節）沒有未處理的 Critical／High；此項已達成，採單模型（使用者指定 ClaudeOnly）。
-- 精簡後的 P0-13 第 0～6 步由使用者回報成功；不以文件交付或離線測試冒充實機驗收，不使用 Plugin Registration Tool。
+- 精簡後的 P0-13 第 0～6 步實機成功；使用者先回報註冊 verify=0，Codex 依 2026-11-04 最新全權授權完成 smoke 與收尾，不以文件交付或離線測試冒充實機驗收，不使用 Plugin Registration Tool。
 - 使用者接受 GitHub CI 過渡範圍：23／26 專案，排除 SpeechMessageProducts.ChurchReport、ChurchReport.MemberInfo.Tests、ToolUtility.Dataverse.Tests。這是範圍接受，不代表已取得 GitHub 成功結果；DevExpress 來源在階段 1 開始後優先解決。
 - 本機 00 §8 維持全方案（含 ChurchReport）；建置 0 錯誤，既有測試不比基準差。
-- 以上達成後才開 `1.0.0.11.Integration.P1`；目前最小驗收尚未完成，不開分支。資料中的舊 LINE token 與會友 `new_lineid` 已由使用者 2026-11-04 核定保留，免除本次 trace 清理前置。
+- 以上達成後才開 `1.0.0.11.Integration.P1`；本次最小驗收及停用清理已完成，階段 0 為 18／18；本次仍未建立 P1 分支。資料中的舊 LINE token 與會友 `new_lineid` 已由使用者 2026-11-04 核定保留，免除本次 trace 清理前置。
+
+
+### P0-13 完成紀錄（2026-11-04）
+
+使用者 net462 apply／verify=0 後，Codex 依全權授權完成 p0-ping-trace（結束碼 0）；當次唯一追蹤的五項驗收全過，FrameworkFileVersion=4.8.4795.0。新 plan 僅一項 UpdateStepState，停用 apply=0、verify=0；追蹤回復例外狀況，smoke task 殘留 0。清單 false 已新提交 f664c685。最新完整本機 §8 為 a4649e3b 前的 1125／1125、Debug 7／7、建置零錯誤／零警告；本輪額外執行 Deployment 56／56。沒有 LINE 推播、沒有建立 P1 或推送。詳細結果見驗收文件；GitHub 過渡實跑與後續階段待辦仍保留。
diff --git "a/\346\226\207\344\273\266/\350\246\217\345\212\203\350\250\255\350\250\210\346\233\270/\345\257\246\344\275\234\350\250\210\347\225\253-3.3/\345\257\246\344\275\234\351\200\262\345\272\246.html" "b/\346\226\207\344\273\266/\350\246\217\345\212\203\350\250\255\350\250\210\346\233\270/\345\257\246\344\275\234\350\250\210\347\225\253-3.3/\345\257\246\344\275\234\351\200\262\345\272\246.html"
index 53c1c7fc7..1cd463681 100644
--- "a/\346\226\207\344\273\266/\350\246\217\345\212\203\350\250\255\350\250\210\346\233\270/\345\257\246\344\275\234\350\250\210\347\225\253-3.3/\345\257\246\344\275\234\351\200\262\345\272\246.html"
+++ "b/\346\226\207\344\273\266/\350\246\217\345\212\203\350\250\255\350\250\210\346\233\270/\345\257\246\344\275\234\350\250\210\347\225\253-3.3/\345\257\246\344\275\234\351\200\262\345\272\246.html"
@@ -350,53 +350,45 @@
  * status 可用：done（已完成）、active（進行中）、todo（未開始）、you（由你操作）、review（階段審查）。
  */
 const DATA = {
-  "snapshot": "2026-11-04（net462 離線驗證及第 10 份獨立複審通過；等待使用者實機重驗）",
+  "snapshot": "2026-11-04 20:41（P0-13 實機通過並完成停用收尾；階段 0 完成）",
   "branch": "1.0.0.11.Integration.P0",
   "worker": "實作：Codex　查核：Claude",
-  "headline": "階段 0 原審查、第 09 份與第 10 份 ClaudeOnly 獨立複審已通過，無未處理 Critical／High。P0-13 已依使用者核定精簡為 <strong>trace 沙箱最小驗收</strong>；FileVersion／回復移至階段 5，推播移至 P2-04 前。net48 註冊失敗後，使用者已核定依 D21 正式改用 net462；離線建置、簽章打包、資產盤點、測試與修正後完整 00 §8 已通過，等待使用者重新實機驗收。",
-  "alert": {
-    "title": "net462 已通過離線驗證與複審，請從第 2 步重新驗收",
-    "body": "使用者 2026-11-04 核定依 D21 正式改用 net462，取代隔離試做。第 10 份初審 High 已修正，獨立複審 Critical 0／High 0，修正後完整 00 §8 通過。審查為單模型（使用者指定 ClaudeOnly），dualModelComplete=false、quotaBlocked=false；net462 是否可註冊仍須使用者實機證實。",
-    "steps": [
-      "由使用者從第 2 步重新打包，再產生並審核新 plan；預期只有 CreateAssembly／CreateType／CreateStep，舊 net48 plan 不可重用。",
-      "apply 成功後 verify=0，再做 smoke；若仍是相同 FormatException／0x80040216 就停下回報，不改伺服器或改用手動註冊。",
-      "停用測試步驟、verify=0、追蹤恢復例外狀況並確認資料清理，再回報結果。"
-    ]
-  },
+  "headline": "階段 0 已完成 <strong>18／18 工作包</strong>。net462 已在 goodshepherdback 註冊並執行成功；使用者回報啟用 verify=0 後，Codex 依最新全權授權完成 smoke=0、五項追蹤、停用 verify=0、追蹤恢復例外狀況及測試 task 清除。原審查、第 09／10 份與 verify 修正審查無未處理 Critical／High；GitHub CI 維持使用者已接受的過渡範圍。本次未開 P1。",
+  "alert": null,
   "tiles": [
     {
       "label": "全計畫進度",
-      "value": "17",
+      "value": "18",
       "unit": "/ 83 工作包",
-      "sub": "17／18；待使用者完成最小驗收，未開 P1",
+      "sub": "階段 0 18／18 完成；尚未開 P1",
       "meter": {
-        "done": 17,
-        "active": 1,
+        "done": 18,
+        "active": 0,
         "total": 83
       }
     },
     {
       "label": "階段 0 進度",
-      "value": "17",
+      "value": "18",
       "unit": "/ 18 工作包",
-      "sub": "17／18；待使用者完成最小驗收，未開 P1",
+      "sub": "階段 0 18／18 完成；尚未開 P1",
       "meter": {
-        "done": 17,
-        "active": 1,
+        "done": 18,
+        "active": 0,
         "total": 18
       }
     },
     {
       "label": "最新完整本機 Release",
-      "value": "1104",
-      "unit": "/ 1104 通過",
-      "sub": "ChurchReport 590／590；零失敗／跳過；Debug tracer 7／7"
+      "value": "1125",
+      "unit": "/ 1125 通過",
+      "sub": "a4649e3b 前完整本機 §8；ChurchReport 596／596，Debug tracer 7／7"
     },
     {
       "label": "最新完整本機建置",
       "value": "通過",
-      "unit": "0 錯誤",
-      "sub": "1 個既有 CS1572；GitHub 過渡實跑結果尚待使用者確認"
+      "unit": "0 警告／0 錯誤",
+      "sub": "a4649e3b 前完整本機 Release；GitHub 過渡實跑結果仍待確認"
     }
   ],
   "wps": [
@@ -529,10 +521,10 @@ const DATA = {
     },
     {
       "id": "P0-13",
-      "name": "使用者最小驗收：trace 沙箱註冊與清理",
-      "status": "you",
-      "note": "net48 apply 失敗後，使用者 2026-11-04 核定正式改用 net462。離線建置、套件／facade 盤點、簽章打包、測試、修正後完整 §8 與第 10 份 ClaudeOnly 獨立複審通過；由使用者重新打包、plan／apply／verify／smoke 及停用收尾。net462 註冊尚未實機證實。",
-      "commit": "9f87187c（精簡）；本次操作文件補充，未實機驗收"
+      "name": "最小驗收：trace 沙箱註冊、執行與清理",
+      "status": "done",
+      "note": "使用者 net462 apply／verify=0；Codex 依 2026-11-04 全權授權完成 smoke=0，P0-PING／Assembly 1.0.0.0／Framework 4.8.4795.0／雙語五項追蹤全通過。單項 UpdateStepState 停用、verify=0、追蹤回復例外狀況，smoke task 殘留 0；不發 LINE。",
+      "commit": "69803bf1（net462）；a4649e3b（verify）；f664c685（清單停用）"
     }
   ],
   "phases": [
@@ -541,9 +533,9 @@ const DATA = {
       "name": "準備、守門與可行性驗證",
       "est": "5～8 天",
       "total": 18,
-      "done": 17,
-      "active": 1,
-      "current": true
+      "done": 18,
+      "active": 0,
+      "current": false
     },
     {
       "id": "階段 1",
@@ -692,8 +684,8 @@ const DATA = {
     {
       "item": "Deployment 離線 L1／L2",
       "before": "未建立",
-      "after": "46／46；82.12%",
-      "note": "含秘密時 verify 為 3 未完成，不比較或輸出實值；無 D365 連線"
+      "after": "56／56；84.95%",
+      "note": "安全欄位 schema 回歸及命令租約釋放通過；P0-13 已另外完成實機 trace 驗收，推播未驗收。"
     },
     {
       "item": "階段 0 外部審查",
@@ -728,7 +720,8 @@ const DATA = {
     },
     {
       "ok": true,
-      "text": "沒有改動產品程式"
+      "text": "本輪實機驗收未修改產品程式",
+      "sub": "沿用既有 Deployment；清單停用另以新提交保存。前面工作包的產品修正仍保留。"
     },
     {
       "ok": true,
@@ -1030,6 +1023,11 @@ const DATA = {
       "t": "2026-11-04",
       "text": "舊 net48 組件在 D365 目標框架驗證器發生 FormatException。使用者核定正式採用 net462；離線驗證與第 10 份獨立複審通過，初審 High 已修正。伺服器註冊仍待使用者重跑；FrameworkFileVersion=4.8.x 指伺服器 mscorlib，不代表 Plugin 目標。",
       "cls": "ok"
+    },
+    {
+      "t": "2026-11-04 20:41",
+      "cls": "ok",
+      "text": "使用者 verify=0 後全權授權自動驗收。Codex 完成 smoke=0、五項追蹤（FrameworkFileVersion=4.8.4795.0）、單項 UpdateStepState 停用及 verify=0；追蹤回復例外狀況，本次 smoke task 殘留 0。清單 false 已新提交，階段 0 為 18／18，未開 P1。"
     }
   ],
   "decisions": [
@@ -1133,7 +1131,7 @@ const DATA = {
     {
       "state": "done",
       "text": "唯一開發測試組織已指定 goodshepherdback",
-      "sub": "使用者確認為正式資料複本；實機驗收由使用者執行，Codex 不連線 D365／LINE。"
+      "sub": "使用者 2026-11-04 全權授權 Codex 自動執行本次 trace 驗收；固定端點與組織白名單不變，未發 LINE。"
     },
     {
       "state": "done",
@@ -1151,9 +1149,9 @@ const DATA = {
       "sub": "目前程序讀得到指定環境變數；正式 Release 簽章與打包檢查已通過，未讀取或輸出金鑰內容"
     },
     {
-      "state": "todo",
-      "text": "ClaudeOnly 審查已完成，P0-13 人工操作待重新執行",
-      "sub": "原審查、第 09 份及第 10 份獨立複審通過；等待使用者以 net462 重跑註冊、trace smoke 與停用收尾，尚未完成 P0-13。"
+      "state": "done",
+      "text": "ClaudeOnly 審查與 P0-13 實機驗收皆已完成",
+      "sub": "原審查、第 09／10 份及 verify 修正獨立審查通過；net462 實機 smoke、五項追蹤、停用及資料清除全數完成。"
     },
     {
       "state": "done",
@@ -1178,9 +1176,9 @@ const DATA = {
       "sub": "使用者 2026-11-04 接受排除三個 DevExtreme 依賴專案；階段 1 優先解決來源。job 空環境值與 GITHUB_ENV 覆寫後的建置效果仍待實跑。"
     },
     {
-      "level": "warn",
-      "text": "goodshepherdback 清理尚未回報",
-      "sub": "不連線 D365／LINE；P0-13 前置確認保持未勾選。"
+      "level": "info",
+      "text": "本次 trace 前置與驗收收尾已完成",
+      "sub": "舊步驟／流程停用與 3 筆作業取消依使用者回報；本次 smoke task 殘留 0。舊 LINE token／會友 LINE ID 依核定保留，此例外不自動延伸至後續階段。"
     },
     {
       "level": "info",
diff --git "a/\346\226\207\344\273\266/\350\246\217\345\212\203\350\250\255\350\250\210\346\233\270/\345\257\246\344\275\234\350\250\210\347\225\253-3.3/\351\251\227\346\224\266/P0_Plugin\351\252\250\346\236\266\344\275\210\347\275\262\346\255\245\351\251\237.md" "b/\346\226\207\344\273\266/\350\246\217\345\212\203\350\250\255\350\250\210\346\233\270/\345\257\246\344\275\234\350\250\210\347\225\253-3.3/\351\251\227\346\224\266/P0_Plugin\351\252\250\346\236\266\344\275\210\347\275\262\346\255\245\351\251\237.md"
index 2e9b4ac10..f240cb336 100644
--- "a/\346\226\207\344\273\266/\350\246\217\345\212\203\350\250\255\350\250\210\346\233\270/\345\257\246\344\275\234\350\250\210\347\225\253-3.3/\351\251\227\346\224\266/P0_Plugin\351\252\250\346\236\266\344\275\210\347\275\262\346\255\245\351\251\237.md"
+++ "b/\346\226\207\344\273\266/\350\246\217\345\212\203\350\250\255\350\250\210\346\233\270/\345\257\246\344\275\234\350\250\210\347\225\253-3.3/\351\251\227\346\224\266/P0_Plugin\351\252\250\346\236\266\344\275\210\347\275\262\346\255\245\351\251\237.md"
@@ -1,6 +1,8 @@
 # P0-13　Plugin 骨架最小驗收
 
-> 使用者 2026-11-04 核定。前置備妥後預留約 1 小時；全部由使用者執行，Codex 不連線 D365／LINE。
+> **P0-13 已完成（2026-11-04，台灣時間 20:41 收尾）。** 使用者先完成註冊與 verify=0，隨後全權授權 Codex 自動執行剩餘驗收、必要調查及收尾；本次授權取代先前僅由使用者操作的限制。不發送 LINE。
+>
+> 本頁保留可重現步驟與歷史問題摘要；本輪已完成，不需再次執行。最新結果見 §0.3 及文末結果表。
 > **只連 goodshepherdback；不要貼出密碼、token、金鑰、User Secrets 或追蹤全文。**
 
 > **LINE 憑證分離補充（2026-11-04）：** ChurchReport 的 `appsettings.json` 已清空 LINE token；正式環境改由專用環境變數提供，Development 的 `DevTest` token 只接受 User Secrets。
@@ -91,27 +93,33 @@ JSON 中的反斜線必須寫**兩條** `\\`，解析後才是一條；只寫一
 
 **已完成的程式工作：** `86ca8bf83` 已完成 ChurchReport LINE 設定分離；離線測試 ChurchReport.MemberInfo.Tests 597／597、Line.Messaging.Tests 32／32 通過。這只證明程式設定與守門，不能據此勾選 P0-13 實機項目。
 
-**完成條件：** 本次兩項資料清理已核定免除。P0-13 仍須由使用者完成追蹤設定、plan／apply／verify、trace smoke 與停用收尾，不能以核定免除取代實機驗收。
-
-### 0.3 目前進度與下一小步
-
-**已完成（使用者 2026-11-04 回報）：** 前置確認、第 1 步追蹤設為「全部」並儲存、第 2 步建置與打包檢查，以及第 3 步清單改為 true 和 plan 審核。plan 只列出 CreateAssembly／CreateType／CreateStep，符合本次 trace 骨架的預期。
+**完成條件：** 本次兩項資料清理已核定免除。P0-13 的追蹤設定、plan／apply／verify、trace smoke 與停用收尾均已實機完成，證據見 §0.3；核定免除並非實機通過的依據。
 
-**先前阻礙（net48，已由後續 net462 apply 成功取代）：** 第二份 plan 的 apply 回報兩層 `0x80040216`。使用者隨後核定由 Codex 操作已開啟的 D365-9.1 虛擬機查看紀錄；已在 2026-11-04 台灣時間 14:16:19 的 `MSCRMWebService` 事件 18176，確認故障位於 `pluginassembly` 的 Create／Stage 30：`System.FormatException` 發生在 `TargetFrameworkVersionValidator.ValidateInternal()`，呼叫鏈包含 `PluginValidatorBase.Validate()` 與 `ValidateAssemblyMetadata(...)`。已定位目標框架驗證階段，但尚未取得伺服器可接受的框架版本上限；verify、smoke 與停用收尾皆未完成。
+### 0.3 本輪實機驗收已完成
 
-**已完成的唯讀查核：** Codex 經使用者核定直接查看虛擬機 GUI 的上述事件，以及 `Microsoft.Crm.ObjectModel.dll` 的詳細資料，版本為 `9.1.49.21825`。續查 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll` 的詳細資料，檔案與產品版本均為 **4.8.4795.0**；這證明該檔案存在，不能直接當成沙箱程序實際載入版本或第 5 步 smoke 的驗收結果。本次沒有產品程式修改，也沒有執行新的 plan／apply。
+**最新授權（使用者 2026-11-04）：** 使用者回報 verify=0 後，明確授權 Codex 自動執行 PowerShell、本次 D365 驗收及必要的 D365-9.1 調查，不再要求逐步貼指令。本次只操作 goodshepherdback 的 DiagnosticsPingPlugin、其 smoke task 與追蹤設定；未發 LINE，未修改其他組織、舊 Plugin／流程或伺服器。
 
-**目前處置（使用者 2026-11-04 核定）：** 依 D21 正式採用 .NET Framework 4.6.2（`net462`）：Plugin 目標為 `net462`；Core 與 Localization 為 `net462;net10.0`；測試執行器可維持 `net48`，但必須確認引用的是 `net462` 產品輸出。使用者最新核定取代前一版「保留正式 net48、只做隔離試做」的決定。Codex 已完成 net462 離線建置、套件／框架檢查、簽章打包、測試與 00 §8；第 10 份 ClaudeOnly 初審的 High 已修正，獨立複審通過，沒有未處理 Critical／High。使用者現在從第 2 步重新打包，再依第 3～6 步驗收。Codex 不連線 D365／LINE、不執行 plan／apply／verify／smoke。
+| 已完成項目 | 證據與結果 |
+| --- | --- |
+| 註冊並啟用 | 使用者完成 net462 plan／apply，並回報 `Deployment state matches the registration manifest.`、結束碼 0 |
+| trace smoke | Codex 20:36:38～20:37:18 執行 `p0-ping-trace`，回報 `Smoke scenario passed.`、結束碼 0 |
+| 五項追蹤 | 依本次時窗及完整 Plugin 型別限定查詢，唯一一筆追蹤五項皆符合；不輸出或保存訊息區塊全文 |
+| 執行環境 | 本次沙箱實際回報 `FrameworkFileVersion=4.8.4795.0`；Plugin 目標仍為 net462 |
+| 停用 | 新 plan 只有同一步驟一項 `UpdateStepState`；apply=0，收尾 verify=0 |
+| 追蹤復原及清理 | 20:41:18 回讀確認步驟已停用、追蹤設定為「例外狀況」（1）、本次 smoke task 殘留 0 |
+| 清單與版控 | 清單已回到 false，另以新提交 `f664c685` 保存；提交後該檔 git status 無輸出，未改寫使用者的 true 提交 |
 
-**net462 重驗目前進度（使用者 2026-11-04 回報）：** 第 2 步全部通過；第 3 步 net462 新 plan 已審核，第 4 步 apply 已回報成功，未再出現先前的 `0x80040216`。使用者隨後執行 verify，回報兩層 `0x80041103`、固定 FaultException 摘要及結束碼 1；尚未通過。查詢欄位已離線修正，完整 §8 與本次新 ClaudeOnly 審查通過，Deployment 已重建；等待使用者重試 verify。暫不重跑 apply，也不進 smoke；停用收尾未完成，P0-13 尚未完成。
+同次 correlation 僅查到 DiagnosticsPingPlugin，沒有 Plugin 例外或推播標記。這是本次限定範圍的結果，不代表已全面稽核其他歷史作業。
 
-現場 `Microsoft.Crm.ObjectModel.dll` 檔案版本為 `9.1.49.21825`，事件指出 `TargetFrameworkVersionValidator.ValidateInternal()` 內的 `String.Format` 發生 `FormatException`。Microsoft KB 4345239 描述此驗證器會拒絕不相容的 Plugin 目標框架；公開案例也記錄相同格式化多載並以 net462 解決。因此「net48 目標遭拒，格式化例外遮住原訊息」是高度可信推論，但原拒絕訊息與格式化失敗的具體原因未取得。官方地端文件列出 4.6.2～4.8，未指出 4.8 沙箱支援的最低伺服器組建，不能只憑現場 DLL 版本宣稱缺少哪個更新。`mscorlib.dll` 檔案版本 `4.8.4795.0` 與 smoke 欄位 `FrameworkFileVersion=4.8.x` 描述伺服器執行環境，不代表 Plugin 的 `TargetFramework`；smoke 仍預期看到 `4.8.x`。只有使用者重新註冊 net462 組件的結果，才能確認該 D365 主機是否接受它。更多依據與限制見[部署失敗調查報告](P0-13_部署失敗調查報告_供Claude複核.md)。
+**已解決的阻礙：**
 
-兩個相同的數字碼沒有指出失敗的是哪一項建立操作，也不能證明沒有部分寫入。後續若需重試，先重新 plan 並審核目前狀態，不重用失敗的 plan，也不要在 apply 後重新以「最新目錄」改寫 `$plan`。
+- net48 apply 曾回報 `0x80040216`；伺服器事件 18176 的 `TargetFrameworkVersionValidator.ValidateInternal()` 出現 FormatException，ObjectModel 為 `9.1.49.21825`。依 D21 改用 net462 後，實機註冊、verify 及 smoke 均通過。原始拒絕訊息被遮住的細節仍屬推論，不能據此認定伺服器接受框架的完整上限。
+- apply 成功後 verify 曾回報 `0x80041103`。`a4649e3b` 已將錯誤查閱欄位 `secureconfigid` 改為 `sdkmessageprocessingstepsecureconfigid`，保留安全設定內容欄位 `secureconfig`；離線回歸及實機 verify 均通過。
+- PowerShell 中文輸出僅部分改善，先前還原開頭兩行仍有亂碼；本次自動命令使用程序層英文 CLI 摘要，沒有宣稱已永久修復使用者視窗的編碼。
 
-**途中提交（2026-11-04）：** 使用者手動提交 `714465fd` 已核對並保留；清單的唯一差異為 goodshepherdback 從 false 改為 true，產品程式未變更，其餘為已驗證的診斷任務／文件與審查紀錄。清單 true 已在 HEAD；第 6 步須另以新提交保存 false，不能再假設改回 false 後工作目錄會自動乾淨。不改寫原提交，目前尚未執行停用收尾。
+詳見[部署失敗調查報告](P0-13_部署失敗調查報告_供Claude複核.md)、主任務的 P0-13 回報及去識別化驗收證據。原始 export、追蹤全文、User Secrets、金鑰與授權內容均不進回報或版控。
 
-**紀錄原則（使用者 2026-11-04 要求）：** 每有修正、完成事項或新阻礙，都同步更新本文件的目前進度、對應步驟與文末結果表；離線測試通過不等於實機驗收通過。
+**完成判定：** 最小 P0-13 實機驗收通過；搭配既有完整 00 §8、階段審查無未處理 Critical／High，及使用者已接受的 GitHub CI 過渡範圍，階段 0 為 18／18 完成。本次未建立 P1 分支，未開始階段 1。
 
 ## 1. 開啟 Plugin 追蹤
 
@@ -119,12 +127,14 @@ JSON 中的反斜線必須寫**兩條** `\\`，解析後才是一條；只寫一
 
 **預期看到什麼：** 儲存成功；稍後可在「Plugin 追蹤記錄」與「系統作業」查看本次執行。階段 0～4 的 Plugin 失敗由這兩處查看。
 
-**使用者執行結果（2026-11-04）：** 已設為「全部」並儲存；驗收收尾時仍須改回「例外狀況」。
+**完成結果（2026-11-04）：** 使用者先設為「全部」並儲存；Codex 收尾時已改回「例外狀況」，並以 API 回讀確認值為 1。
 
 **不對時怎麼辦：** 無權限或找不到選項就停下回報，請管理員確認；驗收後改回「例外狀況」。
 
 ## 2. 建置與打包檢查
 
+**本步已完成。** 以下保留各次建置的歷史回報；當時「尚待」項目均以 §0.3 的最新實機結果為準。
+
 **第 10 份審查修正與複審（2026-11-04）：** 初審沒有 Critical，但發現 facade 參考可能被 System.* 規則放行的 High。已用合成 DLL 先紅後綠修復，PluginPackaging 27／27；合併前與部署 DLL 檢查皆新增獨立 `noNetStandardFacade` 守門，其他檢查保留。修正後完整 00 §8 再次通過；同一自癒入口的獨立複審為 Critical 0、High 0、Medium 0、Low 0。兩次均為單模型（使用者指定 ClaudeOnly），`reviewMode=claude-only`、`dualModelComplete=false`、`quotaBlocked=false`；現在可由使用者重新驗收，仍不代表 D365 已接受 net462。
 
 **做什麼：** 逐行執行，每行成功才執行下一行。
@@ -185,60 +195,28 @@ plan 成功後，**立刻執行取得 `$plan` 與開啟 plan.md 的兩行，在
 
 **net462 重新驗收規則（使用者 2026-11-04 核定）：** 舊計畫綁定 net48 組件雜湊，不能重用。待本機 net462 Release 打包檢查通過並由使用者取得新部署 DLL 後，重新執行第 3 步 plan，立即取得該次 `$plan` 並審核 `plan.md`。預期仍只有 CreateAssembly／CreateType／CreateStep；如果出現其他操作或新 plan 的內容與 trace 骨架不符，停止，不要 apply。
 
-**net462 新 plan 核對結果（2026-11-04）：** 使用者提供 UTC `2026-11-04T12:06:26.9763980+00:00`（台灣時間 20:06）的新計畫，組織為 goodshepherdback，只有 CreateAssembly、DiagnosticsPingPlugin 的 CreateType，以及 Create／task／PostOperation／Asynchronous／rank 1 的 CreateStep；User Secrets 佔位為無，符合本次 trace 骨架預期。清單與組織指紋沿用既定值，組件 SHA-256 已改為 `4F70115499412056FD84A2DDCFE0A853CEED11E55050594266A877D785E74B11`；本次 Plan SHA-256 為 `CC89D4891576FA9579257C22583D60C10002A0CFF6A88BD38BF14D5E1D7D2E51`。本項依使用者貼出的計畫審核；此份 plan 後續 apply 已成功（見第 4 步），verify 尚待修正後重試；Codex 未連線 D365。
+**net462 新 plan 核對結果（2026-11-04）：** 使用者提供 UTC `2026-11-04T12:06:26.9763980+00:00`（台灣時間 20:06）的新計畫，組織為 goodshepherdback，只有 CreateAssembly、DiagnosticsPingPlugin 的 CreateType，以及 Create／task／PostOperation／Asynchronous／rank 1 的 CreateStep；User Secrets 佔位為無，符合本次 trace 骨架預期。清單與組織指紋沿用既定值，組件 SHA-256 已改為 `4F70115499412056FD84A2DDCFE0A853CEED11E55050594266A877D785E74B11`；本次 Plan SHA-256 為 `CC89D4891576FA9579257C22583D60C10002A0CFF6A88BD38BF14D5E1D7D2E51`。本項依使用者貼出的計畫審核；此份 plan 後續 apply 已成功（見第 4 步），後續 verify 已由使用者回報 0，並由 Codex 完成 smoke 及停用收尾；詳見 §0.3。
 
 **不對時怎麼辦：** 出現其他組織、步驟、push 模式或未預期更新就不套用，回報操作種類。不得手改 plan hash；清單或 DLL 改過就重新 plan。
 
 ## 4. 套用並確認 verify=0
 
-**做什麼：** 確認第 3 步 plan.md 內容符合預期，再在**同一個 PowerShell 視窗**逐行執行，沿用剛取得的 `$plan`。
+**做什麼：** 審核第 3 步 plan.md 後，沿用該次已固定的 `$plan`。
 
 ```powershell
 dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- apply --org goodshepherdback --plan $plan
 dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- verify --org goodshepherdback
 ```
 
-**預期看到什麼：** apply 結束碼 0、測試步驟已註冊並啟用；verify 結束碼 **0**、無差異。apply 先在**該次 apply 目錄**保存 export.json；留在本機 artifacts，不上傳。
-
-**使用者 net462 apply 結果（2026-11-04）：** 沿用第 3 步已審核的新 `$plan` 執行 apply，工具回報 `Plan applied. Current state export: export.json`，本次未再回報先前的 `0x80040216`。這是本輪套用成功的使用者回報，支持改用 net462 已越過原註冊阻礙；尚須 verify=0 確認清單一致，smoke 才能確認沙箱執行。Codex 未連線 D365，未讀取 export.json；P0-13 不因此直接完成。
-
-**使用者 net462 verify 結果（2026-11-04）：** apply 成功後執行 verify，回報 `Dataverse fault code[0]: 0x80041103`、`Dataverse fault code[1]: 0x80041103`，以及固定 FaultException 摘要，`$LASTEXITCODE` 為 1。這是查詢階段的新阻礙，不等於 net462 註冊再次失敗。官方將此碼列為 `QueryBuilderNoAttribute`。離線檢查發現部署工具將步驟安全設定查閱欄位誤寫為 `secureconfigid`；官方名稱為 `sdkmessageprocessingstepsecureconfigid`。已先取得 5 個功能紅燈，再用共用常數改正全部 8 處查閱讀寫；Deployment 56／56 離線通過，包含真正 CLI 假資料生命周期及既有安全查閱的存在性匯出／清理。完整 00 §8 全綠：Release 建置 0 警告／0 錯誤、方案 1125／1125（ChurchReport 596／596）、Debug tracer 7／7、編碼／重複碼／I18n 守門通過。Deployment 56／56、行覆蓋率 84.95%；未改 Plugin 或清單。 本次新修正另經自癒入口 ClaudeOnly 審查通過（20261104-202450-p013-verify-schema-review-reviewer），無 Critical／High 或其他缺陷。分析與審查皆為單模型（使用者指定 ClaudeOnly）：reviewMode=claude-only、dualModelComplete=false、quotaBlocked=false、ok=true。 實機尚未重試。暫不重跑 apply、不進 smoke，不讀取原始 fault 或 export。
-
-**使用者執行結果（2026-11-04）：** 首次 apply 只有固定失敗摘要；數字診斷補修後，第二份 plan 的 apply 仍失敗，實際回報為 `Dataverse fault code[0]: 0x80040216`、`Dataverse fault code[1]: 0x80040216`，以及 ``Command failed (FaultException`1). Sensitive details are not shown.``。這兩個是本次實際代碼，不是範例。不能據此判定為權限、框架或其他特定原因，也不能排除部分寫入。verify 尚未執行；暫不進入 smoke。診斷只輸出最多四個八位十六進位數字碼，不輸出原始訊息、追蹤全文或秘密；本機驗證見下方。這只補足診斷，尚未修復或驗證伺服器端拒絕原因。
-
-**錯誤碼判讀與已取得的證據：** Microsoft 官方將 `0x80040216`（`-2147220970`）列為 `UnExpected`，屬通用錯誤；§0.3 已補入伺服器事件的時間、代碼、例外型別、驗證方法與元件版本，只記錄必要診斷，不保存事件全文、機器識別值或資料內容。目前官方地端文件已列出 Plugin 可用 4.6.2～4.8，但未在該段指定所需伺服器更新版本，不能據此斷言現場的 `9.1.49.21825` 必定接受 net48。依據：[Microsoft：Web service error codes](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/web-service-error-codes)、[地端 Plugin 與 .NET Framework 支援](https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/visual-studio-dot-net-framework?view=op-9-1)、[D21 原定目標](../00_總覽與執行規則.md)。
-
-**診斷補修的本機驗證（2026-11-04，Codex 離線執行）：**
-
-- 00 §8：Release 全方案建置成功（本次增量建置 0 錯誤、0 警告）；Release 全方案 1116／1116 通過，含 ChurchReport 596／596；Debug tracer 7／7 通過；Changed 編碼、重複程式與 I18n 守門均通過。曾有 5 份先前工作紀錄的換行不符規範，僅整理換行後編碼守門通過，未放寬守門。
-- Deployment 原 46 例保留，新增 6 例，全套 52／52 通過；模組覆蓋率 **82.62%**。驗證原例外只上報一次、連線先釋放、回報故障、深層／循環 fault、缺失 Detail，以及主控台和共用日誌皆不含合成秘密。
-- 本次補修另經自癒入口分析與審查；兩次皆為 `reviewMode=dual`、`dualModelComplete=true`、`quotaBlocked=false`，沒有未處理的 Critical／High。這是本次補修的新審查，不冒稱由先前階段 0 審查涵蓋。
-- 本機證據位於 `artifacts/p013-apply-fault-tests/`；全方案各測試組的完整摘要保留於 `validation/release-test.log`。這些結果不等於 apply、verify 或 smoke 已通過。
+**預期看到什麼：** apply=0；verify 輸出 `Deployment state matches the registration manifest.`，結束碼 **0**。apply 的 export.json 只留本機 artifacts。
 
-**net462 變更後的下一步：** 全方案 Release、測試、打包、守門與第 10 份 ClaudeOnly 獨立複審均已通過。使用者依第 2～6 步重新建置、產生並審核新 plan、apply、verify=0、smoke 與停用收尾；不得重用舊 net48 plan。若新的 net462 apply 仍在目標框架驗證階段出現相同 `FormatException`／`0x80040216`，停止並回報，表示本次框架推論未解決問題；不改回 Plugin Registration Tool、不修改伺服器。所有失敗回報仍只保留安全的數字錯誤碼與固定摘要，不貼 Exception.log 全文、追蹤全文、帳密或任何設定內容。
+**本輪結果（2026-11-04）：** 使用者已回報 net462 apply 成功；`a4649e3b` 修正查詢欄位後，使用者重新 verify 成功，結束碼 0。因此未重複 apply，也未重建已通過檢查的 Plugin。
 
-**不對時怎麼辦：** 連線、權限、簽章或沙箱錯誤就停下回報並做第 6 步，不改回手動註冊。trace 的 verify=1 或 3 都未通過，不能略過。
+**已修正的 verify 缺陷：** 步驟安全設定查閱必須用 `sdkmessageprocessingstepsecureconfigid`，原 `secureconfigid` 導致 `0x80041103`。獨立 schema 回歸先取得 5 個功能紅燈，修正全部 8 處後 Deployment 56／56；覆蓋率 84.95%。`a4649e3b` 前完整 00 §8 為 Release 0 警告／0 錯誤、方案 1125／1125（ChurchReport 596／596）、Debug tracer 7／7，編碼／重複碼／I18n 均通過。其獨立審查無缺陷：單模型（使用者指定 ClaudeOnly），reviewMode=claude-only、dualModelComplete=false、quotaBlocked=false、ok=true。
 
-### 4.1 verify 查詢欄位修正後的重試
+**不對時怎麼辦：** 只記固定摘要與數字錯誤碼，不貼原始 fault、export 或設定；verify 非 0 不算通過。需要再次寫入時先重新 plan 並審核，不重用已套用或失敗的 plan，不改回手動註冊。
 
-**已定位的程式缺陷（2026-11-04）：** 步驟安全查閱誤用 `secureconfigid`；已改為官方的 `sdkmessageprocessingstepsecureconfigid`。首次建立前不查既有步驟，本次 trace 又不寫安全設定，故 apply 成功後的 verify 才遇到不存在的欄位。假 SDK 原本未檢查欄位，現已補獨立 schema 守門及先紅後綠回歸。
-
-**已完成：** 完整 00 §8 全綠：Release 建置 0 警告／0 錯誤、方案 1125／1125（ChurchReport 596／596）、Debug tracer 7／7、編碼／重複碼／I18n 守門通過。Deployment 56／56、行覆蓋率 84.95%；未改 Plugin 或清單。 本次新修正另經自癒入口 ClaudeOnly 審查通過（20261104-202450-p013-verify-schema-review-reviewer），無 Critical／High 或其他缺陷。分析與審查皆為單模型（使用者指定 ClaudeOnly）：reviewMode=claude-only、dualModelComplete=false、quotaBlocked=false、ok=true。
-
-**做什麼：** Codex 已在本 P0 工作區重建 Deployment Release；使用者在原本同一工作區的 PowerShell 直接重試下面兩行即可。不重新打包 Plugin、不再次 apply，仍以本次現有註冊狀態為準。若日後改用另一份工作區，須先重建該處 Deployment Release。
-
-```powershell
-dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- verify --org goodshepherdback
-$LASTEXITCODE
-```
-
-**預期看到什麼：** `Deployment state matches the registration manifest.`，下一行為 `0`。由使用者回報後才進第 5 步 smoke；Codex 不執行此命令。
-
-**不對時怎麼辦：** 保留固定摘要、數字錯誤碼及結束碼，停下回報；不貼原始 fault、export 或設定。若後續確實需要寫入，須重新 plan 並審核，不重用先前已 apply 的 plan。
-
-依據：[Microsoft 步驟欄位](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/entities/sdkmessageprocessingstep#sdkmessageprocessingstepsecureconfigid)、[Microsoft 錯誤碼](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/web-service-error-codes)。
-
-## 5. smoke trace，人工看五項追蹤
+## 5. smoke trace，核對五項追蹤
 
 **做什麼：**
 
@@ -248,13 +226,15 @@ dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -
 
 開啟「**設定 → 自訂 → 外掛程式追蹤記錄**」，依建立時間找最新一筆 **DiagnosticsPingPlugin**，打開查看「**訊息區塊**」。以本次 smoke 時段核對，必要時對照「系統作業」，不要把舊記錄當成本次成功；確認：
 
-- [ ] `P0-PING OK`。
-- [ ] `Assembly=` 為 SpeechMessage.Dynamics.Plugins，版本 `1.0.0.0`。
-- [ ] `FrameworkFileVersion=` 為 **4.8.x**（mscorlib）；不能用 `EnvironmentVersion=` 代替。
-- [ ] `zh-TW=連線測試成功`。
-- [ ] `qps-ploc=［連線測試成功ǂǂǂ］`。
+- [x] `P0-PING OK`。
+- [x] `Assembly=` 為 SpeechMessage.Dynamics.Plugins，版本 `1.0.0.0`。
+- [x] `FrameworkFileVersion=` 為 **4.8.x**（mscorlib）；不能用 `EnvironmentVersion=` 代替。
+- [x] `zh-TW=連線測試成功`。
+- [x] `qps-ploc=［連線測試成功ǂǂǂ］`。
+
+**預期看到什麼：** 結束碼 0、`Smoke scenario passed.`，五項確認符合；工具最多等 3 分鐘，結束時嘗試刪除自己建立的 `[smoke]` task。
 
-**預期看到什麼：** 結束碼 0、`Smoke scenario passed.`，五項人工確認符合；工具最多等 3 分鐘，結束時嘗試刪除自己建立的 `[smoke]` task。
+**本輪結果（2026-11-04）：** Codex 依全權授權執行 smoke=0，並透過部署工具既有固定端點 connector 查核本次時窗的唯一 DiagnosticsPingPlugin 追蹤，五項全部通過；FrameworkFileVersion 實值為 `4.8.4795.0`。原始訊息區塊只在程序記憶體中比對，未輸出或存檔；查核連線在 finally Dispose。smoke task 殘留 0；沒有 Plugin 例外或推播標記。
 
 **不對時怎麼辦：**
 
@@ -291,9 +271,9 @@ git status --short -- .\d365\registration\plugins.registration.json
 
 **預期看到什麼：**
 
-- [ ] 同一測試步驟已停用、清單回到 false，收尾 verify=0；git 顯示清單沒有未提交變更。
-- [ ] Plugin 追蹤設定已改回「例外狀況」。
-- [ ] smoke 資料已清除，沒有觸發其他舊流程或通知。
+- [x] 同一測試步驟已停用、清單回到 false，收尾 verify=0；`f664c685` 提交後 git 顯示清單沒有未提交變更。
+- [x] Plugin 追蹤設定已改回「例外狀況」，API 回讀為 1。
+- [x] 本次 smoke task 已清除；同次 correlation 僅有 DiagnosticsPingPlugin，trace 無例外或推播標記。
 
 **不對時怎麼辦：** 無法停用或清理就立即回報，由使用者處理 D365 停用／清理，不再建立測試資料。
 
@@ -303,13 +283,14 @@ git status --short -- .\d365\registration\plugins.registration.json
 | --- | --- |
 | 第 0 步前置與準備 | 使用者已回報舊步驟／流程停用、3 筆作業取消、金鑰存在及 User Secrets 儲存；舊 token 與會友 LINE ID 核定保留，免除本次清理前置 |
 | 建置與打包檢查 | 第 2 步全部通過：使用者 net462 Release 打包、合併後 DLL 完整檢查及 Deployment Release 重建成功；兩次建置皆 0 警告／0 錯誤，DLL AssemblyVersion 1.0.0.0、723456 bytes |
-| Plugin 追蹤設定 | 使用者已設為全部並儲存；收尾復原尚未完成 |
-| 一輪 plan／apply／verify=0 | net462 新 plan（台灣時間 20:06）已審核；使用者 apply 回報 Plan applied，未再出現先前的 `0x80040216`。本輪 verify 回報兩層 `0x80041103`、結束碼 1，欄位錯字已修正，完整 §8 與本次 ClaudeOnly 審查通過、Deployment 已重建，等待使用者重試 verify；smoke 與收尾仍待完成 |
-| trace smoke 與五項人工追蹤 | 未驗證 |
-| 停用、收尾 verify=0、追蹤復原及資料清除 | 未驗證 |
+| Plugin 追蹤設定 | 已完成：先設全部（2），收尾回讀為例外狀況（1） |
+| 一輪 plan／apply／verify=0 | 已完成：使用者 net462 註冊成功，欄位修正後實機 verify=0 |
+| trace smoke 與五項追蹤 | 已完成：smoke=0，五項全部符合，FrameworkFileVersion=4.8.4795.0 |
+| 停用與收尾 verify=0 | 已完成：新 plan 僅一項 UpdateStepState，apply=0、verify=0，伺服器步驟已停用 |
+| 清單與資料清除 | false 已提交 f664c685，該檔無未提交變更；本次 smoke task 殘留 0；舊 token／會友 LINE ID 依核定保留 |
 
 同一事件重送不再做實機驗收：使用者接受既有「Retry Key＝來源記錄 ID」單元測試，加上 LINE 官方同 Retry Key 在 24 小時有效期內抑制重複受理的契約作為依據；這不保證訊息一定送達，也不涵蓋超過有效期的重送。
 來源：[LINE 官方重試說明](https://developers.line.biz/en/docs/messaging-api/retrying-api-request/)。
 
 GitHub CI 的 23／26 專案過渡範圍已由使用者 2026-11-04 接受；DevExpress 來源在階段 1 開始後優先處理，本機 §8 仍驗證全方案。
-**文件已備妥，依使用者回報逐步續行最小驗收；P0-13 和階段 0 目前尚未完成，也不開 P1 分支。Codex 不連線 D365／LINE。**
+**P0-13 與階段 0 已完成。使用者先回報啟用註冊 verify=0；Codex 依最新全權授權完成 smoke、五項追蹤、停用與清理。本次未建立 P1 分支、未執行 LINE 推播、未推送、未合併或改寫歷史。**

## 去識別化實機證據
{
  "date": "2026-11-04",
  "organization": "goodshepherdback",
  "result": "passed",
  "authorization": "使用者全權授權 Codex 自動執行本次 P0-13 與必要 D365-9.1 調查，取代原僅由使用者操作限制。",
  "userReportedRegistrationVerifyExitCode": 0,
  "smoke": {
    "organization": "goodshepherdback",
    "scenario": "p0-ping-trace",
    "startUtc": "2026-11-04T12:36:38.5012298+00:00",
    "endUtc": "2026-11-04T12:37:18.3714897+00:00",
    "exitCode": 0,
    "authorization": "使用者 2026-11-04 全權授權 Codex 自動操作本次驗收"
  },
  "traceCheck": {
    "organization": "goodshepherdback",
    "checkedUtc": "2026-11-04T12:40:19.2045172+00:00",
    "operation": "Inspect",
    "matchingTraceCount": 1,
    "checks": {
      "pingOk": true,
      "assemblyVersion": true,
      "framework48": true,
      "traditionalChinese": true,
      "pseudoLocale": true,
      "noPluginException": true,
      "noPushAttempt": true
    },
    "frameworkFileVersion": "4.8.4795.0",
    "onlyDiagnosticInCorrelation": true,
    "remainingSmokeTasks": 0,
    "diagnosticStepDisabled": false,
    "traceModeWithoutSecureConfiguration": true,
    "traceSettingBefore": 2,
    "traceSettingAfter": 2
  },
  "cleanup": {
    "organization": "goodshepherdback",
    "checkedUtc": "2026-11-04T12:41:18.2820756+00:00",
    "operation": "RestoreTrace",
    "matchingTraceCount": 1,
    "checks": {
      "pingOk": true,
      "assemblyVersion": true,
      "framework48": true,
      "traditionalChinese": true,
      "pseudoLocale": true,
      "noPluginException": true,
      "noPushAttempt": true
    },
    "frameworkFileVersion": "4.8.4795.0",
    "onlyDiagnosticInCorrelation": true,
    "remainingSmokeTasks": 0,
    "diagnosticStepDisabled": true,
    "traceModeWithoutSecureConfiguration": true,
    "traceSettingBefore": 2,
    "traceSettingAfter": 1
  },
  "disablePlan": {
    "organization": "goodshepherdback",
    "checkedUtc": "2026-11-04T12:41:09.4479963+00:00",
    "operationCount": 1,
    "operationKind": "UpdateStepState",
    "onlyExpectedStep": true,
    "applyExitCode": 0,
    "verifyExitCode": 0,
    "planSha256": "D0EF5639F4BF7C16E645BCE2667083D82B08B7974283266B37030D2521121154"
  },
  "manifestDisabledCommit": "f664c685",
  "manifestStatusAfterCommit": "clean",
  "deploymentRegression": {
    "executed": 56,
    "passed": 56,
    "failed": 0,
    "skipped": 0
  },
  "priorFullLocalValidation": {
    "sourceCommit": "a4649e3b",
    "releasePassed": 1125,
    "releaseFailed": 0,
    "churchReportPassed": 596,
    "debugTracerPassed": 7,
    "buildErrors": 0,
    "buildWarnings": 0,
    "rerunInThisAcceptance": false
  },
  "scope": {
    "linePushExecuted": false,
    "oldLineDataCleared": false,
    "oldLineDataRetentionApproved": true,
    "otherOrganizationChanged": false,
    "phase1Started": false,
    "githubCiSuccessClaimed": false
  },
  "dataPolicy": "只保存固定結果、計數與版本；不保存原始追蹤、export、record ID、帳密、金鑰、授權或會友個資。"
}

## 本次本機暫存查核器（只審查，不執行）
param([ValidateSet('Inspect','RestoreTrace')][string]$Mode = 'Inspect')
$ErrorActionPreference = 'Stop'
$configuration = $null
$lease = $null
$stage = 'initialization'
try {
    # 僅在目前程序呼叫部署工具既有設定與 connector；不顯示、複製或另存憑證。
    $repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $deploymentFile = Join-Path $repo 'SpeechMessage.Dynamics.Deployment\bin\Release\net10.0\SpeechMessage.Dynamics.Deployment.dll'
    $assembly = [Reflection.Assembly]::LoadFrom($deploymentFile)
    $binding = [Reflection.BindingFlags]'Static,Public,NonPublic'
    $configuration = $assembly.GetType('SpeechMessage.Dynamics.Deployment.DeploymentConfiguration', $true).GetMethod('Create', $binding).Invoke($null, @($assembly))
    $constructor = $assembly.GetType('SpeechMessage.Dynamics.Deployment.UserSecretsOrganizationConnector', $true).GetConstructors() | Where-Object { $_.GetParameters().Count -eq 1 }
    $connector = $constructor.Invoke(@($configuration))
    $stage = 'connect'
    $lease = $connector.Connect('goodshepherdback')
    $service = $lease.Service
    $run = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'smoke-result.json') -Raw | ConvertFrom-Json
    if ($run.exitCode -ne 0 -or $run.organization -ne 'goodshepherdback' -or $run.scenario -ne 'p0-ping-trace') { throw [InvalidOperationException]::new() }
    $start = [DateTimeOffset]::Parse($run.startUtc).UtcDateTime
    $end = [DateTimeOffset]::Parse($run.endUtc).UtcDateTime.AddSeconds(5)
    $pluginType = 'SpeechMessage.Dynamics.Plugins.Diagnostics.DiagnosticsPingPlugin'

    # 查詢精確 Plugin 型別與本次 smoke 時窗；不查安全設定、會友或其他業務內容。
    $stage = 'trace-query'
    $traceQuery = [Microsoft.Xrm.Sdk.Query.QueryExpression]::new('plugintracelog')
    $traceQuery.ColumnSet = [Microsoft.Xrm.Sdk.Query.ColumnSet]::new([string[]]@('messageblock','correlationid','createdon'))
    $traceQuery.TopCount = 3
    $traceQuery.Criteria.AddCondition('typename', [Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal, $pluginType)
    $traceQuery.Criteria.AddCondition('createdon', [Microsoft.Xrm.Sdk.Query.ConditionOperator]::OnOrAfter, $start)
    $traceQuery.Criteria.AddCondition('createdon', [Microsoft.Xrm.Sdk.Query.ConditionOperator]::OnOrBefore, $end)
    $traces = $service.RetrieveMultiple($traceQuery).Entities
    if ($traces.Count -ne 1) { throw [InvalidOperationException]::new() }
    $trace = [string]$traces[0].Attributes['messageblock']
    $runtimeMatch = [regex]::Match($trace, '(?m)^FrameworkFileVersion=(\d+\.\d+\.\d+\.\d+)\r?$')
    $runtimeVersion = if ($runtimeMatch.Success) { $runtimeMatch.Groups[1].Value } else { 'unavailable' }
    $checks = [ordered]@{
        pingOk = [regex]::IsMatch($trace, '(?m)^P0-PING OK\r?$')
        assemblyVersion = [regex]::IsMatch($trace, '(?m)^Assembly=SpeechMessage\.Dynamics\.Plugins, Version=1\.0\.0\.0, ')
        framework48 = $runtimeVersion.StartsWith('4.8.', [StringComparison]::Ordinal)
        traditionalChinese = [regex]::IsMatch($trace, '(?m)^zh-TW=連線測試成功\r?$')
        pseudoLocale = [regex]::IsMatch($trace, '(?m)^qps-ploc=［連線測試成功ǂǂǂ］\r?$')
        noPluginException = -not $trace.Contains('ExceptionType=', [StringComparison]::Ordinal)
        noPushAttempt = -not $trace.Contains('Outcome=', [StringComparison]::Ordinal)
    }
    $trace = $null

    # 只計算同次 correlation 的型別，不發布任何追蹤全文、記錄 ID 或使用者資料。
    $stage = 'correlation-query'
    $relatedQuery = [Microsoft.Xrm.Sdk.Query.QueryExpression]::new('plugintracelog')
    $relatedQuery.ColumnSet = [Microsoft.Xrm.Sdk.Query.ColumnSet]::new([string[]]@('typename'))
    $relatedQuery.TopCount = 20
    $relatedQuery.Criteria.AddCondition('correlationid', [Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal, $traces[0].Attributes['correlationid'])
    $relatedTraces = $service.RetrieveMultiple($relatedQuery).Entities
    $onlyDiagnostic = $relatedTraces.Count -eq 1 -and [string]$relatedTraces[0].Attributes['typename'] -eq $pluginType

    # 精確 smoke 前綴、固定描述及本次時窗，確認 finally 已刪除 task。
    $stage = 'task-query'
    $taskQuery = [Microsoft.Xrm.Sdk.Query.QueryExpression]::new('task')
    $taskQuery.ColumnSet = [Microsoft.Xrm.Sdk.Query.ColumnSet]::new($false)
    $taskQuery.TopCount = 2
    $taskQuery.Criteria.AddCondition('subject', [Microsoft.Xrm.Sdk.Query.ConditionOperator]::BeginsWith, '[smoke] P0-')
    $taskQuery.Criteria.AddCondition('description', [Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal, '佈署工具自動化追蹤驗證')
    $taskQuery.Criteria.AddCondition('createdon', [Microsoft.Xrm.Sdk.Query.ConditionOperator]::OnOrAfter, $start)
    $taskQuery.Criteria.AddCondition('createdon', [Microsoft.Xrm.Sdk.Query.ConditionOperator]::OnOrBefore, $end)
    $remainingTasks = $service.RetrieveMultiple($taskQuery).Entities.Count

    $stage = 'type-query'
    $typeQuery = [Microsoft.Xrm.Sdk.Query.QueryExpression]::new('plugintype')
    $typeQuery.ColumnSet = [Microsoft.Xrm.Sdk.Query.ColumnSet]::new([string[]]@('pluginassemblyid'))
    $typeQuery.TopCount = 2
    $typeQuery.Criteria.AddCondition('typename', [Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal, $pluginType)
    $types = $service.RetrieveMultiple($typeQuery).Entities
    if ($types.Count -ne 1) { throw [InvalidOperationException]::new() }
    $managedAssembly = $service.Retrieve('pluginassembly', $types[0].Attributes['pluginassemblyid'].Id, [Microsoft.Xrm.Sdk.Query.ColumnSet]::new([string[]]@('name')))
    if ([string]$managedAssembly.Attributes['name'] -ne 'SpeechMessage.Dynamics.Plugins') { throw [InvalidOperationException]::new() }
    $stage = 'step-query'
    $stepQuery = [Microsoft.Xrm.Sdk.Query.QueryExpression]::new('sdkmessageprocessingstep')
    $stepQuery.ColumnSet = [Microsoft.Xrm.Sdk.Query.ColumnSet]::new([string[]]@('statecode','configuration','sdkmessageprocessingstepsecureconfigid'))
    $stepQuery.TopCount = 2
    $stepQuery.Criteria.AddCondition('eventhandler', [Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal, $types[0].Id)
    $steps = $service.RetrieveMultiple($stepQuery).Entities
    if ($steps.Count -ne 1) { throw [InvalidOperationException]::new() }
    $stepDisabled = $steps[0].Attributes['statecode'].Value -eq 1
    $stepConfiguration = [string]$steps[0].Attributes['configuration'] | ConvertFrom-Json
    $traceMode = $stepConfiguration.mode -eq 'trace' -and -not $steps[0].Attributes.Contains('sdkmessageprocessingstepsecureconfigid')

    # 固定組織 connector 已驗證唯一端點；設定寫入還要求測試步驟已停用。
    $stage = 'organization-query'
    $who = $service.Execute([Microsoft.Xrm.Sdk.OrganizationRequest]::new('WhoAmI'))
    $organization = $service.Retrieve('organization', $who.Results['OrganizationId'], [Microsoft.Xrm.Sdk.Query.ColumnSet]::new([string[]]@('plugintracelogsetting')))
    $traceBefore = $organization.Attributes['plugintracelogsetting'].Value
    if ($Mode -eq 'RestoreTrace') {
        if (-not $stepDisabled -or -not $traceMode -or $remainingTasks -ne 0 -or $checks.Values -contains $false) { throw [InvalidOperationException]::new() }
        $update = [Microsoft.Xrm.Sdk.Entity]::new('organization', $who.Results['OrganizationId'])
        $update.Attributes['plugintracelogsetting'] = [Microsoft.Xrm.Sdk.OptionSetValue]::new(1)
        $service.Update($update)
        $organization = $service.Retrieve('organization', $who.Results['OrganizationId'], [Microsoft.Xrm.Sdk.Query.ColumnSet]::new([string[]]@('plugintracelogsetting')))
    }
    $result = [ordered]@{
        organization = 'goodshepherdback'
        checkedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        operation = $Mode
        matchingTraceCount = $traces.Count
        checks = $checks
        frameworkFileVersion = $runtimeVersion
        onlyDiagnosticInCorrelation = $onlyDiagnostic
        remainingSmokeTasks = $remainingTasks
        diagnosticStepDisabled = $stepDisabled
        traceModeWithoutSecureConfiguration = $traceMode
        traceSettingBefore = $traceBefore
        traceSettingAfter = $organization.Attributes['plugintracelogsetting'].Value
    }
    $safeJson = $result | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot ($Mode.ToLowerInvariant() + '-result.json')), ($safeJson + "`r`n"), [Text.UTF8Encoding]::new($false))
    Write-Output $safeJson
    if ($checks.Values -contains $false -or $remainingTasks -ne 0 -or -not $traceMode -or -not $onlyDiagnostic) { exit 1 }
    if ($Mode -eq 'RestoreTrace' -and $organization.Attributes['plugintracelogsetting'].Value -ne 1) { exit 1 }
}
catch {
    Write-Output ('Acceptance stage=' + $stage)
    Write-Output ('Acceptance check failed (' + $_.Exception.GetType().Name + '). Sensitive details are not shown.')
    Write-Output ('HelperLine=' + $_.InvocationInfo.ScriptLineNumber)
    exit 1
}
finally {
    if ($null -ne $lease) { $lease.Dispose() }
    if ($null -ne $configuration) { $configuration.Dispose() }
}


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
