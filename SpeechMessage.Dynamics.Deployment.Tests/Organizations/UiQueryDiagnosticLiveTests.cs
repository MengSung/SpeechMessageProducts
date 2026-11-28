/*
檔案整體說明
目的與定位：02A §10.4 的 FS 專用 Live3P 唯讀對照，直接使用方案內已編譯的 SDK 與
連線 owner；不是完整 plan 重試、不是部署驗收，也不連 goodshepherdback 或任何正式組織。
主要流程：逐測試組織先核對目標與伺服器，再針對 account 的 systemform／savedquery，
各送單頁一列、僅主鍵的未發布查詢；唯一變因為同一主鍵排序一次／兩次，不追續頁。
使用邊界：只有明確 Live3P／FSQueryDiagnostics 篩選才執行。呼叫端須保證沒有另一條線
同時寫 D365／IIS；本案不讀業務列、XML、Channel、秘密、LINE userId 或錯誤本文。
資源與隔離：每案擁有三分鐘取消來源、User Secrets 組態、獨占 lease、writer 及唯一診斷
目錄，全部 using／finally 排空；不保存跨組織可變物件、不建立 Task.Run 或其他背景工作。
同步連線及 SDK 已入場時等待返回後才 Dispose；client 傳輸上限三分鐘，不能假稱硬中止。
失敗與驗證：每個 SDK 拒絕都經共用 ExceptionDiagnostics 保存安全型別／stack，LINE 關閉；
測試輸出只來自離線已驗的有界格式。單排序失敗使本案失敗；雙排序結果照實記錄，
即使雙排序失敗而單排序成功，也不能據此斷言就是歷史完整 plan 的同一根因。
*/
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Crm.Sdk.Messages;
using PowerPlatform.Dataverse.Client;
using SpeechMessage.Dynamics.Deployment.Tests.Ui;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace SpeechMessage.Dynamics.Deployment.Tests.Organizations;

/// <summary>三個測試組織的小查詢對照；發現測試不連線，只有執行明確類別才讀 User Secrets。</summary>
[Trait("Category", "Live3P")]
[Trait("WP", "FSQueryDiagnostics")]
public sealed class UiQueryDiagnosticLiveTests
{
    /// <summary>本案唯一的測試輸出 owner；只接受 Probe 產生的固定摘要，不保存 SDK 或身分。</summary>
    private readonly ITestOutputHelper output;

    /// <summary>借入 xUnit 每案獨立輸出，不擁有其關閉責任，也不設成 static。</summary>
    /// <param name="output">測試框架擁有的安全摘要輸出。</param>
    public UiQueryDiagnosticLiveTests(ITestOutputHelper output) => this.output = output;

    /// <summary>
    /// 每案僅連一個白名單組織，依序完成四個小查詢後立即歸還 lease；
    /// 兩個單排序控制必須成功，對照的雙排序不預填成功或失敗。
    /// </summary>
    /// <param name="organization">三個核定測試組織之一，不能由外部網址或 credential 覆寫。</param>
    /// <returns>所有同步呼叫、組態、租約及安全診斷都已排空後完成。</returns>
    [Theory]
    [InlineData("goodshepherd20")]
    [InlineData("construction")]
    [InlineData("association")]
    public async Task ReadOnly_UnpublishedUiQuery_ReportsSingleAndRepeatedOrder(string organization)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SpeechMessage", "evidence", "FS-query-readonly", Guid.NewGuid().ToString("N"));
        ExceptionDiagnostics? diagnostics = null;
        Exception? failure = null;
        try
        {
            diagnostics = new ExceptionDiagnostics(Path.Combine(directory, "Logs"),
                outputOptions: new ExceptionOutputOptions(writeExceptionLog: true, sendLine: false));
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var ct = timeout.Token;
            var catalog = OrganizationCatalog.LoadDefault();
            if (organization is not ("goodshepherd20" or "construction" or "association"))
                throw new InvalidOperationException("FS query diagnostic target rejected.");
            var target = catalog.GetTest(organization);
            _ = ProductionGuard.Authorize(["verify", "--org", target.Name], catalog, null, ct);
            var configuration = DeploymentConfiguration.Create(typeof(UserSecretsOrganizationConnector).Assembly);
            using var configurationOwner = (IDisposable)configuration;
            var connector = new UserSecretsOrganizationConnector(configuration);
            using var lease = connector.Connect(target.Name);
            if (lease.Service is not OnPremiseClient client)
                throw new InvalidOperationException("FS query diagnostic requires the controlled SDK owner.");
            client.Timeout = TimeSpan.FromMinutes(3);
            ProductionGuard.VerifyServer(lease.Service, target, ct);
            output.WriteLine("FS-QUERY organization=" + target.Name + " identity=OK");
            var controlsPassed = true;
            foreach (var entity in new[] { "systemform", "savedquery" })
            {
                foreach (var repeated in new[] { false, true })
                {
                    using var writer = new StringWriter();
                    var result = UiQueryDiagnosticProbe.Run(entity, repeated, query =>
                    {
                        ct.ThrowIfCancellationRequested();
                        var response = lease.Service.Execute(new RetrieveUnpublishedMultipleRequest { Query = query });
                        ct.ThrowIfCancellationRequested();
                        if (response is not RetrieveUnpublishedMultipleResponse typed)
                            throw new InvalidDataException("FS query diagnostic response type rejected.");
                        return typed.EntityCollection;
                    }, writer, exception => _ = diagnostics.Report(exception, "Live3P.FS.QueryDiagnostic", notify: false), ct);
                    foreach (var line in writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
                        output.WriteLine(line);
                    if (!repeated && !result) controlsPassed = false;
                }
            }
            if (!controlsPassed)
                throw new InvalidOperationException("FS query diagnostic single-order control failed.");
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            if (diagnostics is not null)
            {
                if (failure is not null)
                    _ = diagnostics.Report(failure, "Live3P.FS.QueryDiagnosticOwner", notify: false);
                try { await diagnostics.DisposeAsync(); }
                catch (Exception exception)
                {
                    failure ??= exception;
                    Console.Error.WriteLine("[Live3P.FS.QueryDiagnostic] DiagnosticOwnerDisposeFailed");
                }
            }
        }
        if (failure is not null)
            throw new InvalidOperationException("FS readonly query diagnostic failed (" + failure.GetType().Name
                + "); sensitive details suppressed.");
    }
}
