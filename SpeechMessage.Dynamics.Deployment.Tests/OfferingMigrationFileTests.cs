/*
 * 檔案整體說明
 *
 * 目的與定位：保護奉獻類別遷移plan及操作專屬批次紀錄的檔案契約。
 * 主要功能與流程：以單一暫存目錄執行plan序列化、重新讀取、hash核對與apply log逐筆寫入。
 * 輸入皆為合成純值與測試暫存目錄，不呼叫Deployment connector、不讀User Secrets，也不連接組織。
 * 決定性斷言：plan JSON與Markdown須一併以UTF-8無BOM、CRLF完整發布且不可覆寫；reader拒絕重複或
 * 未知JSON欄位及超過容量的檔案；apply紀錄每筆flush後可立即讀取，達容量上限時拒絕後續批次。
 * 使用邊界與維護：檔案及writer由單一測試擁有，測試結束以Dispose及遞迴清除暫存目錄；
 * 產品輸出則由operation owner持有，任何失敗不得覆寫原有證據。
 * 離線檔案測試僅證明本機格式與owner行為，不代表實機plan核准或資料轉換成功。
 */
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using SpeechMessage.Dynamics.Deployment.Offerings;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以獨立暫存資料夾與合成plan驗證遷移輸入輸出有界且失敗不覆蓋既有證據。</summary>
public sealed class OfferingMigrationFileTests
{
    private const string Fingerprint = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    /// <summary>plan輸出同時發布JSON與Markdown，行尾、BOM、重複執行與來源主鍵洩漏皆可決定性檢查。</summary>
    [Fact]
    public void WritePlan_PublishesJsonAndMarkdownWithoutOverwrite()
    {
        using var directory = new TemporaryDirectory();
        var plan = CreatePlan();
        var jsonPath = OfferingMigrationFiles.WritePlan(directory.Path, plan);
        var markdownPath = Path.Combine(directory.Path, "offering-plan.md");

        Assert.True(File.Exists(jsonPath));
        Assert.True(File.Exists(markdownPath));
        foreach (var path in new[] { jsonPath, markdownPath })
        {
            var bytes = File.ReadAllBytes(path);
            Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.True(bytes.AsSpan().EndsWith(new byte[] { 0x0D, 0x0A }));
            for (var index = 0; index < bytes.Length; index++)
                if (bytes[index] == 0x0A) Assert.True(index > 0 && bytes[index - 1] == 0x0D);
        }
        Assert.Contains("RequiredLevel", File.ReadAllText(markdownPath), StringComparison.Ordinal);
        var parsed = OfferingMigrationFiles.ReadPlan(jsonPath, directory.Path, out var hash);
        Assert.Equal(plan.OrganizationId, parsed.OrganizationId);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(jsonPath))), hash);
        Assert.Throws<IOException>(() => OfferingMigrationFiles.WritePlan(directory.Path, plan));
    }

    /// <summary>嚴格reader拒絕重複與未知屬性，不能讓JSON最後值覆蓋或擴充未核定欄位。</summary>
    [Theory]
    [InlineData("{\"Organization\":\"one\",\"Organization\":\"two\"}")]
    [InlineData("{\"Unreviewed\":true}")]
    public void ReadPlan_DuplicateOrUnknownProperties_Rejected(string json)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "plan.json");
        File.WriteAllText(path, json);
        Assert.Throws<InvalidDataException>(() => OfferingMigrationFiles.ReadPlan(path, directory.Path));
    }

    /// <summary>plan超過固定容量時在完整反序列化前拒絕，不配置不受限輸入本文。</summary>
    [Fact]
    public void ReadPlan_OverLimit_Rejected()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "plan.json");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(4L * 1024 * 1024 + 1);
        }
        Assert.Throws<InvalidDataException>(() => OfferingMigrationFiles.ReadPlan(path, directory.Path));
    }

    /// <summary>批次flush後紀錄立即可讀；寫超過注入的小型測試上限時不允許截斷或假稱成功。</summary>
    [Fact]
    public void ApplyLog_FlushesEachBatchAndRejectsCapacityOverflow()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "offering-apply-log.jsonl");
        using (var writer = OfferingMigrationFiles.CreateApplyLog(directory.Path, 64))
        {
            writer.WriteLine("batch=1;requested=1;errorCode=none");
            writer.Flush();
            Assert.Throws<IOException>(() => writer.WriteLine("batch=2;requested=1;errorCode=none"));
        }
        Assert.Contains("batch=1", File.ReadAllText(path), StringComparison.Ordinal);
    }

    private static OfferingMigrationPlan CreatePlan() =>
        new(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), "synthetic-org",
            new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero),
            new[]
            {
                new OfferingCategoryPlan(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    "legacy-100000000", "十一奉獻", 1, 100000000, 1, "None")
            },
            0, Array.Empty<string>(), 1, Fingerprint, Fingerprint, "None", "Unknown",
            Array.Empty<OfferingLabelDifference>());

    /// <summary>只擁有測試建立的唯一暫存目錄，Dispose確定移除其內檔案。</summary>
    private sealed class TemporaryDirectory : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "offering-migration-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

        /// <summary>建立本案獨占目錄，與repo及其他案例輸出隔離。</summary>
        internal TemporaryDirectory() => Directory.CreateDirectory(Path);

        /// <summary>釋放所有本案檔案與目錄，不觸及固定artifacts或其他使用者資料。</summary>
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
