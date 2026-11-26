/*
檔案整體說明
目的與定位：接通 F0 的公開唯讀盤點入口，讓 FS 在任何寫入前以核身後的完整清冊分類複製自動化。
主要流程：只接受 inventory --org goodshepherd20；其他組織、寫入旗標或未知選項均在連線前拒絕。
以共同 connector 取得單次獨占租約後交 ClonedOrganizationInventory 核對固定 server 名稱與正式 ID，
讀取管理 metadata 與 server aggregate count；不讀取任何業務列內容、秘密或定義 XML。
輸出與保留：完整結果才寫入該組織 artifacts 唯一操作目錄的 clone-inventory.json；
使用既有有界、CreateNew、flush、原子發布 writer，不覆寫歷史證據。終端只輸出固定成功摘要。
資源與隔離：命令唯一擁有 lease，所有同步 SDK I/O 返回後立即歸還，不建立寫鎖、快取、Task、
timer、訂閱或跨命令狀態；純值候選只存活至輸出完成，檔案依既有本機證據政策保留。
失敗與驗證：正常 caller 取消回 130 且不告警；真正故障在租約已歸還後把原 Exception 交共用入口，
不輸出 SDK 訊息或欄值。ClonedOrganizationInventoryTests 保護真公開路由、零寫入、完整發布及租約清理；
離線結果不代表任何實機已盤點、停用或可部署，實機另依計畫持測試環境鎖執行。
*/
using System;
using System.IO;
using System.Threading;
using SpeechMessage.Dynamics.Deployment.Organizations;

namespace SpeechMessage.Dynamics.Deployment;

internal static partial class DeploymentCli
{
    /// <summary>只接受核定測試複本的固定唯讀命令；完整盤點成功後才發布證據，所有外部資源皆於回報前歸還。</summary>
    private static int RunCloneInventory(string[] args, OrganizationDefinition target, IOrganizationConnector connector,
        TextWriter output, Action<Exception> report, string? artifactRoot, CancellationToken cancellationToken)
    {
        if (target.Kind != "test" || target.Name != "goodshepherd20"
            || args.Length != 3 || args[1] != "--org" || args[2] != target.Name)
        {
            output.WriteLine("Inventory requires the exact authorized test clone and read-only syntax.");
            return 2;
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = Path.GetFullPath(artifactRoot ?? Path.Combine(FindRepositoryRoot(), "artifacts", "d365"));
            var organizationRoot = Path.Combine(root, target.Name);
            Schema.SchemaDeploymentCli.EnsureNoLinks(organizationRoot);
            using var lease = connector.Connect(target.Name);
            var snapshot = ClonedOrganizationInventory.Read(lease.Service, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var directory = CreateOperationDirectory(organizationRoot);
            _ = Schema.SchemaFiles.WriteNew(directory, "clone-inventory.json", snapshot);
            output.WriteLine("Complete read-only clone inventory created; no business row content was read.");
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            output.WriteLine("Inventory cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            try { report(exception); }
            catch { try { Console.Error.WriteLine("Inventory diagnostic reporting failed."); } catch { } }
            WriteSupplementalFailureDiagnostics(exception, output);
            output.WriteLine("Inventory failed (" + exception.GetType().Name + "). Sensitive details are not shown.");
            return 1;
        }
    }
}
