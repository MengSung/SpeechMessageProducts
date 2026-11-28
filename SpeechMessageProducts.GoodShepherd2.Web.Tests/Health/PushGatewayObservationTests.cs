/*
檔案整體說明
目的與定位：F11 主機接線的正式 F5 發送紀錄相關性與清理測試，替代過去只有純值 sink 的不足，不宣稱實機發送成功。
主要功能與流程：合成 webhook envelope 的 wh correlation 經產品 adapter、真 F5 Gateway、真 membership／DataverseSendRecordStore 到 SDK Create，核對接受數、正式紀錄的相同編號與唯一 HTTP；後續取消及停止案例須驗同圖資源歸零。
使用邊界與維護：每案獨佔有界 fixture 及借入 SDK／HTTP 替身，await using 先排空操作才釋放 Meter／diagnostics；不讀秘密、不保存 HttpContext、真 userId 或跨案 mutable 狀態。此案仍是離線整合，不代替資料庫唯一索引或 LINE200 的 Live3P 證據。
*/
using System.Text.Json;
using Line.Messaging;
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.Core.Gateway;
using SpeechMessage.Hosting.Observability;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests.Health;

/// <summary>以真 Gateway 與真 SDK adapter 決定性核對主機轉交的同一穩定事件編號。</summary>
public sealed class PushGatewayObservationTests
{
    /// <summary>不同於 sink 替身，真正 Create 的 new_correlationid 必須等於 envelope，同時唯一 HTTP 且 lease 歸零。</summary>
    [Fact]
    public async Task Correlation_WebhookToSendRecord_SameId()
    {
        await using var fixture = new PushObservationFixture();
        using var json = JsonDocument.Parse("{\"webhookEventId\":\"observed-event-1\",\"type\":\"message\"}");
        var envelope = new WebhookEnvelope("2012804957", json.RootElement);
        var result = await fixture.Gateway.SendAsync(fixture.Scope, [PushObservationFixture.User(1)],
            new ISendMessage[] { new TextMessage("synthetic-body") }, MessageCategory.Transactional, envelope.CorrelationId, default);
        Assert.Equal(1, result.AcceptedRecipientCount);
        Assert.False(result.RequiresReconciliation);
        Assert.Equal(new[] { envelope.CorrelationId }, fixture.Correlations());
        Assert.Equal(1, fixture.Transport.Requests);
        Assert.Equal(fixture.LeaseAcquires, fixture.LeaseReleases);
        Assert.Equal(new HostingMeterSnapshot(0, 0, 0, 0), fixture.Meters.GetSnapshot());
    }

    /// <summary>五百零一位經真 F5 分成 multicast 與 push 兩批，各 HTTP 清理一次，不能只計整份名單一次。</summary>
    [Fact]
    public async Task Push_ActualTwoBatches_EachOwnsResponseAndRequest()
    {
        await using var fixture = new PushObservationFixture();
        var result = await fixture.Gateway.SendAsync(fixture.Scope,
            Enumerable.Range(1, 501).Select(PushObservationFixture.User).ToArray(),
            new ISendMessage[] { new TextMessage("synthetic-body") }, MessageCategory.Transactional, "wh-two-batches", default);
        Assert.Equal(501, result.AcceptedRecipientCount);
        Assert.Equal(2, result.Batches.Count);
        Assert.Equal(2, fixture.Transport.Requests);
        Assert.Equal(2, fixture.Transport.RequestDisposals);
        Assert.Equal(2, fixture.Transport.ResponseDisposals);
        Assert.Equal(new HostingMeterSnapshot(0, 0, 0, 0), fixture.Meters.GetSnapshot());
    }

    /// <summary>在途 HTTP 仍持 lease 時關閉必須等待；新發送立即拒絕，既有成功紀錄及資源歸還完成才允許關 owner。</summary>
    [Fact]
    public async Task Push_DisposeWaitsForInflightAndRejectsNewOperations()
    {
        await using var fixture = new PushObservationFixture();
        fixture.Transport.Delay = true;
        var send = fixture.Gateway.SendAsync(fixture.Scope, [PushObservationFixture.User(1)],
            new ISendMessage[] { new TextMessage("synthetic-body") }, MessageCategory.Transactional, "wh-drain", default);
        Task? stopping = null;
        try
        {
            await fixture.Transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stopping = fixture.Gateway.DisposeAsync().AsTask();
            Assert.False(stopping.IsCompleted);
            Assert.True(fixture.Meters.GetSnapshot().DataverseLeasesActive > 0);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => fixture.Gateway.SendAsync(fixture.Scope, [PushObservationFixture.User(2)],
                new ISendMessage[] { new TextMessage("synthetic-body") }, MessageCategory.Transactional, "wh-rejected", default));
        }
        finally
        {
            fixture.Transport.Release.TrySetResult();
            await send.WaitAsync(TimeSpan.FromSeconds(5));
            if (stopping is not null) await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(1, (await send).AcceptedRecipientCount);
        Assert.Equal("wh-drain", Assert.Single(fixture.Correlations()));
        Assert.Equal(fixture.LeaseAcquires, fixture.LeaseReleases);
        Assert.Equal(1, fixture.Transport.RequestDisposals);
        Assert.Equal(1, fixture.Transport.ResponseDisposals);
        Assert.Equal(new HostingMeterSnapshot(0, 0, 0, 0), fixture.Meters.GetSnapshot());
    }
}
