/*
檔案整體說明
目的與定位：FS 2026-11-28 22:05決策的「本計畫新建lookup」完整wire預期補值器，
只由完整發布護欄對本輪CreateLookup正式回應ID呼叫；不連SDK、不寫D365或發布。
主要流程：從寫前候選推導固定新關聯／欄位名稱，對明列伺服器預設逐項比對型別和值；
只有原本null或原本null父物件的子節點可補。Merge必須寫前已符合清單，不吞掉錯誤NoCascade。
其他關聯、舊baseline、新label、未知節點、錯預設或型別仍由完整Equal拒絕；絕不按prefix忽略子樹。
來源：兩個指定新關聯的四次唯讀觀察、110節點fixture；這是固定預期，不複製任意寫後來源。
資源與隔離：局部字典最多64個補值，字串只取已捕捉且驗證的正式schema身份；無全域可變cache、
SDK client、timer、Task或stream。取消逐節點檢查，輸出只存活本次同步Apply。
驗證：真fixture正向、未授權CaptureLookup、錯預設、新未知節點及舊label漂移；離線不代表實機。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>只補新關聯的固定預期，不建立信任新來源的通用忽略規則。</summary>
internal static class NewLookupServerDefaults
{
    /// <summary>
    /// 寫前候選保持不變，僅在actual恰好等於明列固定預設時補上原null節點。
    /// actual只用於相等判斷，不將任何來源值放進預期；未知差異留給外層完整比較拒絕。
    /// </summary>
    /// <param name="candidate">已綁本次正式SDK回應ID的寫前純值。</param>
    /// <param name="actual">本次完整published或unpublished投影，不轉移擁有權。</param>
    /// <param name="root">本次正式新關聯在完整基準內的精確路徑。</param>
    /// <param name="ct">同步命令owner的取消token，無延後工作。</param>
    /// <returns>獨立純值候選，最大增量64節點，仍須由完整護欄Equal驗證。</returns>
    internal static IReadOnlyDictionary<string, UiMetadataPublicationGuard.Scalar> Complete(
        IReadOnlyDictionary<string, UiMetadataPublicationGuard.Scalar> candidate,
        IReadOnlyDictionary<string, UiMetadataPublicationGuard.Scalar> actual, string root, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var name = Text(candidate, ".SchemaName");
        var entity = Text(candidate, ".ReferencingEntity");
        var field = Text(candidate, ".ReferencingAttribute");
        var target = Text(candidate, ".ReferencedEntity");
        if (name != "new_" + entity + "_" + field ||
            candidate[".CascadeConfiguration.Merge"] != Enum(target is "account" or "contact" ? CascadeType.Cascade : CascadeType.NoCascade))
            throw new InvalidDataException("New lookup normalization scope or merge policy rejected.");
        var defaults = new Dictionary<string, UiMetadataPublicationGuard.Scalar>(StringComparer.Ordinal)
        {
            [".AssociatedMenuConfiguration"] = Contract(typeof(AssociatedMenuConfiguration)),
            [".AssociatedMenuConfiguration.AvailableOffline"] = Boolean(true),
            [".AssociatedMenuConfiguration.Behavior"] = Enum(AssociatedMenuBehavior.UseCollectionName),
            [".AssociatedMenuConfiguration.Group"] = Enum(AssociatedMenuGroup.Details),
            [".AssociatedMenuConfiguration.Icon"] = Null(),
            [".AssociatedMenuConfiguration.IsCustomizable"] = Boolean(true),
            [".AssociatedMenuConfiguration.Label"] = Contract(typeof(Label)),
            [".AssociatedMenuConfiguration.Label.LocalizedLabels"] = new("Collection", 0),
            [".AssociatedMenuConfiguration.Label.UserLocalizedLabel"] = Null(),
            [".AssociatedMenuConfiguration.MenuId"] = Null(),
            [".AssociatedMenuConfiguration.Order"] = new(typeof(int).FullName!, 10000),
            [".AssociatedMenuConfiguration.QueryApi"] = Null(),
            [".AssociatedMenuConfiguration.ViewId"] = new(typeof(Guid).FullName!, Guid.Empty),
            [".CascadeConfiguration.RollupView"] = Enum(CascadeType.NoCascade),
            [".IntroducedVersion"] = new(typeof(string).FullName!, "1.0.0.0"),
            [".IsCustomRelationship"] = Boolean(true),
            [".IsCustomizable"] = Contract(typeof(BooleanManagedProperty)),
            [".IsCustomizable.CanBeChanged"] = Boolean(true),
            [".IsCustomizable.ManagedPropertyLogicalName"] = new(typeof(string).FullName!, "iscustomizable"),
            [".IsCustomizable.Value"] = Boolean(true),
            [".IsHierarchical"] = Boolean(false),
            [".IsValidForAdvancedFind"] = Boolean(true),
            [".ReferencedEntityNavigationPropertyName"] = new(typeof(string).FullName!, name),
            [".ReferencingEntityNavigationPropertyName"] = new(typeof(string).FullName!, field),
            [".RelationshipAttributes"] = new("Collection", 0),
            [".RelationshipBehavior"] = new(typeof(int).FullName!, 1),
            [".SecurityTypes"] = Enum(SecurityTypes.Append)
        };
        var result = new Dictionary<string, UiMetadataPublicationGuard.Scalar>(candidate, StringComparer.Ordinal);
        foreach (var pair in defaults)
        {
            ct.ThrowIfCancellationRequested();
            var originalNull = candidate.TryGetValue(pair.Key, out var old) ? old == Null() :
                pair.Key.StartsWith(".AssociatedMenuConfiguration.", StringComparison.Ordinal) && candidate[".AssociatedMenuConfiguration"] == Null() ||
                pair.Key.StartsWith(".IsCustomizable.", StringComparison.Ordinal) && candidate[".IsCustomizable"] == Null();
            if (originalNull && actual.TryGetValue(root + pair.Key, out var value) && value == pair.Value)
                result[pair.Key] = pair.Value;
        }
        return result;
    }

    /// <summary>正式schema身份只從寫前純值讀取，缺漏或非文字一律拒絕。</summary>
    private static string Text(IReadOnlyDictionary<string, UiMetadataPublicationGuard.Scalar> values, string key) =>
        values.TryGetValue(key, out var scalar) && scalar.Type == typeof(string).FullName && scalar.Value is string text &&
        text.Length is > 0 and <= 128 ? text : throw new InvalidDataException("New lookup identity rejected.");
    /// <summary>固定SDK契約型別，不接受actual提供其他型別名。</summary>
    private static UiMetadataPublicationGuard.Scalar Contract(Type type) => new("Contract", type.FullName);
    /// <summary>與正式完整投影器相同的enum型別／Int64表示，僅呼叫端明列固定常數。</summary>
    private static UiMetadataPublicationGuard.Scalar Enum<T>(T value) where T : struct, Enum => new(typeof(T).FullName!, Convert.ToInt64(value, CultureInfo.InvariantCulture));
    /// <summary>固定布林純值，不包含可變SDK參照。</summary>
    private static UiMetadataPublicationGuard.Scalar Boolean(bool value) => new(typeof(bool).FullName!, value);
    /// <summary>完整投影器的null標記；不能以空字串或缺少節點代替。</summary>
    private static UiMetadataPublicationGuard.Scalar Null() => new("null", null);
}
