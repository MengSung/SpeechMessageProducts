using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SpeechMessage.Dynamics.WorkerProtocol;

/// <summary>
/// 表示 Worker IPC 唯一允許的 SDK-free typed value tree。
/// Constructor 會複製 array/object 容器，避免 caller 在驗證或序列化期間競態修改；實際深度、項目、成員與 UTF-8 byte 上限由 validator/codec 強制執行。
/// </summary>
public sealed class WorkerValue
{
    /// <summary>建立指定 kind 的 immutable 容器快照；不保存 CRM Entity、stream、credential 或可變 Session 物件。</summary>
    public WorkerValue(
        WorkerValueKind kind,
        string? scalar,
        IReadOnlyList<WorkerValue>? items,
        IReadOnlyDictionary<string, WorkerValue>? members)
    {
        // 對集合做 defensive copy，讓同一 WorkerValue 不會因外部 collection 後續變更而
        // 在不同要求或執行緒看到不同內容；這也是 canonical serialization 可重現的前提。
        Kind = kind;
        Scalar = scalar;
        Items = items is null ? null : items.ToArray();
        Members = members is null
            ? null
            : members.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
    }

    /// <summary>取得決定 scalar／array／object shape 的封閉 value kind。</summary>
    public WorkerValueKind Kind { get; }

    /// <summary>取得 scalar 的 canonical invariant 字串；非 scalar kind 必須為 null。</summary>
    public string? Scalar { get; }

    /// <summary>取得建構時複製的 array items；非 array kind 必須為 null。</summary>
    public IReadOnlyList<WorkerValue>? Items { get; }

    /// <summary>取得以 ordinal key 複製的 object members；非 object kind 必須為 null。</summary>
    public IReadOnlyDictionary<string, WorkerValue>? Members { get; }

    /// <summary>建立不含其他 payload 的 Null 值。</summary>
    public static WorkerValue Null() => new WorkerValue(WorkerValueKind.Null, null, null, null);

    /// <summary>以固定小寫字串建立 Boolean 值。</summary>
    public static WorkerValue FromBoolean(bool value) =>
        new WorkerValue(
            WorkerValueKind.Boolean,
            value ? "true" : "false",
            null,
            null);

    /// <summary>以 invariant canonical 格式建立 Int64 值。</summary>
    public static WorkerValue FromInt64(long value) =>
        new WorkerValue(
            WorkerValueKind.Int64,
            value.ToString(CultureInfo.InvariantCulture),
            null,
            null);

    /// <summary>以無多餘尾零的 invariant canonical 格式建立 Decimal 值。</summary>
    public static WorkerValue FromDecimal(decimal value) =>
        new WorkerValue(
            WorkerValueKind.Decimal,
            value.ToString("G29", CultureInfo.InvariantCulture),
            null,
            null);

    /// <summary>建立一般字串值；UTF-8 byte 上限由 envelope validator 在序列化前檢查。</summary>
    public static WorkerValue FromString(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return new WorkerValue(WorkerValueKind.String, value, null, null);
    }

    /// <summary>以不含分隔符的固定 32 字元格式建立 Guid 值。</summary>
    public static WorkerValue FromGuid(Guid value) =>
        new WorkerValue(WorkerValueKind.Guid, value.ToString("N"), null, null);

    /// <summary>以 UTC ticks 建立不受 culture/time-zone 影響的時間值。</summary>
    public static WorkerValue FromUtcDateTime(DateTimeOffset value) =>
        new WorkerValue(
            WorkerValueKind.UtcDateTime,
            value.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
            null,
            null);

    /// <summary>複製有限 item 清單建立 Array 值；總數與深度由 validator 強制限制。</summary>
    public static WorkerValue FromArray(IReadOnlyList<WorkerValue> items)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        return new WorkerValue(WorkerValueKind.Array, null, items, null);
    }

    /// <summary>以 ordinal key 複製有限 member 字典建立 Object 值；欄位名仍須通過祕密詞彙 denylist。</summary>
    public static WorkerValue FromObject(IReadOnlyDictionary<string, WorkerValue> members)
    {
        if (members is null)
        {
            throw new ArgumentNullException(nameof(members));
        }

        return new WorkerValue(WorkerValueKind.Object, null, null, members);
    }
}
