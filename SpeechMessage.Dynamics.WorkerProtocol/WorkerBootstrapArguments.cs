using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SpeechMessage.Dynamics.WorkerProtocol;

public sealed class WorkerBootstrapArguments
{
    private static readonly string[] RequiredSwitches =
    {
        "--pipe",
        "--nonce",
        "--protocol",
        "--worker-kind",
        "--package-lock",
        "--profile-generation"
    };

    private WorkerBootstrapArguments(
        string pipeName,
        string processNonce,
        int protocolVersion,
        OfficialWorkerKind workerKind,
        string packageLockId,
        string profileGenerationId)
    {
        PipeName = pipeName;
        ProcessNonce = processNonce;
        ProtocolVersion = protocolVersion;
        WorkerKind = workerKind;
        PackageLockId = packageLockId;
        ProfileGenerationId = profileGenerationId;
    }

    public string PipeName { get; }

    public string ProcessNonce { get; }

    public int ProtocolVersion { get; }

    public OfficialWorkerKind WorkerKind { get; }

    public string PackageLockId { get; }

    public string ProfileGenerationId { get; }

    public static WorkerBootstrapArguments Parse(IReadOnlyList<string> arguments)
    {
        if (arguments is null)
        {
            throw new ArgumentNullException(nameof(arguments));
        }

        if (arguments.Count != RequiredSwitches.Length * 2 || arguments.Count % 2 != 0)
        {
            throw InvalidArguments();
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Count; index += 2)
        {
            var key = arguments[index];
            var value = arguments[index + 1];
            if (!RequiredSwitches.Contains(key, StringComparer.Ordinal) ||
                string.IsNullOrWhiteSpace(value) ||
                values.ContainsKey(key))
            {
                throw InvalidArguments();
            }

            values.Add(key, value);
        }

        if (!int.TryParse(
                values["--protocol"],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var protocolVersion) ||
            protocolVersion != WorkerProtocolVersion.Current)
        {
            throw new WorkerProtocolException(
                WorkerProtocolFailureCategory.UnsupportedProtocolVersion,
                "The worker bootstrap protocol version is unsupported.");
        }

        if (!Enum.TryParse(values["--worker-kind"], ignoreCase: false, out OfficialWorkerKind workerKind) ||
            !Enum.IsDefined(typeof(OfficialWorkerKind), workerKind))
        {
            throw InvalidArguments();
        }

        var pipeName = values["--pipe"];
        ValidateIdentifier(pipeName, "pipe name");
        if (!pipeName.StartsWith("speechmessage-dynamics-", StringComparison.Ordinal))
        {
            throw InvalidArguments();
        }

        var processNonce = values["--nonce"];
        if (processNonce.Length != 32 || processNonce.Any(character =>
                !(character is >= '0' and <= '9') &&
                !(character is >= 'a' and <= 'f')))
        {
            throw InvalidArguments();
        }

        var packageLockId = values["--package-lock"];
        var profileGenerationId = values["--profile-generation"];
        ValidateIdentifier(packageLockId, "package lock identifier");
        ValidateIdentifier(profileGenerationId, "profile generation identifier");

        return new WorkerBootstrapArguments(
            pipeName,
            processNonce,
            protocolVersion,
            workerKind,
            packageLockId,
            profileGenerationId);
    }

    public string[] ToArgumentList()
    {
        return new[]
        {
            "--pipe", PipeName,
            "--nonce", ProcessNonce,
            "--protocol", ProtocolVersion.ToString(CultureInfo.InvariantCulture),
            "--worker-kind", WorkerKind.ToString(),
            "--package-lock", PackageLockId,
            "--profile-generation", ProfileGenerationId
        };
    }

    private static void ValidateIdentifier(string value, string category)
    {
        if (value.Length > WorkerProtocolLimits.Default.MaximumIdentifierUtf8Bytes ||
            value.Any(character =>
                !(character is >= 'a' and <= 'z') &&
                !(character is >= 'A' and <= 'Z') &&
                !(character is >= '0' and <= '9') &&
                character is not '.' and not '_' and not '-'))
        {
            throw new WorkerProtocolException(
                WorkerProtocolFailureCategory.InvalidEnvelope,
                $"The worker bootstrap {category} is invalid.");
        }
    }

    private static WorkerProtocolException InvalidArguments() =>
        new WorkerProtocolException(
            WorkerProtocolFailureCategory.InvalidEnvelope,
            "The worker bootstrap arguments are invalid.");
}
