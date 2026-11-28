/*
檔案整體說明
目的與定位：FS完整baseline節點診斷的純值工具，從已驗SHA的JSON還原guard原CLR純值，
不把JsonElement或字串化數字拿來誤判變更。它不連SDK、不修改guard、不建立寫入授權。
主要功能與流程：封閉型別表還原完整values，重複路徑及未知型別拒絕；以既有guard的純函式重建
baseline＋計畫增量，最後回傳僅路徑／型別／差異種類的安全清單，不輸出scalar正文。
邊界與容量：最多一百萬節點、單路徑2048字元、差異最多4096筆，超量拒絕而非截斷。
反射僅呼叫本方案既有純候選工廠及WithAdditions，例外原型別重拋，沒有SDK或背景工作。
使用邊界與維護：JSON與外部service由呼叫端持有，所有新字典、集合及反射描述只存活本次同步呼叫；
結果不持有輸入文件、SDK、身分或秘密。取消逐節點檢查，故障不回部分結果。
驗證：離線測試保護精確數值／enum／GUID型別、重複與未知拒絕、安全差異不含正文。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using Microsoft.Xrm.Sdk.Metadata;
using SpeechMessage.Dynamics.Deployment.Schema;
namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>診斷用純值還原與差異工具，不擁有SDK且永遠沒有寫入或授權能力。</summary>
internal static class BaselineNodeReplayProbe
{
    /// <summary>已驗SHA的values還原，封閉CLR型別與唯一節點保證不被JSON寬鬆轉換改寫。</summary>
    /// <param name="values">呼叫端擁有文件內的values物件，返回後不保留JsonElement。</param>
    /// <returns>當次獨立的完整純值字典，不含任何文件或SDK參照。</returns>
    internal static IReadOnlyDictionary<string, UiMetadataPublicationGuard.Scalar> RestoreScalars(JsonElement values)
    {
        if (values.ValueKind != JsonValueKind.Object) throw Invalid();
        var result = new Dictionary<string, UiMetadataPublicationGuard.Scalar>(StringComparer.Ordinal);
        foreach (var node in values.EnumerateObject())
        {
            CheckPath(node.Name);
            if (result.Count >= 1_000_000 || node.Value.ValueKind != JsonValueKind.Object) throw Invalid();
            var fields = node.Value.EnumerateObject().Select(item => item.Name).ToArray();
            if (fields.Length != 2 || fields.Distinct(StringComparer.Ordinal).Count() != 2 ||
                !node.Value.TryGetProperty("type", out var typeNode) || !node.Value.TryGetProperty("value", out var value)) throw Invalid();
            var type = typeNode.GetString() ?? throw Invalid();
            object? scalar = type switch
            {
                "null" when value.ValueKind == JsonValueKind.Null => null,
                "Contract" or "System.String" => value.GetString() ?? throw Invalid(),
                "System.Boolean" => value.GetBoolean(),
                "System.Byte" => value.GetByte(), "System.SByte" => value.GetSByte(),
                "System.Int16" => value.GetInt16(), "System.UInt16" => value.GetUInt16(),
                "System.Int32" or "Collection" or "DateTimeKind" => value.GetInt32(),
                "System.UInt32" => value.GetUInt32(), "System.Int64" or "DateTime" => value.GetInt64(),
                "System.UInt64" => value.GetUInt64(), "System.Single" => value.GetSingle(),
                "System.Double" => value.GetDouble(), "System.Decimal" => value.GetDecimal(),
                "System.Guid" => value.GetGuid(), "System.Char" => (value.GetString() ?? throw Invalid()).Single(),
                _ when type.StartsWith("Microsoft.Xrm.Sdk.", StringComparison.Ordinal) &&
                    typeof(EntityMetadata).Assembly.GetType(type) is { IsEnum: true } => value.GetInt64(),
                _ => throw Invalid()
            };
            if (!result.TryAdd(node.Name, new(type, scalar))) throw Invalid();
        }
        return result;
    }

    /// <summary>呼叫正式純候選工廠，確保診斷和原寫前欄位完全相同；不執行任何SDK寫入。</summary>
    /// <param name="field">原plan hash綁定的欄位。</param><param name="audit">原引擎採用的稽核值。</param>
    /// <param name="language">原before基礎LCID。</param><returns>當案唯一擁有的SDK候選，僅用於脫離純值投影。</returns>
    internal static AttributeMetadata Candidate(SchemaField field, bool audit, int language) =>
        (AttributeMetadata)Invoke(typeof(SchemaDeploymentEngine), "Attribute", [field, audit, language]);

    /// <summary>呼叫正式guard的完整預期組合；actual僅供既有固定白名單與正式鍵狀態檢查。</summary>
    /// <param name="baseline">已SHA綁定且忠實還原的首寫前基準。</param><param name="delta">本原plan候選與正式ID。</param>
    /// <param name="actual">當前指定來源完整純值。</param><param name="unpublished">未發布來源必須要求全部新增節點存在。</param>
    /// <param name="ct">同步owner取消，沒有註冊或背景工作。</param><returns>正式guard計算的完整預期。</returns>
    internal static IReadOnlyDictionary<string, UiMetadataPublicationGuard.Scalar> Expected(
        UiMetadataPublicationGuard.Snapshot baseline, UiMetadataPublicationGuard.SchemaDelta delta,
        Dictionary<string, UiMetadataPublicationGuard.Scalar> actual, bool unpublished, CancellationToken ct) =>
        (IReadOnlyDictionary<string, UiMetadataPublicationGuard.Scalar>)Invoke(typeof(UiMetadataPublicationGuard),
            "WithAdditions", [baseline, delta.Additions, actual, unpublished, ct]);

    /// <summary>完整比較後只回安全路徑及型別，label／XML／字串正文一律不進報告。</summary>
    /// <param name="expected">原始基準加計畫增量的純值。</param><param name="actual">指定來源完整純值。</param>
    /// <param name="ct">逐節點取消。</param><returns>完整且有界的安全差異，零筆才表示兩字典相同。</returns>
    internal static IReadOnlyList<Difference> Compare(IReadOnlyDictionary<string, UiMetadataPublicationGuard.Scalar> expected,
        IReadOnlyDictionary<string, UiMetadataPublicationGuard.Scalar> actual, CancellationToken ct)
    {
        if (expected.Count > 1_000_000 || actual.Count > 1_000_000) throw Invalid();
        var paths = new SortedSet<string>(expected.Keys, StringComparer.Ordinal); paths.UnionWith(actual.Keys);
        if (paths.Count > 1_000_000) throw Invalid();
        var result = new List<Difference>();
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested(); CheckPath(path);
            var oldFound = expected.TryGetValue(path, out var oldValue); var newFound = actual.TryGetValue(path, out var newValue);
            if (oldFound && newFound && oldValue == newValue) continue;
            if (result.Count == 4096) throw Invalid();
            result.Add(new(path, !oldFound ? "Added" : !newFound ? "Removed" : "Changed",
                oldFound ? oldValue!.Type : "missing", newFound ? newValue!.Type : "missing"));
        }
        ct.ThrowIfCancellationRequested(); return result.AsReadOnly();
    }

    /// <summary>只反射本方案指定純函式，內部拒絕例外原型別保留，沒有catch後繼續或寫入fallback。</summary>
    /// <param name="type">已知本方案型別。</param><param name="method">固定純函式名稱。</param>
    /// <param name="arguments">本次區域候選，不保存。</param><returns>純函式非null結果。</returns>
    private static object Invoke(Type type, string method, object[] arguments)
    {
        var target = type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static) ?? throw Invalid();
        try { return target.Invoke(null, arguments) ?? throw Invalid(); }
        catch (TargetInvocationException failure) when (failure.InnerException is not null)
        { ExceptionDispatchInfo.Capture(failure.InnerException).Throw(); throw; }
    }

    /// <summary>只允許guard metadata路徑的封閉字元，避免正文或未驗文字混入安全差異。</summary>
    /// <param name="path">欲輸出的完整metadata節點路徑。</param>
    private static void CheckPath(string path)
    {
        if (path.Length is 0 or > 2048 || path.Any(character => !char.IsAsciiLetterOrDigit(character)
            && character is not ('.' or '_' or '[' or ']' or ':' or '-'))) throw Invalid();
    }

    /// <summary>固定無正文拒絕訊息，不回顯原值、JSON或任意型別名。</summary>
    private static InvalidDataException Invalid() => new("FS baseline replay boundary rejected.");

    /// <summary>唯讀差異純值，永遠不包含scalar原值或寫入授權。</summary>
    /// <param name="Path">受限metadata路徑。</param><param name="Kind">新增／缺少／改變。</param>
    /// <param name="ExpectedType">預期CLR分類。</param><param name="ActualType">當前CLR分類。</param>
    internal sealed record Difference(string Path, string Kind, string ExpectedType, string ActualType);
}
