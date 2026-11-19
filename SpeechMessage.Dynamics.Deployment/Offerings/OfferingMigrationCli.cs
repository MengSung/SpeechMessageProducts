/*
 * 檔案整體說明
 *
 * 目的與定位：承接奉獻類別轉換的 CLI 語法及 Deployment 共同組織授權後的命令生命週期。
 * 主要功能與流程：嚴格解析唯一組織、plan／apply／verify互斥參數與artifact路徑；apply先取得有界寫鎖，
 * 各命令只借用一個connector lease，接著建立operation-local store並執行對應工作。
 * 參數只允許 plan、apply、verify 三種固定形態；先驗證組織名稱、互斥旗標、完整雜湊與
 * 該組織 artifacts 路徑，再取得 apply 專屬寫鎖及一個 connector lease。
 * 組織身份由共同 catalog／ProductionGuard 與 server identity 核實，CLI 不接受 caller 提供
 * 教會 ID 作授權。Plan、SDK 分頁投影及 ExecuteMultiple 由各自有界元件負責。
 * 使用邊界與維護：所有檔案、SDK參照、候選計畫及操作紀錄只屬單次命令；lease先Dispose，寫鎖後釋放。
 * 取消保留 caller token 分類；其他故障以原始 Exception 交共用 report，不輸出錯誤本文或秘密。
 * 驗證由 OfferingMigrationCliTests 的真入口路由測試及同目錄 SDK／CLI 合成測試保護；
 * 這些測試不連接 Dataverse，也不代表任何組織的中繼資料或資料狀態。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace SpeechMessage.Dynamics.Deployment.Offerings;

/// <summary>奉獻類別轉換命令的嚴格入口；輸入與本次operation狀態不會跨命令保留。</summary>
internal static class OfferingMigrationCli
{
    private const string CommandName = "migrate-offering-categories";
    private const string PlanOption = "--plan";
    private const string ApplyOption = "--apply";
    private const string VerifyOption = "--verify";
    private const string HashOption = "--plan-sha256";

    /// <summary>驗證完整命令語法後才取得寫鎖與唯一SDK租約，避免非法輸入觸發組織I/O。</summary>
    /// <param name="args">共同ProductionGuard核定後的命令參數。</param>
    /// <param name="target">由organization catalog核定的canonical組織。</param>
    /// <param name="connector">每命令建立一個獨占lease的既有connector。</param>
    /// <param name="output">呼叫端持有的安全摘要writer。</param>
    /// <param name="report">SDK lease歸還後接收原始例外的共用診斷入口。</param>
    /// <param name="artifactRoot">測試可注入的隔離根；產品使用repo內artifacts/d365。</param>
    /// <param name="writeLockTimeout">apply等待既有同組織寫鎖的時間上限。</param>
    /// <param name="cancellationToken">命令owner提供的caller cancellation。</param>
    /// <returns>成功0、拒絕2、失敗1或明確caller取消130。</returns>
    internal static int Run(string[] args, OrganizationDefinition target, IOrganizationConnector connector,
        TextWriter output, Action<Exception> report, string? artifactRoot, TimeSpan? writeLockTimeout,
        CancellationToken cancellationToken)
    {
        if (!TryParse(args, target.Name, out var command))
        {
            output.WriteLine("Offering migration arguments are invalid or not allowlisted.");
            return 2;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            output.WriteLine("Command cancelled.");
            return 130;
        }

        try
        {
            var repositoryRoot = DeploymentCli.FindRepositoryRoot();
            var artifactsRoot = Path.GetFullPath(artifactRoot ?? Path.Combine(repositoryRoot, "artifacts", "d365"));
            var organizationRoot = Path.GetFullPath(Path.Combine(artifactsRoot, target.Name));
            if (command.PlanPath is not null)
            {
                if (!DeploymentCli.IsContainedPath(organizationRoot, command.PlanPath, out var planPath))
                {
                    output.WriteLine("Offering migration plan must be inside this organization's artifacts directory.");
                    return 2;
                }
                command = command with { PlanPath = planPath };
            }

            Schema.SchemaDeploymentCli.EnsureNoLinks(organizationRoot);
            using var writeLock = command.IsApply
                ? OrganizationWriteLock.Acquire(organizationRoot, writeLockTimeout ?? TimeSpan.FromMinutes(5), cancellationToken)
                : null;
            cancellationToken.ThrowIfCancellationRequested();
            using var lease = connector.Connect(target.Name);
            ProductionGuard.VerifyServer(lease.Service, target, cancellationToken);
            return Execute(command, target, lease.Service, organizationRoot, output, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            output.WriteLine("Command cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            try { report(exception); }
            catch { try { Console.Error.WriteLine("Offering migration diagnostic reporting failed."); } catch { } }
            output.WriteLine("Offering migration failed (" + exception.GetType().Name + "). Sensitive details are not shown.");
            return 1;
        }
    }

    /// <summary>執行已核定模式；此處集中保持單命令store與operation root的短生命週期。</summary>
    private static int Execute(OfferingMigrationCommand command, OrganizationDefinition target,
        Microsoft.Xrm.Sdk.IOrganizationService service, string organizationRoot, TextWriter output,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var operationRoot = DeploymentCli.CreateOperationDirectory(organizationRoot);
        using var store = new OfferingMigrationSdkStore(service, target, cancellationToken);
        if (command.IsVerify)
        {
            var result = OfferingMigrationExecutor.Verify(store);
            OfferingMigrationOutput.WriteVerification(result, output);
            return result.MissingLookupByChurch.Count == 0 && result.ForeignChurchLookups.Count == 0 ? 0 : 1;
        }

        if (command.IsApply)
        {
            var plan = OfferingMigrationFiles.ReadPlan(command.PlanPath!, organizationRoot, out var rawHash);
            using var operationLog = OfferingMigrationFiles.CreateApplyLog(operationRoot);
            OfferingMigrationExecutor.Apply(store, plan, rawHash, command.PlanSha256!, operationLog);
            output.WriteLine("Offering migration apply completed.");
            return 0;
        }

        var candidate = store.CreatePlan(OfferingMigrationCatalog.ScreenOptions);
        var planPath = OfferingMigrationFiles.WritePlan(operationRoot, candidate);
        var planHash = OfferingMigrationFiles.HashFile(planPath);
        output.WriteLine("Offering migration plan created; planSha256=" + planHash);
        output.WriteLine("Plan file: " + Path.GetFileName(planPath));
        return 0;
    }

    /// <summary>只接受三種互斥語法；重複、未知、空值或錯誤的組織都在connector前拒絕。</summary>
    internal static bool TryParse(string[] args, string expectedOrganization, out OfferingMigrationCommand command)
    {
        command = default;
        if (args.Length < 3 || args[0] != CommandName) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? organization = null;
        string? planPath = null;
        string? hash = null;
        var apply = false;
        var verify = false;
        for (var index = 1; index < args.Length; index++)
        {
            var option = args[index];
            if (!seen.Add(option)) return false;
            if (option is ApplyOption or VerifyOption)
            {
                if (option == ApplyOption) apply = true; else verify = true;
                continue;
            }
            if (option is not ("--org" or PlanOption or HashOption) || index + 1 >= args.Length
                || string.IsNullOrWhiteSpace(args[index + 1])
                || args[index + 1].StartsWith("--", StringComparison.Ordinal)) return false;
            var value = args[++index];
            if (option == "--org") organization = value;
            else if (option == PlanOption) planPath = value;
            else hash = value;
        }

        if (!string.Equals(organization, expectedOrganization, StringComparison.Ordinal)
            || apply && verify || planPath is not null && !apply
            || hash is not null && !apply
            || apply && (planPath is null || !ValidHash(hash))
            || verify && (planPath is not null || hash is not null)
            || !apply && !verify && (planPath is not null || hash is not null)
            || seen.Count != (apply ? 4 : verify ? 2 : 1))
            return false;

        command = new OfferingMigrationCommand(organization!, planPath, hash, apply, verify);
        return true;
    }

    /// <summary>雜湊必須是64個ASCII十六進位字元；格式驗證先於任何連線或檔案讀取。</summary>
    private static bool ValidHash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
}

/// <summary>只保留通過parser的本次命令欄位；不保存未驗證args陣列。</summary>
/// <param name="Organization">catalog中的canonical組織名。</param>
/// <param name="PlanPath">apply使用的組織artifacts plan路徑。</param>
/// <param name="PlanSha256">使用者核對的原始plan檔SHA-256。</param>
/// <param name="IsApply">是否以同組織寫鎖執行apply。</param>
/// <param name="IsVerify">是否執行唯讀驗證。</param>
internal readonly record struct OfferingMigrationCommand(
    string Organization, string? PlanPath, string? PlanSha256, bool IsApply, bool IsVerify);
