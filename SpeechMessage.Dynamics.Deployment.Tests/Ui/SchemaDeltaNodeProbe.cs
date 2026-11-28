/*
檔案整體說明
目的與定位：FS 第三次寫入前的測試專用唯讀節點診斷，僅比較既有完整投影器產生的純值。
主要功能：列出已知計畫增量的多出、缺少及型別／值變更，輸出節點路徑與型別，不輸出標籤、
XML、密鑰或任何 scalar 值；它不替代已遺失的初始完整 baseline，也不提供發布或寫入能力。
隔離與生命週期：所有字典及差異只屬本次同步呼叫，沒有靜態可變集合、SDK client、背景工作、
計時器或資源所有權。取消前後檢查，差異全部驗證完成後才回傳 detached 唯讀結果。
失敗與驗證：未知路徑、型別或超量整體拒絕；合成測試以 server 補物件、舊標籤漂移、缺失、
正常相等及正常取消確認契約。離線結果不代表已完成 D365 連線或第三次 apply。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>只持有純值的安全差異產生器；不把寫後來源視為原始基準。</summary>
internal static class SchemaDeltaNodeProbe
{
    /// <summary>比較指定增量子樹；最多四千九十六節點，回傳結果不含任何原始值。</summary>
    /// <param name="expected">寫前候選加正式身分的完整純值。</param>
    /// <param name="actual">當前同一正式元件的純值。</param>
    /// <param name="ct">同步工作 owner 取消，沒有註冊或延後工作。</param>
    /// <returns>已驗證的節點差異；相等只代表此子樹，不代表原完整基準已恢復。</returns>
    internal static IReadOnlyList<NodeDifference> CompareNodes(
        IReadOnlyDictionary<string, UiMetadataPublicationGuard.Scalar> expected,
        IReadOnlyDictionary<string, UiMetadataPublicationGuard.Scalar> actual,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        ct.ThrowIfCancellationRequested();
        if (expected.Count > 4096 || actual.Count > 4096) throw Invalid();
        var paths = new SortedSet<string>(expected.Keys, StringComparer.Ordinal);
        paths.UnionWith(actual.Keys);
        if (paths.Count > 4096) throw Invalid();
        var differences = new List<NodeDifference>();
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            if (path.Length > 512 || path.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '[' or ']' or ':' or '-')))
                throw Invalid();
            var hasExpected = expected.TryGetValue(path, out var oldValue);
            var hasActual = actual.TryGetValue(path, out var newValue);
            if (hasExpected) ValidateType(oldValue!.Type);
            if (hasActual) ValidateType(newValue!.Type);
            if (hasExpected && hasActual && oldValue == newValue) continue;
            differences.Add(new NodeDifference(path, !hasExpected ? "Added" : !hasActual ? "Removed" : "Changed",
                hasExpected ? oldValue!.Type : "missing", hasActual ? newValue!.Type : "missing"));
        }
        ct.ThrowIfCancellationRequested();
        return differences.AsReadOnly();
    }

    /// <summary>只允許投影器封閉的型別分類；未知或不安全型別名稱不帶入輸出。</summary>
    /// <param name="type">SDK 投影器的型別代號，不是 scalar 值。</param>
    private static void ValidateType(string type)
    {
        if (type.Length > 200 || type.Any(character =>
            !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_')) ||
            type is not ("null" or "Contract" or "Collection" or "DateTime" or "DateTimeKind") &&
            !type.StartsWith("System.", StringComparison.Ordinal) &&
            !type.StartsWith("Microsoft.Xrm.Sdk.", StringComparison.Ordinal))
            throw Invalid();
    }

    /// <summary>固定無本文拒絕，不回顯任何未驗證路徑、型別或來源值。</summary>
    private static InvalidDataException Invalid() => new("FS diagnostic node boundary rejected.");

    /// <summary>不含 scalar 值的純差異；自動系統元件來源未被本工具證明，永遠不可推導寫入許可。</summary>
    /// <param name="Path">SDK 投影器產生的節點路徑。</param>
    /// <param name="Kind">Added、Removed 或 Changed。</param>
    /// <param name="ExpectedType">寫前型別，缺少為 missing。</param>
    /// <param name="ActualType">當前型別，缺少為 missing。</param>
    internal sealed record NodeDifference(string Path, string Kind, string ExpectedType, string ActualType);
}
