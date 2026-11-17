/*
檔案整體說明
目的與定位：P7切換的本機write-ahead回復紀錄，SDK送出前先flush嘗試項目，成功後再flush完成狀態。
主要流程：首次CreateNew固定switch-log.json，後續同目錄temp寫入flush後原子取代；原XAML只在此本機紀錄。
安全與邊界：不保存secure明文，設定快照只接受受管理模板；上限128MiB容納16MiB流程字串的JSON逸出。
資源與隔離：stream每次寫完確定關閉；沒有static mutable或跨命令cache，temp失敗清理且不覆蓋完整舊證據。
失敗與驗證：strict JSON拒未知／重複欄位、links與hash錯誤；executor測試驗送達後故障仍能精確回復。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace SpeechMessage.Dynamics.Deployment.Switching;

/// <summary>已嘗試操作及預先分配的secure正式ID；即使Create回應遺失也能精確清孤兒。</summary>
/// <param name="Index">原plan操作索引。</param><param name="SecureCandidateId">本次可建立secure列的唯一ID。</param>
/// <param name="Completed">SDK全部成功返回且已檢查caller取消。</param><param name="Restored">本次操作已回復。</param>
internal sealed record SwitchLogEntry(int Index, Guid SecureCandidateId, bool Completed, bool Restored)
{
    /// <summary>SDK送出前讀回並flush的當下安全投影；原XAML完整字串在Before快照，不在entry重複保留。</summary>
    public SwitchOperationState? BeforeState { get; init; }
    /// <summary>SDK返回後實際讀回的安全投影；未返回／取消時為null，不偽造為預期成功狀態。</summary>
    public SwitchOperationState? AfterState { get; init; }
}

/// <summary>逐步實際狀態純值；只有新步驟核定設定、secure presence與流程定義hash，沒有secure內容。</summary>
/// <param name="State">正式statecode。</param><param name="Configuration">新步驟核定設定，舊步驟與流程為空。</param>
/// <param name="HasSecureConfiguration">僅新步驟secure presence，不讀或hash秘密。</param><param name="XamlSha256">流程原定義UTF8摘要，其他操作為空。</param>
internal sealed record SwitchOperationState(int State, string Configuration, bool HasSecureConfiguration, string XamlSha256);

/// <summary>不可變單次執行紀錄；只持純值，原XAML在本機不進版控，真secure值永不序列化。</summary>
/// <param name="SchemaVersion">格式識別。</param><param name="DocumentKind">switch-log固定識別。</param><param name="Plan">已審核計畫。</param>
/// <param name="Before">完整原況，設定經模板驗證。</param><param name="Entries">write-ahead順序。</param>
/// <param name="AfterFingerprint">最近完成後的metadata指紋。</param><param name="Sha256">不含自身的完整性摘要。</param>
internal sealed record SwitchLog(int SchemaVersion, string DocumentKind, SwitchPlan Plan, SwitchSnapshot Before,
    IReadOnlyList<SwitchLogEntry> Entries, string AfterFingerprint, string Sha256)
{
    private static readonly JsonSerializerOptions Options = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 64 };

    /// <summary>核對容量、links、重複與未知欄位後從同一buffer解析，所有handle在返回前關閉。</summary>
    internal static SwitchLog Read(string path)
    {
        Schema.SchemaDeploymentCli.EnsureNoLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 1 or > 128 * 1024 * 1024) throw Invalid();
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw Invalid();
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
            Schema.SchemaFiles.RejectDuplicateProperties(document.RootElement);
            var result = JsonSerializer.Deserialize<SwitchLog>(bytes, Options) ?? throw Invalid();
            if (result.SchemaVersion != 1 || result.DocumentKind != "switch-log" || result.Sha256 != SwitchPlanner.Hash(result with { Sha256 = "" })) throw Invalid();
            return result;
        }
        catch (JsonException) { throw Invalid(); }
    }

    /// <summary>首次不可覆蓋，後續原子替換；write-ahead返回前必須flush磁碟，不可先送SDK再寫證據。</summary>
    internal static SwitchLog Persist(string path, SwitchLog value, bool create)
    {
        value = value with { Sha256 = "" };
        value = value with { Sha256 = SwitchPlanner.Hash(value) };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        if (bytes.Length > 128 * 1024 * 1024) throw Invalid();
        Schema.SchemaDeploymentCli.EnsureNoLinks(path);
        var destination = create ? path : path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var created = false;
        try
        {
            using (var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { created = true; stream.Write(bytes); stream.Flush(true); }
            if (!create) File.Move(destination, path, true);
            return value;
        }
        catch
        {
            if (created && !create) File.Delete(destination);
            throw;
        }
    }

    /// <summary>固定安全錯誤不保留原文JSON或XAML。</summary>
    private static InvalidDataException Invalid() => new("Invalid switch log.");
}
