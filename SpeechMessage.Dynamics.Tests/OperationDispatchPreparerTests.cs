// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/OperationDispatchPreparerTests.cs
// 目的：驗證 admission 前的同步 canonical dispatch 準備邊界。
//
// 信任邊界與生命週期：
// - registry 是參數名稱與型別的唯一權威，來自 HTTP/JSON 的 object graph 不可直接進 queue。
// - PreparedOperationDispatch 只能由 executor 單一 owner 持有；Dispose 必須可並行重入且只歸還 buffer 一次。
// - 測試使用可觀測 ArrayPool 來證明「先清零全租借區，再 Return」，不開放 production 危險 buffer API。
// - canonical 測試使用固定版本、type tag、Ordinal 排序與 UInt32 big-endian UTF-8 長度，
//   防止 dictionary 插入順序、多位元字元或未來 serializer 變動改變指紋。
// ============================================================================

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WebApi.Capacity;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 <c>OperationDispatchPreparer</c> 只保留有界 scalar 副本與單一 pooled buffer，
/// 且所有名稱、型別、必要值、冪等鍵與精確 byte 上限都在第一個 await 前 fail closed。
/// </summary>
public sealed class OperationDispatchPreparerTests
{
    /// <summary>
    /// 相同型別參數即使以相反順序插入 dictionary，也必須產生逐 byte 相同的 canonical 資料與 hash。
    /// 固定向量同時鎖定 version=1、null/string/integer tag 與 big-endian 長度格式。
    /// </summary>
    [Fact]
    public void Prepare_is_order_independent_and_matches_fixed_versioned_representation()
    {
        var preparer = new OperationDispatchPreparer(ArrayPool<byte>.Shared);
        var definition = CreateDefinition(
            new OperationParameterDefinition
            {
                Name = "a",
                Type = "integer",
                Required = true,
                EncodingContext = "json-body"
            },
            new OperationParameterDefinition
            {
                Name = "b",
                Type = "string",
                Required = true,
                EncodingContext = "json-body"
            });
        var plan = CreatePlan(1024);

        var firstRequest = CreateRequest(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["b"] = "中",
            ["a"] = 1L
        });
        var secondRequest = CreateRequest(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["a"] = 1L,
            ["b"] = "中"
        });

        preparer.TryPrepare(firstRequest, definition, plan, out var first, out var firstError)
            .Should().BeTrue(firstError?.ErrorMessage);
        preparer.TryPrepare(secondRequest, definition, plan, out var second, out var secondError)
            .Should().BeTrue(secondError?.ErrorMessage);
        var firstPrepared = first!;
        var secondPrepared = second!;
        using (firstPrepared)
        using (secondPrepared)
        {
            var expected = Convert.FromHexString(
                "010000000170000000026F7000000001770000000174000000016800" +
                "00000002000000016104000000000000000100000001620100000003E4B8AD");

            firstPrepared.CanonicalBytes.ToArray().Should().Equal(expected);
            secondPrepared.CanonicalBytes.ToArray().Should().Equal(expected);
            firstPrepared.CanonicalSha256.Should().Be(
                "afff30b764bd18e58fff20d754bfd7cd0d80144aaac54faef99d72b2cc12d275");
            secondPrepared.CanonicalSha256.Should().Be(firstPrepared.CanonicalSha256);
            firstPrepared.Envelope.CanonicalEnvelopeBytes.Should().Be(expected.Length);
        }
    }

    /// <summary>
    /// 以繁體中文（3 bytes）與 emoji（4 bytes）建立精確 UTF-8 邊界；limit 可接受，
    /// 只多一個 byte 就必須在 admission 前回傳 EnvelopeTooLarge，不得以 UTF-16 <c>Length</c> 粗估。
    /// </summary>
    [Fact]
    public void Prepare_enforces_exact_multibyte_utf8_limit()
    {
        var preparer = new OperationDispatchPreparer(ArrayPool<byte>.Shared);
        var definition = CreateDefinition(new OperationParameterDefinition
        {
            Name = "value",
            Type = "string",
            Required = true,
            EncodingContext = "json-body"
        });
        var generousPlan = CreatePlan(4096);

        preparer.TryPrepare(
                CreateRequest(new Dictionary<string, object?> { ["value"] = string.Empty }),
                definition,
                generousPlan,
                out var baseline,
                out var baselineError)
            .Should().BeTrue(baselineError?.ErrorMessage);
        var fixedBytes = baseline!.Envelope.CanonicalEnvelopeBytes;
        baseline.Dispose();

        const int limit = 512;
        var exactValue = BuildMultibyteUtf8Text(limit - fixedBytes);
        Encoding.UTF8.GetByteCount(exactValue).Should().Be(limit - fixedBytes);
        var exactPlan = CreatePlan(limit);

        preparer.TryPrepare(
                CreateRequest(new Dictionary<string, object?> { ["value"] = exactValue }),
                definition,
                exactPlan,
                out var exact,
                out var exactError)
            .Should().BeTrue(exactError?.ErrorMessage);
        var exactPrepared = exact!;
        using (exactPrepared)
        {
            exactPrepared.Envelope.CanonicalEnvelopeBytes.Should().Be(limit);
        }

        var overValue = exactValue + "A";
        preparer.TryPrepare(
                CreateRequest(new Dictionary<string, object?> { ["value"] = overValue }),
                definition,
                exactPlan,
                out var over,
                out var overError)
            .Should().BeFalse();
        over.Should().BeNull();
        overError!.ErrorCode.Should().Be(DynamicsErrorCodes.EnvelopeTooLarge);
    }

    /// <summary>
    /// 必要值、未知名稱、超過 definition 的參數數與錯誤 scalar type 都必須在 prepare 同步失敗；
    /// 錯誤結果不回顯 raw value 或建立 pooled owner。
    /// </summary>
    [Fact]
    public void Prepare_rejects_required_name_count_and_type_violations()
    {
        var preparer = new OperationDispatchPreparer(ArrayPool<byte>.Shared);
        var definition = CreateDefinition(new OperationParameterDefinition
        {
            Name = "value",
            Type = "guid",
            Required = true,
            EncodingContext = "json-body"
        });
        var plan = CreatePlan(4096);

        AssertInvalid(preparer, definition, plan, new Dictionary<string, object?>());
        AssertInvalid(preparer, definition, plan, new Dictionary<string, object?>
        {
            ["unknown"] = "value"
        });
        AssertInvalid(preparer, definition, plan, new Dictionary<string, object?>
        {
            ["value"] = Guid.NewGuid(),
            ["extra"] = "value"
        });
        AssertInvalid(preparer, definition, plan, new Dictionary<string, object?>
        {
            ["value"] = 42L
        });
    }

    /// <summary>
    /// 允許的 scalar 名稱下若出現 JSON object/array，不論 graph 深度或 raw text 大小都立即拒絕；
    /// 同時拒絕 registry 尚未實作 bounded owner 的 object/array definition。
    /// </summary>
    [Fact]
    public void Prepare_rejects_complex_json_and_unsupported_complex_definitions()
    {
        var preparer = new OperationDispatchPreparer(ArrayPool<byte>.Shared);
        var scalarDefinition = CreateDefinition(new OperationParameterDefinition
        {
            Name = "value",
            Type = "string",
            Required = true,
            EncodingContext = "json-body"
        });
        var complexDefinition = CreateDefinition(new OperationParameterDefinition
        {
            Name = "value",
            Type = "object",
            Required = true,
            EncodingContext = "json-body"
        });
        var plan = CreatePlan(4096);
        using var document = JsonDocument.Parse("""{"nested":{"items":[{"value":"large"}]}}""");

        AssertInvalid(preparer, scalarDefinition, plan, new Dictionary<string, object?>
        {
            ["value"] = document.RootElement
        });
        AssertInvalid(preparer, complexDefinition, plan, new Dictionary<string, object?>
        {
            ["value"] = "even-a-scalar-is-rejected-for-an-unsupported-definition"
        });
    }

    /// <summary>
    /// Registry 虽是信任來源，仍必須在啟動/派送時 fail closed：空名稱、超過 128 UTF-8 bytes
    /// 或含無效 surrogate 的 definition name 不得進入 canonical format，即使 caller 沒有提交該 optional 參數。
    /// </summary>
    [Fact]
    public void Prepare_rejects_invalid_registry_parameter_names()
    {
        var preparer = new OperationDispatchPreparer(ArrayPool<byte>.Shared);
        var plan = CreatePlan(4096);
        var invalidNames = new[]
        {
            string.Empty,
            new string('a', 129),
            "\uD800"
        };

        foreach (var invalidName in invalidNames)
        {
            var definition = CreateDefinition(new OperationParameterDefinition
            {
                Name = invalidName,
                Type = "string",
                Required = false,
                EncodingContext = "json-body"
            });

            AssertInvalid(preparer, definition, plan, new Dictionary<string, object?>());
        }
    }

    /// <summary>
    /// Date-time string 必須自帶 <c>Z</c> 或數值 offset；無 offset 文字會依主機時區解釋，
    /// 同一 request 可在不同 runtime host 產生不同 canonical hash，因此必須在 admission 前拒絕。
    /// </summary>
    [Fact]
    public void Prepare_rejects_date_time_string_without_explicit_offset()
    {
        var preparer = new OperationDispatchPreparer(ArrayPool<byte>.Shared);
        var definition = CreateDefinition(new OperationParameterDefinition
        {
            Name = "when",
            Type = "date-time",
            Required = true,
            EncodingContext = "json-body"
        });

        AssertInvalid(
            preparer,
            definition,
            CreatePlan(4096),
            new Dictionary<string, object?>
            {
                ["when"] = "2026-07-29T12:34:56"
            });
    }

    /// <summary>
    /// 冪等鍵信任邊界是 1-128 個 URL-safe ASCII 字元；空字串、129 字元、空白或 Unicode
    /// 一律在 admission 前拒絕，避免未來 durable ledger 出現不同主機的 key 正規化差異。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("contains space")]
    [InlineData("冪等鍵")]
    public void Prepare_rejects_invalid_idempotency_key_format(string idempotencyKey)
    {
        var preparer = new OperationDispatchPreparer(ArrayPool<byte>.Shared);
        var definition = CreateDefinition();
        var plan = CreatePlan(4096);
        var request = new OperationExecutionRequest
        {
            ProfileAlias = "p",
            CapabilityOperationId = "op",
            WorkloadSubjectId = "w",
            Parameters = new Dictionary<string, object?>(),
            IdempotencyKey = idempotencyKey
        };

        preparer.TryPrepare(request, definition, plan, out var prepared, out var error)
            .Should().BeFalse();
        prepared.Should().BeNull();
        error!.ErrorCode.Should().Be(DynamicsErrorCodes.InvalidParameter);
    }

    /// <summary>129 個合法 ASCII 字元仍必須因長度超界而拒絕。</summary>
    [Fact]
    public void Prepare_rejects_idempotency_key_over_128_characters()
    {
        var preparer = new OperationDispatchPreparer(ArrayPool<byte>.Shared);
        var request = new OperationExecutionRequest
        {
            ProfileAlias = "p",
            CapabilityOperationId = "op",
            WorkloadSubjectId = "w",
            IdempotencyKey = new string('a', 129)
        };

        preparer.TryPrepare(request, CreateDefinition(), CreatePlan(4096), out var prepared, out var error)
            .Should().BeFalse();
        prepared.Should().BeNull();
        error!.ErrorCode.Should().Be(DynamicsErrorCodes.InvalidParameter);
    }

    /// <summary>
    /// 多個 thread 同時 Dispose 時只能有一個取得 cleanup ownership；唯一 owner 必須清除參數參考、
    /// 將整個租借陣列（包含未寫入 tail）清零，再且只再 Return 一次。
    /// </summary>
    [Fact]
    public void Dispose_is_concurrent_idempotent_zeros_entire_rented_buffer_and_releases_parameters()
    {
        var pool = new RecordingArrayPool();
        var preparer = new OperationDispatchPreparer(pool);
        var definition = CreateDefinition(new OperationParameterDefinition
        {
            Name = "value",
            Type = "string",
            Required = true,
            EncodingContext = "json-body"
        });

        var prepared = CreatePreparedWithCollectibleParameter(preparer, definition, out var parameterReference);
        Parallel.For(0, 32, _ => prepared.Dispose());
        ForceCollection(parameterReference);

        prepared.IsDisposed.Should().BeTrue();
        parameterReference.IsAlive.Should().BeFalse();
        pool.ReturnCalls.Should().Be(1);
        pool.ReturnedSnapshot.Should().NotBeNull();
        pool.ReturnedSnapshot!.Should().OnlyContain(value => value == 0);
    }

    /// <summary>建立不被 string intern pool 保留的參數，並取得 prepared 容器內實際 scalar 的弱參考。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static PreparedOperationDispatch CreatePreparedWithCollectibleParameter(
        OperationDispatchPreparer preparer,
        OperationDefinition definition,
        out WeakReference parameterReference)
    {
        var value = new string('x', 256);
        var request = CreateRequest(new Dictionary<string, object?> { ["value"] = value });
        preparer.TryPrepare(request, definition, CreatePlan(4096), out var prepared, out var error)
            .Should().BeTrue(error?.ErrorMessage);
        parameterReference = new WeakReference(prepared!.Parameters["value"]!);
        return prepared;
    }

    /// <summary>以有界重複 GC 證明弱參考已無強 owner，避免單次 GC 時機造成假陰性。</summary>
    private static void ForceCollection(WeakReference reference)
    {
        for (var attempt = 0; attempt < 10 && reference.IsAlive; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }

    /// <summary>斷言指定參數集合同步失敗，且不轉移 prepared/buffer ownership。</summary>
    private static void AssertInvalid(
        OperationDispatchPreparer preparer,
        OperationDefinition definition,
        OrganizationAdmissionPlan plan,
        IReadOnlyDictionary<string, object?> parameters)
    {
        preparer.TryPrepare(CreateRequest(parameters), definition, plan, out var prepared, out var error)
            .Should().BeFalse();
        prepared.Should().BeNull();
        error!.ErrorCode.Should().Be(DynamicsErrorCodes.InvalidParameter);
    }

    /// <summary>用三與四 byte scalar 組出精確 UTF-8 byte 數，不以 UTF-16 char 數假裝為 wire size。</summary>
    private static string BuildMultibyteUtf8Text(int requiredBytes)
    {
        for (var emojiCount = 0; emojiCount <= requiredBytes / 4; emojiCount++)
        {
            var remaining = requiredBytes - (emojiCount * 4);
            if (remaining >= 0 && remaining % 3 == 0)
            {
                return string.Concat(
                    new string('中', remaining / 3),
                    string.Concat(Enumerable.Repeat("🙂", emojiCount)));
            }
        }

        throw new InvalidOperationException("The requested UTF-8 byte count cannot be composed from 3/4-byte scalars.");
    }

    /// <summary>建立測試用 immutable operation definition；沒有 runtime、client、token 或 request owner。</summary>
    // Dispatch 準備器不會送出 HTTP request 或讀取 response，仍固定 Unsupported response kind 與 registry 等值的
    // 4 page、64 KiB/page、256 KiB cumulative、4096 rows 上限，避免 test definition 成為繞過正式 fail-closed policy 的例外。
    private static OperationDefinition CreateDefinition(params OperationParameterDefinition[] parameters)
        => new()
        {
            CapabilityOperationId = "op",
            OperationKind = "read",
            TemplateKind = "odata-route",
            TemplateId = "t",
            TemplateHash = "h",
            ResponseKind = OperationResponseKind.Unsupported,
            MaximumPageCount = 4,
            MaximumPageBytes = 64 * 1024,
            MaximumCumulativeResponseBytes = 4 * 64 * 1024,
            MaximumResultItemCount = 4096,
            DataClassification = "internal",
            AuditRequirement = "none",
            IdempotencyClass = "read-only",
            Parameters = parameters,
            Package = "test"
        };

    /// <summary>建立只含可驗證 scalar 的 request，參數 dictionary ownership 在 prepare 返回後不得被保留。</summary>
    private static OperationExecutionRequest CreateRequest(IReadOnlyDictionary<string, object?> parameters)
        => new()
        {
            ProfileAlias = "p",
            CapabilityOperationId = "op",
            WorkloadSubjectId = "w",
            Parameters = parameters
        };

    /// <summary>建立已通過現有容量設定驗證的 plan，唯一變因是 canonical byte 上限。</summary>
    private static OrganizationAdmissionPlan CreatePlan(int maximumEnvelopeBytes)
    {
        var options = new DynamicsWebApiOptions
        {
            OrganizationWebApiBaseUri = "https://crm.example.local/api/data/v9.1/",
            MaxConnectionsPerServer = 1,
            Admission = new OrganizationAdmissionOptions
            {
                ExpectedOrganizationId = Guid.Parse("56565656-5656-5656-5656-565656565656"),
                AggregateMaxInFlight = 1,
                MaximumRuntimeHosts = 1,
                LocalQueueCapacity = 1,
                MaxInFlightAndQueuedPerWorkload = 2,
                MaxDispatchEnvelopeBytes = maximumEnvelopeBytes,
                AdmissionNamespaceId = "dispatch-preparer-tests",
                LeaseNamespaceId = "dispatch-preparer-tests"
            }
        };
        OrganizationAdmissionPlan.TryCreate(options, options.Admission, out var plan, out var error)
            .Should().BeTrue(error?.ErrorMessage);
        return plan!;
    }

    /// <summary>
    /// 以額外 tail 的陣列模擬真實 ArrayPool；Return 時立即複製快照，可驗證清零一定早於歸還。
    /// </summary>
    private sealed class RecordingArrayPool : ArrayPool<byte>
    {
        private int _returnCalls;

        /// <summary>取得實際 Return 次數，可安全由多個 Dispose thread 讀取。</summary>
        public int ReturnCalls => Volatile.Read(ref _returnCalls);

        /// <summary>取得 Return 當下的整個陣列快照，包含 canonical 未寫入 tail。</summary>
        public byte[]? ReturnedSnapshot { get; private set; }

        /// <summary>租借比最小需求多 31 bytes 的陣列，並填入非零值以防止假陽性。</summary>
        public override byte[] Rent(int minimumLength)
        {
            var buffer = new byte[checked(minimumLength + 31)];
            Array.Fill(buffer, (byte)0xA5);
            return buffer;
        }

        /// <summary>記錄歸還前狀態；本 fake 不重用陣列，避免測試自身引入跨案例保留。</summary>
        public override void Return(byte[] array, bool clearArray = false)
        {
            ReturnedSnapshot = array.ToArray();
            Interlocked.Increment(ref _returnCalls);
        }
    }
}
