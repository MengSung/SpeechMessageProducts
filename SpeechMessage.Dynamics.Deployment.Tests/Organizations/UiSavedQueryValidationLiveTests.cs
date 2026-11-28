/*
檔案整體說明
目的與定位：02A §10.4 方案內 FS savedquery 專用唯讀 Live3P 工具，只固定 goodshepherd20；
發現與離線測試不連線。本檔不重試 apply，不修改或發布任何元件，也不讀業務列或 Channel。
主要流程：先 ProductionGuard.Authorize，再獨占連線核對伺服器，只讀 new_linechannel 的
實體 metadata，核對正式主鍵／主名稱／ObjectTypeCode 後載正式 SchemaFiles 及 ViewDefinitions。
依序 Validate 正式 Fetch 與同表主鍵／名稱最小 Fetch；另讀同表 QueryType 0 的 savedquery
版面結構對照，不讀元件名稱，所有輸出僅由離線已測 Probe 產生，沒有 XML、GUID 或任意訊息。
適用邊界：Fetch 通過僅表示 ValidateSavedQueryResponse 成立，Layout 對照也只是結構觀察，
兩者都不能證明 Create、啟用、方案加入或 Publish 成功，不能推論先前 apply 是零寫入。
生命週期與隔離：每案唯一三分鐘 CTS、組態、獨占租約與 writer 皆 using；SDK client 的
Timeout 同為三分鐘，已入場同步 SDK 必須返回後才 Dispose，不以背景 Task 假裝硬中止。
組織候選不跨案保存；先退出 lease、組態、CTS，再於 finally 排空唯一 ExceptionDiagnostics。
故障處理：每個真正 SDK 故障的原 Exception 交共用入口，sendLine:false；reporter 失敗只走
固定 stderr，仍確保 DisposeAsync。對照故障保留 FAIL 並使整案失敗，不把失敗吞成成功。
驗證方式：離線測試只驗語法 owner 順序與純委派故障，不冒稱實機已通過；必須由主線明確
篩選 Category=Live3P／WP=FSSavedQueryDiagnostics 後另行執行，本次子任務禁止執行 Live。
*/
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using PowerPlatform.Dataverse.Client;
using SpeechMessage.Dynamics.Deployment.Schema;
using SpeechMessage.Dynamics.Deployment.Tests.Ui;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace SpeechMessage.Dynamics.Deployment.Tests.Organizations;

/// <summary>僅提供固定測試組織的明確 Live 入口；建構／測試發現不讀秘密、不建立連線或背景資源。</summary>
[Trait("Category", "Live3P")]
[Trait("WP", "FSSavedQueryDiagnostics")]
public sealed class UiSavedQueryValidationLiveTests
{
    /// <summary>借用 xUnit 的每案輸出 owner；僅接收 Probe 安全摘要，不持有身分、租約或診斷資料。</summary>
    private readonly ITestOutputHelper output;

    /// <summary>只保存框架輸出，不在建構時載入組態、連線或建立診斷 owner。</summary>
    /// <param name="output">框架負責生命週期的每案輸出，不由本類 Dispose。</param>
    public UiSavedQueryValidationLiveTests(ITestOutputHelper output) => this.output = output;

    /// <summary>
    /// 固定白名單與正式 metadata 的雙 Fetch 驗證及既有版面結構對照；任何故障使案失敗。
    /// 所有 I/O 都在同一租約內依序執行，沒有重試、並行、業務列讀取或任何持久化操作。
    /// </summary>
    /// <returns>已入場 SDK、租約、組態與診斷 owner 全部完成並排空後才結束。</returns>
    [Fact]
    public async Task ReadOnly_GeneratedAndMinimalFetch_ValidationDoesNotProveCreate()
    {
        const string organization = "goodshepherd20";
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SpeechMessage", "evidence", "FS-savedquery-readonly", Guid.NewGuid().ToString("N"));
        ExceptionDiagnostics? diagnostics = null;
        Exception? failure = null;
        try
        {
            diagnostics = new ExceptionDiagnostics(Path.Combine(directory, "Logs"),
                outputOptions: new ExceptionOutputOptions(writeExceptionLog: true, sendLine: false));
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var ct = timeout.Token;
            var catalog = OrganizationCatalog.LoadDefault();
            var target = catalog.GetTest(organization);
            _ = ProductionGuard.Authorize(["verify", "--org", organization], catalog, null, ct);
            var configuration = DeploymentConfiguration.Create(typeof(UserSecretsOrganizationConnector).Assembly);
            using var configurationOwner = (IDisposable)configuration;
            using var lease = new UserSecretsOrganizationConnector(configuration).Connect(organization);
            if (lease.Service is not OnPremiseClient client)
                throw new InvalidOperationException("Saved query diagnostic requires the controlled SDK owner.");
            client.Timeout = TimeSpan.FromMinutes(3);
            ProductionGuard.VerifyServer(lease.Service, target, ct);
            ct.ThrowIfCancellationRequested();
            var response = lease.Service.Execute(new RetrieveEntityRequest
            {
                LogicalName = "new_linechannel", EntityFilters = EntityFilters.Entity, RetrieveAsIfPublished = true
            });
            ct.ThrowIfCancellationRequested();
            var metadata = (response as RetrieveEntityResponse)?.EntityMetadata;
            if (metadata is null || metadata.MetadataId is not { } id || id == Guid.Empty
                || metadata.LogicalName != "new_linechannel" || metadata.PrimaryIdAttribute != "new_linechannelid"
                || metadata.PrimaryNameAttribute != "new_name" || metadata.ObjectTypeCode is not > 0)
                throw new InvalidDataException("Saved query diagnostic metadata identity rejected.");
            var schema = SchemaFiles.LoadForOrganization(DeploymentCli.FindRepositoryRoot(), organization);
            var view = schema.Views.Single(view => view.Entity == "new_linechannel");
            var generated = ViewDefinitions.Generate(schema, view, primaryId: metadata.PrimaryIdAttribute,
                objectTypeCode: metadata.ObjectTypeCode.Value);
            var passed = true;
            foreach (var minimal in new[] { false, true })
            {
                using var writer = new StringWriter();
                var result = UiSavedQueryValidationProbe.Run(generated, minimal, lease.Service.Execute, writer,
                    exception => _ = diagnostics.Report(exception, "Live3P.FS.SavedQueryValidation", notify: false), ct);
                foreach (var line in writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
                    output.WriteLine(line);
                if (!result) passed = false;
            }
            foreach (var line in UiSavedQueryValidationProbe.ReadLayoutComparison(generated, lease.Service.RetrieveMultiple, ct))
                output.WriteLine(line);
            if (!passed)
                throw new InvalidOperationException("Saved query Fetch validation failed; persistence is not proven.");
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            if (diagnostics is not null)
            {
                try
                {
                    if (failure is not null)
                        _ = diagnostics.Report(failure, "Live3P.FS.SavedQueryValidationOwner", notify: false);
                }
                catch
                {
                    Console.Error.WriteLine("[Live3P.FS.SavedQueryValidation] DiagnosticReportingFailed");
                }
                finally
                {
                    try { await diagnostics.DisposeAsync(); }
                    catch (Exception exception)
                    {
                        failure ??= exception;
                        Console.Error.WriteLine("[Live3P.FS.SavedQueryValidation] DiagnosticOwnerDisposeFailed");
                    }
                }
            }
        }
        if (failure is not null)
            throw new InvalidOperationException("FS readonly saved query diagnostic failed; "
                + UiSavedQueryValidationProbe.DescribeFailure(failure));
    }
}
