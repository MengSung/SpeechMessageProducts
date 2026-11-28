/*
檔案整體說明
目的與定位：保護 FS 唯讀節點差異的安全輸出與精確比較；全部使用合成完整投影純值，不連線。
主要功能：模擬 D365 把 null 補成 AssociatedMenuConfiguration、相同型別下標籤漂移、節點缺少、
內容相等與取消；逐一斷言 Added／Removed／Changed 及型別，禁止洩出原始字串或 XML。
資源擁有權與隔離：每案自行擁有字典及 CTS，沒有共用可變來源、檔案、SDK、背景 Task 或快取。
測試結束以 using 釋放取消來源，回傳資料只能有路徑、固定分類及型別，不帶原始 scalar。
驗證邊界：RED 必須是行為缺失，不能把建置錯誤冒充預期失敗；GREEN 只證明診斷器契約，
不證明原完整 baseline 可還原，也不授權第 3 次 apply 或修改發布 guard。
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>獨立故障注入保護唯讀輸出；每案只使用本地純值與明確釋放的取消來源。</summary>
public sealed class SchemaDeltaNodeProbeTests
{
    /// <summary>server 補物件會改型別並增加子節點；不能只報 count 或靜默忽略全部新子樹。</summary>
    [Fact]
    public void Compare_ServerObjectExpansion_ListsChangedAndAddedWithoutValues()
    {
        var before = Nodes((".AssociatedMenuConfiguration", "null", null));
        var after = Nodes((".AssociatedMenuConfiguration", "Contract", "private-type-value"),
            (".AssociatedMenuConfiguration.Label", "System.String", "<xml secret='token'/>"));
        var differences = SchemaDeltaNodeProbe.CompareNodes(before, after, default);
        Assert.Equal(2, differences.Count);
        Assert.Contains(differences, item => item.Path == ".AssociatedMenuConfiguration" && item.Kind == "Changed"
            && item.ExpectedType == "null" && item.ActualType == "Contract");
        Assert.Contains(differences, item => item.Path == ".AssociatedMenuConfiguration.Label" && item.Kind == "Added");
        var text = JsonSerializer.Serialize(differences);
        Assert.DoesNotContain("private-type-value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<xml", text, StringComparison.Ordinal);
        Assert.DoesNotContain("token", text, StringComparison.Ordinal);
    }

    /// <summary>節點數和型別都一樣時，舊標籤值變更仍須明確列出，且不得輸出任何標籤本文。</summary>
    [Fact]
    public void Compare_SameCountAndType_LabelDriftIsChanged()
    {
        var differences = SchemaDeltaNodeProbe.CompareNodes(
            Nodes((".Description.Label", "System.String", "private-before")),
            Nodes((".Description.Label", "System.String", "private-after")), default);
        var item = Assert.Single(differences);
        Assert.Equal("Changed", item.Kind);
        Assert.DoesNotContain("private-", JsonSerializer.Serialize(item), StringComparison.Ordinal);
    }

    /// <summary>缺少節點與值相等兩案分開斷言；來源後改不會改已產生差異。</summary>
    [Fact]
    public void Compare_RemovedAndEqual_AreDecisive()
    {
        var before = Nodes((".MetadataId", "System.Guid", Guid.NewGuid()));
        var removed = Assert.Single(SchemaDeltaNodeProbe.CompareNodes(before, Nodes(), default));
        Assert.Equal("Removed", removed.Kind);
        Assert.Equal("missing", removed.ActualType);
        Assert.Empty(SchemaDeltaNodeProbe.CompareNodes(before, before, default));
        before.Clear();
        Assert.Equal(".MetadataId", removed.Path);
    }

    /// <summary>正常 caller 取消必須保留原 token 並拒絕產生部分列表，CTS 由本案 using 釋放。</summary>
    [Fact]
    public void Compare_CallerCancellation_DoesNotReturnPartial()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var failure = Assert.Throws<OperationCanceledException>(
            () => SchemaDeltaNodeProbe.CompareNodes(Nodes(), Nodes(), cts.Token));
        Assert.Equal(cts.Token, failure.CancellationToken);
    }

    /// <summary>路徑包含任意本文或超量時拒絕，避免診斷器成為資料洩漏或無界保留通道。</summary>
    [Fact]
    public void Compare_UnsafePathAndOversizedCollection_Refuse()
    {
        Assert.Throws<System.IO.InvalidDataException>(() => SchemaDeltaNodeProbe.CompareNodes(
            Nodes((".Label\nprivate-token", "System.String", "value")), Nodes(), default));
        var large = Enumerable.Range(0, 4097).ToDictionary(
            index => ".Node[index:" + index + "]",
            _ => new UiMetadataPublicationGuard.Scalar("null", null), StringComparer.Ordinal);
        Assert.Throws<System.IO.InvalidDataException>(() => SchemaDeltaNodeProbe.CompareNodes(large, Nodes(), default));
    }

    /// <summary>建立本案可變純值來源，故意允許測試端 mutation，診斷結果不得保留字典參照。</summary>
    /// <param name="rows">路徑、型別與合成值；不含 SDK 或機密。</param>
    /// <returns>只屬本案的純值字典，方法不建任何可釋放資源。</returns>
    private static Dictionary<string, UiMetadataPublicationGuard.Scalar> Nodes(
        params (string Path, string Type, object? Value)[] rows) =>
        rows.ToDictionary(row => row.Path, row => new UiMetadataPublicationGuard.Scalar(row.Type, row.Value), StringComparer.Ordinal);
}
