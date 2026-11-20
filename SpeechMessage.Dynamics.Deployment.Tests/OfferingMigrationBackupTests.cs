/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P8-01c 遷移前備份的資料白名單、身分唯一性與不覆寫行為。
 * 主要流程：使用合成的 goodshepherdback 身分與來源記錄 ID，呼叫正式備份序列化器，
 * 再檢查輸出 JSON 僅含版本、組織識別、時間及來源實體／記錄 ID／教會 ID。
 * 故障注入包含不允許的組織、空白身分、重複實體主鍵、未知來源實體及既有目的檔；
 * 決定性斷言是拒絕不完整或越界候選、絕不覆寫既有證據、輸出不含姓名等額外欄位。
 * 本測試不連 Dataverse、不讀取 User Secrets，合成 GUID 不代表真實組織或資料列。
 * 每案建立唯一暫存目錄並由 finally 遞迴清理；產品備份則由呼叫端固定寫入
 * artifacts/p8，採新檔建立並保留為遷移前復原證據。
 * 使用邊界與維護：暫存檔及測試目錄只由本案例擁有，finally 負責清理；正式備份不可覆寫且留待回復使用。
 */
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using SpeechMessage.Dynamics.Deployment.Offerings;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>保護遷移備份的資料最小化、資料庫身分與不可覆寫契約。</summary>
public sealed class OfferingMigrationBackupTests
{
    private static readonly Guid OrganizationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid ChurchId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid FeeId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    /// <summary>只序列化核定的組織識別與需回填來源的正式ID，不接受姓名或任意資料本文。</summary>
    [Fact]
    public void WriteNew_ContainsOnlyApprovedIdentityFields()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = OfferingMigrationBackupFiles.WriteNew(directory,
                new[] { new OfferingMigrationBackupEntry("new_fee", FeeId, ChurchId) },
                OrganizationId, new DateTimeOffset(2026, 11, 20, 1, 0, 0, TimeSpan.Zero));

            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            Assert.Equal("goodshepherdback", root.GetProperty("organization").GetString());
            Assert.Equal(OrganizationId, root.GetProperty("organizationId").GetGuid());
            var entry = Assert.Single(root.GetProperty("records").EnumerateArray());
            Assert.Equal("new_fee", entry.GetProperty("entity").GetString());
            Assert.Equal(FeeId, entry.GetProperty("recordId").GetGuid());
            Assert.Equal(ChurchId, entry.GetProperty("churchId").GetGuid());
            Assert.Equal(3, entry.EnumerateObject().Count());
            Assert.Equal(5, root.EnumerateObject().Count());
            Assert.DoesNotContain("name", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("contact", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>未知組織、錯誤來源、空ID及重複正式列ID都在建立檔案前拒絕。</summary>
    [Fact]
    public void WriteNew_InvalidOrganizationOrRecordIdentity_RejectsWithoutFile()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            Assert.Throws<InvalidDataException>(() => OfferingMigrationBackupFiles.WriteNew(directory,
                new[] { new OfferingMigrationBackupEntry("new_fee", FeeId, ChurchId) },
                OrganizationId, DateTimeOffset.UtcNow, "other-org"));
            Assert.Throws<InvalidDataException>(() => OfferingMigrationBackupFiles.WriteNew(directory,
                new[] { new OfferingMigrationBackupEntry("contact", FeeId, ChurchId) },
                OrganizationId, DateTimeOffset.UtcNow));
            Assert.Throws<InvalidDataException>(() => OfferingMigrationBackupFiles.WriteNew(directory,
                new[] { new OfferingMigrationBackupEntry("new_fee", Guid.Empty, ChurchId) },
                OrganizationId, DateTimeOffset.UtcNow));
            Assert.Throws<InvalidDataException>(() => OfferingMigrationBackupFiles.WriteNew(directory,
                new[]
                {
                    new OfferingMigrationBackupEntry("new_fee", FeeId, ChurchId),
                    new OfferingMigrationBackupEntry("new_fee", FeeId, ChurchId)
                }, OrganizationId, DateTimeOffset.UtcNow));
            Assert.Empty(Directory.EnumerateFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>既有同時間戳備份必須保留原位元組並使新操作失敗，不以覆寫損壞復原證據。</summary>
    [Fact]
    public void WriteNew_ExistingTimestampedFile_IsNeverOverwritten()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var timestamp = new DateTimeOffset(2026, 11, 20, 1, 0, 0, TimeSpan.Zero);
            var existing = Path.Combine(directory, "offering-backup-20261120-010000000.json");
            File.WriteAllText(existing, "preserve");

            Assert.Throws<IOException>(() => OfferingMigrationBackupFiles.WriteNew(directory,
                new[] { new OfferingMigrationBackupEntry("new_fee", FeeId, ChurchId) },
                OrganizationId, timestamp));

            Assert.Equal("preserve", File.ReadAllText(existing));
            Assert.Single(Directory.EnumerateFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>建立本測試唯一持有的空暫存資料夾；呼叫端在finally負責刪除。</summary>
    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "smv-p8-offering-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
