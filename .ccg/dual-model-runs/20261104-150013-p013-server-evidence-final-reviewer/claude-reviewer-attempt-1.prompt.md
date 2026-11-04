ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: p013-server-evidence-final

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P0.Worktree

## Request
請唯讀審查下附四份文件差異。不得另讀檔案、帳密／金鑰／User Secrets、連線 D365／LINE 或修改檔案。這是 P0-13 伺服器調查紀錄，不是產品修正。
已確認事實：
1. 使用者核定 Codex 接手已開啟的虛擬機，GUI 唯讀查看事件與檔案內容／詳細資料。
2. 2026-11-04 14:16:19+08:00 的 MSCRMWebService 18176：pluginassembly Create Stage30，System.FormatException 在 String.Format 與 TargetFrameworkVersionValidator.ValidateInternal，呼叫鏈含 PluginValidatorBase.Validate 及 ValidateAssemblyMetadata。
3. Microsoft.Crm.ObjectModel.dll 版本9.1.49.21825。
4. 本次新查核：伺服器 C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll 檔案／產品版本均為4.8.4795.0。只看詳細資料，未執行該 DLL；不能當成沙箱載入版本已實測。
5. 原計畫 D21 已核定net48；未核定改net462。既有 Microsoft 官方文件列4.6.2至4.8，但該段沒有最低伺服器更新；未確認驗證器具體失敗條件，不能把net462當成已驗證修法。
6. 使用者手動提交714465fd已核對；清單僅false→true，其餘為前次已驗證紀錄／審查產物，沒有產品碼變更。保留歷史，工作目錄清單目前內容仍與HEAD一致。完成驗收收尾須另以新提交保存false。
7. 尚未成功apply，尚未verify、smoke、伺服器停用收尾；本次無產品修改，不重跑產品測試，文件中1116等數字明確屬前次。
8. 前一輪reviewMode=dual/dualModelComplete=true/quotaBlocked=false：沒有Critical/High；Claude Medium為GUI導覽細節不在提示事實內，Low為未記錄識別值等自述無給定來源，本次刪除這些多餘敘述。新core版本與使用者提交修正未聲稱被前輪涵蓋。
核對是否推論過度、虛報完成、變更框架／清單／伺服器或清理條件錯誤。只列真實可行動問題，沒有就寫無；不要要求等待使用者批准額外的唯讀調查。繁中400字內，分Critical/High/Medium/Low/Info。

diff --git a/.ccg/tasks/p013-apply-fault-investigation/analysis.md b/.ccg/tasks/p013-apply-fault-investigation/analysis.md
index b70ee0d2..fd09b671 100644
--- a/.ccg/tasks/p013-apply-fault-investigation/analysis.md
+++ b/.ccg/tasks/p013-apply-fault-investigation/analysis.md
@@ -8,3 +8,18 @@
 - 原 46 例保留。新增 6 例功能紅燈：5 失敗（缺數字碼）、1 通過（缺失 Detail 的既有相容性）；補修後全套 52／52 通過。
 - 所有故障、秘密與 SDK 服務均為記憶體假資料；Codex 未連線 D365／LINE。
 - 此補修只讓下一輪使用者操作取得數字錯誤碼；真正伺服器根因與 P0-13 實機驗收仍待確認。
+
+## 使用者核定的伺服器唯讀查核（2026-11-04）
+
+- 第二輪 apply 回報兩層 `0x80040216`；使用者另核定 Codex 直接操作已開啟的虛擬機查看事件。此次僅用 Windows GUI，不呼叫 D365／LINE API，不匯出事件全文。
+- 應用程式記錄的 `MSCRMWebService`／18176（台灣時間 14:16:19）顯示 `pluginassembly` 的 Create／Stage 30 失敗；`System.FormatException` 由 `String.Format` 進入 `TargetFrameworkVersionValidator.ValidateInternal()`，呼叫鏈含 `PluginValidatorBase.Validate()` 與 `ValidateAssemblyMetadata(...)`。
+- 另外唯讀查看 `Microsoft.Crm.ObjectModel.dll` 的詳細資料，元件版本為 `9.1.49.21825`。
+- 續查伺服器 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll` 的內容／詳細資料，檔案與產品版本均為 `4.8.4795.0`。這是檔案版本證據，不代表已驗證沙箱程序實際載入版本，也不能當成 smoke 的 FrameworkFileVersion 驗收。
+- 證據已定位框架版本驗證階段，但未呈現可接受的版本上限；不能將失敗歸因為未安裝 .NET 4.8。現行官方地端文件列出 4.6.2～4.8，卻未在該段指定所需更新版本，不能直接套用到現場版本。
+- D21 已核定 `TargetFramework=net48`；是否採 4.6.2 相容建置，或維持 net48 核對支援更新，須由使用者決定。尚未修改產品目標、守門、伺服器設定、服務或資料；未重試 apply，verify 與 smoke 均未執行。
+- 官方來源：https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/visual-studio-dot-net-framework?view=op-9-1 （本次直接取得 HTTP 200；頁面修訂日期 2026-08-10）。原始碼專案目前仍明定 net48。
+
+## 使用者途中提交與收尾條件
+
+- 已核對並保留使用者手動提交 `714465fd`：清單唯一差異為 goodshepherdback 的 false→true，其餘為既有診斷任務、文件與審查紀錄，產品程式未改。
+- 清單 true 已在 HEAD，不能再稱未提交修改。驗收結束仍須設為 false、完成停用驗證，並另以新提交保存 false 後才檢查此檔工作目錄乾淨；目前不改清單，也不宣稱伺服器停用完成。
diff --git a/.ccg/tasks/p013-apply-fault-investigation/review.md b/.ccg/tasks/p013-apply-fault-investigation/review.md
index 08f52839..8395e989 100644
--- a/.ccg/tasks/p013-apply-fault-investigation/review.md
+++ b/.ccg/tasks/p013-apply-fault-investigation/review.md
@@ -2,7 +2,7 @@
 
 ## 範圍及尚未完成事項
 
-此補修僅新增安全數字碼以診斷使用者的 apply 失敗。真正的 SDK 伺服器拒絕原因尚未知；不得將本機綠燈標為 P0-13 或階段 0 完成。後續由使用者重新 plan、審核再 apply。Codex 未連線 D365／LINE，未讀取機密或其他 worktree；使用者的清單 true 變更保留但不得提交。
+此補修僅新增安全數字碼以診斷使用者的 apply 失敗。真正的 SDK 伺服器拒絕原因尚未知；不得將本機綠燈標為 P0-13 或階段 0 完成。後續由使用者重新 plan、審核再 apply。Codex 未連線 D365／LINE，未讀取機密或其他 worktree；使用者手動提交 `714465fd` 的清單 true 變更已核對保留；驗收收尾須另以新提交保存 false，不改寫歷史。
 
 ## TDD 與本機 00 §8
 
@@ -35,3 +35,11 @@ Claude 的兩項 Info：
 2. 文件範例碼不可當成實際伺服器代碼；文件已明確標註，取得使用者新輸出後再同步更新。
 
 原始分析／審查產物留存於上述 runId 對應的 .ccg/dual-model-runs/ 本機目錄；本摘要不含帳密、token、金鑰或授權內容。
+
+## 伺服器診斷紀錄審查（2026-11-04）
+
+- 自癒入口 runId=`20261104-144816-p013-server-event-record-reviewer`；reviewMode=dual、dualModelComplete=true、quotaBlocked=false、degradedFallback=false。
+- 已完整讀取 Gemini 與 Claude 的輸出，兩方均無 Critical／High。Gemini 的 Info/Medium 提醒保留 D21 核定邊界，已遵守。
+- Claude Medium 指出 GUI 導覽與篩選細節超出審查提示列舉的七項事實；Low 指出未記錄識別值／未複製 DLL 的自述不在給定事實內。這些不構成產品缺陷；為縮小紀錄至已列舉證據，文件改為只列 GUI 觀察到的事件與檔案版本，刪除上述多餘敘述。
+- 審查後另作兩項事實補充：唯讀查看伺服器 mscorlib.dll 詳細資料，檔案／產品版本為 4.8.4795.0；核對使用者手動提交 714465fd 並修正清單收尾條件。這兩項不宣稱已由前述審查涵蓋。
+- 本次僅改驗收文件與既有任務紀錄；未重跑產品測試或全方案 §8，前節測試數字屬前次診斷補修。P0-13 仍未通過，尚未執行 verify、smoke 或停用收尾。
diff --git a/.ccg/tasks/p013-apply-fault-investigation/task.json b/.ccg/tasks/p013-apply-fault-investigation/task.json
index 9f86d3a3..8b75404a 100644
--- a/.ccg/tasks/p013-apply-fault-investigation/task.json
+++ b/.ccg/tasks/p013-apply-fault-investigation/task.json
@@ -6,14 +6,14 @@
   "risk": "low",
   "domain": "backend",
   "currentPhase": "analysis",
-  "nextAction": "第二份 plan 的 apply 失敗，實際兩層代碼均為 0x80040216（官方 UnExpected）；先確認使用者能否登入 D365 伺服器桌面，逐步取得約 14:16 的既有伺服器診斷；不重試 apply，不推定根因；verify、smoke 與 P0-13 均未完成",
+  "nextAction": "伺服器 GUI 已定位 pluginassembly Create 的框架驗證器 FormatException，並見 .NET 核心檔案 4.8.4795.0；D21 仍為 net48，如評估改 4.6.2 先取得使用者核定，不變更伺服器、不重試 apply；verify、smoke 與 P0-13 未完成",
   "createdAt": "2026-11-04",
   "branch": "1.0.0.11.Integration.P0",
   "constraints": [
     "不新增 Trellis 任務",
     "Codex 不連線 D365／LINE",
     "不讀取或輸出 User Secrets、帳密、token、金鑰或授權內容",
-    "保留使用者將清單 goodshepherdback 改為 true 的未提交變更",
+    "保留使用者手動提交 714465fd（含清單 goodshepherdback=true）；不改寫歷史，驗收收尾時另以新提交保存 false",
     "不推送、不合併、不改寫歷史"
   ],
   "verification": {
@@ -30,6 +30,19 @@
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
+    "remainingUnknown": "已見伺服器 .NET 核心檔案版本 4.8.4795.0；仍未確認 D365 驗證器的失敗條件、可接受目標上限或所需支援更新，未證明沙箱載入版本，未實測 net462",
+    "frameworkCoreFile": "C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\mscorlib.dll",
+    "frameworkCoreFileVersion": "4.8.4795.0"
+  },
   "lastReviewedUserPlan": {
     "utc": "2026-11-04T06:14:02.6994698+00:00",
     "sha256": "C7C599656278D8E1A6BFB06894D5EC7D16BA182427FA6BBE55C9749D4B84DDDA",
@@ -45,5 +58,10 @@
       "0x80040216",
       "0x80040216"
     ]
+  },
+  "preservedUserCommit": {
+    "commit": "714465fd",
+    "checked": "清單差異僅 goodshepherdback false→true；其餘為已驗證的診斷任務／文件與審查紀錄，產品程式未變更；保留歷史",
+    "cleanup": "完成驗收收尾後，以新提交保存清單 false，之後再確認此檔 git status 無輸出"
   }
 }
diff --git a/文件/規劃設計書/實作計畫-3.3/驗收/P0_Plugin骨架佈署步驟.md b/文件/規劃設計書/實作計畫-3.3/驗收/P0_Plugin骨架佈署步驟.md
index d7f0b392..802698a6 100644
--- a/文件/規劃設計書/實作計畫-3.3/驗收/P0_Plugin骨架佈署步驟.md
+++ b/文件/規劃設計書/實作計畫-3.3/驗收/P0_Plugin骨架佈署步驟.md
@@ -97,12 +97,16 @@ JSON 中的反斜線必須寫**兩條** `\\`，解析後才是一條；只寫一
 
 **已完成（使用者 2026-11-04 回報）：** 前置確認、第 1 步追蹤設為「全部」並儲存、第 2 步建置與打包檢查，以及第 3 步清單改為 true 和 plan 審核。plan 只列出 CreateAssembly／CreateType／CreateStep，符合本次 trace 骨架的預期。
 
-**目前卡在第 4 步：** 使用者在第二份 plan 審核後再次執行 apply，回報 `Dataverse fault code[0]: 0x80040216`、`Dataverse fault code[1]: 0x80040216`，以及 ``Command failed (FaultException`1). Sensitive details are not shown.``。已取得實際數字錯誤碼，但伺服器根因仍未明；verify、smoke 與停用收尾皆未完成。Codex 僅在本機檢查程式與安全診斷，不連線 D365／LINE。
+**目前卡在第 4 步：** 第二份 plan 的 apply 回報兩層 `0x80040216`。使用者隨後核定由 Codex 操作已開啟的 D365-9.1 虛擬機查看紀錄；已在 2026-11-04 台灣時間 14:16:19 的 `MSCRMWebService` 事件 18176，確認故障位於 `pluginassembly` 的 Create／Stage 30：`System.FormatException` 發生在 `TargetFrameworkVersionValidator.ValidateInternal()`，呼叫鏈包含 `PluginValidatorBase.Validate()` 與 `ValidateAssemblyMetadata(...)`。已定位目標框架驗證階段，但尚未取得伺服器可接受的框架版本上限；verify、smoke 與停用收尾皆未完成。
 
-**下一小步：** 先確認使用者能否登入**安裝 Dynamics 365 的伺服器 Windows 桌面**（可使用遠端桌面）；只回覆能／不能登入，不提供位址、帳密或日誌全文。確認可登入後，再逐步查看本次 apply 時段（2026-11-04 台灣時間約 14:16）的既有伺服器錯誤紀錄；此時先不改伺服器設定、不重啟服務，也不再次執行 apply。
+**已完成的唯讀查核：** Codex 經使用者核定直接查看虛擬機 GUI 的上述事件，以及 `Microsoft.Crm.ObjectModel.dll` 的詳細資料，版本為 `9.1.49.21825`。續查 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll` 的詳細資料，檔案與產品版本均為 **4.8.4795.0**；這證明該檔案存在，不能直接當成沙箱程序實際載入版本或第 5 步 smoke 的驗收結果。本次沒有產品程式修改，也沒有執行新的 plan／apply。
+
+**下一小步：** 依原計畫 D21 的 `TargetFramework=net48` 邊界，先由使用者決定是否核定評估並採用 4.6.2 相容建置；若維持 net48，則須繼續核對此 D365 版本的相容性與支援更新。兩個方向均未執行，不自行降低框架、更新伺服器或繞過版本驗證。已見 4.8 核心檔案，因此不把失敗歸因為未安裝 .NET 4.8；目前也尚未證明 4.6.2 相容建置可成功註冊。不需要使用者再次操作畫面來重找這筆事件。
 
 兩個相同的數字碼沒有指出失敗的是哪一項建立操作，也不能證明沒有部分寫入。後續若需重試，先重新 plan 並審核目前狀態，不重用失敗的 plan，也不要在 apply 後重新以「最新目錄」改寫 `$plan`。
 
+**途中提交（2026-11-04）：** 使用者手動提交 `714465fd` 已核對並保留；清單的唯一差異為 goodshepherdback 從 false 改為 true，產品程式未變更，其餘為已驗證的診斷任務／文件與審查紀錄。清單 true 已在 HEAD；第 6 步須另以新提交保存 false，不能再假設改回 false 後工作目錄會自動乾淨。不改寫原提交，目前尚未執行停用收尾。
+
 **紀錄原則（使用者 2026-11-04 要求）：** 每有修正、完成事項或新阻礙，都同步更新本文件的目前進度、對應步驟與文末結果表；離線測試通過不等於實機驗收通過。
 
 ## 1. 開啟 Plugin 追蹤
@@ -170,7 +174,7 @@ dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -
 
 **使用者執行結果（2026-11-04）：** 首次 apply 只有固定失敗摘要；數字診斷補修後，第二份 plan 的 apply 仍失敗，實際回報為 `Dataverse fault code[0]: 0x80040216`、`Dataverse fault code[1]: 0x80040216`，以及 ``Command failed (FaultException`1). Sensitive details are not shown.``。這兩個是本次實際代碼，不是範例。不能據此判定為權限、框架或其他特定原因，也不能排除部分寫入。verify 尚未執行；暫不進入 smoke。診斷只輸出最多四個八位十六進位數字碼，不輸出原始訊息、追蹤全文或秘密；本機驗證見下方。這只補足診斷，尚未修復或驗證伺服器端拒絕原因。
 
-**錯誤碼判讀與下一項證據：** Microsoft 官方將 `0x80040216`（`-2147220970`）列為 `UnExpected`，訊息為「An unexpected error occurred.」；這是通用錯誤，仍需伺服器端的具體錯誤紀錄。先依 §0.3 確認使用者可否登入 D365 伺服器桌面，再引導查看既有紀錄；不預先要求開啟額外追蹤或修改登錄。原始日誌可能包含機密或個資，不整份貼出或加入 repo。依據：[Microsoft：Web service error codes](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/web-service-error-codes)、[地端伺服器監控與疑難排解](https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/deploy/monitor-and-troubleshoot-dynamics-365?view=op-9-1)。
+**錯誤碼判讀與已取得的證據：** Microsoft 官方將 `0x80040216`（`-2147220970`）列為 `UnExpected`，屬通用錯誤；§0.3 已補入伺服器事件的時間、代碼、例外型別、驗證方法與元件版本，只記錄必要診斷，不保存事件全文、機器識別值或資料內容。目前官方地端文件已列出 Plugin 可用 4.6.2～4.8，但未在該段指定所需伺服器更新版本，不能據此斷言現場的 `9.1.49.21825` 必定接受 net48。依據：[Microsoft：Web service error codes](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/web-service-error-codes)、[地端 Plugin 與 .NET Framework 支援](https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/visual-studio-dot-net-framework?view=op-9-1)、[D21 原定目標](../00_總覽與執行規則.md)。
 
 **診斷補修的本機驗證（2026-11-04，Codex 離線執行）：**
 
@@ -179,7 +183,7 @@ dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -
 - 本次補修另經自癒入口分析與審查；兩次皆為 `reviewMode=dual`、`dualModelComplete=true`、`quotaBlocked=false`，沒有未處理的 Critical／High。這是本次補修的新審查，不冒稱由先前階段 0 審查涵蓋。
 - 本機證據位於 `artifacts/p013-apply-fault-tests/`；全方案各測試組的完整摘要保留於 `validation/release-test.log`。這些結果不等於 apply、verify 或 smoke 已通過。
 
-**目前先不重試：** 先取得伺服器端的具體診斷，再決定修法。後續若經判定需要新一輪 plan／apply，失敗時仍只回報數字行與固定失敗摘要，不貼 Exception.log 全文、追蹤全文、帳密或任何設定內容。
+**目前先不重試：** 已取得目標框架驗證器的具體故障位置，待 D21 相容性處置核定與修正驗證後，再重新 plan／審核。失敗時仍只回報數字行與固定失敗摘要，不貼 Exception.log 全文、追蹤全文、帳密或任何設定內容。
 
 **不對時怎麼辦：** 連線、權限、簽章或沙箱錯誤就停下回報並做第 6 步，不改回手動註冊。trace 的 verify=1 或 3 都未通過，不能略過。
 
@@ -232,7 +236,7 @@ dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -
 git status --short -- .\d365\registration\plugins.registration.json
 ```
 
-預期沒有輸出，表示此檔沒有已暫存或未暫存的未提交變更。若有輸出，先核對清單是否確實恢復原設定；不要為了消除輸出而提交 true 的版本，也不要覆寫不屬於本次驗收的修改。
+預期沒有輸出，表示此檔沒有已暫存或未暫存的未提交變更。**本次例外：** 使用者的 `714465fd` 已提交 true，因此改回 false 時會先出現修改；完成伺服器停用與收尾 verify=0 後，須核對只有本次預期的 false 修改，再以**新提交**保存 false，最後重跑這項檢查。保留原提交與歷史，不為消除輸出而還原成 true，也不覆寫其他修改。
 
 **預期看到什麼：**
 
@@ -249,7 +253,7 @@ git status --short -- .\d365\registration\plugins.registration.json
 | 第 0 步前置與準備 | 使用者已回報舊步驟／流程停用、3 筆作業取消、金鑰存在及 User Secrets 儲存；舊 token 與會友 LINE ID 核定保留，免除本次清理前置 |
 | 建置與打包檢查 | 已由使用者執行通過；Deployment 有 1 個 CS1572 警告，詳第 2 步 |
 | Plugin 追蹤設定 | 使用者已設為全部並儲存；收尾復原尚未完成 |
-| 一輪 plan／apply／verify=0 | 第二份 plan 已審核並執行 apply，仍失敗：fault code[0]、[1] 均為 `0x80040216`；根因未明，待確認伺服器診斷方式，verify 未執行 |
+| 一輪 plan／apply／verify=0 | 第二份 plan 的 apply 仍失敗：兩層 `0x80040216`；Codex 經使用者核定查得事件 18176，故障位於目標框架驗證器；伺服器元件 `9.1.49.21825`、核心檔案 `4.8.4795.0`；待 D21 相容性處置核定，verify 未執行 |
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