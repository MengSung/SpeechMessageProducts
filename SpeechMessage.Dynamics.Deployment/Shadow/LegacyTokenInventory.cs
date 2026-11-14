/*
 * 檔案整體說明
 *
 * 目的與定位：D5-18 舊入口受審盤點的固定檔案邊界。只讀 repository 的
 * d365/registration/legacy-token-inventory.json，不接受 CLI 路徑、情境安全旗標
 * 或任意 token 值。盤點與 manifest 都是操作者維護的版本控管輸入，不能把格式
 * 通過當作已閱讀舊程式；每個入口必須引用真實盤點證據，由模組工作包自我審查。
 * externalWrites須明列教會lookup範圍；空陣列表示來源審查未宣告外部寫入，缺項拒絕。
 * 主要功能與流程：以共用有界 JSON reader 驗容量、秘密樣式、重複與未知欄位，
 * 再逐一核對模組全部舊組件／型別恰好被盤點；record-fields 明列 seed 別名、
 * 實體與欄位，none 必須明確表示沒有 token 相依，未釐清或必走寫死 token 即拒絕。
 * 使用邊界與維護：本檔不讀舊原始碼、不建立 SDK、不取得 Secrets、不啟用步驟。
 * token 欄位的正式型別、可寫性與 owned seed 歸屬仍由 factory 再驗；回傳 DTO
 * 沒有秘密與服務參考。JSON 文件、串流及候選只活於一次 Load，無 static cache、
 * 背景工作或跨租戶可變資料；證據只核對受控相對路徑存在且無 reparse point，
 * 不執行或複製其內容。全部成功才發布唯讀陣列，錯誤不回顯檔案本文。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SpeechMessage.Dynamics.Deployment.Shadow;

/// <summary>受審入口的固定 seed 欄位；不保存 token 值，也不能將共享列列為注入目標。</summary>
internal sealed record LegacyInventoryTokenField(string Alias, string Entity, string Attribute);

/// <summary>來源審查宣告的外部既有列實體及其教會lookup；正式metadata與身分仍由CLI核對。</summary>
internal sealed record LegacyInventoryExternalWrite(string Entity, string ChurchAttribute);

/// <summary>一個舊入口的完整盤點結果；NoTokenDependency 為明確受審判定，不是缺欄位的預設值。</summary>
internal sealed record LegacyTokenRule(string Assembly, string Type, bool NoTokenDependency,
    IReadOnlyList<LegacyInventoryTokenField> TokenFields, IReadOnlyList<LegacyInventoryExternalWrite> ExternalWrites);

/// <summary>固定路徑、完整模組匹配的盤點 reader；只提供候選，不取代正式身分及 D34 驗證。</summary>
internal static class LegacyTokenInventory
{
    /// <summary>
    /// 載入最多一百二十八個受審入口，所選模組的組件／型別集合須恰好等於 manifest。
    /// 重複 step 可引用同一型別，但同一盤點鍵不可重複；活動仍由專屬路徑處理。
    /// </summary>
    internal static IReadOnlyList<LegacyTokenRule> Load(string repositoryRoot, string module,
        IReadOnlyList<LegacyStepReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        var root = Path.GetFullPath(repositoryRoot);
        var path = root;
        foreach (var segment in new[] { "d365", "registration", "legacy-token-inventory.json" })
        {
            path = Path.Combine(path, segment);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw Invalid();
        }
        using var document = ShadowScenarioFiles.Read(path);
        var source = document.RootElement;
        Shape(source, "schemaVersion,entries");
        if (source.GetProperty("schemaVersion").ValueKind != JsonValueKind.Number ||
            !source.GetProperty("schemaVersion").TryGetInt32(out var version) || version != 1) throw Invalid();
        var entries = source.GetProperty("entries");
        if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 128 || references.Count is < 1 or > 128)
            throw Invalid();
        var seen = new HashSet<(string Module, string Assembly, string Type)>();
        var result = new List<LegacyTokenRule>();
        foreach (var entry in entries.EnumerateArray())
        {
            Shape(entry, "module,assembly,type,tokenPolicy,evidence,tokenFields,externalWrites");
            var entryModule = Text(entry, "module");
            var assembly = Text(entry, "assembly");
            var type = Text(entry, "type");
            if (entryModule is not ("diagnostics" or "localization" or "weeklyreports" or "attendance" or
                "lessons" or "appointments" or "qrcodes" or "care" or "fees" or "messaging") ||
                !seen.Add((entryModule, assembly, type))) throw Invalid();
            var policy = Text(entry, "tokenPolicy");
            if (policy is not ("record-fields" or "none" or "unconditional-hardcoded" or "unresolved")) throw Invalid();
            var fields = entry.GetProperty("tokenFields");
            var external = entry.GetProperty("externalWrites");
            if (external.ValueKind != JsonValueKind.Array || external.GetArrayLength() > 16) throw Invalid();
            var externalWrites = new List<LegacyInventoryExternalWrite>();
            var externalEntities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var scope in external.EnumerateArray())
            {
                Shape(scope, "entity,churchAttribute");
                var entity = Text(scope, "entity");
                var attribute = Text(scope, "churchAttribute");
                if (!Identifier(entity) || !Identifier(attribute) || !externalEntities.Add(entity)) throw Invalid();
                externalWrites.Add(new LegacyInventoryExternalWrite(entity, attribute));
            }
            if (fields.ValueKind != JsonValueKind.Array || fields.GetArrayLength() > 32) throw Invalid();
            var tokens = new List<LegacyInventoryTokenField>();
            foreach (var field in fields.EnumerateArray())
            {
                Shape(field, "alias,entity,attribute");
                var alias = Text(field, "alias");
                var entity = Text(field, "entity");
                var attribute = Text(field, "attribute");
                if (!Identifier(alias, allowHyphen: true) || !Identifier(entity) || !Identifier(attribute))
                    throw Invalid();
                var token = new LegacyInventoryTokenField(alias, entity, attribute);
                if (tokens.Contains(token)) throw Invalid();
                tokens.Add(token);
            }
            if (entryModule != module) continue;
            ValidateEvidence(root, Text(entry, "evidence"));
            if (policy is "unconditional-hardcoded" or "unresolved" ||
                (policy == "none" ? tokens.Count != 0 : tokens.Count == 0)) throw Invalid();
            result.Add(new LegacyTokenRule(assembly, type, policy == "none", tokens.AsReadOnly(), externalWrites.AsReadOnly()));
        }
        var required = references.Select(reference => (reference.Assembly, reference.Type)).ToHashSet();
        if (required.Count == 0 || !required.SetEquals(result.Select(rule => (rule.Assembly, rule.Type)))) throw Invalid();
        return result.AsReadOnly();
    }

    /// <summary>證據只能是 repo 內已有的 Markdown，逐段拒絕 reparse；不能用外部檔或任意路徑宣稱受審。</summary>
    private static void ValidateEvidence(string root, string relative)
    {
        if (relative.Contains('\\', StringComparison.Ordinal) || relative.Contains(':', StringComparison.Ordinal) ||
            Path.IsPathRooted(relative) || !relative.EndsWith(".md", StringComparison.Ordinal)) throw Invalid();
        var segments = relative.Split('/');
        if (segments.Length is < 2 or > 16 || segments.Any(segment => segment is "" or "." or "..")) throw Invalid();
        var path = root;
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw Invalid();
        }
        if (!File.Exists(path)) throw Invalid();
    }

    /// <summary>每層只接受明列的完整鍵集合；重複鍵已由共用 reader 在建立文件時拒絕。</summary>
    private static void Shape(JsonElement element, string fields)
    {
        var required = fields.Split(',').ToHashSet(StringComparer.Ordinal);
        if (element.ValueKind != JsonValueKind.Object ||
            !required.SetEquals(element.EnumerateObject().Select(property => property.Name))) throw Invalid();
    }

    /// <summary>有限且非空文字不含控制字元，固定錯誤不把輸入串入診斷。</summary>
    private static string Text(JsonElement element, string field) =>
        element.GetProperty(field) is { ValueKind: JsonValueKind.String } value &&
        value.GetString() is { Length: > 0 and <= 512 } text && !text.Any(char.IsControl) ? text : throw Invalid();

    /// <summary>schema 及別名只接受 ASCII 明確識別字，不接受 alias.field、路徑或原始資料。</summary>
    private static bool Identifier(string value, bool allowHyphen = false) =>
        value.Length <= 128 && value.All(character => char.IsAsciiLetterOrDigit(character) ||
            character == '_' || (allowHyphen && character == '-'));

    /// <summary>拒絕不完整盤點，不回顯入口、來源、路徑或 token 欄位內容。</summary>
    private static InvalidDataException Invalid() => new("Legacy token inventory safety precondition failed.");
}
