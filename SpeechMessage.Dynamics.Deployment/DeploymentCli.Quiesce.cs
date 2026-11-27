/*
檔案整體說明
目的與定位：接通 FS quiesce 公開命令，僅盤點或停用指定測試組織的複製自動化。
主要流程：嚴格解析 --org 與可選 --apply；連線前拒絕正式組織與未知選項。apply 要求
該組織 artifacts 根內的 quiesce-policy.json，綁定 F0 盤點的 server 組織 ID、保留／停用清冊
及嚴格 sourceFingerprint；公開 dry-run 可在沒有政策時產生管理來源 canonical 指紋。
每次先核對 SDK 真實組織身份，再寫正式 ID／數量與明確管理純值的 inventory；公開 dry-run 明確啟用
Plugin handler／型別／組件及 workflow 管理純值清冊。有非空 workflow.solutionid 時，
完整有界讀 solution／publisher 並按正式 ID 關聯來源、記錄 Resolution；不讀 configuration、secureconfig、
XAML、content 或 workflow 名稱；dry-run 不自行核准分類。apply 要求 policyVersion=2，強制重讀管理來源，
在首筆 beforeWrite 及 Update 前核對指紋與 02A 的 Plugin／workflow 分類；缺失或漂移拒絕，不能靠 keep 正式 ID 掩蓋來源替換。
指紋只描述來源穩定性、不單獨授權保留；離線規則驗證不代表現場已停用，H1 僅於 goodshepherd20 實機 apply／verify 後結案；
來源讀取不是伺服器交易，不宣稱可偵測最後讀取之後的並行改動或驗證禁讀的內容。
apply 完成後重新盤點，
仍有待停項目即失敗。dry-run 不讀憑證本文、不建立 Plugin DLL 或修改任何自動化。
apply 不先存另一輪 dry-run；Run 完成全部查詢及分類後，把實際寫入的同一候選交寫前回呼。
回呼以有界 WriteNew／Flush(true)／關閉／原子發布保存 before-image，再對正式檔案 bytes 算 SHA-256，
另以同一落盤流程保存 SHA 側檔；兩檔成功才允許第一筆 SDK Update。證據含核定 server 組織 ID、
操作識別、UTC 時間、正式信箱 ID、nullable state/status／delivery 原值與預期新值 0／0／2。
UTC 時間只由本次操作建立的 Localization IChurchClock 取得；時鐘無租戶狀態、快取或待釋放資源。
不讀 email、name、password、OAuth 或未驗證 rowversion；未取得目標 metadata 不宣稱實機驗證。
部分更新故障或取消保留完整原像與預期值供同組織逐 ID 重讀對帳；不自動重試或重新開放外送。
資源與隔離：單一命令擁有一次 lease，寫入另持同機共用組織鎖；先歸還 SDK 再釋放鎖。
檔案限固定 artifacts 範圍，拒絕 link／junction，輸出沿用有界 CreateNew、flush、close、原子發布。
取消在 SDK 前後檢查，正常 caller 取消回 130；失敗交原 Exception 至共用入口後，只補印有界 Dataverse 數字錯誤碼，
絕不輸出 SDK Message、TraceText、Reason 或個資。
驗證方式：QuiesceMailboxDeliveryTests、MultiTestOrganizationTests 與兩個 Quiesce 身分／來源測試檔用合成 SDK
驗公開 dry-run、分頁／狀態、來源純值、取消及租約清理；
離線成功不代表真正組織已停用，Live3P 必須另取鎖並保留實際讀回證據。
*/
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using SpeechMessage.Dynamics.Deployment.Organizations;
using SpeechMessage.Localization;

namespace SpeechMessage.Dynamics.Deployment;

internal static partial class DeploymentCli
{
    /// <summary>固定公開語法及測試組織關卡；所有 SDK／鎖離開 using 後才報告真正故障。</summary>
    private static int RunQuiesce(string[] args, OrganizationDefinition target, IOrganizationConnector connector,
        TextWriter output, Action<Exception> report, string? artifactRoot, TimeSpan? timeout, CancellationToken ct)
    {
        var apply = args.Length == 4 && args[3] == "--apply";
        if (target.Kind != "test" || args.Length is not (3 or 4) || args[1] != "--org"
            || args[2] != target.Name || args.Length == 4 && !apply)
        {
            output.WriteLine("Quiesce arguments or test organization are invalid.");
            return 2;
        }
        try
        {
            ct.ThrowIfCancellationRequested();
            var root = Path.GetFullPath(artifactRoot ?? Path.Combine(FindRepositoryRoot(), "artifacts", "d365"));
            var organizationRoot = Path.Combine(root, target.Name);
            Schema.SchemaDeploymentCli.EnsureNoLinks(organizationRoot);
            var policyPath = Path.Combine(organizationRoot, "quiesce-policy.json");
            var policy = File.Exists(policyPath) ? ReadDocumentFile<QuiescePolicy>(policyPath, organizationRoot) : null;
            if (apply && policy is null)
            {
                output.WriteLine("Quiesce apply requires the F0 organization-bound classification policy.");
                return 2;
            }
            if (apply && policy?.PolicyVersion != QuiescePolicy.CurrentPolicyVersion)
            {
                output.WriteLine("Quiesce apply requires the current versioned policy.");
                return 2;
            }
            using var writeLock = apply ? OrganizationWriteLock.Acquire(organizationRoot, timeout ?? TimeSpan.FromMinutes(5),
                GetSharedOrganizationLockDirectory(), ct) : null;
            using var lease = connector.Connect(target.Name);
            ct.ThrowIfCancellationRequested();
            var response = lease.Service.Execute(new RetrieveCurrentOrganizationRequest());
            ct.ThrowIfCancellationRequested();
            if (response is not RetrieveCurrentOrganizationResponse { Detail: { } detail }
                || detail.OrganizationId == Guid.Empty || !string.Equals(detail.UniqueName, target.Name, StringComparison.OrdinalIgnoreCase)
                || policy is not null && policy.OrganizationId != detail.OrganizationId)
                throw new InvalidDataException("Quiesce server identity does not match its approved scope.");
            var directory = CreateOperationDirectory(organizationRoot);
            // apply 的唯一候選在 Run 內完成分類後直接落盤，避免前一輪 dry-run 與真正寫入集合不同。
            var inventory = ClonedOrganizationQuiesce.Run(lease.Service, target, apply, ct, policy,
                includePluginIdentities: true,
                beforeWrite: apply
                    ? snapshot => WriteQuiesceBeforeImage(directory, detail.OrganizationId, target.Name, snapshot, ct)
                    : null);
            if (apply)
            {
                var after = ClonedOrganizationQuiesce.Run(lease.Service, target, false, ct, policy);
                ct.ThrowIfCancellationRequested();
                _ = Schema.SchemaFiles.WriteNew(directory, "quiesce-after.json", after);
                if (after.PendingWorkflowRuns != 0 || after.WorkflowDefinitionsToDisable != 0
                    || after.PluginStepsToDisable != 0 || after.MailboxConfigurationsToDisable != 0 || after.UnclassifiedPluginSteps != 0)
                    throw new InvalidDataException("Quiesce verification found remaining automation.");
            }
            else
            {
                ct.ThrowIfCancellationRequested();
                _ = Schema.SchemaFiles.WriteNew(directory, "quiesce-inventory.json", new
                { detail.OrganizationId, Organization = target.Name, Inventory = inventory });
            }
            output.WriteLine((apply ? "Quiesce verified; " : "Quiesce inventory only; ") + "candidates="
                + inventory.Candidates.Count.ToString(CultureInfo.InvariantCulture)
                + "; unclassified-steps=" + inventory.UnclassifiedPluginSteps.ToString(CultureInfo.InvariantCulture));
            return 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { output.WriteLine("Quiesce cancelled."); return 130; }
        catch (Exception exception)
        {
            try { report(exception); } catch { try { Console.Error.WriteLine("Quiesce diagnostic reporting failed."); } catch { } }
            WriteSupplementalFailureDiagnostics(exception, output);
            output.WriteLine("Quiesce failed (" + exception.GetType().Name + "). Sensitive details are not shown.");
            return 1;
        }
    }

    /// <summary>
    /// 保存 Run 即將寫入的同一完整快照；沿用 SchemaFiles 的 100 MiB、CreateNew、
    /// Flush(true)、close 與禁止覆寫的原子發布。第一份成功後串流計算實際檔案 SHA-256，
    /// 並將側檔同樣 flush 完成；任一步失敗或 caller 取消直接傳回，Run 尚未送第一筆 Update。
    /// 原像保留 nullable 原值，不讀額外 SDK、秘密或猜測版本；完整檔案由 CLI 操作目錄 owner
    /// 留存供失敗後按正式 ID 對帳，沒有自動回復、重試、背景輸出或跨命令狀態。
    /// 時間經操作內的 IChurchClock 取得零 offset UTC；使用既有無狀態來源，沒有 timer、
    /// 訂閱或 Dispose 責任，時鐘參照不超過本次呼叫，來源故障在任何 SDK Update 前原樣傳回。
    /// </summary>
    /// <param name="directory">CLI 核定且獨占的本次操作目錄；不是使用者或租戶提供的資料路徑。</param>
    /// <param name="organizationId">已由同一借用 SDK 核對的正式 server 組織 ID。</param>
    /// <param name="organization">catalog 解析並與 server 身分核對的 canonical 名稱。</param>
    /// <param name="snapshot">全部查詢及分類驗證後、第一筆 Update 前的同次不可變純值候選。</param>
    /// <param name="ct">caller 取消；在同步檔案 I/O 前後檢查，不保留 token 或註冊。</param>
    internal static void WriteQuiesceBeforeImage(string directory, Guid organizationId, string organization,
        ClonedOrganizationQuiesceResult snapshot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Schema.SchemaDeploymentCli.EnsureNoLinks(directory);
        var clock = new ChurchClock();
        var path = Schema.SchemaFiles.WriteNew(directory, "quiesce-inventory.json", new
        {
            OrganizationId = organizationId,
            Organization = organization,
            OperationId = Path.GetFileName(directory),
            CapturedAtUtc = clock.UtcNow,
            ExpectedMailboxDelivery = new
            {
                IncomingEmailDeliveryMethod = 0,
                OutgoingEmailDeliveryMethod = 0,
                ActDeliveryMethod = 2
            },
            Inventory = snapshot
        });
        ct.ThrowIfCancellationRequested();
        string sha256;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.Length > 100L * 1024 * 1024)
                throw new InvalidDataException("Quiesce evidence exceeds its bounded size.");
            sha256 = Convert.ToHexString(SHA256.HashData(stream));
        }
        ct.ThrowIfCancellationRequested();
        _ = Schema.SchemaFiles.WriteNew(directory, "quiesce-inventory-sha256.json", new { Sha256 = sha256 });
        ct.ThrowIfCancellationRequested();
    }
}
