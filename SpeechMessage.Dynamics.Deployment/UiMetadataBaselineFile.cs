/*
檔案整體說明
目的與定位：FS結構Apply首寫前的完整wire基準證據；保存UiMetadataPublicationGuard已驗證
published=unpublished的所有純值，不以SchemaSnapshot的規劃欄位投影冒充完整原基準。
主要流程：依正式組織ID／plan hash綁定排序節點，先計算預期UTF8無BOM／CRLF本文SHA；
SchemaFiles.CreateNew暫存、Flush(true)、關閉及禁止覆寫發布後，重新開檔串流讀回SHA比對，
成功才保存SHA憑據並返回引擎；任一寫入、關閉、讀回或不符皆拒絕第一個SDK寫入。
適用範圍：只有本輪發布範圍既有表的完整wire；沒有既有表時明確保存空值，不包含不存在新表。
證據是metadata，不是組織DB備份；缺少歷史原基準不能靠新檔宣稱已復原。
隔離與生命週期：每次命令獨有固定輸出目錄；僅純值、無credential或業務列，保留到計畫終點。
單檔100MiB上限；序列化有界暫存，讀回以串流計算SHA；所有stream用using關閉，不跨命令快取。
取消在I/O前後檢查，沒有背景Task或共享可變狀態；故障保留已發布證據、不刪舊檔、不自動重試。
驗證：真Engine首寫故障前完整檔存在、同名衝突零寫入、SHA竄改拒絕及獨占開檔確認釋放。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using SpeechMessage.Dynamics.Deployment.Schema;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>由Apply同步呼叫的證據owner，所有本地handle在返回前關閉，失敗禁止SDK首寫。</summary>
internal static class UiMetadataBaselineFile
{
    private const int MaximumBytes = 100 * 1024 * 1024;

    /// <summary>完整原基準先落檔並驗預期SHA；任何失敗向外保留，不讓引擎繼續寫入。</summary>
    /// <param name="directory">CLI核定的單命令輸出目錄，非業務輸入。</param>
    /// <param name="snapshot">已驗證兩來源相等的純值；null表示沒有既有發布端點。</param>
    /// <param name="plan">已重驗server身分及完整hash的同輪計畫。</param>
    /// <param name="ct">同步命令取消，檔案關閉不依賴取消token。</param>
    internal static void Persist(string directory, UiMetadataPublicationGuard.Snapshot? snapshot, SchemaPlan plan, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var values = new SortedDictionary<string, UiMetadataPublicationGuard.Scalar>(StringComparer.Ordinal);
        if (snapshot is not null)
            foreach (var pair in snapshot.Values) { ct.ThrowIfCancellationRequested(); values.Add(pair.Key, pair.Value); }
        var evidence = new { FormatVersion = 1, plan.OrganizationId, PlanSha256 = plan.Sha256,
            plan.SchemaHash, PublishedAndUnpublishedEquivalent = true, Values = values };
        var content = JsonSerializer.Serialize(evidence, SchemaFiles.Json).Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd('\n').Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n";
        if (Encoding.UTF8.GetByteCount(content) > MaximumBytes) throw new InvalidDataException("Full baseline exceeds its bounded budget.");
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        var path = SchemaFiles.WriteNew(directory, "before-ui-metadata-baseline.json", evidence);
        VerifySha256(path, expected, ct);
        _ = SchemaFiles.WriteNew(directory, "before-ui-metadata-baseline.sha256.json",
            new { plan.OrganizationId, PlanSha256 = plan.Sha256, BaselineSha256 = expected, NodeCount = values.Count });
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>以受限串流讀回已關閉的證據；預期SHA由寫前本文計算，不可重信任當前檔內容。</summary>
    /// <param name="path">本輪固定證據路徑，拒絕link與超量。</param>
    /// <param name="expected">寫前序列化本文的完整SHA256。</param>
    /// <param name="ct">同步命令owner取消token。</param>
    internal static void VerifySha256(string path, string expected, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Full baseline cannot be a link.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > MaximumBytes) throw new InvalidDataException("Full baseline readback budget rejected.");
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        ct.ThrowIfCancellationRequested();
        if (!string.Equals(actual, expected, StringComparison.Ordinal)) throw new InvalidDataException("Full baseline SHA readback mismatch.");
    }
}
