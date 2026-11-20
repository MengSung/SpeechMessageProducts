/*
 * 檔案整體說明
 *
 * 目的與定位：定義 P8-01c 遷移前的 ID-only 復原備份格式與新檔寫入邊界；不連線、不查詢、不持有 SDK 或秘密。
 * 主要功能與流程：以已由唯讀 adapter 核實的來源實體、資料庫記錄 ID 與教會 ID 建立備份；
 * 只接受 goodshepherdback、
 * new_fee／new_dedication_booking、非空 GUID、唯一來源主鍵及有界集合。輸出含格式版本、組織 ID、
 * UTC 時間及 records；每筆恰有 entity、recordId、churchId，不接受姓名、欄位本文或自由擴充資料。
 * 寫入採 UTF-8 無 BOM、FileMode.CreateNew 與 flush-to-disk；發生中斷時僅清理由本次呼叫建立的未完成檔，
 * 不覆寫既有證據。路徑目錄由 CLI 固定在 artifacts/p8 並先核對 link/junction；此型別不接受檔名輸入。
 * 所有候選先完整驗證再序列化，最多 50000 筆且只活在本次命令；不建立背景工作、共享快取或資源租約。
 * OfferingMigrationBackupTests 以合成資料保護資料白名單、身份唯一、拒絕錯誤組織及既有檔不可覆寫；
 * 離線測試不代表備份已從實際組織讀取。
 * 使用邊界與維護：CLI擁有目錄安全檢查與命令生命週期；本型別只建立新檔，驗證錯誤不留下部分輸出。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpeechMessage.Dynamics.Deployment.Offerings;

/// <summary>單一待回填資料列的最小復原身份，不含任何其他欄位或個人資料。</summary>
internal sealed record OfferingMigrationBackupEntry(string Entity, Guid RecordId, Guid ChurchId);

/// <summary>以新檔保存可稽核的 ID-only 遷移前備份；路徑及操作時間由單次CLI owner提供。</summary>
internal static class OfferingMigrationBackupFiles
{
    private const string AuthorizedOrganization = "goodshepherdback";
    private const int MaximumRecords = 50_000;
    private static readonly HashSet<string> AllowedEntities = new(StringComparer.Ordinal)
    {
        "new_fee", "new_dedication_booking"
    };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>驗證完整備份候選後以固定UTC檔名建立新檔；任何既有證據均不覆寫。</summary>
    /// <param name="directory">CLI 已核對且固定的 artifacts/p8 目錄。</param>
    /// <param name="records">完整唯讀查詢後的來源列身份。</param>
    /// <param name="organizationId">經 server identity 驗證的測試組織 ID。</param>
    /// <param name="createdUtc">命令時鐘的 UTC 時間。</param>
    /// <param name="organization">組織名稱；只允許核定的 goodshepherdback。</param>
    /// <returns>新建備份檔的完整路徑。</returns>
    internal static string WriteNew(string directory, IReadOnlyList<OfferingMigrationBackupEntry> records,
        Guid organizationId, DateTimeOffset createdUtc, string organization = AuthorizedOrganization)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(records);
        if (!string.Equals(organization, AuthorizedOrganization, StringComparison.Ordinal)
            || organizationId == Guid.Empty || createdUtc == default || createdUtc.Offset != TimeSpan.Zero
            || records.Count > MaximumRecords)
            throw Invalid();

        var identities = new HashSet<(string Entity, Guid RecordId)>();
        foreach (var record in records)
            if (record is null || !AllowedEntities.Contains(record.Entity)
                || record.RecordId == Guid.Empty || record.ChurchId == Guid.Empty
                || !identities.Add((record.Entity, record.RecordId)))
                throw Invalid();

        Directory.CreateDirectory(directory);
        var fileName = "offering-backup-" + createdUtc.ToString("yyyyMMdd-HHmmssfff",
            System.Globalization.CultureInfo.InvariantCulture) + ".json";
        var path = Path.GetFullPath(Path.Combine(directory, fileName));
        var createdByThisCall = false;
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.WriteThrough);
            createdByThisCall = true;
            var document = new OfferingMigrationBackupDocument(1, organization, organizationId, createdUtc,
                Array.AsReadOnly(records.ToArray()));
            JsonSerializer.Serialize(stream, document, JsonOptions);
            stream.WriteByte((byte)'\n');
            stream.Flush(flushToDisk: true);
            return path;
        }
        catch
        {
            if (createdByThisCall)
            {
                try { File.Delete(path); }
                catch { }
            }
            throw;
        }
    }

    /// <summary>用固定訊息拒絕違反備份契約的身份或內容，避免例外洩漏輸入本文。</summary>
    private static InvalidDataException Invalid() =>
        new("Offering migration backup identity or scope is invalid.");

    /// <summary>只含已核准欄位的序列化文件；不得加入資料列本文或聯絡人資訊。</summary>
    private sealed record OfferingMigrationBackupDocument(int Version, string Organization, Guid OrganizationId,
        DateTimeOffset CreatedUtc, IReadOnlyList<OfferingMigrationBackupEntry> Records);
}
