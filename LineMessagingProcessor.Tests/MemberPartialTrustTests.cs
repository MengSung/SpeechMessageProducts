/*
 * 檔案整體說明
 *
 * 目的與定位：P2-12 以離線 .NET Framework 部分信任 AppDomain 重現私有 Flex 投影
 * 被 JSON 反射存取拒絕的故障；這不是 D365 實機，也不宣稱完全模擬其所有權限。
 * 主要功能與流程：只授予執行與測試輸出目錄唯讀，使用真範本引擎、真會友 Core 及
 * 合成 HTTP handler，bubble／carousel 均須產生完整結構、文字與伺服器 sender 才回 PASS。
 * 使用邊界與維護：不讀秘密、不允許網路；父測試唯一擁有 domain 並於 finally 卸載，
 * 子域 using 擁有 HttpClient／handler，Core 排空十秒傳輸後釋放請求、回應及取消來源。
 * 所有身分及內容均為案例合成值，不跨案例保存狀態；錯誤只回傳合成例外供紅燈定位。
 */
#if NETFRAMEWORK
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Security.Permissions;
using System.Threading;
using System.Threading.Tasks;
using Line.Messaging;
using LineMessagingProcessor.Core;
using LineMessagingProcessor.Core.Templates;
using Newtonsoft.Json.Linq;
using SpeechMessage.Localization;
using Xunit;

namespace LineMessagingProcessor.Tests;

/// <summary>以拒絕非公開反射的真 CAS 權限邊界，保護會友 Flex 推播不依賴 FullTrust。</summary>
public sealed class MemberPartialTrustTests
{
    /// <summary>私有範本容器仍須可經 Core 序列化；失敗不能冒充 HTTP 受理，域必須確定卸載。</summary>
    /// <param name="carousel">分別保護單張 bubble 與 carousel 巢狀協定。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MemberFlex_WithoutPrivateReflection_SendsCompleteSnapshot(bool carousel)
    {
        var root = AppDomain.CurrentDomain.BaseDirectory;
        var permissions = new PermissionSet(PermissionState.None);
        permissions.AddPermission(new SecurityPermission(SecurityPermissionFlag.Execution));
        permissions.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read | FileIOPermissionAccess.PathDiscovery, root));
        var domain = AppDomain.CreateDomain("P212-Member-PartialTrust", null,
            new AppDomainSetup { ApplicationBase = root, ConfigurationFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile }, permissions);
        try
        {
            var probe = (MemberPartialTrustProbe)domain.CreateInstanceAndUnwrap(
                typeof(MemberPartialTrustProbe).Assembly.FullName, typeof(MemberPartialTrustProbe).FullName);
            Assert.Equal("PASS", probe.Run(carousel));
        }
        finally { AppDomain.Unload(domain); }
    }
}

/// <summary>僅在子域使用合成值的遠端入口；不傳 HttpClient、SDK、憑證或可變圖跨域。</summary>
public sealed class MemberPartialTrustProbe : MarshalByRefObject
{
    /// <summary>真範本至真 Core 完整執行，handler 逐欄驗證 body；例外僅含本案合成資料。</summary>
    /// <param name="carousel">只由父測試傳入的合成容器形狀。</param>
    /// <returns>所有決定性斷言成立才回 PASS，其餘回合成失敗以保留紅燈線索。</returns>
    public string Run(bool carousel)
    {
        try
        {
            var church = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var channel = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var template = "{\"type\":\"flex\",\"altText\":\"{{t:Common.Diagnostics.Ping}}\",\"contents\":{\"type\":\"bubble\",\"body\":{\"type\":\"box\",\"layout\":\"vertical\",\"contents\":[{\"type\":\"text\",\"text\":\"{{t:Common.Diagnostics.Ping}}\"}]}}}";
            if (carousel)
            {
                var parsed = JObject.Parse(template);
                parsed["contents"] = new JObject { ["type"] = "carousel", ["contents"] = new JArray(parsed["contents"]!) };
                template = parsed.ToString();
            }
            var message = new FlexTemplateEngine(new SyntheticCatalog()).Fill(template,
                new TextContext(church, "zh-TW", "zh-TW"), new Dictionary<string, string>());
            using var http = new HttpClient(new SnapshotHandler(carousel));
            var client = new LinePushClient(http);
            var result = client.PushToMemberAsync(new MemberRecipient(Guid.NewGuid(), "U" + new string('a', 32), church, "zh-TW"),
                new ChurchChannel(church, channel, channel, new LineChannelCredential("synthetic"), "sender"), church,
                new ISendMessage[] { message }, Guid.NewGuid(), CancellationToken.None).GetAwaiter().GetResult();
            return result.Outcome == LinePushOutcome.Success ? "PASS" : "Unexpected outcome";
        }
        catch (Exception exception) { return exception.ToString(); }
    }

    /// <summary>純委派合成字典，不讀產品資源、授權、cache 或外部狀態。</summary>
    private sealed class SyntheticCatalog : ITextCatalog
    {
        /// <summary>固定合成翻譯，讓測試獨立判定輸出文字沒有遺漏。</summary>
        public string GetText(TextContext context, TextKey key, params TextArg[] args) => "synthetic-text";
    }

    /// <summary>子域 HttpClient 唯一擁有的離線 handler；不開 socket、不保留 request 或內容。</summary>
    private sealed class SnapshotHandler : HttpMessageHandler
    {
        /// <summary>本 handler 的固定合成形狀，不含其他案例的輸入或身分。</summary>
        private readonly bool carousel;
        /// <summary>子域 client 擁有此 handler，固定預期容器以防止遺漏或錯寫形狀。</summary>
        internal SnapshotHandler(bool carousel) => this.carousel = carousel;
        /// <summary>斷言真傳輸候選的固定協定、文字與 sender；回應由 Core using 釋放。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JObject.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false));
            var contents = body["messages"]?[0]?["contents"];
            var bubble = carousel ? contents?["contents"]?[0] : contents;
            if ((string?)body["messages"]?[0]?["type"] != "flex" ||
                (string?)contents?["type"] != (carousel ? "carousel" : "bubble") ||
                (string?)bubble?["body"]?["contents"]?[0]?["text"] != "synthetic-text" ||
                (string?)body["messages"]?[0]?["sender"]?["name"] != "sender")
                throw new InvalidOperationException("The complete synthetic Flex snapshot was not published.");
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
#endif
