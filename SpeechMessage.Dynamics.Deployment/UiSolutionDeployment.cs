/*
檔案整體說明
目的與定位：P5-03b UI 解決方案命令的 SDK 邊界，將 `ExportSolution`、`ImportSolution` 與發布要求接到既有單一組織 lease。
主要功能與流程：匯出時只要求固定未受管理的 SpeechMessageUi，交由 UiSolutionArchive 驗證與 D26 篩選後，以暫存目錄完整寫出 XML／資源；匯入時先從核定組織 artifacts 內讀取 ZIP、驗證完整候選，再一次送出匯入與發布。
信任邊界與資源擁有權：service 只借用 DeploymentCli 的單次 OrganizationLease，不保存或重用；輸入檔案、ZIP bytes、暫存目錄及輸出檔案皆由單次命令擁有，失敗會刪除本次建立的暫存物，不碰其他組織或任意路徑。
適用範圍與驗證：只允許 goodshepherdback、固定方案名稱及固定 repository／organization artifacts 邊界；本檔的 SDK request 契約由離線假服務測試驗證，未宣稱已執行實機 UI 匯出入。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Xrm.Sdk;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>UI 解決方案 SDK 操作；不持有跨命令狀態，所有檔案與服務都由目前命令擁有。</summary>
internal static class UiSolutionDeployment
{
    private const string SolutionName = "SpeechMessageUi";

    /// <summary>匯出指定方案並以安全候選寫到固定 solutions 目錄。</summary>
    internal static void Export(IOrganizationService service, string repositoryRoot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = service.Execute(new OrganizationRequest("ExportSolution")
        {
            Parameters =
            {
                ["SolutionName"] = SolutionName,
                ["Managed"] = false
            }
        });
        cancellationToken.ThrowIfCancellationRequested();
        if (response.Results["ExportSolutionFile"] is not byte[] archive)
            throw new InvalidDataException("The UI solution export did not return an archive.");

        var files = UiSolutionArchive.Prepare(archive);
        var destination = Path.Combine(repositoryRoot, "d365", "solutions", SolutionName);
        PublishDirectory(files, destination);
    }

    /// <summary>驗證 artifacts 內 ZIP 後匯入並發布；讀檔與 SDK 操作均在同一命令內完成。</summary>
    internal static void Import(IOrganizationService service, string archivePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(archivePath))
            throw new InvalidDataException("The UI solution archive is unavailable.");
        var archive = File.ReadAllBytes(archivePath);
        var files = UiSolutionArchive.Prepare(archive);
        var packed = UiSolutionArchive.Pack(files);
        cancellationToken.ThrowIfCancellationRequested();
        service.Execute(new OrganizationRequest("ImportSolution")
        {
            Parameters =
            {
                ["CustomizationFile"] = packed,
                ["PublishWorkflows"] = false,
                ["OverwriteUnmanagedCustomizations"] = false
            }
        });
        cancellationToken.ThrowIfCancellationRequested();
        service.Execute(new OrganizationRequest("PublishAllXml"));
    }

    /// <summary>寫出完整 operation-local 候選；先建立暫存目錄，成功後才發布至固定目標。</summary>
    private static void PublishDirectory(IReadOnlyDictionary<string, byte[]> files, string destination)
    {
        var parent = Path.GetDirectoryName(destination) ?? throw new InvalidDataException("The UI solution destination is invalid.");
        Directory.CreateDirectory(parent);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(temporary);
            foreach (var item in files)
            {
                var path = Path.Combine(temporary, item.Key.Replace('/', Path.DirectorySeparatorChar));
                var directory = Path.GetDirectoryName(path)!;
                Directory.CreateDirectory(directory);
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.Write(item.Value);
                stream.Flush(flushToDisk: true);
            }
            if (Directory.Exists(destination))
                throw new IOException("The UI solution destination already exists.");
            Directory.Move(temporary, destination);
        }
        finally
        {
            if (Directory.Exists(temporary))
                Directory.Delete(temporary, recursive: true);
        }
    }
}
