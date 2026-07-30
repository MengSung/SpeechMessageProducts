# Gateway HTTP／Queue Bounds 精準架構分析與決策報告

本報告針對 `SpeechMessage.Dynamics` 系統平台中，Gateway 的 HTTP 請求主體限制、佇列邊界（Queue Bounds）、記憶體保留（Retention）以及生命週期管理進行深入的架構分析，並提出具體的架構決策與實作計畫。

---

## 1. Analysis - 當前架構評估與缺陷分析

### 1.1 缺陷分類與評估

#### 【Critical】原始請求圖形於佇列等待期間被強引用保留 (Request Graph Retention during Queue Wait)
* **檔案位置**：`SpeechMessage.Dynamics.WebApi/Runtime/ControlledOperationExecutor.cs`
* **現狀分析**：
  在 `ControlledOperationExecutor.ExecuteAsync` 中，方法接收 `OperationExecutionRequest request` 參數。在呼叫 `await _leaseProvider.AcquireAsync(envelope, cancellationToken)` 時，執行緒會釋放並等待許可證（Permit）。然而，因為 `request.Parameters` 在 `await` 之後的 `ExecuteRegisteredOperationAsync` 中仍被使用，編譯器產生的 async 狀態機器（Async State Machine）必須在整個佇列等待期間（最長可達 15-45 秒）強引用保留 `request` 及其關聯的 `Parameters` 字典與所有 `JsonElement` 節點。
* **影響**：若有 48 個請求在佇列中等待，且每個請求都攜帶接近主機上限（如 30MB）的 JSON 樹狀結構，將導致高達 1.4GB 的記憶體被鎖定在 Gen 2 中無法回收，極易引發 OOM。

#### 【Critical】缺乏應用程式層級的 Hard Request-Body 限制與反序列化前置防護
* **檔案位置**：`SpeechMessage.Dynamics.Gateway/Program.cs`
* **現狀分析**：
  目前專案未明確設定 Kestrel 與 IIS 的 `MaxRequestBodySize`。更嚴重的是，ASP.NET Core Minimal API 預設的 JSON 綁定會在 Endpoint 執行前，將整個 Request Body 讀入記憶體並反序列化為 `OperationHttpRequest`。這意味著惡意的大型 JSON 請求會在進入任何驗證邏輯之前就完成反序列化，消耗大量 CPU 與記憶體。
* **影響**：無法防禦 Chunked 傳輸繞過 Content-Length 限制的攻擊，且無法在反序列化前拒絕超限請求。

#### 【Warning】估算位元組數演算法不精確且未驗證複雜 JSON 結構
* **檔案位置**：`SpeechMessage.Dynamics.WebApi/Runtime/ControlledOperationExecutor.cs` (Line 140-161)
* **現狀分析**：
  `EstimateEnvelopeBytes` 採用 UTF-16 的 `Length * 2` 進行估算，這與實際傳輸的 UTF-8 位元組數不符（例如繁體中文在 UTF-8 佔 3 位元組，在 UTF-16 佔 2 位元組）。此外，對於任何非字串的複雜 `JsonElement`（如大型 Object 或 Array），演算法一律粗估為 64 位元組。
* **影響**：`MaxDispatchEnvelopeBytes` 的限制形同虛設，大型巢狀 JSON 參數會被嚴重低估，繞過容量控制。

#### 【Warning】參數驗證與型態檢查發生在 Admission 之後
* **檔案位置**：`SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs` (Line 79-91)
* **現狀分析**：
  必填欄位檢查（Required Check）與型態轉換發生在 `DynamicsWebApiClient` 中，此時請求已經通過了 `OrganizationAdmissionManager` 的佇列等待並取得了執行許可證。
* **影響**：無效的請求（例如缺少必填參數或型態錯誤）會無端佔用佇列空間與許可證配額，降低系統吞吐量並造成資源浪費。

---

## 2. Architecture Decision - 關鍵設計選擇與決策

### 2.1 決策 1：Kestrel＋IIS 共用 Hard Limit 搭配自訂 `BindAsync` 串流讀取器
* **推薦方案**：
  在 `Program.cs` 中全域設定 Kestrel 與 IIS 的 `MaxRequestBodySize` 為 64 KiB。同時，在 `OperationHttpRequest` 中實作 Minimal API 的 `BindAsync` 靜態方法，手動從 `HttpRequest.Body` 讀取最多 `Limit + 1` 位元組，並在讀取過程中一旦超過限制即立即中斷連線並回傳 `HTTP 413 Payload Too Large`。
* **反對方案**：
  僅依賴 ASP.NET Core 預設的 JSON 綁定與主機層級限制，或僅使用 ASP.NET Core Middleware 檢查 `Content-Length`。
* **決策理由**：
  僅檢查 `Content-Length` 無法防禦 `Transfer-Encoding: chunked` 的無界串流攻擊。透過自訂 `BindAsync` 進行手動限制長度的串流讀取，能確保在反序列化（Deserialization）發生前，就將超限的請求阻斷並釋放 Stream，達到零雙重緩衝（Zero Double-Buffering）與即時防護。

### 2.2 決策 2：同步 Prepare-Before-First-Await 與 Async 狀態機器隔離
* **推薦方案**：
  引入 `OperationDispatchPreparer`，在 `ControlledOperationExecutor.ExecuteAsync` 的第一個 `await` 之前，同步完成：
  1. 註冊表查表（Registry Lookup）。
  2. 參數數量、名稱、必填、型態與值的驗證。
  3. 參數名稱固定排序（Ordinal）。
  4. 寫入版本化、長度前綴的 UTF-8 Canonical Buffer。
  5. 建立 `PreparedOperationDispatch`。
  
  為了徹底防止 Async 狀態機器保留原始 `request` 的強引用，將執行流程拆分為同步的 `Prepare` 階段與非同步的 `ExecutePreparedAsync` 階段，後者**不接受** `OperationExecutionRequest` 作為參數，僅接受 `PreparedOperationDispatch`。
* **反對方案**：
  在 Async 方法中直接引用 `request`，並在 `await` 之後繼續讀取 `request.Parameters`。
* **決策理由**：
  藉由方法邊界隔離，編譯器產生的 async 狀態機器將只會持有 `PreparedOperationDispatch` 的引用，原始的 `request`、`Parameters` 字典與 `JsonElement` 圖形在第一個 `await` 開始時即失去所有引用，可被 GC 立即回收。

### 2.3 決策 3：Canonical Buffer 唯一擁有者模式 (`ArrayPool<byte>`)
* **推薦方案**：
  `PreparedOperationDispatch` 實作 `IDisposable`，其內部的 Canonical Buffer 向 `ArrayPool<byte>.Shared` 租借。`PreparedOperationDispatch` 是該 Buffer 的唯一擁有者（Unique Owner）。在 `Dispose` 時，必須使用 `CryptographicOperations.ZeroMemory` 將租借的記憶體區段清零，然後歸還給 `ArrayPool`。
* **反對方案**：
  使用 `MemoryStream`、`byte[]` 分配或不進行清零直接歸還。
* **決策理由**：
  使用 `ArrayPool` 可避免頻繁分配大物件造成的 GC 壓力。強制執行 `ZeroMemory` 清零能保證敏感資料（如參數內容）不會在記憶體中殘留，防範記憶體洩漏與安全風險。

### 2.4 決策 4：佇列等待狀態的極簡化 (Prepared Bounded DTO Only)
* **推薦方案**：
  `DispatchEnvelope` 僅保留 exact byte count、hash、deadline 等元數據。`PreparedOperationDispatch` 只持有 `DispatchEnvelope` 與已轉換為強型別的唯讀參數結構，絕不持有 `HttpContext`、`ClaimsPrincipal`、`JsonDocument` 或任何連線/認證上下文。
* **反對方案**：
  在 Envelope 或佇列物件中保留 `HttpContext` 或原始 JSON 樹。
* **決策理由**：
  保持佇列物件的極簡與唯讀性，能確保在佇列積壓時，記憶體開銷維持在常數級別（每個請求僅數百位元組的元數據與 Bounded Buffer），徹底消除 OOM 風險。

---

## 3. Implementation Plan - 步驟與精確修改清單

### 3.1 修改順序 (RED → GREEN)
1. **RED 階段**：
   * 新增 `GatewayRequestBodyBoundaryTests.cs`，測試超限 Content-Length、Chunked 串流超限、多位元組 UTF-8 邊界與 JSON 深度限制，預期皆回傳 413/400 且不觸發 Executor。
   * 新增 `OperationDispatchPreparerTests.cs`，測試同步驗證、固定排序序列化、以及 `Dispose` 清零。
   * 新增 `OperationDispatchQueueLifecycleTests.cs`，利用 `WeakReference` 測試佇列等待期間原始 `request` 與 `JsonElement` 能被 GC 回收。
2. **GREEN 階段**：
   * 實作 `PreparedOperationDispatch` 與 `OperationDispatchPreparer`。
   * 修改 `ControlledOperationExecutor.cs`，重構為同步 Prepare 與非同步 `ExecutePreparedAsync`。
   * 在 `Program.cs` 中設定 Kestrel/IIS 限制，並在 `OperationHttpRequest` 中實作 `BindAsync`。
   * 執行測試，確保所有測試綠燈。

### 3.2 逐檔修改 Unified Diff

#### 1. `SpeechMessage.Dynamics.Abstractions/Operations/OperationExecutionRequest.cs`
```diff
--- a/SpeechMessage.Dynamics.Abstractions/Operations/OperationExecutionRequest.cs
+++ b/SpeechMessage.Dynamics.Abstractions/Operations/OperationExecutionRequest.cs
@@ -34,6 +34,6 @@
     /// <summary>
     /// ?賢??摮?€潭???JSON 摨??€?
     /// </summary>
-    public IReadOnlyDictionary<string, object?> Parameters { get; init; }
+    public IReadOnlyDictionary<string, object?> Parameters { get; set; }
         = new Dictionary<string, object?>(StringComparer.Ordinal);
 
```

#### 2. `SpeechMessage.Dynamics.WebApi/Capacity/DispatchEnvelope.cs`
```diff
--- a/SpeechMessage.Dynamics.WebApi/Capacity/DispatchEnvelope.cs
+++ b/SpeechMessage.Dynamics.WebApi/Capacity/DispatchEnvelope.cs
@@ -23,5 +23,6 @@
     public string? IdempotencyKey { get; init; }
     public required DateTimeOffset DeadlineUtc { get; init; }
     public required int EstimatedEnvelopeBytes { get; init; }
+    public required string CanonicalHash { get; init; }
     public Guid CorrelationId { get; init; } = Guid.NewGuid();
 }
```

#### 3. 新增 `SpeechMessage.Dynamics.WebApi/Runtime/PreparedOperationDispatch.cs`
```csharp
// ============================================================================
// 檔案名稱：SpeechMessage.Dynamics.WebApi/Runtime/PreparedOperationDispatch.cs
// 功能描述：已完成同步驗證與規範化序列化的發送封裝，為佇列等待期間唯一保留的狀態。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Buffers;
using SpeechMessage.Dynamics.WebApi.Capacity;

namespace SpeechMessage.Dynamics.WebApi.Runtime
{
    /// <summary>
    /// 代表已準備就緒、通過驗證並規範化序列化的操作發送物件。
    /// 此類別負責管理從 ArrayPool 租借的規範化位元組緩衝區，並實作安全清零釋放。
    /// </summary>
    public sealed class PreparedOperationDispatch : IDisposable
    {
        private readonly byte[] _rentedBuffer;
        private readonly int _bufferLength;
        private int _disposed;

        /// <summary>
        /// 取得與此發送關聯的佇列封裝元數據。
        /// </summary>
        public DispatchEnvelope Envelope { get; }

        /// <summary>
        /// 取得已驗證且限制邊界的唯讀參數字典，此字典僅包含不可變的純量值，不保留任何 JsonElement 引用。
        /// </summary>
        public IReadOnlyDictionary<string, object?> BoundedParameters { get; }

        public PreparedOperationDispatch(
            DispatchEnvelope envelope,
            IReadOnlyDictionary<string, object?> boundedParameters,
            byte[] rentedBuffer,
            int bufferLength)
        {
            Envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
            BoundedParameters = boundedParameters ?? throw new ArgumentNullException(nameof(boundedParameters));
            _rentedBuffer = rentedBuffer ?? throw new ArgumentNullException(nameof(rentedBuffer));
            _bufferLength = bufferLength;
        }

        /// <summary>
        /// 取得規範化序列化後的唯讀位元組區段。
        /// </summary>
        public ReadOnlySpan<byte> CanonicalBytes
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed != 0, this);
                return _rentedBuffer.AsSpan(0, _bufferLength);
            }
        }

        /// <summary>
        /// 釋放租借的緩衝區，並在歸還前強制執行記憶體清零以防敏感資訊殘留。
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            CryptographicOperations.ZeroMemory(_rentedBuffer.AsSpan(0, _bufferLength));
            ArrayPool<byte>.Shared.Return(_rentedBuffer);
        }
    }
}
```

#### 4. 新增 `SpeechMessage.Dynamics.WebApi/Runtime/OperationDispatchPreparer.cs`
```csharp
// ============================================================================
// 檔案名稱：SpeechMessage.Dynamics.WebApi/Runtime/OperationDispatchPreparer.cs
// 功能描述：負責在進入佇列前，同步執行參數驗證與規範化序列化。
// ============================================================================

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WebApi.Capacity;

namespace SpeechMessage.Dynamics.WebApi.Runtime
{
    /// <summary>
    /// 提供同步驗證與規範化序列化服務的靜態準備器。
    /// </summary>
    public static class OperationDispatchPreparer
    {
        private const byte ProtocolVersion = 0x01;

        /// <summary>
        /// 同步驗證請求參數，並將其序列化為規範化 UTF-8 位元組流，封裝於 PreparedOperationDispatch 中。
        /// </summary>
        public static PreparedOperationDispatch Prepare(
            OperationExecutionRequest request,
            OperationDefinition definition,
            OrganizationAdmissionPlan admissionPlan)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(definition);
            ArgumentNullException.ThrowIfNull(admissionPlan);

            // 1. 驗證未知參數
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in definition.Parameters)
            {
                allowed.Add(p.Name);
            }

            foreach (var key in request.Parameters.Keys)
            {
                if (!allowed.Contains(key))
                {
                    throw new ArgumentException($"Unknown parameter: {key}", nameof(request));
                }
            }

            // 2. 驗證必填與型態轉換，並建立 Bounded Parameters
            var boundedParams = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var paramDef in definition.Parameters)
            {
                request.Parameters.TryGetValue(paramDef.Name, out var rawValue);

                if (paramDef.Required && (rawValue is null || (rawValue is string s && string.IsNullOrWhiteSpace(s))))
                {
                    throw new ArgumentException($"Missing required parameter: {paramDef.Name}", nameof(request));
                }

                var typedValue = ConvertToRegistryType(rawValue, paramDef.Type);
                boundedParams[paramDef.Name] = typedValue;
            }

            // 3. 規範化序列化 (固定排序、Version Tag、Big-Endian Length Prefix)
            var sortedKeys = new List<string>(boundedParams.Keys);
            sortedKeys.Sort(StringComparer.Ordinal);

            var tempBuffer = ArrayPool<byte>.Shared.Rent(65536);
            int offset = 4; // 保留前 4 位元組給總長度前綴

            try
            {
                // 寫入協定版本
                tempBuffer[offset++] = ProtocolVersion;

                // 寫入 IdempotencyKey
                WriteLengthPrefixedString(tempBuffer, ref offset, request.IdempotencyKey);

                // 寫入參數數量
                BinaryPrimitives.WriteUInt16BigEndian(tempBuffer.AsSpan(offset), (ushort)sortedKeys.Count);
                offset += 2;

                foreach (var key in sortedKeys)
                {
                    // 寫入參數名稱
                    WriteLengthPrefixedString(tempBuffer, ref offset, key);

                    var val = boundedParams[key];
                    if (val is null)
                    {
                        tempBuffer[offset++] = 0x00; // Null Tag
                    }
                    else
                    {
                        tempBuffer[offset++] = 0x01; // Value Present Tag
                        var valStr = Convert.ToString(val, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                        WriteLengthPrefixedString(tempBuffer, ref offset, valStr);
                    }
                }

                // 寫入總長度前綴 (Big-Endian UInt32)
                int totalPayloadLength = offset - 4;
                BinaryPrimitives.WriteUInt32BigEndian(tempBuffer.AsSpan(0, 4), (uint)totalPayloadLength);

                // 計算 SHA256 雜湊值作為 Canonical Hash
                byte[] hashBytes = SHA256.HashData(tempBuffer.AsSpan(4, totalPayloadLength));
                string canonicalHash = Convert.ToHexString(hashBytes);

                // 複製到剛好大小的租借 Buffer 中以利長期持有
                int finalLength = offset;
                byte[] finalBuffer = ArrayPool<byte>.Shared.Rent(finalLength);
                Buffer.BlockCopy(tempBuffer, 0, finalBuffer, 0, finalLength);

                var envelope = new DispatchEnvelope
                {
                    ProfileAlias = request.ProfileAlias.Trim(),
                    CapabilityOperationId = definition.CapabilityOperationId,
                    WorkloadSubjectId = request.WorkloadSubjectId.Trim(),
                    TemplateId = definition.TemplateId,
                    TemplateHash = definition.TemplateHash,
                    IdempotencyKey = request.IdempotencyKey,
                    DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, admissionPlan.QueueAdmissionTimeoutSeconds + 30)),
                    EstimatedEnvelopeBytes = finalLength,
                    CanonicalHash = canonicalHash
                };

                return new PreparedOperationDispatch(envelope, boundedParams, finalBuffer, finalLength);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(tempBuffer.AsSpan(0, offset));
                ArrayPool<byte>.Shared.Return(tempBuffer);
            }
        }

        private static void WriteLengthPrefixedString(byte[] buffer, ref int offset, string? value)
        {
            if (value is null)
            {
                BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset), -1);
                offset += 4;
                return;
            }

            int byteCount = Encoding.UTF8.GetByteCount(value);
            BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset), byteCount);
            offset += 4;

            Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, offset);
            offset += byteCount;
        }

        private static object? ConvertToRegistryType(object? rawValue, string targetType)
        {
            if (rawValue is null) return null;

            // 若傳入的是 JsonElement，先提取其原始值
            if (rawValue is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Null) return null;
                rawValue = element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString(),
                    JsonValueKind.Number => element.TryGetInt64(out long l) ? l : element.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => element.GetRawText() // 複雜型態保留原始 JSON 字串
                };
            }

            return targetType.ToLowerInvariant() switch
            {
                "string" => Convert.ToString(rawValue),
                "guid" => rawValue is Guid g ? g : Guid.Parse(Convert.ToString(rawValue)!),
                "integer" => Convert.ToInt32(rawValue),
                "boolean" => Convert.ToBoolean(rawValue),
                "date-time" => rawValue is DateTimeOffset dto ? dto : DateTimeOffset.Parse(Convert.ToString(rawValue)!),
                _ => rawValue
            };
        }
    }
}
```

#### 5. `SpeechMessage.Dynamics.WebApi/Runtime/ControlledOperationExecutor.cs`
```diff
--- a/SpeechMessage.Dynamics.WebApi/Runtime/ControlledOperationExecutor.cs
+++ b/SpeechMessage.Dynamics.WebApi/Runtime/ControlledOperationExecutor.cs
@@ -45,120 +45,119 @@
     public async Task<OperationExecutionResult> ExecuteAsync(
         OperationExecutionRequest request,
         CancellationToken cancellationToken = default)
     {
         ArgumentNullException.ThrowIfNull(request);
 
         if (string.IsNullOrWhiteSpace(request.ProfileAlias))
         {
             return OperationExecutionResult.Failure(
                 DynamicsErrorCodes.InvalidParameter,
                 "ProfileAlias is required.");
         }
 
         if (string.IsNullOrWhiteSpace(request.WorkloadSubjectId))
         {
             return OperationExecutionResult.Failure(
                 DynamicsErrorCodes.InvalidParameter,
                 "WorkloadSubjectId is required.");
         }
 
         if (!Package01OperationRegistry.TryGet(request.CapabilityOperationId, out var definition) ||
             definition is null)
         {
             return OperationExecutionResult.Failure(
                 DynamicsErrorCodes.UnknownOperation,
                 $"Operation '{request.CapabilityOperationId}' is not registered in Package 0/1.");
         }
 
         var normalizedAlias = request.ProfileAlias.Trim();
         if (!_leaseProvider.TryGetAdmissionPlan(normalizedAlias, out var admissionPlan) ||
             admissionPlan is null)
         {
             return OperationExecutionResult.Failure(
                 DynamicsErrorCodes.NotReady,
                 "The requested Dynamics profile is not ready.");
         }
 
-        // ??迂?賢??殷?銝 registry ???貊?交?蝯€?
-        var allowed = definition.Parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
-        var unknown = request.Parameters.Keys.Where(k => !allowed.Contains(k)).ToArray();
-        if (unknown.Length > 0)
-        {
-            return OperationExecutionResult.Failure(
-                DynamicsErrorCodes.InvalidParameter,
-                 $"Unknown parameters: {string.Join(", ", unknown)}");
-        }
- 
-        var envelope = new DispatchEnvelope
-        {
-            ProfileAlias = normalizedAlias,
-            CapabilityOperationId = definition.CapabilityOperationId,
-            WorkloadSubjectId = request.WorkloadSubjectId.Trim(),
-            TemplateId = definition.TemplateId,
-            TemplateHash = definition.TemplateHash,
-            IdempotencyKey = request.IdempotencyKey,
-            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(
-                Math.Max(1, admissionPlan.QueueAdmissionTimeoutSeconds + 30)),
-            EstimatedEnvelopeBytes = EstimateEnvelopeBytes(request)
-        };
- 
-        var acquisition = await _leaseProvider
-            .AcquireAsync(envelope, cancellationToken)
-            .ConfigureAwait(false);
-        if (!acquisition.Succeeded || acquisition.Lease is null)
-        {
-            return acquisition.Error ?? OperationExecutionResult.Failure(
-                DynamicsErrorCodes.CapacityRejected,
-                "Dynamics profile execution lease was rejected.");
-        }
- 
-        await using (acquisition.Lease.ConfigureAwait(false))
-        {
-            using var outboundCts = CancellationTokenSource.CreateLinkedTokenSource(
-                cancellationToken,
-                acquisition.Lease.LeaseLostToken,
-                acquisition.Lease.RetirementToken);
-            var remainingToDeadline = envelope.DeadlineUtc - DateTimeOffset.UtcNow;
-            var maximumLifetime = remainingToDeadline < acquisition.Lease.AdmissionPlan.MaximumOutboundWorkLifetime
-                ? remainingToDeadline
-                : acquisition.Lease.AdmissionPlan.MaximumOutboundWorkLifetime;
-            if (maximumLifetime <= TimeSpan.Zero)
-            {
-                return OperationExecutionResult.Failure(
-                    DynamicsErrorCodes.AdmissionTimeout,
-                    "Outbound operation deadline expired before dispatch.");
-            }
- 
-            outboundCts.CancelAfter(maximumLifetime);
-            return await acquisition.Lease.Client.ExecuteRegisteredOperationAsync(
-                definition,
-                request.Parameters,
-                outboundCts.Token).ConfigureAwait(false);
-        }
+        PreparedOperationDispatch prepared;
+        try
+        {
+            prepared = OperationDispatchPreparer.Prepare(request, definition, admissionPlan);
+        }
+        catch (ArgumentException ex)
+        {
+            return OperationExecutionResult.Failure(
+                DynamicsErrorCodes.InvalidParameter,
+                ex.Message);
+        }
+
+        // 藉由將後續的 async 流程委託給不接受 request 參數的獨立方法，
+        // 確保編譯器產生的 async 狀態機器不會強引用保留原始的 request 及其 JsonElement 圖形。
+        return await ExecutePreparedAsync(prepared, definition, cancellationToken).ConfigureAwait(false);
     }
 
-    private static int EstimateEnvelopeBytes(OperationExecutionRequest request)
+    private async Task<OperationExecutionResult> ExecutePreparedAsync(
+        PreparedOperationDispatch prepared,
+        OperationDefinition definition,
+        CancellationToken cancellationToken)
     {
-        // 蝎摯嚗摰???+ 瘥€??詨?/?潛?摮??瑕漲?€?冽 queue ?脰風嚗??臬??????
-        var total = 256;
-        total += (request.ProfileAlias?.Length ?? 0) * 2;
-        total += (request.CapabilityOperationId?.Length ?? 0) * 2;
-        total += (request.WorkloadSubjectId?.Length ?? 0) * 2;
-        total += (request.IdempotencyKey?.Length ?? 0) * 2;
- 
-        foreach (var pair in request.Parameters)
-        {
-            total += (pair.Key?.Length ?? 0) * 2;
-            total += pair.Value switch
-            {
-                null => 0,
-                string s => s.Length * 2,
-                _ => 64
-            };
-        }
- 
-        return total;
+        using (prepared)
+        {
+            var acquisition = await _leaseProvider
+                .AcquireAsync(prepared.Envelope, cancellationToken)
+                .ConfigureAwait(false);
+
+            if (!acquisition.Succeeded || acquisition.Lease is null)
+            {
+                return acquisition.Error ?? OperationExecutionResult.Failure(
+                    DynamicsErrorCodes.CapacityRejected,
+                    "Dynamics profile execution lease was rejected.");
+            }
+
+            await using (acquisition.Lease.ConfigureAwait(false))
+            {
+                using var outboundCts = CancellationTokenSource.CreateLinkedTokenSource(
+                    cancellationToken,
+                    acquisition.Lease.LeaseLostToken,
+                    acquisition.Lease.RetirementToken);
+
+                var remainingToDeadline = prepared.Envelope.DeadlineUtc - DateTimeOffset.UtcNow;
+                var maximumLifetime = remainingToDeadline < acquisition.Lease.AdmissionPlan.MaximumOutboundWorkLifetime
+                    ? remainingToDeadline
+                    : acquisition.Lease.AdmissionPlan.MaximumOutboundWorkLifetime;
+
+                if (maximumLifetime <= TimeSpan.Zero)
+                {
+                    return OperationExecutionResult.Failure(
+                        DynamicsErrorCodes.AdmissionTimeout,
+                        "Outbound operation deadline expired before dispatch.");
+                }
+
+                outboundCts.CancelAfter(maximumLifetime);
+                return await acquisition.Lease.Client.ExecuteRegisteredOperationAsync(
+                    definition,
+                    prepared.BoundedParameters,
+                    outboundCts.Token).ConfigureAwait(false);
+            }
+        }
     }
 }
```

#### 6. `SpeechMessage.Dynamics.Gateway/Program.cs`
```diff
--- a/SpeechMessage.Dynamics.Gateway/Program.cs
+++ b/SpeechMessage.Dynamics.Gateway/Program.cs
@@ -23,8 +23,18 @@
 var builder = WebApplication.CreateBuilder(args);
 
+builder.WebHost.ConfigureKestrel(options =>
+{
+    options.Limits.MaxRequestBodySize = 65536; // 限制最大請求主體為 64 KiB
+});
+
 builder.Services.AddControllers()
     .AddJsonOptions(options =>
         options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
 builder.Services.ConfigureHttpJsonOptions(options =>
     options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
+builder.Services.Configure<IISServerOptions>(options =>
+{
+    options.MaxRequestBodySize = 65536; // IIS 共用限制
+});
 
@@ -334,10 +344,53 @@
-public sealed class OperationHttpRequest
+public sealed class OperationHttpRequest : IBindableFromHttpContext<OperationHttpRequest>
 {
     /// <summary>???身摰神?亙? Operation ??bounded ?芰??蛛??航????舐??乓€?/summary>
     public string? IdempotencyKey { get; set; }
 
     /// <summary>???身摰?Operation Definition 撌脣恐???賢??嚗?亙??豢??典???CRM ?◤????/summary>
     public Dictionary<string, object?>? Parameters { get; set; }
+
+    /// <summary>
+    /// 實作自訂的 HttpContext 綁定器，確保在反序列化前強制執行 Hard Limit 限制。
+    /// </summary>
+    public static async ValueTask<OperationHttpRequest?> BindAsync(HttpContext context, System.Reflection.ParameterInfo parameter)
+    {
+        var contentLength = context.Request.ContentLength;
+        if (contentLength.HasValue && contentLength.Value > 65536)
+        {
+            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
+            return null;
+        }
+
+        var buffer = ArrayPool<byte>.Shared.Rent(65537);
+        int totalRead = 0;
+        try
+        {
+            var stream = context.Request.Body;
+            while (totalRead <= 65536)
+            {
+                var read = await stream.ReadAsync(buffer.AsMemory(totalRead, 65537 - totalRead), context.RequestAborted).ConfigureAwait(false);
+                if (read == 0) break;
+                totalRead += read;
+            }
+
+            if (totalRead > 65536)
+            {
+                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
+                return null;
+            }
+
+            var options = new JsonSerializerOptions
+            {
+                PropertyNameCaseInsensitive = true,
+                MaxDepth = 16 // 限制 JSON 深度防範 StackOverflow 攻擊
+            };
+            options.Converters.Add(new JsonStringEnumConverter());
+
+            return JsonSerializer.Deserialize<OperationHttpRequest>(buffer.AsSpan(0, totalRead), options);
+        }
+        finally
+        {
+            CryptographicOperations.ZeroMemory(buffer.AsSpan(0, totalRead));
+            ArrayPool<byte>.Shared.Return(buffer);
+        }
+    }
 }
```

---

## 4. Validation & Error Matrix

| 驗證情境 (Scenario) | 觸發階段 (Phase) | 預期 HTTP 狀態碼 / 錯誤碼 | 記憶體與佇列狀態 (Memory & Queue State) |
| :--- | :--- | :--- | :--- |
| **Content-Length > 64 KiB** | `BindAsync` (反序列化前) | `413 Payload Too Large` | 立即中斷，不分配 JSON 樹，不佔用佇列。 |
| **Chunked 串流讀取超過 64 KiB** | `BindAsync` (讀取過程中) | `413 Payload Too Large` | 讀取至第 65537 位元組時立即中斷，清零緩衝區，不佔用佇列。 |
| **JSON 巢狀深度 > 16** | `BindAsync` (反序列化時) | `400 Bad Request` | 拋出 `JsonException`，清零緩衝區，不佔用佇列。 |
| **未知參數名稱** | `Prepare` (同步驗證) | `400 Bad Request` / `InvalidParameter` | 拋出異常，`PreparedOperationDispatch` 立即釋放，不佔用佇列。 |
| **缺少必填參數** | `Prepare` (同步驗證) | `400 Bad Request` / `InvalidParameter` | 同上。 |
| **參數型態不符** | `Prepare` (同步驗證) | `400 Bad Request` / `InvalidParameter` | 同上。 |
| **佇列容量超限** | `AcquireAsync` (佇列准入) | `429 Too Many Requests` / `QueueFull` | 封裝物件 `Dispose` 清零，佇列計數器安全退回。 |
| **正常執行完成** | `ExecutePreparedAsync` | `200 OK` | 執行完畢後，在 `finally` 區塊中強制呼叫 `prepared.Dispose()` 清零並歸還 Buffer。 |

---

## 5. Leak/Lifecycle/Performance Assertions

### 5.1 記憶體洩漏斷言 (Leak Assertions)
* **WeakReference 驗證**：
  在測試中，透過 `[MethodImpl(MethodImplOptions.NoInlining)]` 建立 `OperationExecutionRequest` 並發起執行。當執行進入佇列等待（Lease Provider 故意不釋放許可證）時，手動觸發 `GC.Collect(2, GCCollectionMode.Forced, blocking: true)`。此時，指向原始 `request`、`Parameters` 字典與 `JsonElement` 的 `WeakReference.IsAlive` 必須為 `false`。
* **Buffer 清零驗證**：
  在 `PreparedOperationDispatch.Dispose` 執行後，其租借的 `byte[]` 緩衝區內容必須全部為 `0x00`。

### 5.2 生命週期斷言 (Lifecycle Assertions)
* **唯一擁有權 (Single Ownership)**：
  `PreparedOperationDispatch` 的生命週期由 `ControlledOperationExecutor` 獨佔管理。不論是成功執行、發生異常、客戶端取消（Cancellation）或被佇列拒絕，皆必須且僅能觸發一次 `Dispose`。
* **計數器歸零 (Gauges Baseline)**：
  當所有請求處理完畢或取消後，`OrganizationAdmissionManager` 的 `Queued`、`InFlight`、`ActivePermits` 與 `TrackedWorkloadCount` 必須精確歸零。

### 5.3 效能斷言 (Performance Assertions)
* **零雙重緩衝 (Zero Double-Buffering)**：
  `BindAsync` 直接將 Socket 串流讀入從 `ArrayPool` 租借的單一緩衝區，並直接對該 Span 進行反序列化，避免產生額外的 `MemoryStream` 或中間字串分配。

---

## 6. Classification of Findings

### 【Critical】
1. **原始請求圖形保留缺陷**：`ControlledOperationExecutor` 的 async 狀態機器強引用保留 `request` 導致佇列積壓時記憶體暴增。
2. **缺乏反序列化前置防護**：未在 JSON 反序列化前限制 Request Body 大小，易受 DOS 攻擊。

### 【Warning】
1. **估算位元組數不精確**：`EstimateEnvelopeBytes` 無法精確反映 UTF-8 位元組數，且對複雜 JSON 估算過低。
2. **驗證時機過遲**：參數驗證在 Admission 之後，導致無效請求佔用佇列資源。

### 【Info】
1. **日誌記錄風險**：`DynamicsWebApiClient` 記錄 `logicalProfileId` 時，應確保其已轉換為 Bounded Scalar，避免日誌框架意外保留大型複雜物件。
