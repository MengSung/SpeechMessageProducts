using System;

namespace SpeechMessage.Dynamics.WorkerProtocol;

/// <summary>
/// 定義 Worker IPC frame、巢狀值、集合與識別字串的 deployment-owned 上限。
/// 所有上限在解析或配置大型陣列前套用，藉由 bounded allocation 與 bounded traversal 防止
/// 記憶體耗盡、無界 CPU 工作與惡意 payload 長時間占用單一 Worker process。
/// </summary>
public sealed class WorkerProtocolLimits
{
    /// <summary>
    /// 取得目前安全預設值；物件為 immutable，可跨 codec 共用，但不含任何要求、使用者或 Profile 狀態。
    /// </summary>
    public static WorkerProtocolLimits Default { get; } = new WorkerProtocolLimits(
        maximumFrameBytes: WorkerFrameCodec.DefaultMaximumFrameBytes,
        maximumValueDepth: 8,
        maximumObjectMembers: 64,
        maximumArrayItems: 256,
        maximumStringUtf8Bytes: 16 * 1024,
        maximumIdentifierUtf8Bytes: 128);

    /// <summary>建立所有欄位均為正數的 immutable protocol limits。</summary>
    public WorkerProtocolLimits(
        int maximumFrameBytes,
        int maximumValueDepth,
        int maximumObjectMembers,
        int maximumArrayItems,
        int maximumStringUtf8Bytes,
        int maximumIdentifierUtf8Bytes)
    {
        MaximumFrameBytes = RequirePositive(maximumFrameBytes, nameof(maximumFrameBytes));
        MaximumValueDepth = RequirePositive(maximumValueDepth, nameof(maximumValueDepth));
        MaximumObjectMembers = RequirePositive(maximumObjectMembers, nameof(maximumObjectMembers));
        MaximumArrayItems = RequirePositive(maximumArrayItems, nameof(maximumArrayItems));
        MaximumStringUtf8Bytes = RequirePositive(maximumStringUtf8Bytes, nameof(maximumStringUtf8Bytes));
        MaximumIdentifierUtf8Bytes = RequirePositive(
            maximumIdentifierUtf8Bytes,
            nameof(maximumIdentifierUtf8Bytes));
    }

    /// <summary>取得單一 length-prefixed payload 可接受的最大 byte 數。</summary>
    public int MaximumFrameBytes { get; }

    /// <summary>取得 WorkerValue 遞迴巢狀的最大深度，避免 stack／CPU 無界成長。</summary>
    public int MaximumValueDepth { get; }

    /// <summary>取得單一 payload 可累積的最大 object member 數。</summary>
    public int MaximumObjectMembers { get; }

    /// <summary>取得單一 payload 可累積的最大 array item 數。</summary>
    public int MaximumArrayItems { get; }

    /// <summary>取得一般 scalar string 的最大 UTF-8 byte 數。</summary>
    public int MaximumStringUtf8Bytes { get; }

    /// <summary>取得 operation、generation、field name 等識別字串的最大 UTF-8 byte 數。</summary>
    public int MaximumIdentifierUtf8Bytes { get; }

    private static int RequirePositive(int value, string parameterName)
    {
        // 零或負值會讓 frame/集合邊界失去安全語意；在 codec 建立前拒絕，可避免執行期間
        // 才發生溢位、負長度或意外無限制配置。
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Worker protocol limits must be positive.");
        }

        return value;
    }
}
