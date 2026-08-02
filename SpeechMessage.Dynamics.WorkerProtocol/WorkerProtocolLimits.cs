using System;

namespace SpeechMessage.Dynamics.WorkerProtocol;

public sealed class WorkerProtocolLimits
{
    public static WorkerProtocolLimits Default { get; } = new WorkerProtocolLimits(
        maximumFrameBytes: WorkerFrameCodec.DefaultMaximumFrameBytes,
        maximumValueDepth: 8,
        maximumObjectMembers: 64,
        maximumArrayItems: 256,
        maximumStringUtf8Bytes: 16 * 1024,
        maximumIdentifierUtf8Bytes: 128);

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

    public int MaximumFrameBytes { get; }

    public int MaximumValueDepth { get; }

    public int MaximumObjectMembers { get; }

    public int MaximumArrayItems { get; }

    public int MaximumStringUtf8Bytes { get; }

    public int MaximumIdentifierUtf8Bytes { get; }

    private static int RequirePositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Worker protocol limits must be positive.");
        }

        return value;
    }
}
