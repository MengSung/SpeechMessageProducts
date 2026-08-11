using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SpeechMessage.Dynamics.WorkerProtocol;

/// <summary>
/// 表示 Supervisor 啟動 Official Worker 時唯一允許的非祕密、bounded 命令列投影。
/// Parser 僅接受固定 switch 集合與嚴格識別字元；credential、token、endpoint、Session 或連線字串
/// 不得進入 command line，避免被 process list、記錄或另一個 Worker generation 觀察到。
/// </summary>
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

    /// <summary>取得 Supervisor 建立且只供目前 Worker process 使用的命名管線名稱。</summary>
    public string PipeName { get; }

    /// <summary>取得綁定本次 process hand-shake 的一次性 nonce，不得跨 Worker 重用。</summary>
    public string ProcessNonce { get; }

    /// <summary>取得必須精確等於目前 IPC 版本的 protocol version。</summary>
    public int ProtocolVersion { get; }

    /// <summary>取得決定版本固定 SDK process graph 的 Worker 種類。</summary>
    public OfficialWorkerKind WorkerKind { get; }

    /// <summary>取得 deployment 驗證過的 SDK package lock 識別碼；不含檔案路徑或 credential。</summary>
    public string PackageLockId { get; }

    /// <summary>取得 immutable Profile generation 識別碼，用於隔離舊、新 runtime。</summary>
    public string ProfileGenerationId { get; }

    /// <summary>
    /// 解析固定成對 switch，拒絕缺漏、重複、未知、空白、過長或含非法字元的輸入。
    /// 解析失敗一律在管線連線、Profile 讀取與 credential ownership 開始前 fail closed。
    /// </summary>
    /// <param name="arguments">由 Supervisor 建立的命令列 token 清單。</param>
    /// <returns>不可變且只含非祕密 scalar 的啟動參數。</returns>
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

    /// <summary>
    /// 以固定順序重新投影命令列 token，供 Supervisor 無 shell interpolation 地傳給 Worker。
    /// 結果不含 password、token、endpoint 或連線字串，因此不會擴張 process-list 洩漏面。
    /// </summary>
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
        // 僅允許 ASCII identifier 字元，可同時維持 byte 上限可預測性並阻止引號、空白、路徑或
        // shell 控制字元改變 process 啟動語意；驗證本身不配置長生命週期資源。
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
