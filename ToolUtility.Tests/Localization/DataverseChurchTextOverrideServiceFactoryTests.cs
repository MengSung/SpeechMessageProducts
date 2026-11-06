using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using SpeechMessage.Localization;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Localization;
using Xunit;

namespace ToolUtility.Tests.Localization;

/// <summary>以真來源及真 factory 轉接既有 manager 契約；SDK／manager 只用合成替身，不連資料庫或讀憑證。</summary>
public sealed class DataverseChurchTextOverrideServiceFactoryTests
{
    /// <summary>同一 manager 的固定組織產品才可入場，傳遞 owner token；每次讀完整歸還 lease，不處置 manager 或 raw SDK。</summary>
    [Fact]
    public async Task Source_UsesRealManagerLease_AndReturnsItOnce()
    {
        var context = Context();
        using var cancellation = new CancellationTokenSource();
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(new EntityCollection());
        var lease = new Mock<IClientLease>(MockBehavior.Strict);
        lease.SetupGet(value => value.Service).Returns(sdk.Object);
        lease.Setup(value => value.Dispose());
        var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
        manager.Setup(value => value.Acquire(cancellation.Token)).Returns(lease.Object);
        var source = new D365ChurchTextOverrideSource(new DataverseChurchTextOverrideServiceFactory(manager.Object, "Alpha", "ChurchReport"));
        Assert.Empty(await source.ReadAsync(context, cancellation.Token));
        lease.Verify(value => value.Dispose(), Times.Once);
        lease.Verify(value => value.MarkFaulted(), Times.Never);
        manager.Verify(value => value.Dispose(), Times.Never);
    }

    /// <summary>不確定 SDK／內部取消／無效頁結果一律淘汰租約，不能把狀態不明 client 交給下一請求；仍保留原故障。</summary>
    /// <param name="fault">合成傳輸故障種類。</param>
    [Theory]
    [InlineData("transport")]
    [InlineData("internal-cancel")]
    [InlineData("invalid-page")]
    public async Task Source_FailedRead_MarksLeaseFaultedBeforeReturn(string fault)
    {
        var context = Context();
        Exception original = fault == "internal-cancel" ? new OperationCanceledException("synthetic timeout")
            : new InvalidOperationException("synthetic transport failure");
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        if (fault == "invalid-page") sdk.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((EntityCollection)null!);
        else sdk.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>())).Throws(original);
        var returned = false;
        var faulted = false;
        var lease = new Mock<IClientLease>(MockBehavior.Strict);
        lease.SetupGet(value => value.Service).Returns(sdk.Object);
        lease.Setup(value => value.MarkFaulted()).Callback(() => { Assert.False(returned); faulted = true; });
        lease.Setup(value => value.Dispose()).Callback(() => { Assert.True(faulted); returned = true; });
        var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
        manager.Setup(value => value.Acquire(It.IsAny<CancellationToken>())).Returns(lease.Object);
        var source = new D365ChurchTextOverrideSource(new DataverseChurchTextOverrideServiceFactory(manager.Object, "Alpha", "ChurchReport"));
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => source.ReadAsync(context, CancellationToken.None));
        if (fault != "invalid-page") Assert.Same(original, failure);
        Assert.True(returned);
        lease.Verify(value => value.MarkFaulted(), Times.Once);
        lease.Verify(value => value.Dispose(), Times.Once);
    }

    /// <summary>錯組織／錯產品均在 manager Acquire 前拒絕；context 建構是值驗證，不代表已授權另一個固定部署。</summary>
    /// <param name="organization">合成組織。</param>
    /// <param name="product">合成產品。</param>
    [Theory]
    [InlineData("Beta", "ChurchReport")]
    [InlineData("Alpha", "Other")]
    public void Factory_ForeignDeployment_RejectsBeforeAcquire(string organization, string product)
    {
        var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
        var factory = new DataverseChurchTextOverrideServiceFactory(manager.Object, "Alpha", "ChurchReport");
        Assert.Throws<InvalidOperationException>(() => factory.Open(new TextContext(Guid.NewGuid(), "zh-TW", "zh-TW", organization, product), CancellationToken.None));
        manager.VerifyNoOtherCalls();
    }

    /// <summary>明確 caller 取消不取得 manager lease；wrapper 重複 Dispose 不重複歸還，之後不得讀 Service 或標故障。</summary>
    [Fact]
    public void Factory_CanceledAndDisposed_StopsNewAccess()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var lease = new Mock<IClientLease>(MockBehavior.Strict);
        lease.Setup(value => value.Dispose());
        var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
        manager.Setup(value => value.Acquire(CancellationToken.None)).Returns(lease.Object);
        var factory = new DataverseChurchTextOverrideServiceFactory(manager.Object, "Alpha", "ChurchReport");
        Assert.ThrowsAny<OperationCanceledException>(() => factory.Open(Context(), cancellation.Token));
        var wrapper = factory.Open(Context(), CancellationToken.None);
        wrapper.Dispose();
        wrapper.Dispose();
        Assert.Throws<ObjectDisposedException>(() => wrapper.Service);
        Assert.Throws<ObjectDisposedException>(() => wrapper.MarkFaulted());
        lease.Verify(value => value.Dispose(), Times.Once);
    }

    /// <summary>單案獨立已授權範圍，不持有 request／使用者。</summary>
    private static TextContext Context() => new(Guid.NewGuid(), "zh-TW", "zh-TW", "Alpha", "ChurchReport");
}
