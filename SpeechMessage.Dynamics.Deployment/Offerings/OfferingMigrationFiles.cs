/*
 * 檔案整體說明
 *
 * 目的與定位：提供奉獻類別轉換的plan嚴格讀取、安全輸出及單次apply操作紀錄。
 * 主要功能與流程：先驗證組織artifact路徑及輸入格式，再用單次檔案快照計算SHA；輸出以暫存檔完整flush後發布。
 * Plan只存組織伺服器身份、選項標籤、RequiredLevel、彙總筆數與穩定摘要，不含業務列主鍵、
 * 付款人、credential或SDK物件。apply對輸入plan使用固定4MiB上限、嚴格JSON、重複欄位拒絕，
 * 並以原始檔案SHA-256供使用者核對；hash只保護完整性，不代表簽章或寫入授權。
 * 每個輸出檔在operation專屬目錄以CreateNew建立暫存檔，UTF-8無BOM、CRLF、Flush(true)、
 * 關閉後才以禁止覆寫的Move發布；失敗只刪本次暫存檔。Apply log最多16MiB，每批Flush會同步
 * flush到底層檔案，容量耗盡或寫入失敗即讓呼叫端停止後續批次。
 * 使用邊界與維護：所有stream、serializer buffer與候選都由單次呼叫持有並Dispose，沒有跨命令快取或背景工作。
 * 驗證由合成輸入測試涵蓋格式、容量、重複欄位、禁止覆寫及flush錯誤；不代表組織實機結果。
 */
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SpeechMessage.Dynamics.Deployment.Schema;

namespace SpeechMessage.Dynamics.Deployment.Offerings;

/// <summary>奉獻遷移的strict JSON與有界操作專屬證據writer。</summary>
internal static class OfferingMigrationFiles
{
    private const int MaximumPlanBytes = 4 * 1024 * 1024;
    private const int MaximumLogBytes = 16 * 1024 * 1024;

    /// <summary>開檔後重驗大小與link屬性，嚴格拒絕未知／重複屬性，不把不完整plan當核准內容。</summary>
    internal static OfferingMigrationPlan ReadPlan(string path, string organizationRoot) =>
        ReadPlan(path, organizationRoot, out _);

    /// <summary>以單一受限檔案快照解析及計算SHA，避免檢查內容與實際核准摘要來自兩次不同讀取。</summary>
    internal static OfferingMigrationPlan ReadPlan(string path, string organizationRoot, out string sha256)
    {
        if (!DeploymentCli.IsContainedPath(organizationRoot, path, out var fullPath))
            throw new InvalidDataException("Offering migration plan path is outside its authorized directory.");
        var info = new FileInfo(fullPath);
        if (!info.Exists || info.Length is <= 0 or > MaximumPlanBytes
            || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Offering migration plan is unavailable or exceeds its bounded budget.");
        byte[] bytes;
        using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.Length is <= 0 or > MaximumPlanBytes)
                throw new InvalidDataException("Offering migration plan exceeds its bounded budget.");
            bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) throw new InvalidDataException("Offering migration plan changed while being read.");
        }
        sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            SchemaFiles.RejectDuplicateProperties(document.RootElement);
            return document.RootElement.Deserialize<OfferingMigrationPlan>(SchemaFiles.Json)
                ?? throw new InvalidDataException("Offering migration plan is empty.");
        }
        catch (JsonException)
        {
            throw new InvalidDataException("Offering migration plan JSON does not match the reviewed format.");
        }
    }

    /// <summary>計算plan原始位元組的SHA-256，保持大小寫不敏感驗證但不正規化檔案本文。</summary>
    internal static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > MaximumPlanBytes)
            throw new InvalidDataException("Offering migration plan exceeds its bounded budget.");
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>以唯一檔名發布JSON plan，先完整flush及關閉暫存檔，永不覆寫既有證據。</summary>
    internal static string WritePlan(string directory, OfferingMigrationPlan plan)
    {
        var path = Path.Combine(directory, "offering-plan.json");
        WriteNew(path, JsonSerializer.Serialize(plan, SchemaFiles.Json));
        try
        {
            WriteNew(Path.Combine(directory, "offering-plan.md"), FormatPlanMarkdown(plan));
        }
        catch
        {
            File.Delete(path);
            throw;
        }
        return path;
    }

    /// <summary>建立CreateNew的apply log；每批Flush均落到檔案，硬上限不可超過產品16MiB。</summary>
    internal static TextWriter CreateApplyLog(string directory, int maximumBytes = MaximumLogBytes)
    {
        if (maximumBytes is < 1 or > MaximumLogBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var path = Path.Combine(directory, "offering-apply-log.jsonl");
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        return new BoundedFlushingWriter(stream, maximumBytes);
    }

    /// <summary>輸出人工可讀的結構與聚合數量；不包含來源記錄主鍵、付款人或例外內容。</summary>
    [I18nIgnore("只產生供操作者審核的固定繁中計畫文件，不是產品執行期介面資源。")]
    private static string FormatPlanMarkdown(OfferingMigrationPlan plan)
    {
        var text = new StringBuilder();
        text.AppendLine("# 奉獻類別轉換計畫");
        text.AppendLine();
        text.AppendLine("- 組織：" + plan.Organization);
        text.AppendLine("- 組織 ID：" + plan.OrganizationId.ToString("D"));
        text.AppendLine("- 建立時間（UTC）：" + plan.CreatedUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        text.AppendLine("- 來源筆數：" + plan.SourceRecordCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        text.AppendLine("- 無法判定教會筆數：" + plan.UnresolvedChurchRecordCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        text.AppendLine("- `new_fee.new_category` RequiredLevel：" + plan.FeeCategoryRequiredLevel);
        text.AppendLine("- `new_dedication_booking.new_dedication_category` RequiredLevel：" + plan.BookingCategoryRequiredLevel);
        text.AppendLine("- 來源摘要：" + plan.SourceFingerprint);
        text.AppendLine("- 中繼資料摘要：" + plan.MetadataFingerprint);
        text.AppendLine();
        text.AppendLine("## 類別候選");
        foreach (var category in plan.Categories)
            text.AppendLine("- 教會 `" + category.ChurchId.ToString("D") + "`；代碼 `" + category.Code
                + "`；名稱「" + category.Name + "」；選項值 " + category.LegacyOptionValue!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "；順序 " + category.SortOrder.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "；來源筆數 " + category.RecordCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        text.AppendLine();
        text.AppendLine("## 未對應畫面標籤");
        if (plan.UnmatchedScreenLabels.Count == 0) text.AppendLine("- 無");
        else foreach (var label in plan.UnmatchedScreenLabels) text.AppendLine("- " + label);
        return text.ToString();
    }

    /// <summary>一次性writer避免半份JSON被讀取；UTF-8無BOM及CRLF符合repo證據格式。</summary>
    private static void WriteNew(string path, string content)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd('\n').Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n";
        var bytes = Encoding.UTF8.GetBytes(normalized);
        if (bytes.Length > MaximumPlanBytes) throw new InvalidDataException("Offering migration output exceeds its bounded budget.");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>操作專屬apply log，Flush逐批flush OS檔案handle且在超出上限時拒絕後續寫入。</summary>
    private sealed class BoundedFlushingWriter(FileStream stream, int maximumBytes) : TextWriter
    {
        private readonly StreamWriter writer = new(stream, new UTF8Encoding(false), 4096, leaveOpen: true);
        private long bufferedBytes;
        private bool disposed;

        /// <summary>固定UTF-8輸出，不建立跨命令文化或可變格式狀態。</summary>
        public override Encoding Encoding => new UTF8Encoding(false);

        /// <summary>以實際UTF-8 byte count核對硬上限後寫入，不將超限批次摘要截斷成似成功紀錄。</summary>
        public override void WriteLine(string? value)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var text = (value ?? string.Empty) + "\r\n";
            var textBytes = Encoding.GetByteCount(text);
            if (stream.Length + bufferedBytes + textBytes > maximumBytes)
                throw new IOException("Offering migration operation log reached its bounded capacity.");
            bufferedBytes += textBytes;
            writer.Write(text);
        }

        /// <summary>先清空文字buffer，再Flush(true)，成功返回才允許executor送出下一批。</summary>
        public override void Flush()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            writer.Flush();
            stream.Flush(flushToDisk: true);
            bufferedBytes = 0;
        }

        /// <summary>釋放唯一文字writer與底層檔案stream；重複Dispose安全。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposed) return;
            try
            {
                if (disposing)
                {
                    try { Flush(); }
                    finally
                    {
                        try { writer.Dispose(); }
                        finally { stream.Dispose(); }
                    }
                }
            }
            finally
            {
                disposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
