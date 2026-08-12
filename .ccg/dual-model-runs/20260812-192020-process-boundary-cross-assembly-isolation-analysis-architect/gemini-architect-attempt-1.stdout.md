## 1. Analysis (架構評估)

當前測試基礎設施在並行執行多個 xUnit 測試程序集（Test Assemblies）時，面臨跨進程資源干擾的問題。具體而言：
- `ChurchReport.MemberInfo.Tests` 中的 `FeatureDisabledDynamicsProcessBoundaryTests` 會在啟動前後掃描作業系統中的 Dynamics 邊界程序（如 `SpeechMessage.Dynamics.WorkerTestHost`），以驗證在停用 Dynamics 功能時，系統不會建立任何 Dynamics 相關程序。
- 同時，`SpeechMessage.Dynamics.Tests` 中的多個測試類別（如 `OfficialWorkerSoakAndPerformanceTests`）在執行時會合法地啟動 `WorkerTestHost`。
- 由於 xUnit 的 `[Collection]` 隔離機制僅在**單一測試程序集內**生效，當整個方案（Solution）並行執行時，這兩個程序集的測試會同時運行，導致 ChurchReport 測試掃描到 Dynamics 測試啟動的 `WorkerTestHost`，從而產生 False Positive 誤判失敗。

---

## 2. Architecture Decision (架構決策)

### 決策：採用基於 `FileShare.None` 的跨進程租約鎖定（Interprocess Lease Lock）
我們選擇在兩個測試程序集之間建立一個共享的、基於檔案系統的獨佔鎖定機制，並將所有相關測試類別歸入同一個共享的 xUnit Collection。

#### 關鍵設計與原理：
1. **排除具名 Mutex/Semaphore (Rejected Alternative)**：
   - *原因*：.NET 中的具名 `Mutex` 具有執行緒親和性（Thread-Affinity），釋放鎖定的執行緒必須與獲取鎖定的執行緒相同。然而，xUnit 的 Fixture 生命週期（建構子與 `Dispose`）經常在不同的執行緒上執行，使用 `Mutex` 會導致 `ApplicationException`。
2. **採用 `FileStream` 搭配 `FileShare.None`**：
   - *優勢*：檔案鎖定由作業系統核心（OS Kernel）強制執行，且**不具備執行緒親和性**。任何執行緒都可以安全地處置 `FileStream` 來釋放鎖定。
3. **異常中斷復原力 (Abort Resilience)**：
   - *優勢*：若測試主機（testhost）異常崩潰或被強制終止，作業系統會自動關閉該程序持有的所有檔案控制代碼（File Handles），從而立即釋放鎖定，避免死鎖（Deadlock）。
4. **有界輪詢 (Bounded Polling)**：
   - *優勢*：在獲取鎖定時設定最大逾時時間（如 60 秒），逾時後拋出明確的 `TimeoutException`，防止 CI/CD 管道無限期掛起。

---

## 3. Implementation Plan (實作計畫)

1. **建立共享 Fixture 原始碼**：在 `SpeechMessage.Dynamics.Tests` 中建立 `Shared/WorkerTestHostProcessBoundaryFixture.cs`。
2. **專案檔連結 (Source Link)**：在 `ChurchReport.MemberInfo.Tests.csproj` 中以 `<Compile Include="..." Link="..." />` 方式引用該共享檔案。
3. **套用 Collection 屬性**：
   - 將 ChurchReport 的 `FeatureDisabledDynamicsProcessBoundaryTests` 改為套用 `[Collection(WorkerTestHostProcessBoundaryCollection.Name)]`。
   - 將 Dynamics 的 `OfficialWorkerSoakAndPerformanceTests`、`OfficialWorkerProfileExecutorTests`、`OfficialWorkerControlPlaneAdmissionTests`、`DedicatedGatewayProcessBoundaryTests` 皆改為套用同一個 Collection。

### Unified Diff Patch

```diff
diff --git a/SpeechMessage.Dynamics.Tests/Shared/WorkerTestHostProcessBoundaryFixture.cs b/SpeechMessage.Dynamics.Tests/Shared/WorkerTestHostProcessBoundaryFixture.cs
new file mode 100644
index 0000000..1234567
--- /dev/null
+++ b/SpeechMessage.Dynamics.Tests/Shared/WorkerTestHostProcessBoundaryFixture.cs
@@ -0,0 +1,48 @@
+using System;
+using System.IO;
+using System.Threading;
+using Xunit;
+
+namespace SpeechMessage.Dynamics.Tests.Shared;
+
+public sealed class WorkerTestHostProcessBoundaryFixture : IDisposable
+{
+    private readonly FileStream _lockStream;
+    private const string LockFileName = "speechmessage-worker-testhost-process-boundary-v1.lock";
+    private static readonly TimeSpan AcquireTimeout = TimeSpan.FromSeconds(60);
+    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
+
+    public WorkerTestHostProcessBoundaryFixture()
+    {
+        var tempPath = Path.GetTempPath();
+        var lockFilePath = Path.Combine(tempPath, LockFileName);
+        var startTime = DateTime.UtcNow;
+
+        while (true)
+        {
+            try
+            {
+                _lockStream = new FileStream(
+                    lockFilePath,
+                    FileMode.OpenOrCreate,
+                    FileAccess.ReadWrite,
+                    FileShare.None,
+                    bufferSize: 1,
+                    FileOptions.DeleteOnClose);
+                break;
+            }
+            catch (IOException)
+            {
+                if (DateTime.UtcNow - startTime > AcquireTimeout)
+                {
+                    throw new TimeoutException(
+                        $"Failed to acquire interprocess lease on '{lockFilePath}' within {AcquireTimeout.TotalSeconds} seconds.");
+                }
+                Thread.Sleep(PollInterval);
+            }
+        }
+    }
+
+    public void Dispose()
+    {
+        _lockStream?.Dispose();
+    }
+}
+
+[CollectionDefinition(WorkerTestHostProcessBoundaryCollection.Name)]
+public sealed class WorkerTestHostProcessBoundaryCollection : ICollectionFixture<WorkerTestHostProcessBoundaryFixture>
+{
+    public const string Name = "WorkerTestHostProcessBoundaryCollection";
+}
diff --git a/ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj b/ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
--- a/ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
+++ b/ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
@@ -26,4 +26,7 @@
   </ItemGroup>
+  <ItemGroup>
+    <Compile Include="..\SpeechMessage.Dynamics.Tests\Shared\WorkerTestHostProcessBoundaryFixture.cs" Link="Shared\WorkerTestHostProcessBoundaryFixture.cs" />
+  </ItemGroup>
 </Project>
diff --git a/ChurchReport.MemberInfo.Tests/FeatureDisabledDynamicsProcessBoundaryTests.cs b/ChurchReport.MemberInfo.Tests/FeatureDisabledDynamicsProcessBoundaryTests.cs
--- a/ChurchReport.MemberInfo.Tests/FeatureDisabledDynamicsProcessBoundaryTests.cs
+++ b/ChurchReport.MemberInfo.Tests/FeatureDisabledDynamicsProcessBoundaryTests.cs
@@ -5,2 +5,3 @@
 using FluentAssertions;
+using SpeechMessage.Dynamics.Tests.Shared;
 using Xunit;
@@ -18,7 +19,2 @@
-[CollectionDefinition(Name, DisableParallelization = true)]
-public sealed class FeatureDisabledDynamicsProcessBoundaryCollection
-{
-    public const string Name = "Feature-disabled Dynamics process boundary";
-}

-[Collection(FeatureDisabledDynamicsProcessBoundaryCollection.Name)]
+[Collection(WorkerTestHostProcessBoundaryCollection.Name)]
 public sealed class FeatureDisabledDynamicsProcessBoundaryTests
diff --git a/SpeechMessage.Dynamics.Tests/OfficialWorkerSoakAndPerformanceTests.cs b/SpeechMessage.Dynamics.Tests/OfficialWorkerSoakAndPerformanceTests.cs
--- b/SpeechMessage.Dynamics.Tests/OfficialWorkerSoakAndPerformanceTests.cs
+++ b/SpeechMessage.Dynamics.Tests/OfficialWorkerSoakAndPerformanceTests.cs
@@ -8,2 +8,3 @@
 using Xunit.Abstractions;
+using SpeechMessage.Dynamics.Tests.Shared;

@@ -17,12 +18,2 @@
-internal static class OfficialWorkerSoakTestCollection
-{
-    internal const string Name = "Official worker soak and performance";
-}
-
-[CollectionDefinition(OfficialWorkerSoakTestCollection.Name, DisableParallelization = true)]
-public sealed class OfficialWorkerSoakTestCollectionDefinition
-{
-}

-[Collection(OfficialWorkerSoakTestCollection.Name)]
+[Collection(WorkerTestHostProcessBoundaryCollection.Name)]
 public sealed class OfficialWorkerSoakAndPerformanceTests
diff --git a/SpeechMessage.Dynamics.Tests/OfficialWorkerProfileExecutorTests.cs b/SpeechMessage.Dynamics.Tests/OfficialWorkerProfileExecutorTests.cs
--- a/SpeechMessage.Dynamics.Tests/OfficialWorkerProfileExecutorTests.cs
+++ b/SpeechMessage.Dynamics.Tests/OfficialWorkerProfileExecutorTests.cs
@@ -6,2 +6,3 @@
 using SpeechMessage.Dynamics.WorkerSupervisor;
+using SpeechMessage.Dynamics.Tests.Shared;

@@ -14,2 +15,3 @@
 /// </summary>
+[Collection(WorkerTestHostProcessBoundaryCollection.Name)]
 public sealed class OfficialWorkerProfileExecutorTests
diff --git a/SpeechMessage.Dynamics.Tests/OfficialWorkerControlPlaneAdmissionTests.cs b/SpeechMessage.Dynamics.Tests/OfficialWorkerControlPlaneAdmissionTests.cs
--- b/SpeechMessage.Dynamics.Tests/OfficialWorkerControlPlaneAdmissionTests.cs
+++ b/SpeechMessage.Dynamics.Tests/OfficialWorkerControlPlaneAdmissionTests.cs
@@ -10,2 +10,3 @@
 using SpeechMessage.Dynamics.WorkerSupervisor;
+using SpeechMessage.Dynamics.Tests.Shared;

@@ -19,2 +20,3 @@
 /// </summary>
+[Collection(WorkerTestHostProcessBoundaryCollection.Name)]
 public sealed class OfficialWorkerControlPlaneAdmissionTests
diff --git a/SpeechMessage.Dynamics.Tests/DedicatedGatewayProcessBoundaryTests.cs b/SpeechMessage.Dynamics.Tests/DedicatedGatewayProcessBoundaryTests.cs
--- a/SpeechMessage.Dynamics.Tests/DedicatedGatewayProcessBoundaryTests.cs
+++ b/SpeechMessage.Dynamics.Tests/DedicatedGatewayProcessBoundaryTests.cs
@@ -7,2 +7,3 @@
 using Xunit;
+using SpeechMessage.Dynamics.Tests.Shared;

@@ -18,8 +19,2 @@
-[CollectionDefinition(Name, DisableParallelization = true)]
-public sealed class DedicatedGatewayProcessBoundaryCollection
-{
-    public const string Name = "Dedicated Gateway process boundary";
-}

-[Collection(DedicatedGatewayProcessBoundaryCollection.Name)]
+[Collection(WorkerTestHostProcessBoundaryCollection.Name)]
 public sealed class DedicatedGatewayProcessBoundaryTests
```

---

## 4. Considerations (考量因素)

- **效能影響 (Performance Impact)**：
  由於引入了跨進程鎖定，原本可以並行執行的 Dynamics 測試與 ChurchReport 測試將會改為序列化執行。然而，這僅限於受保護的 5 個測試類別，其餘與 `WorkerTestHost` 無關的測試仍可保持完全並行，因此對整體測試套件的執行時間影響極小。
- **維護性 (Maintainability)**：
  使用 Source Link 方式共享 Fixture 原始碼，避免了程式碼重複，且所有鎖定邏輯皆封裝在 `WorkerTestHostProcessBoundaryFixture` 中，未來若需調整鎖定路徑或逾時時間，只需修改單一檔案。
- **安全性 (Security)**：
  鎖定檔案建立於系統臨時目錄（`%TEMP%`），且為 0 位元組的空檔案，不包含任何敏感資訊（如認證、路徑、PID 等），完全符合去識別化與安全規範。

---

## 5. Findings (審查發現)

### ⚠️ Warning: Thread-Affinity Risk with Mutex
- **位置**：`WorkerTestHostProcessBoundaryFixture.cs` (設計階段)
- **原因**：若在此類跨進程同步設計中誤用 .NET `Mutex`，會因為 xUnit 的非同步排程機制（建構子與 `Dispose` 可能在不同執行緒執行）而導致 `ApplicationException`。必須堅持使用 `FileStream` 搭配 `FileShare.None` 的無執行緒親和性設計。

### ⚠️ Warning: Deadlock Risk on Timeout
- **位置**：`WorkerTestHostProcessBoundaryFixture.cs` (設計階段)
- **原因**：若輪詢鎖定沒有設定合理的逾時上限（Acquire Timeout），在 CI/CD 環境中若發生鎖定洩漏，會導致整個測試管道無限期掛起。必須實作 bounded polling 與明確的 `TimeoutException`。

### ℹ️ Info: Minimal Test Set Scope
- **位置**：`SpeechMessage.Dynamics.Tests` 與 `ChurchReport.MemberInfo.Tests`
- **原因**：為了避免不必要的測試序列化降低並行效能，只有會建立 `WorkerTestHost` 的測試類別與要求零 `WorkerTestHost` 的 ChurchReport 測試類別需要加入此 collection。其他無關的測試應保持並行。
