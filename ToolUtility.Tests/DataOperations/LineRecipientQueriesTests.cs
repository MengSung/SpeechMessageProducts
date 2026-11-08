/*
 * 檔案整體說明
 *
 * 目的與定位：P2-04最小欄位查詢的雙目標離線SDK契約，不連D365或讀真實個資。
 * 主要功能與流程：假IOrganizationService只接受預期主鍵與ColumnSet，回傳合成
 * Contact、Account、Channel；斷言無AllColumns、正式lookup身分與結果純值。
 * 使用邊界與維護：每案using擁有adapter，SDK由測試借出不轉移所有權；Dispose後
 * 新操作拒絕、取消零SDK。故障／錯表或錯ID不能假裝查無資料，例外交最終主機，
 * 不自行通知LINE。所有記錄只在單案保留，不包含真實Token、key或User Secrets。
 */
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.LineRecipients;
using Xunit;

namespace ToolUtility.Tests.DataOperations;

/// <summary>以真查詢adapter驗最小ColumnSet與正式資料列身分，兩宿主載入同一產品來源。</summary>
public sealed class LineRecipientQueriesTests
{
    /// <summary>通知所需三次真正SDK讀取只有明列欄位，頻道依Account lookup而非名稱或外部Channel ID。</summary>
    [Fact]
    public void Notify_ColumnSetIsMinimal()
    {
        var contact = Guid.NewGuid(); var church = Guid.NewGuid(); var channel = Guid.NewGuid();
        var calls = new List<(string Table, Guid Id, string[] Columns)>();
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(x => x.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>()))
            .Returns<string, Guid, ColumnSet>((table, id, columns) =>
            {
                Assert.False(columns.AllColumns);
                calls.Add((table, id, columns.Columns.ToArray()));
                return table switch
                {
                    "contact" => new Entity(table, id) {
                        ["new_lineid"] = "U00000000000000000000000000000000",
                        ["parentcustomerid"] = new EntityReference("account", church), ["new_preferredlanguage"] = "qps-ploc" },
                    "account" => new Entity(table, id) {
                        ["name"] = "Alpha", ["new_linechannelid"] = new EntityReference("new_linechannel", channel),
                        ["new_defaultlanguage"] = "zh-TW", ["new_enabledlanguages"] = "zh-TW,qps-ploc" },
                    _ => new Entity(table, id) { ["new_accesstokenencrypted"] = "synthetic-envelope" }
                };
            });
        using var source = new LineRecipientQueries(sdk.Object);
        var member = source.ReadMember(contact, default);
        var record = source.ReadChannel(church, default);
        Assert.Equal(church, member.ChurchId);
        Assert.Equal("qps-ploc", member.Language);
        Assert.Equal(channel, record.AssignedChannelId);
        Assert.Equal(channel, record.ChannelId);
        Assert.Equal(3, calls.Count);
        Assert.Equal(new[] { "new_lineid", "parentcustomerid", "new_preferredlanguage" }, calls[0].Columns);
        Assert.Equal(new[] { "name", "new_linechannelid", "new_defaultlanguage", "new_enabledlanguages" }, calls[1].Columns);
        Assert.Equal(new[] { "new_accesstokenencrypted" }, calls[2].Columns);
        Assert.Equal(("new_linechannel", channel), (calls[2].Table, calls[2].Id));
    }

    /// <summary>錯資料庫ID不得發布；已取消或Dispose後不再觸SDK，adapter不處置借用SDK。</summary>
    [Fact]
    public void Read_WrongIdentityCancellationAndDispose_FailClosed()
    {
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(x => x.Retrieve("contact", It.IsAny<Guid>(), It.IsAny<ColumnSet>()))
            .Returns(new Entity("contact", Guid.NewGuid()));
        using var source = new LineRecipientQueries(sdk.Object);
        Assert.Throws<InvalidOperationException>(() => source.ReadMember(Guid.NewGuid(), default));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => source.ReadMember(Guid.NewGuid(), cancelled.Token));
        source.Dispose();
        Assert.Throws<ObjectDisposedException>(() => source.ReadMember(Guid.NewGuid(), default));
        sdk.Verify(x => x.Retrieve("contact", It.IsAny<Guid>(), It.IsAny<ColumnSet>()), Times.Once);
    }
}
