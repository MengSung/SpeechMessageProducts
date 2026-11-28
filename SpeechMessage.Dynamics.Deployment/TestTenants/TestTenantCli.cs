/*
檔案整體說明
目的與定位：F12 三產品測試租戶 CLI 的單命令組合根，只接受 create、invite 與 cleanup。
主要流程：先完整驗證固定組織、產品、代碼與有界輸入，再經 ProductionGuard、整台電腦共用組織鎖及獨占 SDK lease；建立與清理委派 TestTenantService，邀請委派 TestInviteService 並明確交付 LIFF fragment 網址與 QR。
信任與資料：LINE 金鑰僅來自 User Secrets 中明列測試 Channel，絕不載入複製的舊 Channel 或按資料庫第一列猜金鑰。LINE userId 不回顯、不入一般 log 或 manifest；邀請 token 僅存在指定交付網址及 QR。所有可變狀態只存單次命令。
資源擁有權與隔離：SDK、鎖、短命 LINE client、DI budget scope 與 QR 圖像均有唯一 owner，按操作排空後反序釋放；取消不遺棄同步 SDK 或 HTTP。參數拒絕與額度耗盡使用固定文字，真正故障交外層共用診斷。離線 parser 與 owner 測試不能代表實機資料建立。
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using LineMessagingProcessor.Core;
using SpeechMessage.Scheduling;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessage.Dynamics.Deployment.TestTenants;

/// <summary>只在單命令保留輸入；格式化固定遮罩，不列印私人 LINE userId。</summary>
internal sealed record TestTenantCommand(string Operation, string Organization, string Product, string Code,
    string? Name, string? ExpectedChannel, IReadOnlyList<string> Admins, IReadOnlyList<string> Members, string? Role, int Minutes)
{
    /// <summary>不讓 record 自動 ToString 把使用者識別或邀請資訊寫到診斷。</summary>
    public override string ToString() => "TestTenantCommand([REDACTED])";
}

/// <summary>測試專用 CLI 解析及單命令 owner，與正式部署命令保持明確分派。</summary>
internal static class TestTenantCli
{
    /// <summary>命令先通過解析及測試白名單才建立資源；所有 owner 釋放後才回報真正失敗。</summary>
    internal static async Task<int> RunAsync(string[] args, OrganizationCatalog catalog, IOrganizationConnector connector,
        TextWriter output, IConfiguration configuration, Action<Exception> report, string? artifactRoot,
        ExceptionDiagnostics? diagnostics, CancellationToken ct)
    {
        TestTenantCommand command;
        OrganizationDefinition target;
        try
        {
            command = Parse(args);
            target = ProductionGuard.Inspect(args, catalog, ct).Organization;
            if (target.Kind != "test" || target.Name != command.Organization) throw Invalid();
        }
        catch (Exception failure) when (failure is ArgumentException or InvalidDataException)
        {
            output.WriteLine("Test tenant command arguments or organization were refused.");
            return 2;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            output.WriteLine("Test tenant command cancelled before resource acquisition.");
            return 130;
        }
        var owners = new List<Func<ValueTask>>(5);
        ExceptionDispatchInfo? primary = null;
        List<Exception>? cleanupFailures = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            // 只核定公開白名單是否存在；尚未配發前不能以第一筆 Channel 代替 F3 的選擇。
            ReadApprovedChannels(command.Product, configuration);
            var artifacts = Path.GetFullPath(artifactRoot ?? Path.Combine(DeploymentCli.FindRepositoryRoot(), "artifacts"));
            Schema.SchemaDeploymentCli.EnsureNoLinks(artifacts);
            var organizationRoot = Path.Combine(artifacts, "d365", target.Name);
            Schema.SchemaDeploymentCli.EnsureNoLinks(organizationRoot);
            var writeLock = OrganizationWriteLock.Acquire(organizationRoot, TimeSpan.FromMinutes(5),
                artifactRoot is null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SpeechMessage", "d365-locks") :
                    Path.Combine(artifacts, "shared-locks"), ct);
            owners.Add(() => { writeLock.Dispose(); return ValueTask.CompletedTask; });
            ct.ThrowIfCancellationRequested();
            var lease = connector.Connect(target.Name);
            owners.Add(() => { lease.Dispose(); return ValueTask.CompletedTask; });
            ProductionGuard.VerifyServer(lease.Service, target, ct);
            await ExecuteCommandAsync(command, lease.Service, artifacts, output,
                configuration, diagnostics, owners, ct).ConfigureAwait(false);
        }
        catch (Exception failure) { primary = ExceptionDispatchInfo.Capture(failure); }
        finally
        {
            for (var index = owners.Count - 1; index >= 0; index--)
            {
                try { await owners[index]().ConfigureAwait(false); }
                catch (Exception failure) { (cleanupFailures ??= []).Add(failure); }
            }
            owners.Clear();
        }
        if (primary is null && cleanupFailures is null) return 0;
        if (cleanupFailures is null && primary?.SourceException is OperationCanceledException && ct.IsCancellationRequested)
        {
            output.WriteLine("Test tenant command cancelled; retained evidence can be resumed.");
            return 130;
        }
        if (cleanupFailures is null && primary?.SourceException is TestTenantLineCleanupBudgetExceededException)
        {
            output.WriteLine("Test tenant cleanup retained its plan; the Channel hourly budget is exhausted.");
            return 3;
        }
        Exception reported;
        if (cleanupFailures is null) reported = primary!.SourceException;
        else
        {
            if (primary is not null) cleanupFailures.Insert(0, primary.SourceException);
            reported = new AggregateException("Test tenant command or resource cleanup failed.", cleanupFailures);
        }
        try { report(reported); }
        catch (Exception) { Console.Error.WriteLine("[TestTenant] DiagnosticReportingFailed"); }
        output.WriteLine("Test tenant command failed. Sensitive details are not shown; retained manifests must be preserved.");
        return 1;
    }

    /// <summary>操作只借用當次 SDK；所有外部 client 與 SQL scope 登錄於最外層反序 owner 清單。</summary>
    private static async Task ExecuteCommandAsync(TestTenantCommand command, IOrganizationService service, string artifacts,
        TextWriter output, IConfiguration configuration, ExceptionDiagnostics? diagnostics,
        List<Func<ValueTask>> owners, CancellationToken ct)
    {
        var tenants = new TestTenantService(service, command.Organization, command.Product, artifacts);
        if (command.Operation == "create")
        {
            tenants.Create(command.Code, command.Name!, command.ExpectedChannel, command.Admins, command.Members, ct,
                actual => SelectChannel(command.Product, actual, configuration));
            output.WriteLine("Test tenant created; identifier-only manifest: test-tenants/" + command.Code + ".json");
            return;
        }
        var manifest = TestTenantManifest.Load(artifacts, command.Organization, command.Code);
        if (manifest.Product != command.Product) throw new InvalidDataException("Test tenant manifest product does not match.");
        if (command.Operation == "invite")
        {
            var channelKey = tenants.ReadChannelKey(command.Code, ct);
            var secretPrefix = SelectChannel(command.Product, channelKey, configuration);
            var liffId = ReadLiffId(configuration, command.Product);
            var line = OpenLine(secretPrefix);
            var bot = await line.Client.GetBotInfoAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (bot?.BasicId is not { Length: >= 2 and <= 64 } basic || basic[0] != '@' ||
                basic.Skip(1).Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')))
                throw new InvalidDataException("The approved test Channel basic identity is invalid.");
            var expectedBasic = configuration[secretPrefix + ":BasicId"];
            if (!string.IsNullOrEmpty(expectedBasic) && !string.Equals(expectedBasic, basic, StringComparison.Ordinal))
                throw new InvalidDataException("The approved test Channel bot identity does not match.");
            var invite = new TestInviteService(service, command.Organization, command.Product, channelKey: channelKey)
                .Create(manifest.TenantId, command.Role!, command.Minutes, ct);
            var url = "https://liff.line.me/" + liffId + "?liff.state=enroll#token=" + invite.Token;
            var friend = "https://line.me/R/ti/p/" + Uri.EscapeDataString(basic);
            var paths = TestTenantInviteQr.Write(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".speechmessage", "3p-phone"),
                command.Code, url, friend);
            // 這是使用者明確要求的邀請交付，不可把本輸出重新導向一般 evidence/log。
            output.WriteLine("Private LIFF invitation (expires UTC " + invite.ExpiresAt.ToString("O", CultureInfo.InvariantCulture) + "): " + url);
            output.WriteLine("Invitation QR: " + paths.Invite);
            output.WriteLine("Add-friend URL: " + friend);
            output.WriteLine("Add-friend QR: " + paths.Friend);
            return;
        }
        var connection = configuration["TestTenants:Products:" + command.Product + ":SchedulingConnectionString"];
        if (string.IsNullOrWhiteSpace(connection) || diagnostics is null)
            throw new InvalidOperationException("A validated platform Scheduling configuration is required for cleanup.");
        ct.ThrowIfCancellationRequested();
        var current = service.Execute(new RetrieveCurrentOrganizationRequest());
        if (current is not RetrieveCurrentOrganizationResponse { Detail: { } detail } || detail.OrganizationId == Guid.Empty ||
            !string.Equals(detail.UniqueName, command.Organization, StringComparison.Ordinal))
            throw new InvalidDataException("Test tenant Scheduling organization identity is invalid.");
        var services = new ServiceCollection();
        services.AddSingleton(diagnostics); // 外部 instance 註冊，不把借入 diagnostics 的 Dispose 責任交容器。
        services.AddProductScheduling(new SchedulingOptions(command.Product, detail.OrganizationId, connection));
        var provider = services.BuildServiceProvider();
        owners.Add(provider.DisposeAsync);
        var scope = provider.CreateAsyncScope();
        owners.Add(scope.DisposeAsync);
        // 不啟動 IHostedService；僅延遲使用既有 SQL budget，不建立 Hangfire worker。
        await tenants.CleanupAsync(command.Code, async (actualChannel, users, cancellation) =>
        {
            var secretPrefix = SelectChannel(command.Product, actualChannel, configuration);
            var line = OpenLine(secretPrefix);
            var cleanup = new TestTenantLineCleanup(line.Client, scope.ServiceProvider.GetRequiredService<IChannelRateBudget>(), command.Product, actualChannel);
            var plan = await TestTenantLinePlanStore.LoadOrCreateAsync(artifacts, command.Organization, command.Product,
                actualChannel, command.Code, () => cleanup.CreatePlanAsync(command.Code, cancellation), cancellation).ConfigureAwait(false);
            await cleanup.CleanupAsync(plan, users, cancellation).ConfigureAwait(false);
            // LINE 全部成功才刪可信續跑計畫；若刪檔失敗，SDK 尚未刪除租戶，仍可精確續跑。
            TestTenantLinePlanStore.Delete(artifacts, command.Organization, command.Product, actualChannel, command.Code);
        }, ct).ConfigureAwait(false);
        output.WriteLine("Test tenant cleanup completed; only the verified tenant scope was removed.");

        // 傳輸只由此次 operation 建立並登錄外層唯一 owner；任何 await 返回前均不處置或遺棄 I/O。
        LineMessagingClientLease OpenLine(string prefix)
        {
            var token = configuration[prefix + ":ChannelAccessToken"];
            if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("The approved test Channel credential is unavailable.");
            ct.ThrowIfCancellationRequested();
            var lease = new LineMessagingClientLease(token);
            owners.Add(() => { lease.Dispose(); return ValueTask.CompletedTask; });
            return lease;
        }
    }

    /// <summary>只核對伺服器配發的公開身分；未命中明列白名單就拒絕，絕不退回第一筆設定。</summary>
    internal static string SelectChannel(string product, string actualChannel, IConfiguration configuration)
    {
        var approved = ReadApprovedChannels(product, configuration);
        return approved.TryGetValue(actualChannel, out var prefix) ? prefix :
            throw new InvalidDataException("The allocated Channel is not in the approved test allowlist.");
    }

    /// <summary>只讀各產品核定 Channel 的公開 ID；重複映射、空白名單或錯產品一律拒絕，載入不到任何 token。</summary>
    private static Dictionary<string, string> ReadApprovedChannels(string product, IConfiguration configuration)
    {
        var names = product switch
        {
            "GoodButler" => new[] { "GoodButler" },
            "GoodShepherd2" or "GoodGuild" => new[] { "T01", "T02" },
            _ => throw Invalid()
        };
        var approved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var prefix = "ThreeProducts:Line:" + name;
            var key = configuration[prefix + ":ChannelId"];
            if (key is null) continue;
            if (!ValidChannel(key) || !approved.TryAdd(key, prefix))
                throw new InvalidDataException("The approved test Channel mapping is invalid or duplicated.");
        }
        if (approved.Count == 0) throw new InvalidOperationException("An approved test Channel mapping is required; no fallback is allowed.");
        return approved;
    }

    /// <summary>只使用伺服器設定的公開 LIFF app ID，固定官方網址，不接受任意目的地或 caller 指定 app。</summary>
    private static string ReadLiffId(IConfiguration configuration, string product)
    {
        var value = configuration["TestTenants:Products:" + product + ":LiffId"];
        if (value is not { Length: >= 12 and <= 106 } || value[10] != '-' ||
            value.Take(10).Any(character => character is < '0' or > '9') ||
            value.Skip(11).Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
            throw new InvalidOperationException("The approved test LIFF public app mapping is unavailable.");
        var login = configuration["ThreeProducts:Line:LiffLogin:ChannelId"];
        if (string.IsNullOrEmpty(login) || !value.StartsWith(login + "-", StringComparison.Ordinal))
            throw new InvalidDataException("The test LIFF app does not belong to the approved Login Channel.");
        return value;
    }

    /// <summary>完整解析且拒未知／重複選項；先於秘密讀取、鎖及連線執行。</summary>
    internal static TestTenantCommand Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length is < 2 or > 80 || args[0] != "test-tenant" || args[1] is not ("create" or "invite" or "cleanup")) throw Invalid();
        var operation = args[1];
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var members = new List<string>();
        for (var index = 2; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || string.IsNullOrEmpty(args[index + 1]) || args[index + 1].Length > 200 ||
                args[index + 1].StartsWith("--", StringComparison.Ordinal)) throw Invalid();
            var key = args[index];
            var value = args[index + 1];
            var allowed = key is "--org" or "--code" || operation == "create" && key is "--product" or "--name" or "--expect-channel" or "--admin" or "--member" ||
                operation == "invite" && key is "--role" or "--minutes";
            if (!allowed) throw Invalid();
            if (key == "--member") members.Add(value);
            else if (!values.TryAdd(key, value)) throw Invalid();
        }
        var organization = Require("--org");
        var product = organization switch { "goodshepherd20" => "GoodShepherd2", "construction" => "GoodButler", "association" => "GoodGuild", _ => throw Invalid() };
        var code = Require("--code");
        var prefix = product == "GoodShepherd2" ? 'c' : product == "GoodButler" ? 'b' : 'g';
        if (code.Length != 4 || code[0] != prefix || code.Skip(1).Any(character => character is < '0' or > '9') ||
            !int.TryParse(code.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number is < 1 or > 299) throw Invalid();
        string? name = null, expected = null, role = null;
        var admins = new List<string>(1);
        var minutes = 30;
        if (operation == "create")
        {
            if (Require("--product") != product) throw Invalid();
            name = Require("--name");
            if (string.IsNullOrWhiteSpace(name) || name.Length > 150 || name.Any(char.IsControl)) throw Invalid();
            expected = values.GetValueOrDefault("--expect-channel");
            if (expected is not null && !ValidChannel(expected)) throw Invalid();
            if (values.TryGetValue("--admin", out var admin)) admins.Add(admin);
            if (members.Count > 24 || admins.Concat(members).Any(user => !ValidUserId(user)) ||
                members.Distinct(StringComparer.Ordinal).Count() != members.Count) throw Invalid();
        }
        if (operation == "invite")
        {
            role = Require("--role");
            if (role is not ("member" or "admin") || values.TryGetValue("--minutes", out var text) &&
                (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out minutes) || minutes is < 1 or > 30)) throw Invalid();
        }
        return new TestTenantCommand(operation, organization, product, code, name, expected,
            admins.AsReadOnly(), members.AsReadOnly(), role, minutes);

        string Require(string key) => values.TryGetValue(key, out var value) ? value : throw Invalid();
    }

    /// <summary>只接受 canonical LINE 使用者格式，不回顯錯誤內容；格式本身不代表授權。</summary>
    private static bool ValidUserId(string value) => value.Length == 33 && value[0] == 'U' &&
        value.Skip(1).All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    /// <summary>公開 Channel 身份只接受有限 ASCII 數字，不接受名稱或路徑。</summary>
    private static bool ValidChannel(string value) => value.Length is >= 10 and <= 20 && value.All(character => character is >= '0' and <= '9');

    /// <summary>參數拒絕不在訊息內回顯未驗證值或使用者識別。</summary>
    private static ArgumentException Invalid() => new("Test tenant command arguments are invalid.");
}
