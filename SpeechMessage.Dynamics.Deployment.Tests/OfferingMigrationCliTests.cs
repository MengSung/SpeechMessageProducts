/*
 * 檔案整體說明
 *
 * 目的與定位：保護奉獻類別轉換 CLI 的共同入口路由及連線前命令識別。
 * 主要功能與流程：以正式CLI入口送入plan、apply、verify與backup語法，確認命令分流後才會呼叫connector。
 * 測試經正式 DeploymentCli 與 organizations 白名單，但 connector 只會拋出本測試專用故障，
 * 不建立 OrganizationLease、不建立 Dataverse client、不呼叫任何組織。
 * 故障注入包含固定connector拒絕與backup旗標互斥錯誤；決定性斷言是合法plan／backup語法
 * 必須抵達connector邊界、原例外型別保留，錯誤混用或非核定組織在連線前拒絕。
 * 這些離線測試不代表plan、備份讀取或SDK adapter已對實際組織完成。
 * 使用邊界與維護：StringWriter 與例外集合均由單一測試持有；測試不開檔、不讀 secrets、
 * 不啟動 task、timer 或背景工作，結束即釋放所有測試物件。
 */
using System;
using System.Collections.Generic;
using System.IO;
using SpeechMessage.Dynamics.Deployment.Offerings;
using SpeechMessage.Dynamics.Deployment;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>驗奉獻轉換命令由正式CLI入口識別，且解析成功前不進舊Plugin parser。</summary>
public sealed class OfferingMigrationCliTests
{
    private const string ValidHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private static readonly string[] PlanArguments =
        { "migrate-offering-categories", "--org", "goodshepherdback" };
    private static readonly string[] ApplyArguments =
    {
        "migrate-offering-categories", "--apply", "--plan", "artifacts/d365/goodshepherdback/plan.json",
        "--plan-sha256", ValidHash, "--org", "goodshepherdback"
    };
    private static readonly string[] VerifyArguments =
        { "migrate-offering-categories", "--verify", "--org", "goodshepherdback" };
    private static readonly string[] BackupArguments =
        { "migrate-offering-categories", "--backup", "--org", "goodshepherdback" };
    private static readonly string[] MutuallyExclusiveArguments =
        { "migrate-offering-categories", "--org", "goodshepherdback", "--verify", "--apply" };
    private static readonly string[] DuplicateArguments =
        { "migrate-offering-categories", "--org", "goodshepherdback", "--verify", "--verify" };

    /// <summary>Apply與verify旗標可出現在固定參數的任意位置，但重複及互斥模式必須在SDK前拒絕。</summary>
    [Fact]
    public void Parse_ApplyAndVerifyFlags_AcceptAnyOrderAndRejectAmbiguity()
    {
        Assert.True(OfferingMigrationCli.TryParse(ApplyArguments, "goodshepherdback", out var apply));
        Assert.True(apply.IsApply);
        Assert.Equal("artifacts/d365/goodshepherdback/plan.json", apply.PlanPath);

        Assert.True(OfferingMigrationCli.TryParse(VerifyArguments, "goodshepherdback", out var verify));
        Assert.True(verify.IsVerify);

        Assert.False(OfferingMigrationCli.TryParse(MutuallyExclusiveArguments, "goodshepherdback", out _));
        Assert.False(OfferingMigrationCli.TryParse(DuplicateArguments, "goodshepherdback", out _));
    }

    /// <summary>備份模式只接受核定組織與單一旗標，不能和apply或verify混用。</summary>
    [Fact]
    public void Parse_BackupFlag_IsExclusiveAndBoundToApprovedOrganization()
    {
        Assert.True(OfferingMigrationCli.TryParse(BackupArguments, "goodshepherdback", out var backup));
        Assert.True(backup.IsBackup);
        Assert.False(backup.IsApply);
        Assert.False(backup.IsVerify);
        Assert.False(OfferingMigrationCli.TryParse(
            ["migrate-offering-categories", "--backup", "--verify", "--org", "goodshepherdback"],
            "goodshepherdback", out _));
        Assert.False(OfferingMigrationCli.TryParse(BackupArguments, "other-org", out _));
    }

    /// <summary>合法plan命令應抵達唯一connector；合成連線故障被安全處理且不建立真實服務。</summary>
    [Fact]
    public void Run_PlanCommand_RoutesToOfferingMigrationBeforePluginManifest()
    {
        var connector = new RejectingConnector();
        using var output = new StringWriter();
        var reported = new List<Exception>();

        var result = DeploymentCli.Run(
            PlanArguments,
            connector,
            output,
            reported.Add,
            Path.GetTempPath(),
            cancellationToken: default);

        Assert.Equal(1, result);
        Assert.Equal(1, connector.Attempts);
        Assert.Single(reported);
        Assert.IsType<SyntheticConnectorBoundaryException>(reported[0]);
        Assert.DoesNotContain("Synthetic connector boundary", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>backup旗標通過共同Deployment授權與奉獻CLI分流，但在合成connector邊界停止。</summary>
    [Fact]
    public void Run_BackupCommand_RoutesThroughApprovedTestOrganizationGate()
    {
        var connector = new RejectingConnector();
        using var output = new StringWriter();
        var reported = new List<Exception>();

        var result = DeploymentCli.Run(
            BackupArguments,
            connector,
            output,
            reported.Add,
            Path.GetTempPath(),
            cancellationToken: default);

        Assert.Equal(1, result);
        Assert.Equal(1, connector.Attempts);
        Assert.IsType<SyntheticConnectorBoundaryException>(Assert.Single(reported));
        Assert.DoesNotContain("Synthetic connector boundary", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>僅本測試可用的connector sentinel；永不建立服務或lease。</summary>
    private sealed class RejectingConnector : IOrganizationConnector
    {
        /// <summary>記錄被呼叫次數，僅用於命令路由斷言。</summary>
        internal int Attempts { get; private set; }

        /// <summary>在建立任何SDK服務之前中止，將執行停在本機命令邊界。</summary>
        public OrganizationLease Connect(string organization)
        {
            Attempts++;
            throw new SyntheticConnectorBoundaryException();
        }
    }

    /// <summary>固定測試控制流程例外，不含秘密或外部服務資訊。</summary>
    private sealed class SyntheticConnectorBoundaryException()
        : InvalidOperationException("Synthetic connector boundary.");
}
