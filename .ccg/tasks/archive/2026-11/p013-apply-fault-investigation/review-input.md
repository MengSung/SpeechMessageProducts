# P0-13 安全數字診斷補修審查

只做唯讀審查，不修改檔案，不跑測試（主程序正在跑完整驗證），不連線 D365／LINE 或其他網路，不讀設定、User Secrets、金鑰、授權或其他 worktree。

背景：使用者按驗收文件執行 plan 成功，但 apply 僅顯示 FaultException`1。伺服器根因尚不明。這次只補安全錯誤碼以取得診斷，不改 SDK 寫入、連線、重試、驗證、Plugin 或清單。

分析已經同入口雙模型完成（dualModelComplete=true, quotaBlocked=false）。下列補修維持 report(original) 先執行一次、原始租約與互斥鎖先 Dispose，回傳 1；只印 OrganizationServiceFault.ErrorCode，Int32 X8 InvariantCulture，最多四節點有界迭代（含最外層；循環也最多四次）；不讀或序列化任何文字欄位。不需要 HashSet 因為迭代有硬上限。缺失 Detail 不印數字。補充診斷獨立 try/catch，不妨礙原回報。

測試：原 46 例保留，新增 6 例；新增先 5 失敗／1 通過（缺失 Detail 為相容性案例），補修後 52／52 通過。假的 SDK fault 與內層 Message、TraceText、ErrorDetails 鍵值、FaultReason 都含合成秘密；同一原始 Exception 只回報一次、reporter 故障仍失敗及可得數字碼、租約先釋放、四層深度與 cycle、正負整數與零的八位格式，以及真正共用日誌不含假秘密。

驗收文件同步記錄使用者已完成事項、apply 未通過、尚未 verify／smoke。不得宣稱數字診斷修補等同實機成功。後續只由使用者重新 plan 審核後 apply，Codex 不連線。

請只列實際可定位的 Critical／High／Medium／Low；區分真實問題與未採用的危險寫法，不要把假設性警告當作現存缺陷。特別檢查秘密外洩、資源釋放、例外責任、有界處理、測試是否有假綠燈，以及文件狀態是否失實。輸出明確結論與檔案位置。

## 完整本次差異（排除使用者 true 清單與原始模型產物）

```diff
diff --git a/.trellis/spec/backend/deployment-contract.md b/.trellis/spec/backend/deployment-contract.md
index 65e8339c..0ec95e6f 100644
--- a/.trellis/spec/backend/deployment-contract.md
+++ b/.trellis/spec/backend/deployment-contract.md
@@ -22,6 +22,18 @@ settings use presence bits or hashes. Never export trace text, token or userId.
 Report real exceptions through the shared diagnostic owner, which writes only
 safe type/stack metadata; never print raw exception messages or CLI values.
 
+P0-13 diagnostic correction (2026-11-04): after reporting the original exception
+once, the CLI may print only `ErrorCode` from a typed
+`FaultException<OrganizationServiceFault>` and its SDK `InnerFault` chain. Format
+each Int32 as eight uppercase hexadecimal digits with invariant culture, and
+iterate at most four nodes including the outer detail, even for a cyclic chain.
+A missing detail prints no codes. Never read or serialize Message, Reason,
+TraceText, ErrorDetails, Data or raw stack for this supplemental output. Its
+failure must not prevent original reporting or change exit 1. Keep command-local
+lease disposal and caller-cancellation classification unchanged. Offline fault
+injection must cover secret-bearing fields, reporter failure, deep/cyclic chains,
+missing detail, release-before-report and actual shared-log redaction.
+
 Plan hashes cover manifest, DLL and current organization fingerprint. Apply
 revalidates these, exports and flushes the current state before writing. Stable
 step identities use type/message/entity/stage/mode/rank, and duplicate database
diff --git a/SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs b/SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs
index eb5b48ac..22b09d20 100644
--- a/SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs
+++ b/SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs
@@ -5,6 +5,7 @@ using System.IO;
 using System.Linq;
 using System.Reflection;
 using System.Security.Cryptography;
+using System.ServiceModel;
 using System.Threading;
 using System.Threading.Tasks;
 using Microsoft.CodeAnalysis;
@@ -656,6 +657,148 @@ public sealed class DeploymentContractTests
         Assert.DoesNotContain(Fixture.SecretValue, output.ToString(), StringComparison.Ordinal);
     }
 
+    /// <summary>
+    /// SDK fault 的各文字欄位及回報故障均注入假秘密；只有兩層數字碼可跨越輸出邊界。
+    /// 斷言原例外只上報一次、上報前租約已釋放，且回報失敗也不改變失敗碼或遺失數字診斷。
+    /// </summary>
+    /// <param name="reporterFails">是否讓既有共用回報入口在接收原例外後故障。</param>
+    [Theory(DisplayName = "Dataverse 失敗只顯示數字碼且保留原始回報與釋放")]
+    [InlineData(false)]
+    [InlineData(true)]
+    public void Cli_DataverseFault_ShowsOnlyCodesAndReportsOriginalOnce(bool reporterFails)
+    {
+        using var f = new Fixture();
+        var detail = CreateSensitiveFault(unchecked((int)0x80040220),
+            CreateSensitiveFault(unchecked((int)0x80040217)));
+        var failure = new FaultException<OrganizationServiceFault>(detail, new FaultReason(Fixture.SecretValue));
+        f.Service.RetrieveFailure = failure;
+        var connector = new TrackingConnector(f.Service);
+        using var output = new StringWriter(CultureInfo.InvariantCulture);
+        var reportCount = 0;
+        Exception? reported = null;
+        var disposedBeforeReport = false;
+
+        var exitCode = DeploymentCli.Run(
+            new[] { "export", "--org", Fixture.Organization },
+            connector,
+            output,
+            exception =>
+            {
+                reportCount++;
+                reported = exception;
+                disposedBeforeReport = connector.DisposeCount == 1;
+                if (reporterFails) throw new InvalidOperationException(Fixture.SecretValue);
+            },
+            artifactRoot: f.Root);
+
+        Assert.Equal(1, exitCode);
+        Assert.Equal(1, reportCount);
+        Assert.Same(failure, reported);
+        Assert.True(disposedBeforeReport);
+        Assert.Equal(1, connector.ConnectCount);
+        Assert.Equal(1, connector.DisposeCount);
+        Assert.Empty(f.Service.Writes);
+        var text = output.ToString();
+        Assert.Contains("Dataverse fault code[0]: 0x80040220", text, StringComparison.Ordinal);
+        Assert.Contains("Dataverse fault code[1]: 0x80040217", text, StringComparison.Ordinal);
+        Assert.Contains("Sensitive details are not shown.", text, StringComparison.Ordinal);
+        Assert.DoesNotContain(Fixture.SecretValue, text, StringComparison.Ordinal);
+        if (reporterFails)
+            Assert.Contains("Diagnostic reporting was unavailable.", text, StringComparison.Ordinal);
+    }
+
+    /// <summary>
+    /// 注入五層 fault 或回指第一層的循環；輸出至多四個固定八位數字碼，不可無界走訪。
+    /// 人工列出的正負整數期待值同時保護符號位、零值與前導零，案例結束必須釋放唯一租約。
+    /// </summary>
+    /// <param name="cyclic">是否在第四層後回指第一層，取代第五層。</param>
+    [Theory(DisplayName = "Dataverse 內層與循環 fault 最多輸出四碼")]
+    [InlineData(false)]
+    [InlineData(true)]
+    public void Cli_DataverseFault_NestedOrCyclicChain_IsBounded(bool cyclic)
+    {
+        using var f = new Fixture();
+        var fourth = CreateSensitiveFault(int.MaxValue, CreateSensitiveFault(42));
+        var first = CreateSensitiveFault(int.MinValue,
+            CreateSensitiveFault(0, CreateSensitiveFault(1, fourth)));
+        if (cyclic) fourth.InnerFault = first;
+        f.Service.RetrieveFailure = new FaultException<OrganizationServiceFault>(first);
+        var connector = new TrackingConnector(f.Service);
+        using var output = new StringWriter(CultureInfo.InvariantCulture);
+
+        var exitCode = DeploymentCli.Run(
+            new[] { "export", "--org", Fixture.Organization },
+            connector, output, _ => { }, artifactRoot: f.Root);
+
+        Assert.Equal(1, exitCode);
+        var lines = output.ToString().Split(Environment.NewLine)
+            .Where(line => line.StartsWith("Dataverse fault code[", StringComparison.Ordinal)).ToArray();
+        Assert.Collection(lines,
+            line => Assert.Equal("Dataverse fault code[0]: 0x80000000", line),
+            line => Assert.Equal("Dataverse fault code[1]: 0x00000000", line),
+            line => Assert.Equal("Dataverse fault code[2]: 0x00000001", line),
+            line => Assert.Equal("Dataverse fault code[3]: 0x7FFFFFFF", line));
+        Assert.DoesNotContain(Fixture.SecretValue, output.ToString(), StringComparison.Ordinal);
+        Assert.Equal(1, connector.DisposeCount);
+    }
+
+    /// <summary>缺失 Detail 的 SDK fault 仍須上報原例外並釋放租約；不能為取得數字診斷再拋空參考例外。</summary>
+    [Fact(DisplayName = "Dataverse fault 沒有 Detail 時維持安全失敗")]
+    public void Cli_DataverseFault_NullDetail_PreservesFailure()
+    {
+        using var f = new Fixture();
+        var failure = new FaultException<OrganizationServiceFault>(null!, new FaultReason(Fixture.SecretValue));
+        f.Service.RetrieveFailure = failure;
+        var connector = new TrackingConnector(f.Service);
+        using var output = new StringWriter(CultureInfo.InvariantCulture);
+        var reports = new List<Exception>();
+
+        Assert.Equal(1, DeploymentCli.Run(
+            new[] { "export", "--org", Fixture.Organization },
+            connector, output, reports.Add, artifactRoot: f.Root));
+
+        Assert.Same(failure, Assert.Single(reports));
+        Assert.Equal(1, connector.DisposeCount);
+        Assert.Contains("Sensitive details are not shown.", output.ToString(), StringComparison.Ordinal);
+        Assert.DoesNotContain("Dataverse fault code[", output.ToString(), StringComparison.Ordinal);
+        Assert.DoesNotContain(Fixture.SecretValue, output.ToString(), StringComparison.Ordinal);
+    }
+
+    /// <summary>
+    /// 經真正程序診斷 owner 注入含假秘密的 SDK fault，檢查 stdout 與全部暫存日誌。
+    /// 同時要求數字碼可見及共用日誌已建立，防止以跳過回報或完全隱藏診斷取得假綠燈。
+    /// </summary>
+    [Fact(DisplayName = "Dataverse fault 的控制台與共用日誌皆不含秘密")]
+    public void Logs_DataverseFault_NeverContainSecretValues()
+    {
+        var run = typeof(Program).GetMethod("RunCommand", BindingFlags.NonPublic | BindingFlags.Static);
+        Assert.NotNull(run);
+        using var f = new Fixture();
+        f.Service.RetrieveFailure = new FaultException<OrganizationServiceFault>(
+            CreateSensitiveFault(unchecked((int)0x80040220), CreateSensitiveFault(1)),
+            new FaultReason(Fixture.SecretValue));
+        var connector = new TrackingConnector(f.Service);
+        var configuration = CreateConnectorConfiguration();
+        using var configurationOwner = (IDisposable)configuration;
+        using var output = new StringWriter(CultureInfo.InvariantCulture);
+
+        var exitCode = (int)run!.Invoke(null, new object?[]
+        {
+            new[] { "export", "--org", Fixture.Organization },
+            connector, output, configuration, Path.Combine(f.Root, "Logs"), CancellationToken.None
+        })!;
+
+        Assert.Equal(1, exitCode);
+        Assert.Equal(1, connector.DisposeCount);
+        Assert.False(ExceptionReporting.IsActive);
+        Assert.True(File.Exists(Path.Combine(f.Root, "Logs", "Exception.log")));
+        var combined = string.Join(Environment.NewLine,
+            Directory.EnumerateFiles(f.Root, "*", SearchOption.AllDirectories)
+                .Select(File.ReadAllText).Append(output.ToString()));
+        Assert.DoesNotContain(Fixture.SecretValue, combined, StringComparison.Ordinal);
+        Assert.Contains("Dataverse fault code[0]: 0x80040220", output.ToString(), StringComparison.Ordinal);
+    }
+
     /// <summary>plan 不讀取佔位；apply 才把假值送入假服務，備份與輸出仍不得保存該值。</summary>
     [Fact(DisplayName = "佔位秘密只在套用時解析且不落入輸出")]
     public void Apply_SecretPlaceholdersResolvedOnlyForWrites()
@@ -741,6 +884,23 @@ public sealed class DeploymentContractTests
         Assert.Equal(f.Manifest.Steps[0].Type, Assert.Single(image.PluginTypes));
     }
 
+    /// <summary>
+    /// 建立只屬當前案例的 SDK fault；各文字與字典鍵值都注入假秘密，不保留真實連線資訊。
+    /// 只允許其數字碼輸出；InnerFault 可供深度與循環故障注入，不涉及任何背景資源。
+    /// </summary>
+    /// <param name="errorCode">人工指定、可獨立核對格式的數字碼。</param>
+    /// <param name="innerFault">本案例的內層 fault；不跨案例共用。</param>
+    /// <returns>含多個敏感文字欄位的合成 fault。</returns>
+    private static OrganizationServiceFault CreateSensitiveFault(int errorCode, OrganizationServiceFault? innerFault = null) =>
+        new()
+        {
+            ErrorCode = errorCode,
+            Message = Fixture.SecretValue,
+            TraceText = Fixture.SecretValue,
+            ErrorDetails = { [Fixture.SecretValue] = Fixture.SecretValue },
+            InnerFault = innerFault
+        };
+
     /// <summary>
     /// 建立只含合成 connector 設定的記憶體組態；所有憑證樣本均為假值，
     /// 不讀取 User Secrets、環境變數或任何 D365 組織。
diff --git a/SpeechMessage.Dynamics.Deployment/DeploymentCli.cs b/SpeechMessage.Dynamics.Deployment/DeploymentCli.cs
index f427da74..3a70489e 100644
--- a/SpeechMessage.Dynamics.Deployment/DeploymentCli.cs
+++ b/SpeechMessage.Dynamics.Deployment/DeploymentCli.cs
@@ -1,9 +1,11 @@
 using System;
 using System.Globalization;
 using System.IO;
+using System.ServiceModel;
 using System.Text.Json;
 using System.Threading;
 using System.Threading.Tasks;
+using Microsoft.Xrm.Sdk;
 
 namespace SpeechMessage.Dynamics.Deployment;
 
@@ -172,11 +174,43 @@ internal static class DeploymentCli
                 WriteFixedError(output);
             }
 
+            WriteDataverseFaultCodes(exception, output);
             output.WriteLine("Command failed (" + exception.GetType().Name + "). Sensitive details are not shown.");
             return 1;
         }
     }
 
+    /// <summary>
+    /// 原例外交給共用回報入口後，僅補充 SDK fault 的數字碼；不讀取任何訊息、Reason、
+    /// TraceText、ErrorDetails 或 Data。最多迭代四個節點，循環鏈也會停止，不跨命令保留 fault。
+    /// 此補充輸出失敗不覆蓋原例外、不遞迴回報，也不改變命令的失敗結果或既有資源釋放順序。
+    /// </summary>
+    /// <param name="exception">已交給共用回報入口的原始例外；只辨識 SDK 的強型別 fault。</param>
+    /// <param name="output">由呼叫端持有的摘要 writer，本方法不取得其所有權。</param>
+    private static void WriteDataverseFaultCodes(Exception exception, TextWriter output)
+    {
+        if (exception is not FaultException<OrganizationServiceFault> fault)
+        {
+            return;
+        }
+
+        try
+        {
+            var current = fault.Detail;
+            for (var index = 0; index < 4 && current is not null; index++)
+            {
+                // 在 Int32 本身格式化，負數保持八位而不因提升成 Int64 產生符號延伸。
+                output.WriteLine("Dataverse fault code[" + index.ToString(CultureInfo.InvariantCulture) +
+                    "]: 0x" + current.ErrorCode.ToString("X8", CultureInfo.InvariantCulture));
+                current = current.InnerFault;
+            }
+        }
+        catch
+        {
+            // 原始失敗已先上報；補充診斷不可另起告警或中斷既有固定失敗摘要。
+        }
+    }
+
     /// <summary>
     /// 將規劃結果轉成 verify 的固定輸出與程序結束碼；純輸出步驟不持有服務、秘密或可變跨命令狀態。
     /// 與 CLI 共用同一實作，離線測試可以直接用記憶體組織的實際計畫檢查輸出契約。
diff --git "a/\346\226\207\344\273\266/\350\246\217\345\212\203\350\250\255\350\250\210\346\233\270/\345\257\246\344\275\234\350\250\210\347\225\253-3.3/\351\251\227\346\224\266/P0_Plugin\351\252\250\346\236\266\344\275\210\347\275\262\346\255\245\351\251\237.md" "b/\346\226\207\344\273\266/\350\246\217\345\212\203\350\250\255\350\250\210\346\233\270/\345\257\246\344\275\234\350\250\210\347\225\253-3.3/\351\251\227\346\224\266/P0_Plugin\351\252\250\346\236\266\344\275\210\347\275\262\346\255\245\351\251\237.md"
index ce1df6eb..61a1f9bc 100644
--- "a/\346\226\207\344\273\266/\350\246\217\345\212\203\350\250\255\350\250\210\346\233\270/\345\257\246\344\275\234\350\250\210\347\225\253-3.3/\351\251\227\346\224\266/P0_Plugin\351\252\250\346\236\266\344\275\210\347\275\262\346\255\245\351\251\237.md"
+++ "b/\346\226\207\344\273\266/\350\246\217\345\212\203\350\250\255\350\250\210\346\233\270/\345\257\246\344\275\234\350\250\210\347\225\253-3.3/\351\251\227\346\224\266/P0_Plugin\351\252\250\346\236\266\344\275\210\347\275\262\346\255\245\351\251\237.md"
@@ -35,7 +35,7 @@ Test-Path $env:SPEECHMESSAGE_PLUGIN_KEYFILE
 
 已開著的 PowerShell 視窗與 Visual Studio 看不到後來新增的環境變數；請重新開啟。
 
-- [x] 使用者已在 **SpeechMessage.Dynamics.Deployment** 的「管理使用者密碼」儲存以下三個值；內容未由 Codex 讀取，尚未驗證登入成功。
+- [x] 使用者已在 **SpeechMessage.Dynamics.Deployment** 的「管理使用者密碼」儲存以下三個值；內容未由 Codex 讀取。後續第 3 步 plan 已成功讀取組織狀態，但不代表具有註冊 Plugin 的寫入權限。
 
 ```json
 {
@@ -95,18 +95,22 @@ JSON 中的反斜線必須寫**兩條** `\\`，解析後才是一條；只寫一
 
 ### 0.3 目前進度與下一小步
 
-**已完成：** 使用者已確認金鑰存在、儲存 Deployment User Secrets，並完成第 2 步的三個離線命令；本次資料清理前置已核定免除。接著進入第 1 步追蹤設定，尚未執行部署或 smoke。
+**已完成（使用者 2026-11-04 回報）：** 前置確認、第 1 步追蹤設為「全部」並儲存、第 2 步建置與打包檢查，以及第 3 步清單改為 true 和 plan 審核。plan 只列出 CreateAssembly／CreateType／CreateStep，符合本次 trace 骨架的預期。
 
-**現在只做：** 回到 goodshepherdback 主畫面，點「設定 → 系統管理」，先停在該畫面；回報「已開啟系統管理」後，再繼續找到系統設定中的 Plugin 追蹤選項。
+**目前卡在第 4 步：** 使用者執行 apply，回報 ``Command failed (FaultException`1). Sensitive details are not shown.``。尚未取得 Dataverse 數字錯誤碼，伺服器根因未明；verify、smoke 與停用收尾皆未完成。Codex 僅在本機檢查程式與安全診斷，不連線 D365／LINE。
 
-**不對時怎麼辦：** 找不到「系統管理」就停下，回報看見的選單名稱；如需截圖，先遮住帳號與個資。追蹤設定完成後，再依第 3 步進行 plan。
+**下一小步：** 先補足只顯示數字錯誤碼的診斷並通過離線測試，再由使用者重建 Deployment、重新 plan 並審核。apply 可能已完成部分寫入，不能直接重用舊 plan，也不能將此次失敗記為完全沒有寫入；收到新 plan 後再決定下一步。
+
+**紀錄原則（使用者 2026-11-04 要求）：** 每有修正、完成事項或新阻礙，都同步更新本文件的目前進度、對應步驟與文末結果表；離線測試通過不等於實機驗收通過。
 
 ## 1. 開啟 Plugin 追蹤
 
-**做什麼：** 在 goodshepherdback「設定 → 系統管理 → 系統設定 → 自訂」，把「啟用外掛程式追蹤記錄」設為「全部」。
+**做什麼：** 在 goodshepherdback 開啟「設定」，於「系統」區塊點「管理 → 系統設定 → 自訂」，把「啟用外掛程式追蹤記錄」設為「全部」。實際選單名稱是「管理」。
 
 **預期看到什麼：** 儲存成功；稍後可在「Plugin 追蹤記錄」與「系統作業」查看本次執行。階段 0～4 的 Plugin 失敗由這兩處查看。
 
+**使用者執行結果（2026-11-04）：** 已設為「全部」並儲存；驗收收尾時仍須改回「例外狀況」。
+
 **不對時怎麼辦：** 無權限或找不到選項就停下回報，請管理員確認；驗收後改回「例外狀況」。
 
 ## 2. 建置與打包檢查
@@ -145,6 +149,8 @@ plan 成功後，**立刻執行取得 `$plan` 與開啟 plan.md 的兩行，在
 
 **預期看到什麼：** 全新安裝只列 CreateAssembly／CreateType／CreateStep，沒有秘密佔位。CreateStep 會直接建立為啟用狀態，不必先停用註冊再跑第二輪；trace 不發出 LINE HTTP 請求。若已安裝同一骨架，可能只有啟用狀態更新或無差異，須確認為預期的同一個測試步驟。
 
+**使用者執行結果（2026-11-04）：** 清單已改為 true 並儲存；plan 成功，只列 CreateAssembly、DiagnosticsPingPlugin 的 CreateType，以及 Create／task／PostOperation／Asynchronous／rank 1 的 CreateStep，秘密佔位為無。此為首次 plan 結果；第 4 步 apply 失敗後須重新 plan，不保證新 plan 仍為三項建立。
+
 **不對時怎麼辦：** 出現其他組織、步驟、push 模式或未預期更新就不套用，回報操作種類。不得手改 plan hash；清單或 DLL 改過就重新 plan。
 
 ## 4. 套用並確認 verify=0
@@ -158,6 +164,8 @@ dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -
 
 **預期看到什麼：** apply 結束碼 0、測試步驟已註冊並啟用；verify 結束碼 **0**、無差異。apply 先在**該次 apply 目錄**保存 export.json；留在本機 artifacts，不上傳。
 
+**使用者執行結果（2026-11-04）：** 首次 apply 回報 ``Command failed (FaultException`1). Sensitive details are not shown.``，尚未通過。現有安全日誌未包含 SDK 的數字錯誤碼，不能據此判定為權限、框架或其他伺服器原因，也不能排除部分寫入。verify 尚未執行；暫不進入 smoke。診斷補修已加入：SDK fault 只額外顯示最多四個八位十六進位錯誤碼（例如 `Dataverse fault code[0]: 0x80040220`，此處僅為格式示例，並非本次伺服器實際代碼）。不輸出原始訊息、追蹤全文或秘密。離線先紅後綠：新增案例原先 5 失敗、1 通過，補修後 Deployment 全套 52／52 通過；完整 §8 與修正審查結果另行補記。這只補足診斷，尚未修復或驗證伺服器端拒絕原因。
+
 **不對時怎麼辦：** 連線、權限、簽章或沙箱錯誤就停下回報並做第 6 步，不改回手動註冊。trace 的 verify=1 或 3 都未通過，不能略過。
 
 ## 5. smoke trace，人工看五項追蹤
@@ -225,8 +233,8 @@ git status --short -- .\d365\registration\plugins.registration.json
 | --- | --- |
 | 第 0 步前置與準備 | 使用者已回報舊步驟／流程停用、3 筆作業取消、金鑰存在及 User Secrets 儲存；舊 token 與會友 LINE ID 核定保留，免除本次清理前置 |
 | 建置與打包檢查 | 已由使用者執行通過；Deployment 有 1 個 CS1572 警告，詳第 2 步 |
-| Plugin 追蹤設定 | 待確認 |
-| 一輪 plan／apply／verify=0 | 未驗證 |
+| Plugin 追蹤設定 | 使用者已設為全部並儲存；收尾復原尚未完成 |
+| 一輪 plan／apply／verify=0 | 首次 plan 符合預期；apply 回報 FaultException`1，根因待取得數字錯誤碼；verify 未執行 |
 | trace smoke 與五項人工追蹤 | 未驗證 |
 | 停用、收尾 verify=0、追蹤復原及資料清除 | 未驗證 |
 

```
