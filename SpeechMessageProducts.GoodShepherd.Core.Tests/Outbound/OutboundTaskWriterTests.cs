/*
 * 檔案整體說明
 *
 * 目的與定位：保護 P2-07 Writer 的雙目標入列契約，不連 D365、LINE 或真實金流。
 * 主要功能與流程：以合成 store 記錄真實 Writer 輸出的凍結命令，驗證穩定事件鍵、
 * 教會／類型分區、payload 型別白名單、取消與故障傳遞。文字、巢狀值、無效 GUID
 * 或超量輸入必須在儲存前拒絕；重送不產生新的事件鍵或覆寫命令。
 * 使用邊界與維護：替身只模擬已授權儲存邊界；資料庫唯一約束及 SDK 衝突處理
 * 由 ToolUtility 的適配器測試另驗，不能把本檔 Dictionary 當作跨程序唯一證據。
 * 每案自有替身、集合及 CTS，所有背景呼叫等待完成，不讀秘密、不共用可變狀態。
 */
using System;
using System.Collections.Generic;
using System.Threading;
using SpeechMessageProducts.GoodShepherd.Core.Outbound;
using ToolUtilityNameSpace.Outbound;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Outbound;

/// <summary>驗證 Writer 真實輸出與先驗後寫順序；SDK 原子防重在下層獨立測試。</summary>
public sealed class OutboundTaskWriterTests
{
    /// <summary>同一來源事件兩次入列鍵相同且取得同一正式 ID；未以新 GUID 假造 retry 身分。</summary>
    [Fact]
    public void Enqueue_SameIdempotencyKey_SingleRecord()
    {
        var store = new Store();
        var writer = new OutboundTaskWriter(store);
        var church = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var source = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var data = new Dictionary<string, object> { ["sourceId"] = source, ["quantity"] = 2 };
        var first = writer.Enqueue(OutboundTaskType.QrImageGenerate, church, null, source, data);
        var second = writer.Enqueue(OutboundTaskType.QrImageGenerate, church, null, source, data);
        Assert.Equal(first, second);
        Assert.Single(store.Rows);
        Assert.Equal("11111111111111111111111111111111-100000001-22222222222222222222222222222222",
            store.Last!.IdempotencyKey);
        Assert.Equal("{\"quantity\":2,\"sourceId\":\"22222222-2222-2222-2222-222222222222\"}", store.Last.PayloadJson);
    }

    /// <summary>自由文字、巢狀輸入及未白名單型別零 I/O；例外不得回顯被拒的本文。</summary>
    [Theory]
    [InlineData("text")]
    [InlineData("nested")]
    [InlineData("boolean")]
    [InlineData("float")]
    [InlineData("empty-guid")]
    [InlineData("null")]
    public void Enqueue_PayloadWithText_Rejected(string fault)
    {
        object value = fault switch
        {
            "text" => "secret-body",
            "nested" => new[] { 1 },
            "boolean" => true,
            "float" => double.NaN,
            "empty-guid" => Guid.Empty,
            _ => null!
        };
        var store = new Store();
        var ex = Assert.Throws<ArgumentException>(() => new OutboundTaskWriter(store).Enqueue(
            OutboundTaskType.QrImageGenerate, Guid.NewGuid(), null, Guid.NewGuid(),
            new Dictionary<string, object> { ["sourceId"] = value }));
        Assert.Empty(store.Rows);
        Assert.DoesNotContain("secret-body", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>字典 mutation 不改已驗證命令，兩教會同來源與兩類型的鍵各自獨立。</summary>
    [Fact]
    public void Enqueue_SnapshotsPayloadAndPartitionsScope()
    {
        var store = new Store();
        var writer = new OutboundTaskWriter(store);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var source = Guid.NewGuid();
        var payload = new Dictionary<string, object> { ["amount"] = 1.25m, ["quantity"] = 2L };
        writer.Enqueue(OutboundTaskType.AllotQuery, a, null, source, payload);
        var original = store.Last!;
        payload["amount"] = "mutated";
        Assert.Equal("{\"amount\":1.25,\"quantity\":2}", original.PayloadJson);
        payload["amount"] = 1.25m;
        writer.Enqueue(OutboundTaskType.AllotQuery, b, null, source, payload);
        writer.Enqueue(OutboundTaskType.RichMenuGenerate, a, null, source, payload);
        Assert.Equal(3, store.Rows.Count);
    }

    /// <summary>預取消不入場，真實儲存失敗維持同一例外，避免以假成功隱藏不確定寫入。</summary>
    [Fact]
    public void Enqueue_CancellationAndFailure_PreserveBoundary()
    {
        var store = new Store();
        var writer = new OutboundTaskWriter(store);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => writer.Enqueue(OutboundTaskType.AllotQuery,
            Guid.NewGuid(), null, Guid.NewGuid(), new Dictionary<string, object>(), cancellation.Token));
        Assert.Null(store.Last);
        store.Failure = new InvalidOperationException("synthetic");
        var ex = Assert.Throws<InvalidOperationException>(() => writer.Enqueue(OutboundTaskType.AllotQuery,
            Guid.NewGuid(), null, Guid.NewGuid(), new Dictionary<string, object>()));
        Assert.Same(store.Failure, ex);
    }

    /// <summary>相同十進位數值不同 scale 仍形成同一正規本文，避免同事件被誤報 payload 衝突。</summary>
    [Fact]
    public void Enqueue_DecimalScale_UsesCanonicalValue()
    {
        var source = Guid.NewGuid();
        var church = Guid.NewGuid();
        var first = new OutboundTaskCommand(OutboundTaskType.AllotQuery, church, null, source,
            new Dictionary<string, object> { ["amount"] = 1.00m });
        var second = new OutboundTaskCommand(OutboundTaskType.AllotQuery, church, null, source,
            new Dictionary<string, object> { ["amount"] = 1m });
        Assert.Equal("{\"amount\":1}", first.PayloadJson);
        Assert.Equal(first.PayloadJson, second.PayloadJson);
    }

    /// <summary>儲存層回空身分時 Writer 不假稱成功；呼叫端無法取得臨時或來源身分代替正式 ID。</summary>
    [Fact]
    public void Enqueue_EmptyStoreIdentity_Rejects()
    {
        var store = new Store { ReturnEmptyIdentity = true };
        Assert.Throws<InvalidOperationException>(() => new OutboundTaskWriter(store).Enqueue(
            OutboundTaskType.AllotQuery, Guid.NewGuid(), null, Guid.NewGuid(), new Dictionary<string, object>()));
    }

    /// <summary>缺正式識別、未知類型、超量欄位或不合法程式鍵在 store 前拒絕。</summary>
    /// <param name="fault">合成的輸入損壞種類。</param>
    [Theory]
    [InlineData("church")]
    [InlineData("source")]
    [InlineData("contact")]
    [InlineData("type")]
    [InlineData("count")]
    [InlineData("key")]
    public void Enqueue_InvalidInput_RejectsBeforeStore(string fault)
    {
        var payload = new Dictionary<string, object> { ["sourceId"] = Guid.NewGuid() };
        if (fault == "count") for (var i = 0; i < 17; i++) payload["field" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = i;
        if (fault == "key") payload["invalid key"] = 1;
        var store = new Store();
        Assert.ThrowsAny<ArgumentException>(() => new OutboundTaskWriter(store).Enqueue(
            fault == "type" ? (OutboundTaskType)7 : OutboundTaskType.QrImageGenerate,
            fault == "church" ? Guid.Empty : Guid.NewGuid(), fault == "contact" ? Guid.Empty : null,
            fault == "source" ? Guid.Empty : Guid.NewGuid(), payload));
        Assert.Null(store.Last);
    }

    /// <summary>每案專用的合成儲存邊界；只記純值命令，不建立網路／Task／需 Dispose 的資源。</summary>
    private sealed class Store : IOutboundEnqueueStore
    {
        internal Dictionary<string, Guid> Rows { get; } = new(StringComparer.Ordinal);
        internal OutboundTaskCommand? Last { get; private set; }
        internal Exception? Failure { get; set; }
        internal bool ReturnEmptyIdentity { get; set; }

        /// <summary>按穩定鍵回同一合成正式 ID；注入的原例外直接傳回 Writer。</summary>
        /// <param name="command">真實 Writer 已建立的純值命令。</param>
        /// <param name="cancellationToken">本案呼叫 token；不建立額外工作。</param>
        /// <returns>此替身的正式列 ID；不替代真實 Dataverse 測試。</returns>
        public Guid CreateOrGet(OutboundTaskCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Last = command;
            if (Failure is not null) throw Failure;
            if (ReturnEmptyIdentity) return Guid.Empty;
            if (!Rows.TryGetValue(command.IdempotencyKey, out var id)) Rows.Add(command.IdempotencyKey, id = Guid.NewGuid());
            return id;
        }
    }
}
