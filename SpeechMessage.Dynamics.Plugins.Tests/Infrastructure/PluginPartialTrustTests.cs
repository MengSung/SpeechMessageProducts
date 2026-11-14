/*
 * 檔案整體說明
 *
 * 目的與定位：保護 05A 更新回復前置第零步，在離線 .NET Framework 受限 AppDomain
 * 串接真正 PluginShellBase、RecordingOrganizationService、ShadowPushRecorder、
 * ShadowRecordWriter 及會友通知／Flex 序列化，不把 FullTrust 成功當成沙箱證據。
 * 主要功能與流程：bubble 與 carousel 各自建立獨立域，只授予 Execution 與測試輸出
 * 目錄 Read／PathDiscovery；以 SDK RemoteExecutionContext 代替需要動態代理的 Moq。
 * Microsoft.Xrm.Sdk 是平台強式命名依賴，單獨列入 fullTrustAssemblies，否則其例外型別
 * 的序列化覆寫在 CLR 載入時即違反繼承透明度；產品、Core 與測試組件仍明確為部分信任。
 * 真外殼記錄 Create、Update 與 NotifyComposedAsync 的 Push，檢查完整 end、序號、
 * 正式身分與摘要；無效加密封包仍能完成 Shadow，且合成 HTTP handler 呼叫數為零。
 * 再經真 LinePushClient 對同一份會友訊息做離線傳輸，逐欄驗證巢狀內容及 server sender。
 * 另以既有 NotifyDiagnosticAsync 執行 Live 診斷鏈：有效合成封包經真解密、語系、
 * 嵌入 Flex 與 server sender 到離線 HTTP；MAC 損毀必須於 handler 前拒絕。
 * 使用邊界與維護：全部資料為合成值，沒有真秘密、真寫入或網路；子域不取得寫檔、
 * 網路及私有反射權限。每案 using 擁有 HttpClient／handler，Core 擁有 response，
 * 同步等待十秒有界傳輸排空後驗證 Dispose；外殼 finally 排空並停止兩種 recorder。
 * 父域僅產生測試封包，子域獨立建立同值 key 並執行真正 Unprotect；兩域的 64-byte
 * 合成 key 各由自己的 finally 清零，字串只保留於當次操作，不授權子域建立 RNG。
 * 父測試 finally 卸載域，即使子域斷言失敗亦不保留 SDK、訊息或背景工作；跨域只回傳
 * 合成結果字串。本測試不是 D365 實機驗收，後續模組須追加自己的規則案例。
 * 另在獨立 Framework 程序重跑 Live 案例，必須收到卸載後標記、空 stderr 與零退出碼，
 * 防止測試宿主吞掉 finalizer 例外。程序 owner 最多等三十秒，逾時終止並排空兩條管線。
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Security.Permissions;
using System.Security.Policy;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Line.Messaging;
using LineMessagingProcessor.Core;
using LineMessagingProcessor.Core.Templates;
using LineMessagingProcessor.Core.Security;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using ToolUtilityNameSpace.LineRecipients;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests.Infrastructure;

/// <summary>
/// 以真 CAS 執行邊界檢驗外殼與會友序列化；父測試只擁有域，不將完整信任服務傳入子域。
/// 每種形狀獨立載入、斷言及卸載，錯誤不得透過略過測試或放寬權限變成成功。
/// </summary>
public sealed class PluginPartialTrustTests
{
    /// <summary>
    /// 獨立 Framework 程序內依序重跑有效及壞 MAC 的真 Live 受限域案例；必須有兩筆
    /// 卸載後標記、空 stderr 與零退出碼。單一 PowerShell 不啟動 runner 或其他子程序，
    /// owner 的三十秒期限、終止及 pipe 排空因此涵蓋本案所有程序資源。
    /// </summary>
    [Fact]
    public async Task PartialTrust_MemberDiagnosticLifecycle_ExitsCleanly()
    {
        // xUnit 會將 Assembly.Location 分散 shadow-copy；原輸出閉包應以 ApplicationBase 定位。
        var root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar).Replace("'", "''");
        var assemblyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            typeof(PluginPartialTrustTests).Assembly.GetName().Name + ".dll").Replace("'", "''");
        var script = "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; try { " +
            "$a=[Reflection.Assembly]::LoadFrom('" + assemblyPath + "'); " +
            "$s=New-Object AppDomainSetup; $s.ApplicationBase='" + root + "'; " +
            "$s.ConfigurationFile='" + assemblyPath + ".config'; " +
            "$d=[AppDomain]::CreateDomain('P5-lifecycle-host',$null,$s); try { " +
            "$p=$d.CreateInstanceFromAndUnwrap('" + assemblyPath + "'," +
            "'SpeechMessage.Dynamics.Plugins.Tests.Infrastructure.PluginPartialTrustProbe'); " +
            "$null=$p.RunLiveLifecycle() } finally { [AppDomain]::Unload($d) }; " +
            "exit 0 } catch { [Console]::Error.WriteLine($_.Exception.ToString()); exit 1 }";
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    @"WindowsPowerShell\v1.0\powershell.exe"),
                Arguments = "-NoProfile -NonInteractive -EncodedCommand " +
                    Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            Assert.True(process.WaitForExit(30000), "The isolated CAS diagnostic exceeded its thirty-second deadline.");
            await Task.WhenAll(output, error);
            var transcript = output.Result + error.Result;
            Assert.True(process.ExitCode == 0, "The isolated CAS diagnostic failed: " + transcript);
            Assert.Equal(string.Empty, error.Result);
            Assert.DoesNotContain("SecurityException", transcript, StringComparison.Ordinal);
            Assert.DoesNotContain("SafeCspHandle", transcript, StringComparison.Ordinal);
            Assert.True(transcript.Split('\n').Count(line => line.IndexOf("P5_AFTER_UNLOAD", StringComparison.Ordinal) >= 0) == 2,
                "Both restricted domains must report after unloading: " + transcript);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
                Assert.True(process.WaitForExit(5000), "The isolated CAS process did not drain after termination.");
            }
            // 程序退出會關閉 pipe writer；收尾兩個已啟動 reader，不留下未觀察的背景讀取。
            await Task.WhenAll(output, error);
        }
    }

    /// <summary>
    /// 三筆真影子意圖與完整會友訊息必須在沒有私有反射的域內完成；finally 無條件卸載。
    /// 只有執行及測試輸出唯讀權限，故真網路、寫檔或額外反射需求都會使案例失敗。
    /// </summary>
    /// <param name="carousel">false 驗證 bubble；true 驗證 carousel 的巢狀 bubble。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialTrust_ShellShadowChain_Runs(bool carousel)
    {
        var root = AppDomain.CurrentDomain.BaseDirectory;
        var permissions = new PermissionSet(PermissionState.None);
        permissions.AddPermission(new SecurityPermission(SecurityPermissionFlag.Execution));
        permissions.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read | FileIOPermissionAccess.PathDiscovery, root));
        Assert.Null(permissions.GetPermission(typeof(ReflectionPermission)));
        // 平台 SDK 的例外序列化型別需要平台信任；不得將任何產品或測試組件加入此清單。
        var platformIdentity = typeof(IPlugin).Assembly.Evidence.GetHostEvidence<StrongName>();
        Assert.NotNull(platformIdentity);
        var domain = AppDomain.CreateDomain("P5-ShellShadow-" + Guid.NewGuid().ToString("N"), null,
            new AppDomainSetup { ApplicationBase = root, ConfigurationFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile },
            permissions, platformIdentity);
        try
        {
            var probe = (PluginPartialTrustProbe)domain.CreateInstanceAndUnwrap(
                typeof(PluginPartialTrustProbe).Assembly.FullName, typeof(PluginPartialTrustProbe).FullName);
            var result = probe.Run(carousel);
            Assert.True(string.Equals("PASS", result, StringComparison.Ordinal), result);
        }
        finally
        {
            AppDomain.Unload(domain);
        }
    }

    /// <summary>
    /// 既有 Live 診斷入口必須在原最小 CAS 授權內完成真解密與完整傳輸；MAC 損毀必須
    /// 於 HTTP 前以固定加密例外拒絕。每案獨立域，無論成功或失敗均由父 finally 卸載。
    /// </summary>
    /// <param name="corrupt">只翻轉合法封包的 MAC 位元，保持 canonical Base64 與加密內容。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialTrust_MemberDiagnosticLive_Runs(bool corrupt)
    {
        Assert.Equal("PASS", RunDiagnosticDomain(corrupt));
        Console.WriteLine("P5_AFTER_UNLOAD");
    }

    /// <summary>
    /// xUnit 與子程序共用的真實 CAS 邊界；不依賴 assertion 套件的載入或測試 runner。
    /// 回傳功能結果前一定卸載受限域，讓程序 owner 能觀察 finalizer 崩潰及成功標記。
    /// </summary>
    /// <param name="corrupt">是否只損毀公開合成封包的 MAC。</param>
    /// <returns>子域完成合成通知斷言後的結果，卸載失敗不會回傳。</returns>
    internal static string RunDiagnosticDomain(bool corrupt)
    {
        var envelope = CreateSyntheticEnvelope();
        var root = AppDomain.CurrentDomain.BaseDirectory;
        var permissions = new PermissionSet(PermissionState.None);
        permissions.AddPermission(new SecurityPermission(SecurityPermissionFlag.Execution));
        permissions.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read | FileIOPermissionAccess.PathDiscovery, root));
        if (permissions.GetPermission(typeof(ReflectionPermission)) is not null)
            throw new InvalidOperationException("The restricted domain cannot receive reflection permission.");
        // 與 Shadow 相同，僅精確 SDK StrongName 為平台例外，不授權其他組件或額外權限。
        var platformIdentity = typeof(IPlugin).Assembly.Evidence.GetHostEvidence<StrongName>();
        if (platformIdentity is null)
            throw new InvalidOperationException("The platform SDK must provide its strong name identity.");
        var domain = AppDomain.CreateDomain("P5-MemberLive-" + Guid.NewGuid().ToString("N"), null,
            new AppDomainSetup { ApplicationBase = root, ConfigurationFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile },
            permissions, platformIdentity);
        try
        {
            var probe = (PluginPartialTrustProbe)domain.CreateInstanceAndUnwrap(
                typeof(PluginPartialTrustProbe).Assembly.FullName, typeof(PluginPartialTrustProbe).FullName);
            return probe.RunDiagnostic(corrupt, envelope);
        }
        finally
        {
            AppDomain.Unload(domain);
        }
    }


    /// <summary>
    /// 父域以產品 Protect 建立合成測試資料；RNG 建立不屬於既有 Live 讀取／解密入口。
    /// 僅封包跨域，子域使用相同公開合成向量自建 key；此 owner 的 key 無論失敗與否均清零。
    /// </summary>
    /// <returns>只供當次離線案例使用的有效封包，沒有真實憑證。</returns>
    private static string CreateSyntheticEnvelope()
    {
        var key = Enumerable.Range(0, 64).Select(index => (byte)index).ToArray();
        try
        {
            return ChannelTokenProtector.Protect("synthetic-token", key);
        }
        finally
        {
            Array.Clear(key, 0, key.Length);
        }
    }

}

/// <summary>
/// 僅供測試的公開跨域入口；所有真正服務與斷言都在子域建立，不傳遞 SDK、client 或可變圖。
/// 遠端參照最多存活至父測試 finally 卸載，沒有自訂租約、timer 或背景資源。
/// </summary>
public sealed class PluginPartialTrustProbe : MarshalByRefObject
{
    /// <summary>
    /// 子程序唯一的完整信任父域入口；依序呼叫相同的最小 CAS 建域方法，任何功能錯誤
    /// 都拒絕成功標記。每次真正卸載後才輸出標記，最後父域亦由 PowerShell finally 卸載。
    /// 不把父域的完整信任或組件物件傳給受限域；不使用 runner、網路或額外程序。
    /// </summary>
    public void RunLiveLifecycle()
    {
        Require(!hasRun && AppDomain.CurrentDomain.IsFullyTrusted, "The lifecycle host must run once in its parent domain.");
        hasRun = true;
        Require(PluginPartialTrustTests.RunDiagnosticDomain(false) == "PASS", "The valid diagnostic failed.");
        Console.WriteLine("P5_AFTER_UNLOAD");
        Require(PluginPartialTrustTests.RunDiagnosticDomain(true) == "PASS", "The corrupted diagnostic failed.");
        Console.WriteLine("P5_AFTER_UNLOAD");
    }

    /// <summary>遠端入口僅允許本案同步執行一次，避免意外重用已完成的域內測試 owner。</summary>
    private bool hasRun;

    /// <summary>
    /// 在子域執行真外殼並檢查收尾與資源歸還；例外只含合成案例，回傳字串供父測試定位。
    /// 無效 token 封包用來保護 Shadow 不解密契約；HTTP 呼叫只由隨後的離線序列化驗證產生。
    /// </summary>
    /// <param name="carousel">指定本案的完整 Flex 容器形狀。</param>
    /// <returns>全部契約成立才回 PASS；否則回傳合成例外。</returns>
    public string Run(bool carousel)
    {
        try
        {
            Require(!hasRun, "Each remote probe must be used once.");
            hasRun = true;
            Require(!AppDomain.CurrentDomain.IsFullyTrusted, "The probe must run with restricted trust.");
            Require(!GetType().Assembly.IsFullyTrusted && !typeof(PluginShellBase).Assembly.IsFullyTrusted &&
                !typeof(MemberNotificationService).Assembly.IsFullyTrusted && !typeof(LinePushClient).Assembly.IsFullyTrusted,
                "Only the platform SDK may receive explicit full trust.");
            using var organization = new FakeOrganizationService();
            var tracing = new FakeTracingService();
            var context = new RemoteExecutionContext
            {
                Depth = 1, Stage = 40, Mode = 0, MessageName = "Create", PrimaryEntityName = "task",
                PrimaryEntityId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(),
                OrganizationId = Guid.NewGuid(), OrganizationName = "synthetic", UserId = Guid.NewGuid()
            };
            var factory = new SyntheticFactory(organization, context.UserId);
            var provider = new FakeServiceProvider()
                .Add<IPluginExecutionContext>(context)
                .Add<ITracingService>(tracing)
                .Add<IOrganizationServiceFactory>(factory);
            var shell = new ShadowShell(carousel);
            shell.Execute(provider);

            Require(factory.Calls == 1 && organization.Calls.Count == 0, "Shadow must not call the borrowed SDK.");
            var lines = tracing.Entries.Where(line => line.StartsWith("SMV-SHADOW|1|", StringComparison.Ordinal))
                .Select(line => JObject.Parse(line.Substring("SMV-SHADOW|1|".Length))).ToArray();
            Require(lines.Length == 4, "Three meaningful intents and the end marker are required.");
            Require((string?)lines[0]["operation"] == "Create" && (string?)lines[0]["entity"] == "task",
                "The first intent must create a task.");
            Require((string?)lines[1]["operation"] == "Update" && (int?)lines[1]["fields"]?["new_status"] == 7,
                "The second intent must update the task.");
            Require((string?)lines[0]["id"] == (string?)lines[1]["id"] &&
                Guid.TryParse((string?)lines[0]["id"], out var created) && created != Guid.Empty,
                "Create and Update must share a nonempty stable identity.");
            Require((string?)lines[2]["operation"] == "Push" &&
                (string?)lines[2]["contactId"] == shell.ContactId.ToString("D") &&
                (string?)lines[2]["retryKey"] == context.PrimaryEntityId.ToString("D") &&
                (string?)lines[2]["templateKey"] == "member.partialtrust" &&
                (int?)lines[2]["messageCount"] == 1, "The member notification intent is incomplete.");
            Require((string?)lines[2]["contentSha256"] == shell.ContentHash, "The complete member snapshot digest must survive projection.");
            Require(Enumerable.Range(0, 3).All(index => (int?)lines[index]["seq"] == index + 1),
                "Intent sequence must be contiguous.");
            Require((bool?)lines[3]["end"] == true && (int?)lines[3]["count"] == 3 &&
                lines.All(line => line["overflow"] is null), "The trace must end with a complete count.");
            Require(tracing.Entries.Sum(line => line.Length + 2) <= 8000, "Shadow trace exceeded its bounded budget.");
            var trace = string.Join("\n", tracing.Entries);
            Require(!trace.Contains("synthetic-token") && !trace.Contains("invalid-envelope") &&
                !trace.Contains("連線測試成功") && !trace.Contains("U" + new string('a', 32)),
                "Shadow must not publish credentials, member address or message text.");
            shell.VerifyDrained();
            tracing.Entries.Clear();
            organization.Dispose();
            Require(organization.EntityCount == 0 && organization.QueryResultCount == 0 && organization.AssociationCount == 0,
                "The synthetic SDK owner must drain its state.");
            return "PASS";
        }
        catch (Exception exception)
        {
            return exception.ToString();
        }
    }

    /// <summary>
    /// 在受限子域以真 Unprotect、嵌入範本與 Core 傳輸執行既有診斷通知；父域只建立封包。
    /// MAC 故障仍須完成語系解析與來源讀取，但不能呼叫 handler；成功須逐欄驗證真 JSON。
    /// HttpClient 在同步排空後釋放 handler，Core 釋放 response；最外 finally 清零案例 key。
    /// </summary>
    /// <param name="corrupt">是否對有效封包的 MAC 注入單一字元變更。</param>
    /// <param name="envelope">父域用公開合成 key 與固定合成 token 產生的有效認證封包。</param>
    /// <returns>所有斷言與清理完成才回 PASS；否則回傳只涉及合成資料的錯誤。</returns>
    public string RunDiagnostic(bool corrupt, string envelope)
    {
        var key = Enumerable.Range(0, 64).Select(index => (byte)index).ToArray();
        try
        {
            try
            {
                Require(!hasRun, "Each remote probe must be used once.");
                hasRun = true;
                Require(!AppDomain.CurrentDomain.IsFullyTrusted && !GetType().Assembly.IsFullyTrusted &&
                    !typeof(PluginShellBase).Assembly.IsFullyTrusted && !typeof(MemberNotificationService).Assembly.IsFullyTrusted &&
                    !typeof(LinePushClient).Assembly.IsFullyTrusted && !typeof(ChannelTokenProtector).Assembly.IsFullyTrusted,
                    "The Live diagnostic domain, test and product assemblies must remain restricted.");
                if (corrupt)
                {
                    var parts = envelope.Split(':');
                    // 僅改 MAC 首字元；IV、密文、長度與 Base64 尾端 padding 完全保留，必須走認證拒絕。
                    parts[3] = (parts[3][0] == 'A' ? "B" : "A") + parts[3].Substring(1);
                    envelope = string.Join(":", parts);
                }
                var contactId = Guid.NewGuid();
                var retryKey = Guid.NewGuid();
                var source = new SyntheticSource(contactId, envelope);
                // 故意給不同初始文化碼，必須由來源會友與教會政策解析為 zh-TW。
                var context = new TextContext(source.ChurchId, "en-US", "en-US");
                var catalog = new TextCatalog(new SystemTextTable(), new EmptyOverrides());
                var handler = new SnapshotHandler(false, contactId, retryKey);
                var resolvedCount = 0;
                var rejected = false;
                try
                {
                    using var http = new HttpClient(handler);
                    var notification = new MemberNotificationService(new LinePushClient(http),
                        new MemberCultureResolver(new LocalizationOptions()), catalog);
                    try
                    {
                        var result = PluginServices.Wait(notification.NotifyDiagnosticAsync(source, context,
                            contactId, retryKey, key, culture =>
                            {
                                Require(culture == "zh-TW", "Live diagnostics must resolve the member culture.");
                                resolvedCount++;
                            }, CancellationToken.None));
                        Require(result.Success, "The valid authenticated diagnostic must succeed.");
                    }
                    catch (ChannelTokenProtectionException exception)
                    {
                        Require(corrupt && exception.Message == "Channel token protection failed." &&
                            exception.InnerException is null && exception.Data.Count == 0,
                            "Only the corrupted MAC may produce the fixed crypto rejection.");
                        rejected = true;
                    }
                    Require(rejected == corrupt && source.MemberReads == 1 && source.ChannelReads == 1 && resolvedCount == 1,
                        "The existing Live entry must read its source and resolve culture once before authentication.");
                    Require(handler.Calls == (corrupt ? 0 : 1) && handler.ResponseDisposals == (corrupt ? 0 : 1),
                        "A corrupt MAC must stop before HTTP; valid delivery must dispose its single response.");
                }
                finally
                {
                    Require(handler.Disposals == 1, "The Live HttpClient must dispose its handler even after rejection.");
                }
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
            Require(key.All(value => value == 0), "The diagnostic key owner must zero its key before returning.");
            return "PASS";
        }
        catch (Exception exception)
        {
            return exception.ToString();
        }
    }

    /// <summary>不依賴 xUnit 在子域反射錯誤物件；固定契約文字只包含測試合成資訊。</summary>
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    /// <summary>借用案例唯一 SDK；驗證執行使用者且不產生動態代理、連線或額外資源。</summary>
    private sealed class SyntheticFactory(FakeOrganizationService organization, Guid userId) : IOrganizationServiceFactory
    {
        /// <summary>單次外殼取得服務次數；只由同步測試讀寫。</summary>
        internal int Calls { get; private set; }

        /// <summary>只接受本案平台使用者；不得把呼叫端任意身分當成已驗證的服務範圍。</summary>
        public IOrganizationService CreateOrganizationService(Guid? requestedUser)
        {
            Require(requestedUser == userId, "The shell must preserve its execution identity.");
            Calls++;
            return organization;
        }
    }

    /// <summary>
    /// 單案外殼衍生類別，執行真 recording 與會友組合服務；測試暫持 recorder 只為驗證 finally。
    /// 沒有可變 static；本案結束後整個域卸載，不把暫持參照放入產品外殼。
    /// </summary>
    private sealed class ShadowShell(bool carousel) : PluginShellBase("{\"mode\":\"Shadow\"}", string.Empty, new DepthPolicy(2))
    {
        /// <summary>只暫持本案外殼擁有的 wrapper，以驗證 Execute 返回時已清空且拒絕新操作。</summary>
        private RecordingOrganizationService? writes;
        /// <summary>只暫持本案推播 sink 供收尾斷言；測試域卸載時解除參照，不交給後續案例。</summary>
        private ShadowPushRecorder? pushes;

        /// <summary>本案合成正式會友識別，不以顯示名稱或 LINE ID 當作正式主鍵。</summary>
        internal Guid ContactId { get; } = Guid.NewGuid();
        /// <summary>完整序列化訊息的摘要，用來驗證 writer 未丟失或修改推播意圖。</summary>
        internal string? ContentHash { get; private set; }

        /// <summary>
        /// 真 Shadow 通知先完成且零 HTTP，再以同一訊息執行合成 HTTP 的完整序列化斷言。
        /// 無效加密封包與不提供 tokenKey 使意外解密立即失敗；HttpClient 在同步排空後關閉 handler。
        /// </summary>
        internal override void ExecuteRule(PluginInput input, PluginServices services)
        {
            Require(services.IsShadow && services.Organization is RecordingOrganizationService && services.ShadowPush is not null,
                "The real shell must install both Shadow recorders.");
            writes = (RecordingOrganizationService)services.Organization;
            pushes = services.ShadowPush!;
            var id = writes.Create(new Entity("task"));
            writes.Update(new Entity("task", id) { ["new_status"] = 7 });
            Require(writes.Retrieve("task", id, new ColumnSet("new_status")).GetAttributeValue<int>("new_status") == 7,
                "The real recording overlay must retain the candidate update.");
            var source = new SyntheticSource(ContactId);
            var context = new TextContext(source.ChurchId, "zh-TW", "zh-TW");
            var catalog = new TextCatalog(new SystemTextTable(), new EmptyOverrides());
            var handler = new SnapshotHandler(carousel, ContactId, input.PrimaryEntityId);
            using (var http = new HttpClient(handler))
            {
                var client = new LinePushClient(http);
                var notification = new MemberNotificationService(client, new MemberCultureResolver(new LocalizationOptions()), catalog);
                IReadOnlyList<ISendMessage>? composed = null;
                var result = PluginServices.Wait(notification.NotifyComposedAsync(source, context, ContactId,
                    input.PrimaryEntityId, "member.partialtrust", resolved =>
                    {
                        Require(composed is null && resolved.ChurchId == source.ChurchId && resolved.TextCulture == "zh-TW",
                            "Composition must use the resolved server scope once.");
                        composed = new ISendMessage[] { CreateMessage(catalog, resolved, carousel) };
                        return composed;
                    }, pushes, CancellationToken.None));
                Require(result.Outcome == LinePushOutcome.Rejected && !result.Success && result.Reason == "ShadowRecorded",
                    "Shadow recording must not claim network delivery.");
                Require(source.MemberReads == 1 && source.ChannelReads == 1 && handler.Calls == 0 && composed is not null,
                    "The member chain must compose once without HTTP or decryption.");
                ContentHash = pushes.Records.Single().ContentSha256;
                Require(ContentHash.Length == 64 && ContentHash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'),
                    "The complete member snapshot must produce a SHA-256 digest.");
                var sender = new SenderNameFactory(catalog).Create(context, "Alpha");
                Require(ContentHash == LinePushClient.ComputeMemberContentSha256(composed!, sender),
                    "Shadow must hash the same complete messages with the server sender.");
                var channelId = Guid.NewGuid();
                var sent = PluginServices.Wait(client.PushToMemberAsync(
                    new MemberRecipient(ContactId, "U" + new string('a', 32), source.ChurchId, "zh-TW"),
                    new ChurchChannel(source.ChurchId, channelId, channelId, new LineChannelCredential("synthetic-token"), sender),
                    source.ChurchId, composed!, input.PrimaryEntityId, CancellationToken.None));
                Require(sent.Success && handler.Calls == 1 && handler.ResponseDisposals == 1,
                    "Only the explicit offline serialization probe may send, and Core must dispose its response.");
            }
            Require(handler.Disposals == 1, "The HttpClient owner must dispose its handler after drain.");
        }

        /// <summary>重用真範本引擎的私有容器，讓沒有私有反射權限的序列化確實經過既有安全 converter。</summary>
        private static FlexMessage CreateMessage(ITextCatalog catalog, TextContext context, bool carousel)
        {
            var template = JObject.Parse("{\"type\":\"flex\",\"altText\":\"{{t:Common.Diagnostics.Ping}}\",\"contents\":{\"type\":\"bubble\",\"body\":{\"type\":\"box\",\"layout\":\"vertical\",\"contents\":[{\"type\":\"text\",\"text\":\"{{t:Common.Diagnostics.Ping}}\"}]}}}");
            if (carousel)
                template["contents"] = new JObject { ["type"] = "carousel", ["contents"] = new JArray(template["contents"]!) };
            return new FlexTemplateEngine(catalog).Fill(template.ToString(), context, new Dictionary<string, string>());
        }

        /// <summary>驗證外殼唯一 finally owner 清空集合並拒絕後續寫入，不靠域卸載掩蓋 recorder 遺留。</summary>
        internal void VerifyDrained()
        {
            Require(writes is not null && pushes is not null && writes.Writes.Count == 0 && pushes.Records.Count == 0,
                "The shell must drain both recorders.");
            var rejected = false;
            try { writes!.Create(new Entity("task")); }
            catch (ObjectDisposedException) { rejected = true; }
            Require(rejected, "The write recorder must be disposed.");
            rejected = false;
            try { pushes!.Record(ContactId, "member.partialtrust", Guid.NewGuid(), 1, new string('a', 64)); }
            catch (ObjectDisposedException) { rejected = true; }
            Require(rejected, "The push recorder must be disposed.");
        }
    }

    /// <summary>當次會友來源純合成值，無 SDK／背景工作；Shadow 預設無效封包，Live 借入當次合成封包。</summary>
    private sealed class SyntheticSource(Guid contactId, string envelope = "invalid-envelope") : ILineRecipientSource
    {
        /// <summary>唯讀固定語系政策，不含任何教會或會友狀態，也不公開底層陣列。</summary>
        private static readonly IReadOnlyList<string> EnabledCultures = Array.AsReadOnly(new[] { "zh-TW" });

        /// <summary>由本案伺服器來源固定的教會識別。</summary>
        internal Guid ChurchId { get; } = Guid.NewGuid();
        /// <summary>已完成的同步會友讀取數，不跨案例累計。</summary>
        internal int MemberReads { get; private set; }
        /// <summary>已完成的同步頻道讀取數，不保存回傳圖。</summary>
        internal int ChannelReads { get; private set; }

        /// <summary>只回傳本案相同正式會友 ID；錯範圍或取消立即失敗。</summary>
        public MemberRecipient ReadMember(Guid requestedContact, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Require(requestedContact == contactId, "Unexpected synthetic member.");
            MemberReads++;
            return new MemberRecipient(contactId, "U" + new string('a', 32), ChurchId, "zh-TW");
        }

        /// <summary>以完整教會範圍回傳同一頻道指派；只持有當次合成封包，未解密 token 不跨案例保留。</summary>
        public ChurchChannelRecord ReadChannel(Guid sourceChurchId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Require(sourceChurchId == ChurchId, "Unexpected synthetic church.");
            ChannelReads++;
            var channelId = Guid.NewGuid();
            return new ChurchChannelRecord(ChurchId, channelId, channelId, "Alpha", envelope,
                new ChurchLocaleSettings("zh-TW", EnabledCultures));
        }
    }

    /// <summary>真字典使用的無覆寫來源；每次回傳本範圍空快照，不建立 cache 或保存會友資料。</summary>
    private sealed class EmptyOverrides : IChurchTextOverrideSnapshotProvider
    {
        /// <summary>只建立完整文字範圍的不可變空快照，由當次組合呼叫消費。</summary>
        public IChurchTextOverrideSnapshot GetSnapshot(TextContext context) =>
            new ChurchTextOverrideSnapshot(context, Array.Empty<ChurchTextOverride>());
    }

    /// <summary>HttpClient 唯一擁有的離線 handler，逐欄檢查真 JSON；不開 socket 或保存 request／body。</summary>
    private sealed class SnapshotHandler(bool carousel, Guid contactId, Guid retryKey) : HttpMessageHandler
    {
        /// <summary>有效 Live 或顯式序列化探針僅一次；Shadow 與 MAC 損毀案必須維持零。</summary>
        internal int Calls { get; private set; }
        /// <summary>Core 回應 owner 的確定性釋放次數。</summary>
        internal int ResponseDisposals { get; private set; }
        /// <summary>HttpClient 結束時的 handler 釋放次數。</summary>
        internal int Disposals { get; private set; }

        /// <summary>檢查目的地、重試鍵、完整 Flex 與 sender；只回傳合成 200，任何遺漏直接失敗。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Require(request.Headers.Authorization?.Scheme == "Bearer" &&
                request.Headers.Authorization.Parameter == "synthetic-token",
                "The authenticated synthetic token must reach the HTTP authorization header.");
            Require(contactId != Guid.Empty && request.Method == HttpMethod.Post &&
                request.Headers.GetValues("X-Line-Retry-Key").Single() == retryKey.ToString("D"),
                "The member event retry identity must reach serialization.");
            var body = JObject.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false));
            var messages = body["messages"] as JArray;
            var message = messages?.Single();
            var contents = message?["contents"];
            var bubble = carousel ? (contents?["contents"] as JArray)?.Single() : contents;
            Require((string?)body["to"] == "U" + new string('a', 32) && messages?.Count == 1 &&
                (string?)message?["type"] == "flex" && (string?)message?["altText"] == "連線測試成功" &&
                (string?)contents?["type"] == (carousel ? "carousel" : "bubble") &&
                (string?)bubble?["type"] == "bubble" && (string?)bubble?["body"]?["type"] == "box" &&
                (string?)bubble?["body"]?["layout"] == "vertical" &&
                (bubble?["body"]?["contents"] as JArray)?.Count == 1 &&
                (string?)bubble?["body"]?["contents"]?[0]?["type"] == "text" &&
                (string?)bubble?["body"]?["contents"]?[0]?["text"] == "連線測試成功" &&
                (string?)message?["sender"]?["name"] == "Alpha助手",
                "The complete synthetic member Flex snapshot was not published.");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ResponseContent(this) };
        }

        /// <summary>不持有訊息快照；只計數實際釋放，父 client 必須在方法返回前關閉。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposals++;
            base.Dispose(disposing);
        }

        /// <summary>回應內容由 Core response using 擁有，不由測試預先釋放或以 GC 補救。</summary>
        private sealed class ResponseContent(SnapshotHandler owner) : StringContent("{}")
        {
            /// <summary>真實 Dispose 才回報歸還，避免僅檢查成功狀態而漏掉 response 資源洩漏。</summary>
            protected override void Dispose(bool disposing)
            {
                if (disposing) owner.ResponseDisposals++;
                base.Dispose(disposing);
            }
        }
    }
}
