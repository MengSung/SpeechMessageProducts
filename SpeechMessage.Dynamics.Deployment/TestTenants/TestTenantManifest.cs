/*
檔案整體說明
目的與定位：保存 F12 測試租戶建立作業的最小本地清理清單，供後續 cleanup 命令精確識別本次建立的 Dataverse 資料列。
主要功能與流程：TestTenantRecordId 將允許的實體邏輯名稱與非空資料庫 GUID 綁成穩定複合身份；TestTenantManifest 複製並凍結輸入集合、驗證產品／組織／租戶代碼及必要主記錄，將版本化 JSON 原子建立於 artifacts/test-tenants/{code}.json，或以有界、嚴格 JSON parser 載入並核對呼叫端要求的組織與代碼。
依賴與輸出：只使用本機檔案系統與 System.Text.Json，不持有 SDK、LINE client、SQL 連線、token、LINE userId 或個資。呼叫端必須把固定 artifacts 根目錄交給本類，不能由 request 決定任意刪除路徑；暫存檔由當次 Save 呼叫唯一擁有，成功後原子移至最終路徑，失敗時只清理自身暫存檔。
使用邊界與維護：manifest 是 cleanup 的必要限制條件但不是遠端授權；cleanup 仍須依資料庫父子 lookup 重驗每筆資料屬於 manifest 的租戶。只允許明列的三個測試組織、三個產品、有限代碼字元與受控實體清單；最多 256 個記錄身份、JSON 最多 64 KiB，重複實體+GUID、未知欄位、錯誤版本、符號連結、範圍不符或缺少 account／slotrequest 一律 fail closed。原始邀請碼及使用者輸入不進入 manifest。
失敗與驗證：儲存採同目錄 CreateNew 暫存檔、Flush(true) 後不覆寫搬移；讀取使用嚴格重複屬性拒絕、深度及大小上限。合成測試驗證白名單往返、身份衝突及跨組織拒絕；通過只代表離線格式契約成立，不代表 D365 實機部署或資料清理。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SpeechMessage.Dynamics.Deployment.TestTenants;

/// <summary>manifest 允許的單筆本地記錄身份；不包含顯示名稱、秘密或使用者識別資料。</summary>
internal sealed record TestTenantRecordId
{
    /// <summary>僅接受 D365 實體邏輯名稱白名單與非空 GUID，避免 manifest 指向任意資料表。</summary>
    /// <param name="entityLogicalName">F12 擁有或明確追蹤的資料列實體名稱。</param>
    /// <param name="id">該實體的伺服器資料庫主鍵。</param>
    internal TestTenantRecordId(string entityLogicalName, Guid id)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName) || !TestTenantManifest.IsAllowedEntity(entityLogicalName))
            throw new InvalidDataException("Test tenant manifest entity is not allowed.");
        if (id == Guid.Empty)
            throw new InvalidDataException("Test tenant manifest record identity is invalid.");
        EntityLogicalName = entityLogicalName;
        Id = id;
    }

    /// <summary>明確列於 manifest 白名單的 D365 實體邏輯名稱。</summary>
    internal string EntityLogicalName { get; }

    /// <summary>伺服器產生且非空的資料庫主鍵；不得以重試鍵或暫存鍵替代。</summary>
    internal Guid Id { get; }
}

/// <summary>只含 F12 清理所需記錄身份的不可變 manifest；檔案系統操作僅限固定租戶子目錄。</summary>
internal sealed class TestTenantManifest
{
    private const int CurrentVersion = 1;
    private const int MaximumRecords = 256;
    private const int MaximumBytes = 64 * 1024;
    private static readonly HashSet<string> OrganizationNames = new(StringComparer.Ordinal)
        { "goodshepherd20", "construction", "association" };
    private static readonly HashSet<string> ProductNames = new(StringComparer.Ordinal)
        { "GoodShepherd2", "GoodButler", "GoodGuild" };
    private static readonly HashSet<string> AllowedEntities = new(StringComparer.Ordinal)
        { "account", "contact", "new_slotrequest", "new_roleassignment", "new_testinvite" };

    /// <summary>以唯讀查詢檢查固定實體白名單，不向其他型別暴露可變集合。</summary>
    internal static bool IsAllowedEntity(string entity) => AllowedEntities.Contains(entity);

    /// <summary>建立時由呼叫端選定、載入時與授權目標逐字比對的測試組織名稱。</summary>
    internal string Organization { get; }

    /// <summary>建立租戶時使用的固定產品鍵；只接受三大產品名稱。</summary>
    internal string Product { get; }

    /// <summary>建立與清理命令共同提供的受限租戶代碼。</summary>
    internal string Code { get; }

    /// <summary>唯一 account 主鍵，從唯一的 account 記錄身份推導，不重複保存第二份可分歧狀態。</summary>
    internal Guid TenantId => Records.Single(record => record.EntityLogicalName == "account").Id;

    /// <summary>以唯讀副本發布的完整本次記錄身份；不同 entity+ID 保留為獨立資料列。</summary>
    internal IReadOnlyList<TestTenantRecordId> Records { get; }

    /// <summary>只由已驗證欄位建構；所有公開於本 assembly 的建立路徑均會複製及檢查集合。</summary>
    private TestTenantManifest(string organization, string product, string code, IReadOnlyList<TestTenantRecordId> records)
    {
        Organization = organization;
        Product = product;
        Code = code;
        Records = records;
    }

    /// <summary>複製、排序並驗證候選列，只有完整唯一清單通過後才建立可發布的 manifest。</summary>
    /// <param name="organization">三個固定測試組織之一，不接受正式組織。</param>
    /// <param name="product">三個固定產品鍵之一。</param>
    /// <param name="code">僅含小寫 ASCII 字母、數字及中線的租戶代碼。</param>
    /// <param name="records">本次建立之資料列身份；呼叫端集合不會被保留或暴露。</param>
    /// <returns>所有資料均通過固定白名單後的不可變候選。</returns>
    internal static TestTenantManifest Create(string organization, string product, string code,
        IEnumerable<TestTenantRecordId> records)
    {
        ValidateScope(organization, product, code);
        ArgumentNullException.ThrowIfNull(records);
        var snapshot = new List<TestTenantRecordId>(capacity: 8);
        foreach (var record in records)
        {
            if (record is null || snapshot.Count == MaximumRecords)
                throw new InvalidDataException("Test tenant manifest record set is invalid or oversized.");
            snapshot.Add(new TestTenantRecordId(record.EntityLogicalName, record.Id));
        }

        ValidateRecords(snapshot);
        var ordered = snapshot.OrderBy(record => record.EntityLogicalName, StringComparer.Ordinal)
            .ThenBy(record => record.Id).ToArray();
        return new TestTenantManifest(organization, product, code, Array.AsReadOnly(ordered));
    }

    /// <summary>以不覆寫的原子發布寫入本地清單，並只回傳固定格式檔案路徑。</summary>
    /// <param name="artifactRoot">由程序組合根固定指定的 artifacts 根目錄，不得來自 HTTP 或秘密。</param>
    /// <param name="cancellationToken">呼叫端取消訊號；任何尚未發布的本次暫存檔都會清理。</param>
    /// <returns>唯一 test-tenants/{code}.json 路徑；呼叫端只應輸出檔名而非記錄內容。</returns>
    internal string Save(string artifactRoot, CancellationToken cancellationToken = default)
    {
        var directory = GetDirectory(artifactRoot, create: true);
        var path = Path.Combine(directory, Code + ".json");
        var temporaryPath = Path.Combine(directory, "." + Code + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       bufferSize: 4096, FileOptions.WriteThrough))
            {
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("version", CurrentVersion);
                    writer.WriteString("organization", Organization);
                    writer.WriteString("product", Product);
                    writer.WriteString("code", Code);
                    writer.WriteStartArray("records");
                    foreach (var record in Records)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        writer.WriteStartObject();
                        writer.WriteString("entity", record.EntityLogicalName);
                        writer.WriteString("id", record.Id.ToString("D"));
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                    writer.Flush();
                }
                if (stream.Length > MaximumBytes)
                    throw new InvalidDataException("Test tenant manifest exceeds its storage bound.");
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, overwrite: false);
            return path;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    /// <summary>以大小、深度、欄位、身份與呼叫端 scope 上限載入 manifest；缺檔或任何歧義均拒絕。</summary>
    /// <param name="artifactRoot">程序固定的 artifacts 根目錄。</param>
    /// <param name="expectedOrganization">已由 ProductionGuard 核准的測試組織名稱。</param>
    /// <param name="expectedCode">本次 cleanup 的租戶代碼；不從檔案反向授權。</param>
    /// <returns>完整驗證且與呼叫端範圍吻合的 manifest。</returns>
    internal static TestTenantManifest Load(string artifactRoot, string expectedOrganization, string expectedCode)
    {
        ValidateCode(expectedCode);
        if (!OrganizationNames.Contains(expectedOrganization))
            throw new InvalidDataException("Test tenant manifest organization is not allowed.");
        var directory = GetDirectory(artifactRoot, create: false);
        var path = Path.Combine(directory, expectedCode + ".json");
        RejectReparsePoint(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096,
            FileOptions.SequentialScan);
        if (stream.Length is <= 0 or > MaximumBytes)
            throw new InvalidDataException("Test tenant manifest size is invalid.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowDuplicateProperties = false,
            MaxDepth = 8
        });
        var root = document.RootElement;
        RequireProperties(root, "version", "organization", "product", "code", "records");
        if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out var parsedVersion) || parsedVersion != CurrentVersion)
            throw new InvalidDataException("Test tenant manifest version is invalid.");

        var organization = ReadString(root, "organization");
        var product = ReadString(root, "product");
        var code = ReadString(root, "code");
        if (!string.Equals(organization, expectedOrganization, StringComparison.Ordinal) ||
            !string.Equals(code, expectedCode, StringComparison.Ordinal))
            throw new InvalidDataException("Test tenant manifest scope does not match the cleanup request.");
        var recordsElement = root.GetProperty("records");
        if (recordsElement.ValueKind != JsonValueKind.Array || recordsElement.GetArrayLength() > MaximumRecords)
            throw new InvalidDataException("Test tenant manifest record set is invalid or oversized.");
        var records = new List<TestTenantRecordId>(recordsElement.GetArrayLength());
        foreach (var element in recordsElement.EnumerateArray())
        {
            RequireProperties(element, "entity", "id");
            var entity = ReadString(element, "entity");
            var idText = ReadString(element, "id");
            if (!Guid.TryParseExact(idText, "D", out var id) || id == Guid.Empty ||
                !string.Equals(idText, id.ToString("D"), StringComparison.Ordinal))
                throw new InvalidDataException("Test tenant manifest record identity is invalid.");
            records.Add(new TestTenantRecordId(entity, id));
        }

        return Create(organization, product, code, records);
    }

    /// <summary>建立固定子目錄或檢查既有路徑，拒絕根目錄及子目錄被 junction／symlink 導出。</summary>
    private static string GetDirectory(string artifactRoot, bool create)
    {
        if (string.IsNullOrWhiteSpace(artifactRoot))
            throw new ArgumentException("Artifact root is required.", nameof(artifactRoot));
        var root = Path.GetFullPath(artifactRoot);
        if (create)
            Directory.CreateDirectory(root);
        RejectDirectoryReparsePoint(root);
        var directory = Path.Combine(root, "test-tenants");
        if (create)
            Directory.CreateDirectory(directory);
        if (!Directory.Exists(directory))
            throw new FileNotFoundException("Test tenant manifest was not found.");
        RejectDirectoryReparsePoint(directory);
        return directory;
    }

    /// <summary>以固定 allowed-property set 拒絕未知、缺少或重複欄位，避免忽略清理範圍擴張資料。</summary>
    private static void RequireProperties(JsonElement element, params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Test tenant manifest object is invalid.");
        var remaining = new HashSet<string>(expected, StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!remaining.Remove(property.Name))
                throw new InvalidDataException("Test tenant manifest contains an unknown or repeated property.");
        if (remaining.Count != 0)
            throw new InvalidDataException("Test tenant manifest is incomplete.");
    }

    /// <summary>讀取必要非空字串欄位，不回顯不可信本文。</summary>
    private static string ReadString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException("Test tenant manifest text field is invalid.");
        return value.GetString()!;
    }

    /// <summary>驗證固定產品、測試組織與路徑安全代碼；拒絕正式環境和任意檔案路徑。</summary>
    private static void ValidateScope(string organization, string product, string code)
    {
        if (!OrganizationNames.Contains(organization))
            throw new InvalidDataException("Test tenant manifest organization is not allowed.");
        if (!ProductNames.Contains(product))
            throw new InvalidDataException("Test tenant manifest product is not allowed.");
        ValidateCode(code);
    }

    /// <summary>代碼限制為一至三十二個小寫 ASCII 字母、數字或中線，且首字必須為英數。</summary>
    private static void ValidateCode(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length > 32 || !IsAsciiAlphaNumeric(code[0]) ||
            code.Any(character => !IsAsciiAlphaNumeric(character) && character != '-'))
            throw new InvalidDataException("Test tenant manifest code is invalid.");
    }

    /// <summary>回傳固定 ASCII 字母數字測試結果，不受目前文化或 Unicode 相容字影響。</summary>
    private static bool IsAsciiAlphaNumeric(char value) => value is >= 'a' and <= 'z' or >= '0' and <= '9';

    /// <summary>驗證必須恰有一個 account 與一個 slot request，並拒絕重複 entity+GUID 身份。</summary>
    private static void ValidateRecords(List<TestTenantRecordId> records)
    {
        if (records.Count is < 2 or > MaximumRecords || records.Count(record => record.EntityLogicalName == "account") != 1 ||
            records.Count(record => record.EntityLogicalName == "new_slotrequest") != 1)
            throw new InvalidDataException("Test tenant manifest must identify one tenant and one slot request.");
        var identities = new HashSet<(string Entity, Guid Id)>();
        foreach (var record in records)
            if (!identities.Add((record.EntityLogicalName, record.Id)))
                throw new InvalidDataException("Test tenant manifest contains a duplicate stable record identity.");
    }

    /// <summary>拒絕存在的 reparse point，避免清理證據路徑導向 repo 外或使用者選定位置。</summary>
    private static void RejectDirectoryReparsePoint(string path)
    {
        if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Test tenant manifest directory is unsafe.");
    }

    /// <summary>拒絕 manifest 檔案本身為 link，防止 reader 讀到固定租戶路徑外的內容。</summary>
    private static void RejectReparsePoint(string path)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Test tenant manifest file is unsafe.");
    }
}
