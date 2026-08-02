using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SpeechMessage.Dynamics.WorkerProtocol;

public sealed class WorkerValue
{
    public WorkerValue(
        WorkerValueKind kind,
        string? scalar,
        IReadOnlyList<WorkerValue>? items,
        IReadOnlyDictionary<string, WorkerValue>? members)
    {
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

    public WorkerValueKind Kind { get; }

    public string? Scalar { get; }

    public IReadOnlyList<WorkerValue>? Items { get; }

    public IReadOnlyDictionary<string, WorkerValue>? Members { get; }

    public static WorkerValue Null() => new WorkerValue(WorkerValueKind.Null, null, null, null);

    public static WorkerValue FromBoolean(bool value) =>
        new WorkerValue(
            WorkerValueKind.Boolean,
            value ? "true" : "false",
            null,
            null);

    public static WorkerValue FromInt64(long value) =>
        new WorkerValue(
            WorkerValueKind.Int64,
            value.ToString(CultureInfo.InvariantCulture),
            null,
            null);

    public static WorkerValue FromDecimal(decimal value) =>
        new WorkerValue(
            WorkerValueKind.Decimal,
            value.ToString("G29", CultureInfo.InvariantCulture),
            null,
            null);

    public static WorkerValue FromString(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return new WorkerValue(WorkerValueKind.String, value, null, null);
    }

    public static WorkerValue FromGuid(Guid value) =>
        new WorkerValue(WorkerValueKind.Guid, value.ToString("N"), null, null);

    public static WorkerValue FromUtcDateTime(DateTimeOffset value) =>
        new WorkerValue(
            WorkerValueKind.UtcDateTime,
            value.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
            null,
            null);

    public static WorkerValue FromArray(IReadOnlyList<WorkerValue> items)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        return new WorkerValue(WorkerValueKind.Array, null, items, null);
    }

    public static WorkerValue FromObject(IReadOnlyDictionary<string, WorkerValue> members)
    {
        if (members is null)
        {
            throw new ArgumentNullException(nameof(members));
        }

        return new WorkerValue(WorkerValueKind.Object, null, null, members);
    }
}
