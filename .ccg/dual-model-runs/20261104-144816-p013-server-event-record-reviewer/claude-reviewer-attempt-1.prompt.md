ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: p013-server-event-record

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P0.Worktree

## Request
請審查 P0-13 這次「唯讀伺服器診斷紀錄」的推論與文件一致性；沒有產品程式修改。
只能使用下列事實與附上的三份檔案差異，不要另讀 repo、User Secrets、appsettings、任何金鑰／授權、伺服器原始日誌，不得連線 D365／LINE，不得修改檔案或執行部署。

已直接在使用者授權操作的虛擬機 GUI 觀察：
1. 本次 apply 的故障碼兩層皆 0x80040216。
2. 2026-11-04 台灣時間 14:16:19，MSCRMWebService 事件 18176。EntityName=pluginassembly，MessageName=Create，Stage=30。
3. 例外 System.FormatException，堆疊依序為 StringBuilder.FormatError / AppendFormatHelper、String.Format、Microsoft.Crm.ObjectModel.TargetFrameworkVersionValidator.ValidateInternal、PluginValidatorBase.Validate、ValidateAssemblyMetadata。
4. 唯讀檔案詳細資料顯示 Microsoft.Crm.ObjectModel.dll 版本 9.1.49.21825。
5. 原計畫 D21 明定 TargetFramework=net48，且規定框架相容性問題回報使用者決定、不自行降低框架。產品專案仍為 net48。
6. 此次直接取得 Microsoft 官方地端 Visual Studio and the .NET Framework 頁面 HTTP 200；2026-08-10 修訂的頁面列出 plug-ins 4.6.2 through 4.8、sandbox 4.8 support added，但相關段落未指定所需伺服器更新版本。
7. 沒有證明該伺服器缺少 .NET 4.8、沒有測得可接受目標上限、沒有實測 net462 可註冊，也沒有查明所需更新。

審查重點：是否把故障位置誤報為已證實的根因；是否錯將官方目前支援範圍套到特定版本；是否虛報完成或不當核定框架變更。不得建議未經使用者核定更新伺服器、改註冊限制或降低框架。請用繁體中文，列 Critical/High/Medium/Low/Info（沒有則明確寫無），總長不超過 400 字。

以下為紀錄差異：
diff --git a/.ccg/tasks/p013-apply-fault-investigation/analysis.md b/.ccg/tasks/p013-apply-fault-investigation/analysis.md
index b70ee0d2..492a08b6 100644
--- a/.ccg/tasks/p013-apply-fault-investigation/analysis.md
+++ b/.ccg/tasks/p013-apply-fault-investigation/analysis.md
@@ -8,3 +8,12 @@
 - 原 46 例保留。新增 6 例功能紅燈：5 失敗（缺數字碼）、1 通過（缺失 Detail 的既有相容性）；補修後全套 52／52 通過。
 - 所有故障、秘密與 SDK 服務均為記憶體假資料；Codex 未連線 D365／LINE。
 - 此補修只讓下一輪使用者操作取得數字錯誤碼；真正伺服器根因與 P0-13 實機驗收仍待確認。
+
+## 使用者核定的伺服器唯讀查核（2026-11-04）
+
+- 第二輪 apply 回報兩層 `0x80040216`；使用者另核定 Codex 直接操作已開啟的虛擬機查看事件。此次僅用 Windows GUI，不呼叫 D365／LINE API，不匯出事件全文。
+- 應用程式記錄的 `MSCRMWebService`／18176（台灣時間 14:16:19）顯示 `pluginassembly` 的 Create／Stage 30 失敗；`System.FormatException` 由 `String.Format` 進入 `TargetFrameworkVersionValidator.ValidateInternal()`，呼叫鏈含 `PluginValidatorBase.Validate()` 與 `ValidateAssemblyMetadata(...)`。
+- 另外唯讀查看 `Microsoft.Crm.ObjectModel.dll` 的詳細資料，元件版本為 `9.1.49.21825`。沒有記錄事件中的機器、組織或步驟識別值，沒有複製伺服器 DLL。
+- 證據已定位框架版本驗證階段，但未呈現可接受的版本上限，不能斷言沒有安裝 .NET 4.8。現行官方地端文件列出 4.6.2～4.8，卻未在該段指定所需更新版本，不能直接套用到現場版本。
+- D21 已核定 `TargetFramework=net48`；是否採 4.6.2 相容建置，或維持 net48 核對支援更新，須由使用者決定。尚未修改產品目標、守門、伺服器設定、服務或資料；未重試 apply，verify 與 smoke 均未執行。
+- 官方來源：https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/visual-studio-dot-net-framework?view=op-9-1 （本次直接取得 HTTP 200；頁面修訂日期 2026-08-10）。原始碼專案目前仍明定 net48。
diff --git a/.ccg/tasks/p013-apply-fault-investigation/task.json b/.ccg/tasks/p013-apply-fault-investigation/task.json
index 9f86d3a3..04a9a3e5 100644
--- a/.ccg/tasks/p013-apply-fault-investigation/task.json
+++ b/.ccg/tasks/p013-apply-fault-investigation/task.json
@@ -6,7 +6,7 @@
   "risk": "low",
   "domain": "backend",
   "currentPhase": "analysis",
-  "nextAction": "第二份 plan 的 apply 失敗，實際兩層代碼均為 0x80040216（官方 UnExpected）；先確認使用者能否登入 D365 伺服器桌面，逐步取得約 14:16 的既有伺服器診斷；不重試 apply，不推定根因；verify、smoke 與 P0-13 均未完成",
+  "nextAction": "已經使用者核定直接查看虛擬機事件：pluginassembly Create 的 TargetFrameworkVersionValidator.ValidateInternal 拋出 FormatException；待使用者核定 D21 相容性處置（評估採用 4.6.2 相容建置，或維持 net48 調查支援更新），不自行降低框架、不重試 apply；P0-13 未完成",
   "createdAt": "2026-11-04",
   "branch": "1.0.0.11.Integration.P0",
   "constraints": [
@@ -30,6 +30,17 @@
     "dualModelComplete": true,
     "quotaBlocked": false
   },
+  "serverEvidence": {
+    "authorization": "使用者核定接手操作已開啟的虛擬機，唯讀查看事件與元件版本；不呼叫 D365／LINE API",
+    "localEventTime": "2026-11-04T14:16:19+08:00",
+    "source": "MSCRMWebService",
+    "eventId": 18176,
+    "operation": "pluginassembly Create Stage 30",
+    "exceptionType": "System.FormatException",
+    "method": "Microsoft.Crm.ObjectModel.TargetFrameworkVersionValidator.ValidateInternal",
+    "componentVersion": "9.1.49.21825",
+    "remainingUnknown": "驗證器失敗的具體條件、可接受的目標框架上限與所需支援更新均未確認；未以此認定缺少 .NET 4.8，未實測 net462"
+  },
   "lastReviewedUserPlan": {
     "utc": "2026-11-04T06:14:02.6994698+00:00",
     "sha256": "C7C599656278D8E1A6BFB06894D5EC7D16BA182427FA6BBE55C9749D4B84DDDA",
diff --git "a/\346\226\207\344\273\266/\350\246\217\345\212\203\350\250\255\350\250\210\346\233\270/\345\257\246\344\275\234\350\250\210\347\225\253-3.3/\351\251\227\346\224\266/P0_Plugin\351\252\250\346\236\266\344\275\210\347\275\262\346\255\245\351\251\237.md" "b/\346\226\207\344\273\266/\350\246\217\345\212\203\350\250\255\350\250\210\346\233\270/\345\257\246\344\275\234\350\250\210\347\225\253-3.3/\351\251\227\346\224\266/P0_Plugin\351\252\250\346\236\266\344\275\210\347\275\262\346\255\245\351\251\237.md"
index d7f0b392..a1c9a610 100644
--- "a/\346\226\207\344\273\266/\350\246\217\345\212\203\350\250\255\350\250\210\346\233\270/\345\257\246\344\275\234\350\250\210\347\225\253-3.3/\351\251\227\346\224\266/P0_Plugin\351\252\250\346\236\266\344\275\210\347\275\262\346\255\245\351\251\237.md"
+++ "b/\346\226\207\344\273\266/\350\246\217\345\212\203\350\250\255\350\250\210\346\233\270/\345\257\246\344\275\234\350\250\210\347\225\253-3.3/\351\251\227\346\224\266/P0_Plugin\351\252\250\346\236\266\344\275\210\347\275\262\346\255\245\351\251\237.md"
@@ -97,9 +97,11 @@ JSON 中的反斜線必須寫**兩條** `\\`，解析後才是一條；只寫一
 
 **已完成（使用者 2026-11-04 回報）：** 前置確認、第 1 步追蹤設為「全部」並儲存、第 2 步建置與打包檢查，以及第 3 步清單改為 true 和 plan 審核。plan 只列出 CreateAssembly／CreateType／CreateStep，符合本次 trace 骨架的預期。
 
-**目前卡在第 4 步：** 使用者在第二份 plan 審核後再次執行 apply，回報 `Dataverse fault code[0]: 0x80040216`、`Dataverse fault code[1]: 0x80040216`，以及 ``Command failed (FaultException`1). Sensitive details are not shown.``。已取得實際數字錯誤碼，但伺服器根因仍未明；verify、smoke 與停用收尾皆未完成。Codex 僅在本機檢查程式與安全診斷，不連線 D365／LINE。
+**目前卡在第 4 步：** 第二份 plan 的 apply 回報兩層 `0x80040216`。使用者隨後核定由 Codex 操作已開啟的 D365-9.1 虛擬機查看紀錄；已在 2026-11-04 台灣時間 14:16:19 的 `MSCRMWebService` 事件 18176，確認故障位於 `pluginassembly` 的 Create／Stage 30：`System.FormatException` 發生在 `TargetFrameworkVersionValidator.ValidateInternal()`，呼叫鏈包含 `PluginValidatorBase.Validate()` 與 `ValidateAssemblyMetadata(...)`。已定位目標框架驗證階段，但尚未取得伺服器可接受的框架版本上限；verify、smoke 與停用收尾皆未完成。
 
-**下一小步：** 先確認使用者能否登入**安裝 Dynamics 365 的伺服器 Windows 桌面**（可使用遠端桌面）；只回覆能／不能登入，不提供位址、帳密或日誌全文。確認可登入後，再逐步查看本次 apply 時段（2026-11-04 台灣時間約 14:16）的既有伺服器錯誤紀錄；此時先不改伺服器設定、不重啟服務，也不再次執行 apply。
+**已完成的唯讀查核：** 使用者提供伺服器桌面後，明確要求 Codex 接手操作。Codex 已透過虛擬機畫面開啟「事件檢視器 → Windows 記錄 → 應用程式」、篩選最近一小時，並查看該筆事件；另外只查看 `Microsoft.Crm.ObjectModel.dll` 的檔案內容／詳細資料，版本為 `9.1.49.21825`。未匯出原始日誌、未變更伺服器設定或服務、未呼叫 D365／LINE API，也未執行新的 plan／apply。
+
+**下一小步：** 依原計畫 D21 的 `TargetFramework=net48` 邊界，先由使用者決定是否核定評估並採用 4.6.2 相容建置；若維持 net48，則須繼續核對此 D365 版本的相容性與支援更新。兩個方向均未執行，不自行降低框架、更新伺服器或繞過版本驗證。這次事件不足以斷言伺服器沒有安裝 .NET 4.8，也尚未證明 4.6.2 相容建置可成功註冊；不需要使用者再次操作畫面來重找這筆事件。
 
 兩個相同的數字碼沒有指出失敗的是哪一項建立操作，也不能證明沒有部分寫入。後續若需重試，先重新 plan 並審核目前狀態，不重用失敗的 plan，也不要在 apply 後重新以「最新目錄」改寫 `$plan`。
 
@@ -170,7 +172,7 @@ dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -
 
 **使用者執行結果（2026-11-04）：** 首次 apply 只有固定失敗摘要；數字診斷補修後，第二份 plan 的 apply 仍失敗，實際回報為 `Dataverse fault code[0]: 0x80040216`、`Dataverse fault code[1]: 0x80040216`，以及 ``Command failed (FaultException`1). Sensitive details are not shown.``。這兩個是本次實際代碼，不是範例。不能據此判定為權限、框架或其他特定原因，也不能排除部分寫入。verify 尚未執行；暫不進入 smoke。診斷只輸出最多四個八位十六進位數字碼，不輸出原始訊息、追蹤全文或秘密；本機驗證見下方。這只補足診斷，尚未修復或驗證伺服器端拒絕原因。
 
-**錯誤碼判讀與下一項證據：** Microsoft 官方將 `0x80040216`（`-2147220970`）列為 `UnExpected`，訊息為「An unexpected error occurred.」；這是通用錯誤，仍需伺服器端的具體錯誤紀錄。先依 §0.3 確認使用者可否登入 D365 伺服器桌面，再引導查看既有紀錄；不預先要求開啟額外追蹤或修改登錄。原始日誌可能包含機密或個資，不整份貼出或加入 repo。依據：[Microsoft：Web service error codes](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/web-service-error-codes)、[地端伺服器監控與疑難排解](https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/deploy/monitor-and-troubleshoot-dynamics-365?view=op-9-1)。
+**錯誤碼判讀與已取得的證據：** Microsoft 官方將 `0x80040216`（`-2147220970`）列為 `UnExpected`，屬通用錯誤；§0.3 已補入伺服器事件的時間、代碼、例外型別、驗證方法與元件版本，只記錄必要診斷，不保存事件全文、機器識別值或資料內容。目前官方地端文件已列出 Plugin 可用 4.6.2～4.8，但未在該段指定所需伺服器更新版本，不能據此斷言現場的 `9.1.49.21825` 必定接受 net48。依據：[Microsoft：Web service error codes](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/web-service-error-codes)、[地端 Plugin 與 .NET Framework 支援](https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/visual-studio-dot-net-framework?view=op-9-1)、[D21 原定目標](../00_總覽與執行規則.md)。
 
 **診斷補修的本機驗證（2026-11-04，Codex 離線執行）：**
 
@@ -179,7 +181,7 @@ dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -
 - 本次補修另經自癒入口分析與審查；兩次皆為 `reviewMode=dual`、`dualModelComplete=true`、`quotaBlocked=false`，沒有未處理的 Critical／High。這是本次補修的新審查，不冒稱由先前階段 0 審查涵蓋。
 - 本機證據位於 `artifacts/p013-apply-fault-tests/`；全方案各測試組的完整摘要保留於 `validation/release-test.log`。這些結果不等於 apply、verify 或 smoke 已通過。
 
-**目前先不重試：** 先取得伺服器端的具體診斷，再決定修法。後續若經判定需要新一輪 plan／apply，失敗時仍只回報數字行與固定失敗摘要，不貼 Exception.log 全文、追蹤全文、帳密或任何設定內容。
+**目前先不重試：** 已取得目標框架驗證器的具體故障位置，待 D21 相容性處置核定與修正驗證後，再重新 plan／審核。失敗時仍只回報數字行與固定失敗摘要，不貼 Exception.log 全文、追蹤全文、帳密或任何設定內容。
 
 **不對時怎麼辦：** 連線、權限、簽章或沙箱錯誤就停下回報並做第 6 步，不改回手動註冊。trace 的 verify=1 或 3 都未通過，不能略過。
 
@@ -249,7 +251,7 @@ git status --short -- .\d365\registration\plugins.registration.json
 | 第 0 步前置與準備 | 使用者已回報舊步驟／流程停用、3 筆作業取消、金鑰存在及 User Secrets 儲存；舊 token 與會友 LINE ID 核定保留，免除本次清理前置 |
 | 建置與打包檢查 | 已由使用者執行通過；Deployment 有 1 個 CS1572 警告，詳第 2 步 |
 | Plugin 追蹤設定 | 使用者已設為全部並儲存；收尾復原尚未完成 |
-| 一輪 plan／apply／verify=0 | 第二份 plan 已審核並執行 apply，仍失敗：fault code[0]、[1] 均為 `0x80040216`；根因未明，待確認伺服器診斷方式，verify 未執行 |
+| 一輪 plan／apply／verify=0 | 第二份 plan 的 apply 仍失敗：兩層 `0x80040216`；Codex 經使用者核定查得事件 18176，故障位於目標框架驗證器；伺服器元件 `9.1.49.21825`，待 D21 相容性處置核定，verify 未執行 |
 | trace smoke 與五項人工追蹤 | 未驗證 |
 | 停用、收尾 verify=0、追蹤復原及資料清除 | 未驗證 |
 


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.