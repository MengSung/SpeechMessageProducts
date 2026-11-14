/*
 * 檔案整體說明
 * 目的與定位：P2-11以真正net462外掛與通知鏈，驗證task關聯Contact、教會邊界、
 * 偽語系與來源Retry Key；SDK及HTTP均為單案合成替身，不連D365或LINE。
 * 主要功能與流程：假SDK回傳最小正式資料，真Token解密、語言、Flex範本與Core序列化
 * 送至假handler；錯教會與未綁定必須零HTTP，正常與偽語系斷言完整body及安全追蹤。
 * 使用邊界與維護：fixture擁有HttpClient、合成64byte key與追蹤，所有Execute等待排空，
 * Dispose關client、清key及快照；回應由Core確定性Dispose，不以GC或程序結束代替。
 * 包裝驗證由專用AppDomain執行同一組案例，反射僅使用既有注入建構式；卸載Domain後
 * 不在測試程序保留合併組件，不能把一般bin的測試冒充實際合併產物。
 * 合併公開型別皆須實作IPlugin，只有診斷型別可具體註冊；抽象共用外殼不是另一個步驟，
 * 因此以可實例化型別與精確名稱驗證閉包，不以公開型別總數冒充可註冊入口數。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LineMessagingProcessor.Core;
using LineMessagingProcessor.Core.Security;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Diagnostics;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>合成SDK/HTTP只隔離外部I/O，產品解密、語言、範本、sender及傳輸完整執行。</summary>
public sealed class DiagnosticsMemberTests
{
    private readonly string? packagedPath;

    /// <summary>一般xUnit使用未合併net462輸出，保持既有型別身分與正常無金鑰建置。</summary>
    public DiagnosticsMemberTests() { }

    /// <summary>只由隔離Domain的包裝探針指定實際合併DLL；路徑不來自產品輸入。</summary>
    internal DiagnosticsMemberTests(string packagedPath) => this.packagedPath = packagedPath;

    /// <summary>正常Contact產生真Flex、server sender、task Retry Key及Accepted，不記錄秘密或內容。</summary>
    [Fact]
    public void Execute_Member_Normal_AcceptedWithTaskRetryKey()
    {
        using var f = new Fixture(packagedPath);
        f.Run();
        var body = JObject.Parse(f.Handler.Body!);
        Assert.Equal("flex", (string?)body["messages"]![0]!["type"]);
        Assert.Equal("連線測試成功", (string?)body["messages"]![0]!["altText"]);
        Assert.Equal("Alpha助手", (string?)body["messages"]![0]!["sender"]!["name"]);
        Assert.Equal(f.TaskId.ToString("D", CultureInfo.InvariantCulture), f.Handler.Retry);
        Assert.Contains("Outcome=Accepted; Status=200; RequestId=11111111-2222-3333-4444-555555555555", f.Trace, StringComparison.Ordinal);
        Assert.Contains("TemplateKey=Diagnostics; Culture=zh-TW", f.Trace, StringComparison.Ordinal);
        f.AssertCleanTrace();
        Assert.Equal(1, f.Handler.Disposed);
    }

    /// <summary>來源教會由註冊設定固定；Contact屬另一教會時真查詢完成後零HTTP且不讀Channel。</summary>
    [Fact]
    public void Execute_Member_ChurchMismatch_NoHttp()
    {
        using var f = new Fixture(packagedPath);
        f.Member["parentcustomerid"] = new EntityReference("account", Guid.NewGuid());
        f.Run();
        Assert.Contains("Reason=ChurchMismatch", f.Trace, StringComparison.Ordinal);
        Assert.Equal(0, f.Handler.Calls);
        Assert.Equal(2, f.Reads.Count);
    }

    /// <summary>未綁定只回NotBound且不解密不送訊，不把缺值當成傳輸成功。</summary>
    [Fact]
    public void Execute_Member_NotBound_NoHttp()
    {
        using var f = new Fixture(packagedPath);
        f.Member.Attributes.Remove("new_lineid");
        f.Run();
        Assert.Contains("Reason=NotBound", f.Trace, StringComparison.Ordinal);
        Assert.Equal(0, f.Handler.Calls);
        Assert.Equal(2, f.Reads.Count);
    }

    /// <summary>偏好偽語系由真resolver選用，Flex alt/body與sender包框，追蹤只記key/culture。</summary>
    [Fact]
    public void Execute_Member_Pseudo_UsesMemberCultureAndTemplate()
    {
        using var f = new Fixture(packagedPath);
        f.Member["new_preferredlanguage"] = "qps-ploc";
        f.Run();
        var body = JObject.Parse(f.Handler.Body!);
        Assert.Equal("［連線測試成功ǂǂǂ］", (string?)body["messages"]![0]!["altText"]);
        Assert.Equal("［連線測試成功ǂǂǂ］", (string?)body["messages"]![0]!["contents"]!["body"]!["contents"]![0]!["text"]);
        Assert.Contains("TemplateKey=Diagnostics; Culture=qps-ploc", f.Trace, StringComparison.Ordinal);
        f.AssertCleanTrace();
        Assert.Equal(1, f.Handler.Disposed);
    }

    /// <summary>關聯非Contact時在讀會友前fail-closed，不能把其他正式表的同GUID當Contact。</summary>
    [Fact]
    public void Execute_Member_WrongRegarding_FailsBeforeHttp()
    {
        using var f = new Fixture(packagedPath);
        f.Task["regardingobjectid"] = new EntityReference("account", f.ContactId);
        Assert.Throws<InvalidPluginExecutionException>(() => f.Run());
        Assert.Equal(0, f.Handler.Calls);
        Assert.Single(f.Reads);
    }

    /// <summary>完整鏈不捕捉沙箱同步內容；只要發生Post立即失敗，不用耗盡逾時判死結。</summary>
    [Fact]
    public void Execute_Member_DoesNotCaptureSynchronizationContext()
    {
        using var f = new Fixture(packagedPath);
        var previous = SynchronizationContext.Current;
        var rejecting = new RejectingContext();
        try
        {
            SynchronizationContext.SetSynchronizationContext(rejecting);
            f.Run();
            Assert.Equal(0, rejecting.Posts);
            Assert.Equal(1, f.Handler.Disposed);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    /// <summary>測試擁有的拒絕同步內容，不排程任何callback或保存傳入state。</summary>
    private sealed class RejectingContext : SynchronizationContext
    {
        internal int Posts;
        /// <summary>違反不捕捉契約即決定性失敗；沒有未觀測工作。</summary>
        public override void Post(SendOrPostCallback d, object? state)
        {
            Posts++;
            throw new InvalidOperationException("The sandbox context was captured.");
        }
    }

    /// <summary>member只接受完整secure tokenKey JSON；原push raw token不可被誤解為可解密的key。</summary>
    [Theory]
    [InlineData("synthetic-member-token")]
    [InlineData("{\"tokenKey\":\"bad\"}")]
    [InlineData("{\"tokenKey\":\"\",\"extra\":true}")]
    public void Ctor_Member_InvalidSecureKey_FailsClosed(string secure)
    {
        var error = Assert.Throws<InvalidPluginExecutionException>(() => new DiagnosticsPingPlugin(
            "{\"mode\":\"member\",\"culture\":\"zh-TW\",\"sourceChurchId\":\"11111111-1111-1111-1111-111111111111\"}", secure));
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("synthetic", error.Message, StringComparison.Ordinal);
    }

    /// <summary>每案擁有合成資料與key；SDK由fixture借入，產品不得保存provider、資料列或HTTP回應。</summary>
    private sealed class Fixture : IDisposable
    {
        internal Guid TaskId { get; } = Guid.NewGuid();
        internal Guid ContactId { get; } = Guid.NewGuid();
        private Guid ChurchId { get; } = Guid.NewGuid();
        private Guid ChannelId { get; } = Guid.NewGuid();
        private readonly byte[] key = Enumerable.Range(0,64).Select(i => (byte)i).ToArray();
        private readonly List<string> traces = new();
        internal List<string> Reads { get; } = new();
        internal Entity Task { get; }
        internal Entity Member { get; }
        internal Handler Handler { get; } = new();
        private readonly HttpClient http;
        private readonly Mock<IServiceProvider> provider = new(MockBehavior.Strict);
        private readonly string? packagedPath;
        internal string Trace => string.Join("\n", traces);

        /// <summary>只提供明列ColumnSet，任意AllColumns或未知表立即測試失敗；不容網路fallback。</summary>
        internal Fixture(string? packagedPath)
        {
            this.packagedPath = packagedPath;
            http = new HttpClient(Handler);
            Task = new Entity("task", TaskId) { ["regardingobjectid"] = new EntityReference("contact", ContactId) };
            Member = new Entity("contact", ContactId) { ["parentcustomerid"] = new EntityReference("account",ChurchId),
                ["new_lineid"] = "U11111111111111111111111111111111", ["new_preferredlanguage"] = "zh-TW" };
            var account = new Entity("account", ChurchId) { ["name"] = "Alpha", ["new_linechannelid"] = new EntityReference("new_linechannel",ChannelId),
                ["new_defaultlanguage"] = "zh-TW", ["new_enabledlanguages"] = "zh-TW,qps-ploc" };
            var channel = new Entity("new_linechannel", ChannelId) { ["new_accesstokenencrypted"] = ChannelTokenProtector.Protect("synthetic-member-token", key) };
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(x => x.Retrieve(It.IsAny<string>(),It.IsAny<Guid>(),It.IsAny<ColumnSet>()))
                .Returns<string,Guid,ColumnSet>((table,id,columns) => {
                    Assert.False(columns.AllColumns);
                    Reads.Add(table);
                    var row = table switch { "task" => Task, "contact" => Member, "account" => account, "new_linechannel" => channel, _ => throw new InvalidOperationException("Unexpected table.") };
                    Assert.Equal(row.Id, id);
                    var expected = table switch {
                        "task" => "regardingobjectid",
                        "contact" => "new_lineid,parentcustomerid,new_preferredlanguage",
                        "account" => "name,new_linechannelid,new_defaultlanguage,new_enabledlanguages",
                        _ => "new_accesstokenencrypted" };
                    Assert.Equal(expected.Split(',').OrderBy(x=>x,StringComparer.Ordinal), columns.Columns.OrderBy(x=>x,StringComparer.Ordinal));
                    return row;
                });
            var factory = new Mock<IOrganizationServiceFactory>(MockBehavior.Strict);
            var user = Guid.NewGuid();
            factory.Setup(x=>x.CreateOrganizationService(user)).Returns(sdk.Object);
            var ctx = new Mock<IPluginExecutionContext>(MockBehavior.Strict);
            ctx.SetupGet(x=>x.Depth).Returns(1);
            ctx.SetupGet(x=>x.PrimaryEntityName).Returns("task");
            ctx.SetupGet(x=>x.PrimaryEntityId).Returns(TaskId);
            ctx.SetupGet(x=>x.MessageName).Returns("Create");
            ctx.SetupGet(x=>x.UserId).Returns(user);
            ctx.SetupGet(x=>x.OrganizationId).Returns(Guid.NewGuid());
            var tracing = new Mock<ITracingService>(MockBehavior.Strict);
            tracing.Setup(x=>x.Trace(It.IsAny<string>(),It.IsAny<object[]>())).Callback<string,object[]>((format,args)=>traces.Add(string.Format(CultureInfo.InvariantCulture,format,args)));
            provider.Setup(x=>x.GetService(typeof(ITracingService))).Returns(tracing.Object);
            provider.Setup(x=>x.GetService(typeof(IPluginExecutionContext))).Returns(ctx.Object);
            provider.Setup(x=>x.GetService(typeof(IOrganizationServiceFactory))).Returns(factory.Object);
        }
        /// <summary>真Core與64byte secure JSON輸入；不把raw test channel token誤當加密key。</summary>
        internal void Run()
        {
            var unsecure = "{\"mode\":\"member\",\"culture\":\"zh-TW\",\"allowPseudoLocale\":true,\"sourceChurchId\":\"" + ChurchId.ToString("D", CultureInfo.InvariantCulture) + "\"}";
            var secure = "{\"tokenKey\":\"" + Convert.ToBase64String(key) + "\"}";
            IPlugin plugin;
            if (packagedPath is null)
                plugin = CreateUnmerged(unsecure, secure);
            else
            {
                // 只在可卸載Domain載入合併DLL；Core、SDK模型、查詢及嵌入範本必須真的來自同一DLL。
                var assembly = Assembly.LoadFrom(packagedPath);
                Assert.Equal(new Version(1, 0, 0, 0), assembly.GetName().Version);
                Assert.NotEmpty(assembly.GetName().GetPublicKeyToken()!);
                var exportedTypes = assembly.GetExportedTypes();
                Assert.All(exportedTypes, type => Assert.True(typeof(IPlugin).IsAssignableFrom(type)));
                var pluginType = Assert.Single(exportedTypes.Where(type => !type.IsAbstract));
                Assert.Equal(typeof(DiagnosticsPingPlugin).FullName, pluginType.FullName);
                var clientType = assembly.GetType("LineMessagingProcessor.Core.LinePushClient", true)!;
                var clientConstructor = clientType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(HttpClient) }, null)!;
                var client = clientConstructor.Invoke(new object[] { http });
                var constructor = pluginType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Single(c => c.GetParameters().Length == 4);
                try
                {
                    plugin = (IPlugin)constructor.Invoke(new object[] { unsecure, secure, client, (Func<DateTimeOffset>)(() => DateTimeOffset.UtcNow) });
                }
                catch (TargetInvocationException error) { throw error.InnerException!; }
            }
            plugin.Execute(provider.Object);
        }
        /// <summary>
        /// 將一般bin的型別注入與合併DLL隔離，避免JIT先解析未合併Core的建構式身分。
        /// 包裝Domain不執行此方法；HTTP仍由fixture擁有並在案例排空後Dispose。
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private DiagnosticsPingPlugin CreateUnmerged(string unsecure, string secure) =>
            new DiagnosticsPingPlugin(unsecure, secure, new LinePushClient(http), () => DateTimeOffset.UtcNow);

        /// <summary>追蹤不含token、LINE ID、訊息本文或Base64 key；只驗固定診斷欄位。</summary>
        internal void AssertCleanTrace()
        {
            Assert.DoesNotContain("synthetic-member-token", Trace, StringComparison.Ordinal);
            Assert.DoesNotContain("U11111111111111111111111111111111", Trace, StringComparison.Ordinal);
            Assert.DoesNotContain(Convert.ToBase64String(key), Trace, StringComparison.Ordinal);
            Assert.DoesNotContain("連線測試成功", Trace, StringComparison.Ordinal);
        }
        /// <summary>Execute已等待排空，關連線後清除合成key與資料快照，無背景Task。</summary>
        public void Dispose() { http.Dispose(); Array.Clear(key,0,key.Length); traces.Clear(); Reads.Clear(); }
    }

    /// <summary>只複製本案body與Retry Key，回應交Core唯一owner，結束時丟棄合成快照。</summary>
    private sealed class Handler : HttpMessageHandler
    {
        internal string? Body;
        internal string? Retry;
        internal int Calls;
        internal int Disposed;
        /// <summary>真序列化HTTP進入後立即回200，無外部傳輸或未觀測Task。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            Calls++;
            // 真非同步完成，不能以同步完成的fake Task掩蓋SynchronizationContext捕捉。
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            Body = await request.Content!.ReadAsStringAsync().ConfigureAwait(false);
            Retry = request.Headers.GetValues("X-Line-Retry-Key").Single();
            Assert.Equal("synthetic-member-token",request.Headers.Authorization!.Parameter);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content=new Content(this) };
            response.Headers.Add("x-line-request-id","11111111-2222-3333-4444-555555555555");
            return response;
        }
        /// <summary>fixture owner關閉後解除合成內容引用。</summary>
        protected override void Dispose(bool disposing) { if(disposing) Body=Retry=null; base.Dispose(disposing); }
        /// <summary>Core的response using確定性Dispose計數，不由fixture提前處置。</summary>
        private sealed class Content(Handler owner) : StringContent("{}")
        {
            /// <summary>只在實際Dispose回報數量，斷言每次傳輸完整歸還。</summary>
            protected override void Dispose(bool disposing) { if(disposing) owner.Disposed++; base.Dispose(disposing); }
        }
    }
}
