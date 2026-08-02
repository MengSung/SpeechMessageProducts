// ============================================================================
// 檔案：SpeechMessage.Dynamics.Gateway/OfficialWorkerDeploymentConfiguration.cs
// 目的：把部署流程產生的官方 Worker overlay 以有限、不可 reload 的設定快照加入 Gateway。
// ============================================================================

using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

[assembly: InternalsVisibleTo("SpeechMessage.Dynamics.Tests")]

namespace SpeechMessage.Dynamics.Gateway;

/// <summary>
/// 載入與 Gateway 執行檔相鄰的部署 overlay。此邊界只接受官方 Worker identity／artifact 欄位，
/// 不接受 Authentication、Credential、Token、Password、Secret、Session 或任意 Gateway 設定。
/// 檔案只在 startup 同步讀取一次，完整驗證後才加入固定 scalar configuration snapshot；不建立
/// 檔案 reload-on-change／FileSystemWatcher、Timer、背景 Task、helper-owned mutable cache 或跨 session 狀態。
/// </summary>
internal static class OfficialWorkerDeploymentConfiguration
{
    internal const string FileName = "dynamics-official-workers.gateway.json";

    private const int MaximumOverlayBytes = 256 * 1024;
    private const int MaximumProfiles = 64;
    private const int MaximumScalarLength = 2048;
    private const string InvalidOverlayMessage =
        "The official Dynamics worker deployment overlay is invalid.";

    private static readonly string[] RootProperties = ["DynamicsProfiles"];
    private static readonly string[] DynamicsProperties = ["Profiles"];
    private static readonly string[] ProfileProperties =
    [
        "WorkerProfileGenerationId",
        "WorkerKind",
        "WorkerExecutablePath",
        "WorkerExecutableSha256",
        "PackageLockId",
        "OrganizationBaseUri",
        "Admission"
    ];
    private static readonly string[] AdmissionProperties = ["ExpectedOrganizationId"];
    /// <summary>
    /// 若執行檔目錄存在核准 overlay，先完整讀取及驗證，再以一個 bounded in-memory provider
    /// 覆寫基底 appsettings 的部署占位值。檔案不存在時保留原設定；檔案存在但不合法時 fail closed。
    /// </summary>
    /// <param name="configuration">Gateway startup 擁有的 configuration manager。</param>
    /// <param name="applicationBasePath">Gateway executable 所在目錄。</param>
    /// <returns>是否找到並加入 deployment overlay。</returns>
    internal static bool TryAddAdjacentOverlay(
        ConfigurationManager configuration,
        string applicationBasePath)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBasePath);

        var overlayPath = Path.GetFullPath(Path.Combine(applicationBasePath, FileName));
        if (!File.Exists(overlayPath))
        {
            return false;
        }

        byte[]? payload = null;
        try
        {
            payload = ReadBoundedPayload(overlayPath);
            using var document = JsonDocument.Parse(
                payload,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 8
                });
            var values = ParseAllowlistedValues(document.RootElement);

            // ConfigurationManager 會持有固定快照 provider 與一個有界的 change-token registration，
            // 並在 Host 結束時一併釋放；此 helper 不保留原始列舉、第二份字典、檔案監看器、
            // reload-on-change、計時器、背景工作或任何跨 Host 共用的可變設定快取。
            ((IConfigurationBuilder)configuration).Add(
                new FixedSnapshotConfigurationSource(values));
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException or
            InvalidDataException)
        {
            throw new InvalidOperationException(InvalidOverlayMessage);
        }
        finally
        {
            if (payload is not null)
            {
                Array.Clear(payload);
            }
        }
    }

    private static byte[] ReadBoundedPayload(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.SequentialScan);
        if (stream.Length is < 1 or > MaximumOverlayBytes)
        {
            throw new InvalidDataException();
        }

        var payload = GC.AllocateUninitializedArray<byte>((int)stream.Length);
        var offset = 0;
        while (offset < payload.Length)
        {
            var read = stream.Read(payload, offset, payload.Length - offset);
            if (read == 0)
            {
                Array.Clear(payload);
                throw new InvalidDataException();
            }

            offset += read;
        }

        return payload;
    }

    private static Dictionary<string, string?> ParseAllowlistedValues(
        JsonElement root)
    {
        ValidateObjectShape(root, RootProperties);
        var dynamicsProfiles = root.GetProperty("DynamicsProfiles");
        ValidateObjectShape(dynamicsProfiles, DynamicsProperties);
        var profiles = dynamicsProfiles.GetProperty("Profiles");
        if (profiles.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException();
        }

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var profileCount = 0;
        foreach (var profile in profiles.EnumerateObject())
        {
            profileCount++;
            if (profileCount > MaximumProfiles ||
                !aliases.Add(profile.Name) ||
                !IsSafeIdentifier(profile.Name))
            {
                throw new InvalidDataException();
            }

            ValidateObjectShape(profile.Value, ProfileProperties);
            var prefix = $"DynamicsProfiles:Profiles:{profile.Name}:";
            AddRequiredScalar(values, prefix, profile.Value, "WorkerProfileGenerationId");
            AddRequiredScalar(values, prefix, profile.Value, "WorkerKind");
            AddRequiredScalar(values, prefix, profile.Value, "WorkerExecutablePath");
            AddRequiredScalar(values, prefix, profile.Value, "WorkerExecutableSha256");
            AddRequiredScalar(values, prefix, profile.Value, "PackageLockId");
            AddRequiredScalar(values, prefix, profile.Value, "OrganizationBaseUri");

            var admission = profile.Value.GetProperty("Admission");
            ValidateObjectShape(admission, AdmissionProperties);
            AddRequiredScalar(
                values,
                prefix + "Admission:",
                admission,
                "ExpectedOrganizationId");
            ValidateProfileValues(values, prefix);
        }

        if (profileCount == 0)
        {
            throw new InvalidDataException();
        }

        return values;
    }

    private static void ValidateObjectShape(JsonElement element, IReadOnlyCollection<string> allowed)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var property in element.EnumerateObject())
        {
            count++;
            if (!seen.Add(property.Name) ||
                !allowed.Contains(property.Name, StringComparer.Ordinal))
            {
                throw new InvalidDataException();
            }
        }

        if (count != allowed.Count)
        {
            throw new InvalidDataException();
        }
    }

    private static void AddRequiredScalar(
        IDictionary<string, string?> values,
        string prefix,
        JsonElement owner,
        string propertyName)
    {
        var element = owner.GetProperty(propertyName);
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException();
        }

        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > MaximumScalarLength ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidDataException();
        }

        values.Add(prefix + propertyName, value);
    }

    private static void ValidateProfileValues(
        IReadOnlyDictionary<string, string?> values,
        string prefix)
    {
        var workerKind = values[prefix + "WorkerKind"];
        if (!string.Equals(workerKind, "OfficialCrm82Worker", StringComparison.Ordinal) &&
            !string.Equals(workerKind, "OfficialCrm91Worker", StringComparison.Ordinal))
        {
            throw new InvalidDataException();
        }

        var executablePath = values[prefix + "WorkerExecutablePath"];
        var expectedExecutableName = string.Equals(
                workerKind,
                "OfficialCrm82Worker",
                StringComparison.Ordinal)
            ? "SpeechMessage.Dynamics.Crm82Worker.exe"
            : "SpeechMessage.Dynamics.Crm91Worker.exe";
        if (executablePath is null ||
            !Path.IsPathFullyQualified(executablePath) ||
            !string.Equals(
                Path.GetFileName(executablePath),
                expectedExecutableName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException();
        }

        var executableHash = values[prefix + "WorkerExecutableSha256"];
        if (executableHash is null ||
            executableHash.Length != 64 ||
            executableHash.All(character => character == '0') ||
            executableHash.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException();
        }

        var expectedOrganizationIdText =
            values[prefix + "Admission:ExpectedOrganizationId"];
        if (!Guid.TryParseExact(expectedOrganizationIdText, "D", out var expectedOrganizationId) ||
            HasOneRepeatedByte(expectedOrganizationId))
        {
            throw new InvalidDataException();
        }
    }

    /// <summary>
    /// 以 GUID 的實際 16 位元組判斷是否為測試 placeholder；全部位元組相同的值（包含全零、
    /// 全 FF 與文字形式的 11/22 等重複值）都不具備可接受的部署識別熵，必須在啟動前拒絕。
    /// 此方法只使用 stackalloc 的固定 16 位元組，不建立長生命週期集合或跨要求狀態。
    /// </summary>
    /// <param name="value">已通過標準 D 格式解析的組織 GUID。</param>
    /// <returns>若 16 個位元組完全相同則為 <see langword="true"/>。</returns>
    private static bool HasOneRepeatedByte(Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!value.TryWriteBytes(bytes))
        {
            return true;
        }

        var first = bytes[0];
        for (var index = 1; index < bytes.Length; index++)
        {
            if (bytes[index] != first)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 將已驗證且有界的部署字典一次性交給設定 provider。ConfigurationManager 會保留 source，
    /// 因此 Build 必須用原子交換清空欄位，避免 source 與 provider 同時持有同一份部署快照。
    /// 此 source 僅供 Gateway 啟動時加入一次；重複建置會失敗關閉，而不複製或共用可變資料。
    /// </summary>
    private sealed class FixedSnapshotConfigurationSource : IConfigurationSource
    {
        private Dictionary<string, string?>? _values;

        /// <summary>
        /// 接收唯一的已驗證字典所有權；實際所有權會在 <see cref="Build"/> 中轉移給 provider。
        /// </summary>
        /// <param name="values">已套用欄位白名單與大小限制的設定值。</param>
        internal FixedSnapshotConfigurationSource(Dictionary<string, string?> values)
        {
            _values = values ?? throw new ArgumentNullException(nameof(values));
        }

        /// <summary>
        /// 原子轉移快照所有權並建立唯一 provider。完成後 source 不再保留字典或原始列舉；
        /// 若框架或呼叫端嘗試重複建置同一 source，立即失敗以避免不明確的共享生命週期。
        /// </summary>
        /// <param name="builder">擁有 provider 與 change-token registration 的設定建置器。</param>
        /// <returns>唯一擁有固定部署字典的 provider。</returns>
        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            var values = Interlocked.Exchange(ref _values, null)
                ?? throw new InvalidOperationException(
                    "The official Dynamics worker deployment snapshot was already consumed.");
            return new FixedSnapshotConfigurationProvider(values);
        }
    }

    /// <summary>
    /// 擁有單一、有界且只在 Gateway 啟動組態中使用的字典。它不監看檔案、不觸發 reload、
    /// 不建立計時器或背景工作，也沒有靜態可變狀態；生命週期由 ConfigurationManager/Host 擁有。
    /// </summary>
    private sealed class FixedSnapshotConfigurationProvider : ConfigurationProvider
    {
        /// <summary>
        /// 將 source 轉移的唯一字典直接設為 provider 資料，避免 AddInMemoryCollection 額外保留
        /// InitialData 列舉與複製字典。provider 隨 Host 結束而釋放其唯一強參考。
        /// </summary>
        /// <param name="values">由 source 一次性交付的已驗證部署快照。</param>
        internal FixedSnapshotConfigurationProvider(Dictionary<string, string?> values)
        {
            Data = values ?? throw new ArgumentNullException(nameof(values));
        }
    }

    private static bool IsSafeIdentifier(string value) =>
        value.Length is >= 1 and <= 128 &&
        value.All(character =>
            character is >= 'a' and <= 'z' or
            >= 'A' and <= 'Z' or
            >= '0' and <= '9' or '-' or '_' or '.');
}
