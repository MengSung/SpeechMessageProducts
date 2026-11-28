/*
檔案整體說明
目的與定位：F12 部署 CLI 專用的測試租戶 LINE 清理計畫持久化 owner，在任何 alias／menu 刪除前保存不可變 Plan，跨程序重跑即使 alias 已消失仍保留原 provider menu ID。
主要功能與流程：可信 artifacts 根下只使用 test-tenants/{code}.line-cleanup.bin；既有檔案直接讀取，不呼叫 lazy factory。新候選經 scope 驗證及嚴格版本 JSON 編碼，以 Windows CurrentUser DPAPI 加密，熵綁定用途、版本、組織、產品、Channel 及代碼，再 CreateNew 暫存、Flush(true)、同目錄不覆寫原子發布。競爭落敗只回傳實際已發布的計畫。
依賴與輸出：只依賴既有 Plan 驗證、System.Text.Json、正式 ProtectedData API 與本機 Windows 檔案 API；沒有 HTTP、CRM、SQL、userId、token、Session、通知、背景工作或共用可變快取。JSON 只含版本、完整 scope 及 alias／menu；所有自有 byte buffer 於 finally 或 Dispose 清零，已還原的不可變字串只隨回傳 Plan 由 CLI 持有。
使用邊界與資源：根目錄必須由 CLI 組合根固定提供，scope 必須已通過測試組織、manifest 與實際 Channel 憑證守門；本類不授予遠端刪除權。逐層以不跟隨 reparse、禁止 write／delete share 的目錄 handle 固定所有祖先，所有目標由同一 handle 確認型別、讀取與驗證。CurrentUser DPAPI 防止檔案直接竄改，不防同一 Windows 身分的惡意程式偽造；可信 artifacts 的 ACL 與 CLI 跨程序 Channel 寫入鎖仍由主線負責。
取消與生命週期：factory 僅在缺檔時執行，await 真完成而不遺棄工作；進入、factory 完成及發布前檢查 caller token，原子發布是提交點，提交後不以晚到取消隱藏結果。只有競爭落敗後讀勝者時的分享衝突可最多重試二十次、每次 awaited 十毫秒；Delay 的計時器及取消註冊由 Task owner 在完成／取消時回收，沒有常駐背景工作或重試佇列。密文與明文各最多 512 KiB、alias 最多一千筆、JSON 深度三；格式、身份或 scope 漂移全部拒絕，外部 factory timeout 仍由 CLI owner 管理。
失敗處理與驗證：本次成功建立的 temp 於失敗／取消時精確移除，不清掃其他程序暫存或 manifest；所有 stream／handle 反序關閉，錯誤保留交 CLI 共用告警入口。Delete 只有完整 Load 驗證成功後，才在同一 handle 標記刪除；缺檔冪等，損毀／錯 scope 證據保留，非 Windows 明確拒絕且絕不降明文。聚焦測試驗證 alias 消失續跑、竄改、並行、格式拒絕、reparse、取消與資源排空；不代表實機驗收。
*/
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Plan = SpeechMessage.Dynamics.Deployment.TestTenants.TestTenantLineCleanup.Plan;

namespace SpeechMessage.Dynamics.Deployment.TestTenants;

/// <summary>為單一已驗證組織／產品／Channel／租戶保存不可變清理證據，拒絕不完整發布、跨 scope 重用及不安全檔案路徑。</summary>
internal static class TestTenantLinePlanStore
{
    private const int MaximumBytes = 512 * 1024;
    private const int MaximumAliases = 1000;

    /// <summary>優先載入已保存計畫；只有確定缺檔才等待 factory，並在可靠落檔後交回可供遠端清理的同一證據。</summary>
    /// <param name="artifactRoot">CLI 固定的本機 artifacts 根；不得由 HTTP 或任意使用者路徑提供，所有祖先禁止 reparse。</param>
    /// <param name="organization">已核准測試組織的 canonical 名稱，參與 DPAPI 熵與 JSON scope 的精確比較。</param>
    /// <param name="product">與 manifest 及實際 Channel 憑證相符的固定產品鍵。</param>
    /// <param name="channelKey">已驗證的 Channel 鍵，不能以相同 code 跨 Channel 重用既有檔案。</param>
    /// <param name="code">manifest 的精確租戶代碼，唯一決定固定子目錄內的檔名。</param>
    /// <param name="create">只讀建立 Plan 的非同步委派；既有計畫、損毀檔或錯 scope 均不呼叫，不可在委派中刪除遠端資源。</param>
    /// <param name="ct">caller 取消只阻止未提交操作；已啟動 factory 完整排空，提交後不再取消成功結果。</param>
    /// <returns>已保存且與完整 scope 一致的不可變計畫；競爭落敗回傳勝者，不回傳未持久化候選。</returns>
    internal static async Task<Plan> LoadOrCreateAsync(string artifactRoot, string organization, string product,
        string channelKey, string code, Func<Task<Plan>> create, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("LINE cleanup plan storage requires Windows DPAPI.");
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(create);
        var scope = ValidateScope(organization, product, channelKey, code);
        using var paths = StorePaths.Open(artifactRoot, code, createDirectories: true)!;
        var existing = TryLoad(paths.PlanPath, scope, delete: false);
        if (existing is not null) return existing;
        var candidate = await create().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (candidate is null || !string.Equals(candidate.Product, product, StringComparison.Ordinal) ||
            !string.Equals(candidate.ChannelKey, channelKey, StringComparison.Ordinal) ||
            !string.Equals(candidate.Code, code, StringComparison.Ordinal))
            throw new InvalidDataException("Test tenant LINE cleanup factory returned an invalid scope.");
        return await SaveAsync(paths, scope, candidate, ct).ConfigureAwait(false);
    }

    /// <summary>只在同一檔案 handle 完成完整 Load、DPAPI 完整性與 scope 驗證後標記刪除；缺檔可安全重跑，從不刪除 manifest。</summary>
    /// <param name="artifactRoot">與載入時相同的 CLI 固定 artifacts 根；不存在時不建立目錄。</param>
    /// <param name="organization">與保存時相同的已核准測試組織 canonical 名稱。</param>
    /// <param name="product">與保存時相同的固定產品鍵。</param>
    /// <param name="channelKey">與保存時相同的已驗證 Channel 鍵。</param>
    /// <param name="code">與 manifest 相同的精確租戶代碼；呼叫端仍須持有同一 Channel 寫入鎖。</param>
    internal static void Delete(string artifactRoot, string organization, string product, string channelKey, string code)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("LINE cleanup plan storage requires Windows DPAPI.");
        var scope = ValidateScope(organization, product, channelKey, code);
        using var paths = StorePaths.Open(artifactRoot, code, createDirectories: false);
        if (paths is not null) _ = TryLoad(paths.PlanPath, scope, delete: true);
    }

    /// <summary>直接解析已驗證來源的單一明文 buffer，不建立第二份 JSON 文件；僅發布完整且完全相同 scope 的不可變 Plan。</summary>
    /// <param name="data">當次操作擁有且最多 512 KiB 的 UTF-8 buffer；本方法不保存引用，caller 必須於 finally 清零。</param>
    /// <param name="organization">已驗證的預期組織名稱，與文件逐字 Ordinal 比較。</param>
    /// <param name="product">已驗證的預期固定產品。</param>
    /// <param name="channelKey">已驗證的預期 Channel。</param>
    /// <param name="code">已驗證的預期租戶代碼。</param>
    /// <returns>只含有界 scope 與 alias／menu 字串的獨立不可變計畫，沒有解密 buffer 或外部資源引用。</returns>
    internal static Plan ParsePlan(ReadOnlySpan<byte> data, string organization, string product, string channelKey, string code)
    {
        _ = ValidateScope(organization, product, channelKey, code);
        if (data.Length is 0 or > MaximumBytes) throw InvalidDocument();
        List<KeyValuePair<string, string>>? aliases = null;
        try
        {
            var reader = new Utf8JsonReader(data, new JsonReaderOptions
                { MaxDepth = 3, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
            RequireRead(ref reader, JsonTokenType.StartObject);
            var seen = 0;
            string? actualOrganization = null, actualProduct = null, actualChannel = null, actualCode = null;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var field = ReadField(ref reader, ref seen, root: true);
                if (!reader.Read()) throw InvalidDocument();
                switch (field)
                {
                    case 1:
                        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out var version) || version != 1)
                            throw InvalidDocument();
                        break;
                    case 2: actualOrganization = ReadString(ref reader, 128); break;
                    case 4: actualProduct = ReadString(ref reader, 32); break;
                    case 8: actualChannel = ReadString(ref reader, 128); break;
                    case 16: actualCode = ReadString(ref reader, 32); break;
                    case 32: aliases = ReadAliases(ref reader); break;
                }
            }
            if (reader.TokenType != JsonTokenType.EndObject || seen != 63 || reader.Read() || aliases is null ||
                !string.Equals(actualOrganization, organization, StringComparison.Ordinal) ||
                !string.Equals(actualProduct, product, StringComparison.Ordinal) ||
                !string.Equals(actualChannel, channelKey, StringComparison.Ordinal) ||
                !string.Equals(actualCode, code, StringComparison.Ordinal))
                throw InvalidDocument();
            return new Plan(product, channelKey, code, aliases);
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException ||
                                     error is InvalidOperationException { InnerException: DecoderFallbackException })
        {
            throw new InvalidDataException("Test tenant LINE cleanup plan JSON is invalid.", error);
        }
        finally { aliases?.Clear(); }
    }

    /// <summary>以 bitset 要求固定屬性集合，解碼後同名（含 JSON escape）也視為重複；未知屬性絕不跳過。</summary>
    private static int ReadField(ref Utf8JsonReader reader, ref int seen, bool root)
    {
        if (reader.TokenType != JsonTokenType.PropertyName || reader.ValueSpan.Length > 96) throw InvalidDocument();
        var field = root ? reader.GetString() switch
        {
            "version" => 1, "organization" => 2, "product" => 4, "channelKey" => 8, "code" => 16, "aliases" => 32, _ => 0
        } : reader.GetString() switch { "alias" => 1, "menu" => 2, _ => 0 };
        if (field == 0 || (seen & field) != 0) throw InvalidDocument();
        seen |= field;
        return field;
    }

    /// <summary>在解析階段即限制一千筆，每列只接受唯一 alias／menu；最終由 Plan 建構子再驗穩定身份與代碼前綴。</summary>
    private static List<KeyValuePair<string, string>> ReadAliases(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw InvalidDocument();
        var rows = new List<KeyValuePair<string, string>>();
        try
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.StartObject || rows.Count == MaximumAliases) throw InvalidDocument();
                string? alias = null, menu = null;
                var seen = 0;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var field = ReadField(ref reader, ref seen, root: false);
                    if (!reader.Read()) throw InvalidDocument();
                    if (field == 1) alias = ReadString(ref reader, 100);
                    else menu = ReadString(ref reader, 128);
                }
                if (reader.TokenType != JsonTokenType.EndObject || seen != 3 || alias is null || menu is null)
                    throw InvalidDocument();
                rows.Add(new KeyValuePair<string, string>(alias, menu));
            }
            if (reader.TokenType != JsonTokenType.EndArray) throw InvalidDocument();
            return rows;
        }
        catch { rows.Clear(); throw; }
    }

    /// <summary>先限制原始 token 大小再解碼，避免超長字串配置；單一 ASCII 身份最大可使用六字節 JSON escape。</summary>
    private static string ReadString(ref Utf8JsonReader reader, int maximum)
    {
        if (reader.TokenType != JsonTokenType.String || reader.ValueSpan.Length > maximum * 6) throw InvalidDocument();
        var value = reader.GetString();
        if (string.IsNullOrEmpty(value) || value.Length > maximum) throw InvalidDocument();
        return value;
    }

    /// <summary>只接受預期 JSON token，不以缺漏或截斷輸入補預設值。</summary>
    private static void RequireRead(ref Utf8JsonReader reader, JsonTokenType expected)
    {
        if (!reader.Read() || reader.TokenType != expected) throw InvalidDocument();
    }

    /// <summary>所有格式拒絕均用固定訊息，不將文件、身份、宮格或 provider 本文寫入例外文字。</summary>
    private static InvalidDataException InvalidDocument() => new("Test tenant LINE cleanup plan document is invalid.");

    /// <summary>限制 canonical 組織名稱及重用既有 Plan 的產品／Channel／代碼驗證；這些字串檢查不能取代遠端授權。</summary>
    private static StoreScope ValidateScope(string organization, string product, string channelKey, string code)
    {
        if (string.IsNullOrEmpty(organization) || organization.Length > 128 ||
            organization.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new InvalidDataException("Test tenant LINE cleanup organization scope is invalid.");
        _ = new Plan(product, channelKey, code, []);
        return new StoreScope(organization, product, channelKey, code);
    }

    /// <summary>單次操作的純值 scope，不保存依賴、認證或可變使用者資料，也不進入任何 static 集合。</summary>
    /// <param name="Organization">已驗證的 canonical 測試組織。</param>
    /// <param name="Product">已驗證的固定產品。</param>
    /// <param name="ChannelKey">已驗證的 Channel 鍵。</param>
    /// <param name="Code">已驗證的租戶代碼。</param>
    private sealed record StoreScope(string Organization, string Product, string ChannelKey, string Code);

    /// <summary>以同一不允許寫入／替換的 handle 讀取有界密文、解密並驗證，僅 delete=true 且成功時標記同一檔案刪除。</summary>
    [SupportedOSPlatform("windows")]
    private static Plan? TryLoad(string path, StoreScope scope, bool delete)
    {
        using var handle = NativeFiles.Open(path, directory: false, delete);
        if (handle is null) return null;
        using var stream = new FileStream(handle, FileAccess.Read, bufferSize: 1, isAsync: false);
        if (stream.Length is <= 0 or > MaximumBytes) throw InvalidDocument();
        var cipher = new byte[checked((int)stream.Length)];
        byte[]? plain = null;
        byte[]? entropy = null;
        try
        {
            stream.ReadExactly(cipher);
            if (stream.ReadByte() != -1 || stream.Length != cipher.Length) throw InvalidDocument();
            entropy = CreateEntropy(scope);
            try { plain = ProtectedData.Unprotect(cipher.AsSpan(), DataProtectionScope.CurrentUser, entropy); }
            catch (CryptographicException error)
            {
                throw new InvalidDataException("Test tenant LINE cleanup plan integrity or scope validation failed.", error);
            }
            var plan = ParsePlan(plain, scope.Organization, scope.Product, scope.ChannelKey, scope.Code);
            if (delete) NativeFiles.MarkDeleted(handle);
            return plan;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cipher);
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            if (entropy is not null) CryptographicOperations.ZeroMemory(entropy);
        }
    }

    /// <summary>明文只存在可清零的操作私有 writer；密文寫入 CreateNew temp 並可靠 flush 後才原子發布，任何失敗精確清除本次 temp。</summary>
    [SupportedOSPlatform("windows")]
    private static async Task<Plan> SaveAsync(StorePaths paths, StoreScope scope, Plan candidate, CancellationToken ct)
    {
        using var plain = EncodePlan(scope, candidate);
        var entropy = CreateEntropy(scope);
        byte[]? cipher = null;
        try
        {
            cipher = ProtectedData.Protect(plain.WrittenSpan, DataProtectionScope.CurrentUser, entropy);
            if (cipher.Length is 0 or > MaximumBytes) throw InvalidDocument();
            ct.ThrowIfCancellationRequested();
            var temporary = Path.Combine(paths.DirectoryPath, "." + scope.Code + "." + Guid.NewGuid().ToString("N") + ".tmp");
            var ownsTemporary = false;
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                           bufferSize: 1, FileOptions.WriteThrough))
                {
                    ownsTemporary = true;
                    stream.Write(cipher);
                    stream.Flush(flushToDisk: true);
                    ct.ThrowIfCancellationRequested();
                }
                try { File.Move(temporary, paths.PlanPath, overwrite: false); }
                catch (IOException error) when ((error.HResult & 0xffff) is 80 or 183)
                {
                    return await LoadPublicationWinnerAsync(paths.PlanPath, scope, ct).ConfigureAwait(false);
                }
                ownsTemporary = false;
                // 此處已完成提交；晚到取消不能隱藏已可靠保存的清理證據。
                return candidate;
            }
            finally
            {
                // 只有 CreateNew 真正成功後才擁有此檔，不能清除碰巧已存在或其他程序的暫存。
                if (ownsTemporary) File.Delete(temporary);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(entropy);
            if (cipher is not null) CryptographicOperations.ZeroMemory(cipher);
        }
    }

    /// <summary>Windows 原子 rename 的短命 handle 可能仍持有 DELETE 權限；只在已確認發布競爭後有界等待分享衝突，完整等待 Delay 排空再讀同一勝者。</summary>
    [SupportedOSPlatform("windows")]
    private static async Task<Plan> LoadPublicationWinnerAsync(string path, StoreScope scope, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return TryLoad(path, scope, delete: false) ??
                    throw new IOException("Test tenant LINE cleanup plan publication winner disappeared.");
            }
            catch (IOException error) when (attempt < 19 && error.InnerException is Win32Exception { NativeErrorCode: 32 or 33 })
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>固定版本、明列完整 scope 與每個 alias／menu；writer 擴充時清零舊陣列，避免 MemoryStream 或 pool 留下明文副本。</summary>
    private static ClearingBufferWriter EncodePlan(StoreScope scope, Plan plan)
    {
        var bytes = new ClearingBufferWriter();
        try
        {
            using var writer = new Utf8JsonWriter(bytes);
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteString("organization", scope.Organization);
            writer.WriteString("product", scope.Product);
            writer.WriteString("channelKey", scope.ChannelKey);
            writer.WriteString("code", scope.Code);
            writer.WriteStartArray("aliases");
            foreach (var row in plan.Aliases)
            {
                writer.WriteStartObject();
                writer.WriteString("alias", row.Key);
                writer.WriteString("menu", row.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
            return bytes;
        }
        catch { bytes.Dispose(); throw; }
    }

    /// <summary>用途及四個 scope 欄位都以 UTF-8 長度前綴組成熵，阻止分隔符碰撞或其他產品的 DPAPI blob 被當成本格式。</summary>
    private static byte[] CreateEntropy(StoreScope scope)
    {
        string[] fields = ["SpeechMessage.TestTenantLineCleanupPlan.v1", scope.Organization, scope.Product, scope.ChannelKey, scope.Code];
        var bytes = new byte[fields.Sum(field => 4 + Encoding.UTF8.GetByteCount(field))];
        try
        {
            var offset = 0;
            foreach (var field in fields)
            {
                var size = Encoding.UTF8.GetByteCount(field);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), size);
                offset += 4;
                offset += Encoding.UTF8.GetBytes(field.AsSpan(), bytes.AsSpan(offset, size));
            }
            return bytes;
        }
        catch { CryptographicOperations.ZeroMemory(bytes); throw; }
        finally { Array.Clear(fields); }
    }

    /// <summary>操作私有且最多 512 KiB 的 JSON writer owner，擴充及 Dispose 都清零全部自有容量，不使用跨操作共用 pool。</summary>
    private sealed class ClearingBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private byte[] buffer = new byte[4096];
        private int written;
        /// <summary>只暴露本次已寫入區域給 DPAPI，不配置另一個明文副本；Dispose 後不能再使用。</summary>
        internal ReadOnlySpan<byte> WrittenSpan => buffer.AsSpan(0, written);
        /// <summary>確認 writer 宣告的已寫入長度在目前容量內，不允許發布未初始化或越界區域。</summary>
        public void Advance(int count)
        {
            if (count < 0 || count > buffer.Length - written) throw InvalidDocument();
            written += count;
        }
        /// <summary>保證 writer 所需的有界記憶體；擴充複製後先清零舊容量，再移交新 owner。</summary>
        private void EnsureCapacity(int sizeHint)
        {
            var required = Math.Max(sizeHint, 1);
            if (sizeHint < 0 || required > MaximumBytes - written) throw InvalidDocument();
            if (required <= buffer.Length - written) return;
            var next = new byte[Math.Min(MaximumBytes, Math.Max(buffer.Length * 2, written + required))];
            buffer.AsSpan(0, written).CopyTo(next);
            CryptographicOperations.ZeroMemory(buffer);
            buffer = next;
        }
        /// <summary>借出 writer 使用的操作私有記憶體，不得於下一次 GetMemory／GetSpan 後繼續持有舊區域。</summary>
        public Memory<byte> GetMemory(int sizeHint = 0) { EnsureCapacity(sizeHint); return buffer.AsMemory(written); }
        /// <summary>借出有界可寫 span；Utf8JsonWriter 以 Advance 回報真正寫入長度。</summary>
        public Span<byte> GetSpan(int sizeHint = 0) { EnsureCapacity(sizeHint); return buffer.AsSpan(written); }
        /// <summary>清除全部容量及長度，讓任何成功、格式錯誤或加密失敗路徑都不保留自有明文 buffer。</summary>
        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(buffer);
            buffer = [];
            written = 0;
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>固定整條本機目錄路徑的唯一 owner，持有期間祖先不可被換成 reparse，結束反序釋放而不遺留 static 鎖表。</summary>
    [SupportedOSPlatform("windows")]
    private sealed class StorePaths : IDisposable
    {
        private readonly List<SafeFileHandle> directories = [];
        /// <summary>經逐層驗證並固定的 test-tenants 目錄，只能由本次 CLI 作為可信檔案發布位置。</summary>
        internal string DirectoryPath { get; }
        /// <summary>精確 code 的 bin 路徑；從不回傳 manifest 或可由 caller 提供的其他檔名。</summary>
        internal string PlanPath { get; }
        /// <summary>只組成經驗證的固定路徑，實際目錄固定由 Open 逐層完成，失敗由 owner Dispose 反序回收。</summary>
        private StorePaths(string directory, string code)
        {
            DirectoryPath = directory;
            PlanPath = Path.Combine(directory, code + ".line-cleanup.bin");
        }
        /// <summary>在任何寫入前驗證原始路徑語意，從磁碟根逐層開啟不跟隨 reparse 的 handle；缺層只可逐層建立再驗證。</summary>
        internal static StorePaths? Open(string artifactRoot, string code, bool createDirectories)
        {
            if (string.IsNullOrWhiteSpace(artifactRoot)) throw new InvalidDataException("Test tenant LINE cleanup artifact root is invalid.");
            var supplied = artifactRoot.Replace('/', '\\');
            if (supplied.StartsWith(@"\\", StringComparison.Ordinal)) throw UnsafePath();
            var suppliedRoot = Path.GetPathRoot(supplied) ?? "";
            if (suppliedRoot.Length > 0 && !Path.IsPathFullyQualified(supplied)) throw UnsafePath();
            foreach (var part in supplied[suppliedRoot.Length..].TrimEnd('\\').Split('\\'))
                ValidatePathPart(part);
            var root = Path.GetFullPath(supplied);
            var drive = Path.GetPathRoot(root);
            if (drive is null || drive.Length != 3 || drive[1] != ':') throw UnsafePath();
            ValidatePathPart(code + ".line-cleanup.bin");
            var directory = Path.Combine(root, "test-tenants");
            var paths = new StorePaths(directory, code);
            try
            {
                var current = drive;
                var parts = new[] { drive }.Concat(directory[drive.Length..].Split('\\'));
                foreach (var part in parts)
                {
                    if (part != drive) current = Path.Combine(current, part);
                    var handle = NativeFiles.Open(current, directory: true, delete: false);
                    if (handle is null && createDirectories)
                    {
                        Directory.CreateDirectory(current);
                        handle = NativeFiles.Open(current, directory: true, delete: false);
                    }
                    if (handle is null) { paths.Dispose(); return null; }
                    paths.directories.Add(handle);
                }
                return paths;
            }
            catch { paths.Dispose(); throw; }
        }
        /// <summary>拒絕跳脫、ADS、尾端正規化及 Windows 裝置名稱，避免同一文字在 managed 與 Win32 API 指向不同對象。</summary>
        private static void ValidatePathPart(string part)
        {
            if (part.Length == 0 || part.EndsWith(' ') || part.EndsWith('.') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw UnsafePath();
            var stem = part.Split('.')[0];
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                                    stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9')
                throw UnsafePath();
        }
        /// <summary>回報固定路徑拒絕原因，不將本機帳號、路徑或 scope 回顯到診斷內容。</summary>
        private static InvalidDataException UnsafePath() => new("Test tenant LINE cleanup artifact path is unsafe.");
        /// <summary>反序關閉所有祖先 handle，確保等待 factory、格式錯誤、取消及發布失敗後目錄均能立即改名或刪除。</summary>
        public void Dispose()
        {
            for (var index = directories.Count - 1; index >= 0; index--) directories[index].Dispose();
            directories.Clear();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>最小 Windows 檔案邊界，只開啟精確路徑、查 handle 屬性及在成功驗證後標記同一檔案刪除；不跟隨最後一層 reparse。</summary>
    [SupportedOSPlatform("windows")]
    private static class NativeFiles
    {
        private const uint GenericRead = 0x80000000;
        private const uint ReadAttributes = 0x80;
        private const uint DeleteAccess = 0x10000;
        private const uint ShareRead = 1;
        private const uint OpenExisting = 3;
        private const uint OpenReparsePoint = 0x00200000;
        private const uint BackupSemantics = 0x02000000;

        /// <summary>不存在才回 null，權限／分享衝突保留為失敗；對同一 handle 檢查 reparse 與檔案／目錄型別，失敗立即關閉。</summary>
        internal static SafeFileHandle? Open(string path, bool directory, bool delete)
        {
            var handle = CreateFileW(path, directory ? ReadAttributes : GenericRead | (delete ? DeleteAccess : 0),
                ShareRead, IntPtr.Zero, OpenExisting, OpenReparsePoint | BackupSemantics, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                var code = Marshal.GetLastWin32Error();
                handle.Dispose();
                if (code is 2 or 3) return null;
                throw NativeFailure(code);
            }
            try
            {
                if (!GetFileInformationByHandleEx(handle, 9, out var information, 8))
                    throw NativeFailure(Marshal.GetLastWin32Error());
                var attributes = (FileAttributes)(information & uint.MaxValue);
                if ((attributes & FileAttributes.ReparsePoint) != 0 || ((attributes & FileAttributes.Directory) != 0) != directory)
                    throw new InvalidDataException("Test tenant LINE cleanup artifact is a reparse point or has an invalid type.");
                return handle;
            }
            catch { handle.Dispose(); throw; }
        }

        /// <summary>Delete 權限只用於已通過完整驗證的同一 handle；不是 DeleteOnClose，驗證失敗不會自動刪檔。</summary>
        internal static void MarkDeleted(SafeFileHandle handle)
        {
            byte delete = 1;
            if (!SetFileInformationByHandle(handle, 4, ref delete, 1))
                throw NativeFailure(Marshal.GetLastWin32Error());
        }

        /// <summary>原生錯誤只包含固定文字及數值系統代碼，保留真實原因供 CLI 統一告警且不輸出檔案內容。</summary>
        private static IOException NativeFailure(int code) => new("Test tenant LINE cleanup local file operation failed.", new Win32Exception(code));

        /// <summary>使用系統 DLL 與 Unicode 精確路徑；SafeFileHandle 是唯一關閉 owner，不允許 Windows 自動跟隨 reparse。</summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern SafeFileHandle CreateFileW(string fileName, uint access, uint share, IntPtr security,
            uint creation, uint flags, IntPtr template);

        /// <summary>以 FileAttributeTagInfo 的兩個 DWORD（八 byte）讀同一 handle 屬性，避免另依路徑取屬性的競賽窗口。</summary>
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
            out long information, uint size);

        /// <summary>FILE_DISPOSITION_INFO 的 BOOLEAN 只在成功驗證後設為一；檔案在本次 owner 關閉 handle 後才刪除。</summary>
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass,
            ref byte information, uint size);
    }
}
