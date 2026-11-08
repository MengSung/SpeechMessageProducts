/*
 * 檔案整體說明
 * 目的與定位：以假的IOrganizationService覆蓋被Link進ToolUtility.Core.PluginSubset的正式會友查詢adapter。
 * 主要功能與流程：對照Contact、Account、Channel最小欄位及資料庫ID；注入缺值、錯型別、錯lookup、
 * 錯列、取消、超長文化清單與Dispose等故障，斷言fail-closed與不跨操作保留資料。
 * 使用邊界與維護：只使用Moq及合成Entity，不連Dataverse、不保存token或登入資料；每個adapter由using owner
 * 確定Dispose，SDK由案例借入而不轉移所有權，無background task、連線或快取。
 */
using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using SpeechMessage.Localization;
using ToolUtilityNameSpace.LineRecipients;
using Xunit;

namespace SpeechMessage.Tools.PluginPackaging.Subsets.Tests;

/// <summary>覆蓋正式Link查詢來源與純值驗證，故障斷言不依賴真實D365或非決定性資源清理。</summary>
public sealed class LineRecipientsCoverageTests
{
    /// <summary>缺少選用綁定欄位回傳null，空教會語系套用明確預設，channel仍使用正式lookup ID。</summary>
    [Fact]
    public void MissingOptionalValues_UsesDocumentedDefaultsAndKeepsDatabaseIdentity()
    {
        var ids = new Ids();
        using var source = new LineRecipientQueries(CreateService(ids,
            new Entity("contact", ids.Contact) { ["parentcustomerid"] = new EntityReference("account", ids.Church) },
            new Entity("account", ids.Church)
            {
                ["name"] = "Alpha", ["new_linechannelid"] = new EntityReference("new_linechannel", ids.Channel)
            },
            new Entity("new_linechannel", ids.Channel) { ["new_accesstokenencrypted"] = "synthetic-envelope" }).Object);
        var member = source.ReadMember(ids.Contact, default);
        var channel = source.ReadChannel(ids.Church, default);
        Assert.Null(member.LineUserId);
        Assert.Null(member.Language);
        Assert.Equal(ids.Channel, channel.AssignedChannelId);
        Assert.Equal("zh-TW", channel.Locales.DefaultCulture);
        Assert.Equal("zh-TW", Assert.Single(channel.Locales.EnabledCultures));
        Assert.Equal("ChurchChannelRecord(***)", channel.ToString());
    }

    /// <summary>每個主鍵、lookup、有限欄位與解密封包錯誤都在純值邊界拒絕。</summary>
    [Fact]
    public void InvalidRowsAndFields_FailClosedWithoutPublishingWrongIdentity()
    {
        var ids = new Ids();
        using (var source = new LineRecipientQueries(CreateService(ids,
            new Entity("account", ids.Contact), new Entity("account", ids.Church), new Entity("new_linechannel", ids.Channel)).Object))
            Assert.Throws<InvalidOperationException>(() => source.ReadMember(ids.Contact, default));
        using (var source = new LineRecipientQueries(CreateService(ids,
            new Entity("contact", ids.Contact) { ["parentcustomerid"] = new EntityReference("contact", ids.Church) },
            new Entity("account", ids.Church), new Entity("new_linechannel", ids.Channel)).Object))
            Assert.Throws<InvalidOperationException>(() => source.ReadMember(ids.Contact, default));
        using (var source = new LineRecipientQueries(CreateService(ids,
            new Entity("contact", ids.Contact) { ["parentcustomerid"] = new EntityReference("account", Guid.Empty) },
            new Entity("account", ids.Church), new Entity("new_linechannel", ids.Channel)).Object))
            Assert.Throws<InvalidOperationException>(() => source.ReadMember(ids.Contact, default));
        using (var source = new LineRecipientQueries(CreateService(ids,
            new Entity("contact", ids.Contact) { ["new_lineid"] = 12, ["parentcustomerid"] = new EntityReference("account", ids.Church) },
            new Entity("account", ids.Church), new Entity("new_linechannel", ids.Channel)).Object))
            Assert.Throws<InvalidOperationException>(() => source.ReadMember(ids.Contact, default));
        using (var source = new LineRecipientQueries(CreateService(ids,
            new Entity("contact", ids.Contact) { ["parentcustomerid"] = new EntityReference("account", ids.Church) },
            new Entity("account", ids.Church)
            {
                ["name"] = "Alpha", ["new_linechannelid"] = new EntityReference("new_linechannel", ids.Channel),
                ["new_enabledlanguages"] = string.Join(",", Enumerable.Repeat("zh-TW", 65))
            },
            new Entity("new_linechannel", ids.Channel) { ["new_accesstokenencrypted"] = "envelope" }).Object))
            Assert.Throws<InvalidOperationException>(() => source.ReadChannel(ids.Church, default));
    }

    /// <summary>同步SDK開始前取消、完成後Dispose均不產生額外讀取或重用已關閉adapter。</summary>
    [Fact]
    public void CancellationAndDisposal_ReleaseAdapterAndPreventLaterReads()
    {
        var ids = new Ids();
        var sdk = CreateService(ids,
            new Entity("contact", ids.Contact) { ["parentcustomerid"] = new EntityReference("account", ids.Church) },
            new Entity("account", ids.Church), new Entity("new_linechannel", ids.Channel));
        using var source = new LineRecipientQueries(sdk.Object);
        using var cancelled = new System.Threading.CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => source.ReadMember(ids.Contact, cancelled.Token));
        source.Dispose();
        Assert.Throws<ObjectDisposedException>(() => source.ReadMember(ids.Contact, default));
        sdk.Verify(service => service.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>()), Times.Never);
    }

    /// <summary>組合每表精確合成列；adapter始終借用service，不讀全欄位或保留外部資源。</summary>
    private static Mock<IOrganizationService> CreateService(Ids ids, Entity contact, Entity church, Entity channel)
    {
        var rows = new[] { contact, church, channel };
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(service => service.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>()))
            .Returns<string, Guid, ColumnSet>((table, id, columns) =>
            {
                Assert.False(columns.AllColumns);
                Assert.NotEmpty(columns.Columns);
                return Array.Find(rows, row => row.LogicalName == table && row.Id == id)
                    ?? throw new InvalidOperationException("No synthetic row for requested identity.");
            });
        return sdk;
    }

    /// <summary>案例內唯一正式資料列ID集合，不依顯示名稱或行列位置建立身分。</summary>
    private sealed class Ids
    {
        /// <summary>合成Contact主鍵。</summary>
        internal Guid Contact { get; } = Guid.NewGuid();
        /// <summary>合成教會資料庫主鍵。</summary>
        internal Guid Church { get; } = Guid.NewGuid();
        /// <summary>合成Channel資料庫主鍵。</summary>
        internal Guid Channel { get; } = Guid.NewGuid();
    }
}
