/*
 * 檔案整體說明
 *
 * 目的與定位：P2-03 Channel 憑證寫入與教會指派的終端邊界；由 DeploymentCli 明確分流，
 * 不需要 Plugin DLL。只接受 goodshepherdback、公開 Channel ID 及必要 account GUID。
 * 主要功能與流程：先驗選項及 caller 取消，protect-token 再讀 User Secrets 的 TokenKey
 * 和恰好兩行有界 stdin，完成加密及欄位容量驗證後，才取得組織寫鎖與唯一 SDK lease。
 * assign-channel 不讀秘密。真正寫入委派無狀態服務，成功只輸出固定文字，不建立密文 artifact。
 * 資源擁有權與隔離：reader、writer、secret 委派由 Host 借入；當次字元 buffer 與解碼 key
 * 在 finally 清零，字串無法保證清零，只存單次 stack scope，不快取、不記錄、不跨組織重用。
 * 輸入最多4096字元；真程序由ChannelSecretInput取得重新導向stdin，pipe有三十秒deadline，
 * 逐byte嚴格UTF-8、最多16KiB，等待可取消且無背景讀取。測試借入reader必須有限；
 * 任意自訂reader及檔案裝置同步I/O入場後須drain，不宣稱可以中斷所有來源。
 * SDK 同步呼叫亦須 drain；先 Dispose lease 再釋放寫鎖，最後才交共用 report 原 Exception。
 * 維護與驗證：正常 caller 取消回130且不告警，內部逾時仍失敗；parser拒絕回2、其他故障回1。
 * 離線正反例不能取代實機索引、授權、原況備份及回復驗收；真實寫入仍須遵守既有部署界線。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Threading;
using LineMessagingProcessor.Core.Security;

namespace SpeechMessage.Dynamics.Deployment.Channels;

/// <summary>只處理兩個受限命令；所有秘密及可變資料僅在當次命令持有。</summary>
internal static class ChannelProvisioningCli
{
    /// <summary>安全讀取的總字元上限，含分隔符；超出時不繼續配置或建立連線。</summary>
    private const int MaximumInputCharacters = 4096;

    /// <summary>共用部署邊界，先驗輸入、加密，再取得唯一鎖與租約；失敗時完整歸還後上報。</summary>
    /// <param name="args">未信任的命令列，只允許固定選項，拒絕秘密參數。</param>
    /// <param name="connector">Host 擁有的合法 User Secrets 連線來源。</param>
    /// <param name="output">借入的固定摘要輸出，不回顯輸入或例外訊息。</param>
    /// <param name="report">原始例外的共用告警入口，不可保存秘密。</param>
    /// <param name="input">由 Host 擁有的有限秘密輸入；本層不關閉它。</param>
    /// <param name="artifactRoot">僅供離線測試指定的獨立鎖目錄根。</param>
    /// <param name="secretReader">只讀當次 TokenKey，不保留委派或組態。</param>
    /// <param name="lockTimeout">同組織寫鎖等待上限。</param>
    /// <param name="cancellationToken">呼叫端取消，保持最後參數。</param>
    /// <returns>0成功、2參數拒絕、1故障、130正常取消。</returns>
    internal static int Run(string[] args, IOrganizationConnector connector, TextWriter output,
        Action<Exception> report, TextReader? input, string? artifactRoot,
        Func<string, string>? secretReader, TimeSpan? lockTimeout, CancellationToken cancellationToken)
    {
        if (!TryParse(args, out var channelId, out var accountId))
        {
            output.WriteLine("Channel command arguments are invalid or not allowlisted.");
            return 2;
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? tokenEnvelope = null;
            string? secretEnvelope = null;
            if (args[0] == "protect-token")
                (tokenEnvelope, secretEnvelope) = ReadAndProtect(input, secretReader, cancellationToken);

            var artifacts = Path.GetFullPath(artifactRoot ??
                Path.Combine(DeploymentCli.FindRepositoryRoot(), "artifacts", "d365"));
            var organizationRoot = Path.Combine(artifacts, "goodshepherdback");
            Schema.SchemaDeploymentCli.EnsureNoLinks(organizationRoot);
            ExecuteWrite(connector, organizationRoot, lockTimeout, channelId, accountId,
                tokenEnvelope, secretEnvelope, cancellationToken);
            output.WriteLine("Channel command completed.");
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            output.WriteLine("Command cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            try { report(exception); }
            catch { WriteReportingFailure(); }
            output.WriteLine("Channel command failed. Sensitive details are not shown.");
            return 1;
        }
    }

    /// <summary>唯一鎖／租約 owner；完整嘗試清理，保留SDK與清理原物件，不讓finally覆蓋主故障。</summary>
    /// <param name="connector">Host借入的合法連線來源。</param>
    /// <param name="organizationRoot">已驗證非link的固定組織鎖根。</param>
    /// <param name="lockTimeout">鎖等待上限，null為五分鐘。</param>
    /// <param name="channelId">已驗公開數字ID。</param>
    /// <param name="accountId">assign的正式教會ID，protect為空。</param>
    /// <param name="tokenEnvelope">protect密文，assign為null。</param>
    /// <param name="secretEnvelope">protect密文，assign為null。</param>
    /// <param name="cancellationToken">呼叫端取消；已入場同步SDK先drain。</param>
    private static void ExecuteWrite(IOrganizationConnector connector, string organizationRoot,
        TimeSpan? lockTimeout, string channelId, Guid accountId, string? tokenEnvelope,
        string? secretEnvelope, CancellationToken cancellationToken)
    {
        OrganizationWriteLock? writeLock = null;
        OrganizationLease? lease = null;
        ExceptionDispatchInfo? primary = null;
        try
        {
            writeLock = OrganizationWriteLock.Acquire(
                organizationRoot, lockTimeout ?? TimeSpan.FromMinutes(5), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            lease = connector.Connect("goodshepherdback");
            if (tokenEnvelope is not null)
                ChannelProvisioningService.Protect(lease.Service, channelId, tokenEnvelope, secretEnvelope!, cancellationToken);
            else
                ChannelProvisioningService.Assign(lease.Service, accountId, channelId, cancellationToken);
        }
        catch (Exception exception)
        {
            primary = ExceptionDispatchInfo.Capture(exception);
        }
        // 所有一般成功／例外／取消路徑會合到此清理點；不在finally拋出新錯誤覆蓋主故障。
        List<Exception>? failures = null;
        try { lease?.Dispose(); }
        catch (Exception exception) { (failures ??= new(3)).Add(exception); }
        try { writeLock?.Dispose(); }
        catch (Exception exception) { (failures ??= new(3)).Add(exception); }
        if (failures is not null)
        {
            if (primary is not null) failures.Insert(0, primary.SourceException);
            throw new AggregateException("Channel command cleanup failed.", failures);
        }
        primary?.Throw();
    }

    /// <summary>嚴格固定選項、白名單及公開識別驗證，過程不讀 stdin、秘密或 SDK。</summary>
    /// <param name="args">原始參數，未知或重複選項立即拒絕。</param>
    /// <param name="channelId">成功時唯一的公開數字識別。</param>
    /// <param name="accountId">assign 的非空正式 ID；protect 保持空值。</param>
    /// <returns>所有選項與命令相容才為 true。</returns>
    private static bool TryParse(string[] args, out string channelId, out Guid accountId)
    {
        channelId = "";
        accountId = Guid.Empty;
        string? organization = null;
        string? account = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index++)
        {
            var option = args[index];
            if (option is not ("--org" or "--channel-id" or "--account") ||
                !seen.Add(option) || index + 1 >= args.Length)
                return false;
            var value = args[++index];
            if (string.IsNullOrWhiteSpace(value)) return false;
            switch (option)
            {
                case "--org": organization = value; break;
                case "--channel-id": channelId = value; break;
                case "--account": account = value; break;
            }
        }
        if (!string.Equals(organization, "goodshepherdback", StringComparison.OrdinalIgnoreCase) ||
            channelId.Length is < 1 or > 20)
            return false;
        foreach (var character in channelId)
            if (character is < '0' or > '9') return false;
        return args[0] == "protect-token" ? account is null :
            args[0] == "assign-channel" && account is not null &&
            Guid.TryParseExact(account, "D", out accountId) && accountId != Guid.Empty;
    }

    /// <summary>有界解碼64-byte key、讀兩行並產生密文；所有可變秘密緩衝區成功失敗皆清零。</summary>
    /// <param name="input">Host 借入的有限輸入，不能以命令列秘密替代。</param>
    /// <param name="secretReader">既有 User Secrets reader，只要求 TokenKey。</param>
    /// <param name="cancellationToken">每次同步字元讀取前檢查；不假裝可中斷已阻塞的外部 reader。</param>
    /// <returns>符合4000／1000字元欄位容量的兩個密文。</returns>
    private static (string Token, string Secret) ReadAndProtect(TextReader? input,
        Func<string, string>? secretReader, CancellationToken cancellationToken)
    {
        var buffer = new char[MaximumInputCharacters];
        var key = new byte[64];
        try
        {
            if (secretReader is null) throw InvalidInput();
            var encodedKey = secretReader("TokenKey");
            if (encodedKey is null || encodedKey.Length != 88 ||
                !Convert.TryFromBase64String(encodedKey, key, out var keyLength) || keyLength != 64 ||
                !string.Equals(Convert.ToBase64String(key), encodedKey, StringComparison.Ordinal))
                throw InvalidInput();

            using var ownedInput = input is null ? ChannelSecretInput.Open(cancellationToken) : null;
            var source = input ?? ownedInput!;
            var count = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var value = source.Read();
                if (value < 0) break;
                if (count == buffer.Length) throw InvalidInput();
                buffer[count++] = (char)value;
            }
            var span = buffer.AsSpan(0, count);
            var separator = span.IndexOf('\n');
            if (separator < 1) throw InvalidInput();
            var tokenSpan = span[..separator];
            if (tokenSpan[^1] == '\r') tokenSpan = tokenSpan[..^1];
            var secretSpan = span[(separator + 1)..];
            if (!secretSpan.IsEmpty && secretSpan[^1] == '\n')
            {
                secretSpan = secretSpan[..^1];
                if (!secretSpan.IsEmpty && secretSpan[^1] == '\r') secretSpan = secretSpan[..^1];
            }
            if (tokenSpan.IsEmpty || secretSpan.IsEmpty ||
                tokenSpan.IndexOfAny('\r', '\n') >= 0 || secretSpan.IndexOfAny('\r', '\n') >= 0)
                throw InvalidInput();
            var token = ChannelTokenProtector.Protect(new string(tokenSpan), key);
            var secret = ChannelTokenProtector.Protect(new string(secretSpan), key);
            if (token.Length > 4000 || secret.Length > 1000) throw InvalidInput();
            return (token, secret);
        }
        finally
        {
            Array.Clear(buffer);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>所有 key／stdin 拒絕都使用固定例外，不帶輸入、長度或 provider inner。</summary>
    private static InvalidDataException InvalidInput() => new("Channel secret input is invalid.");

    /// <summary>共用 reporter 故障只能固定 stderr，不遞迴、不覆蓋原始失敗。</summary>
    private static void WriteReportingFailure()
    {
        try { Console.Error.WriteLine("[Deployment] ChannelDiagnosticReportingFailed"); }
        catch { }
    }
}
