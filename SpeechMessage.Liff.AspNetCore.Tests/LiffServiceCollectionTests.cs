/*
檔案整體說明
目的與定位：F6共用DI組合的離線生命週期測試，保護verifier的request scope、借用HttpClient的唯一處置及F8前空權限。
主要功能與流程：以真實ServiceProvider和可觀察的合成IHttpClientFactory建立兩個scope，確認同scope重用verifier、不同scope各建無狀態client；scope結束必處置自己的client而不影響另一個scope，Host最後釋放容器。
使用邊界與維護：只回合成官方claims，不連LINE、SQL或D365，不讀秘密。client和handler由當案scope擁有、provider由當案using處置，沒有工作或計時器；Dispose後再送出必拋ObjectDisposedException。空角色測試不代表F8權限已實作。
*/
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Localization;
using Xunit;

namespace SpeechMessage.Liff.AspNetCore.Tests;

/// <summary>以真實DI驗資源owner，不能只靠直接new驗證器推論產品scope已正確接線。</summary>
public sealed class LiffServiceCollectionTests
{
    /// <summary>兩scope的client各自處置；第一scope結束後第二scope仍能驗證，不重用已關閉或帶有身份的request。</summary>
    [Fact]
    public async Task Scope_DisposesOnlyItsClient_AndDoesNotRetainIdentity()
    {
        var factory = new ClientFactory();
        var services = new ServiceCollection();
        services.AddSingleton<IHttpClientFactory>(factory);
        services.AddLiffAuthentication(new LiffOptions("2012805181", "2012804957", ""));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
        using var second = provider.CreateScope();
        var secondVerifier = second.ServiceProvider.GetRequiredService<LiffIdTokenVerifier>();
        using (var first = provider.CreateScope())
        {
            var firstVerifier = first.ServiceProvider.GetRequiredService<LiffIdTokenVerifier>();
            Assert.Same(firstVerifier, first.ServiceProvider.GetRequiredService<LiffIdTokenVerifier>());
            Assert.NotSame(firstVerifier, secondVerifier);
            Assert.Equal("synthetic-only", (await firstVerifier.VerifyAsync("synthetic-token", "2012805181", default)).UserId);
            Assert.Equal(2, factory.Clients.Count);
        }
        Assert.Equal(1, factory.Handlers[1].Disposed);
        Assert.Equal(0, factory.Handlers[0].Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => factory.Clients[1].GetAsync("https://synthetic.invalid/"));
        Assert.Equal("synthetic-only", (await secondVerifier.VerifyAsync("synthetic-token", "2012805181", default)).UserId);
        second.Dispose();
        Assert.All(factory.Handlers, handler => Assert.Equal(1, handler.Disposed));
    }

    /// <summary>F8尚未註冊時不能捏造管理員；不同租戶皆為空權限，明確caller取消仍須傳遞。</summary>
    [Fact]
    public async Task DefaultRoles_AreEmpty_AndCallerCancellationIsPreserved()
    {
        var services = new ServiceCollection();
        services.AddLiffAuthentication(new LiffOptions("", "", ""));
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ILiffRoleResolver>();
        Assert.Empty(await resolver.ResolveRolesAsync(Guid.NewGuid(), Guid.NewGuid(), default));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            resolver.ResolveRolesAsync(Guid.NewGuid(), Guid.NewGuid(), cancelled.Token));
    }

    /// <summary>只提供本案client且保存處置探針，不擁有產品資源；scope是所有傳回client的唯一owner。</summary>
    private sealed class ClientFactory : IHttpClientFactory
    {
        /// <summary>當案client參照，只用來斷言已關閉；不保存任何token或回應。</summary>
        internal List<HttpClient> Clients { get; } = [];
        /// <summary>當案handler處置探針，沒有實際transport。</summary>
        internal List<Handler> Handlers { get; } = [];
        /// <summary>每個scope只索取固定名稱的新client；不得從請求資料決定驗證目的地。</summary>
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("SpeechMessage.Liff.IdToken", name);
            var handler = new Handler();
            var client = new HttpClient(handler);
            Handlers.Add(handler);
            Clients.Add(client);
            return client;
        }
    }

    /// <summary>本案client唯一擁有的合成handler，以計數確認確定性清理。</summary>
    private sealed class Handler : HttpMessageHandler
    {
        /// <summary>真正Dispose次數，scope結束應為一。</summary>
        internal int Disposed { get; private set; }
        /// <summary>只回有界合成claims，verifier必自行處置response，沒有外部I/O。</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("""{"aud":"2012805181","sub":"synthetic-only","exp":1893456000}""", Encoding.UTF8, "application/json") });
        /// <summary>不持有額外資源；委派base後紀錄client已釋放此owner。</summary>
        protected override void Dispose(bool disposing) { if (disposing) Disposed++; base.Dispose(disposing); }
    }
}
