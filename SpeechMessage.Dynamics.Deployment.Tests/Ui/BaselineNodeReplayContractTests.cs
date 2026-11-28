/*
檔案整體說明
目的與定位：FS construction完整baseline唯讀診斷的離線契約，確保JSON純值能恢復
正式guard使用的CLR型別，避免把JsonElement型別差異誤報為實機metadata漂移。
主要功能與流程：合成有限節點包含整數、集合、GUID、enum、日期及null；逐型別／值斷言，
未知型別與重複路徑注入必須整批拒絕。不存在探針時以反射明確失敗形成RED，非編譯錯誤。
使用邊界與維護：每案using擁有JsonDocument，結果僅當案持有；沒有SDK、秘密、檔案、
網路、執行緒或背景資源。本檔只證明離線純值還原，不授權任何組織寫入或冒稱實機。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Xunit;
namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>以合成完整節點測型別忠實性與拒絕邊界，文件生命週期全部由單案using擁有。</summary>
public sealed class BaselineNodeReplayContractTests
{
    /// <summary>保留整數／集合與enum的實際CLR差異，避免合法基準全面誤報而掩蓋真正漂移。</summary>
    [Fact]
    public void SavedScalars_RestoreExactRuntimeKinds()
    {
        using var document = JsonDocument.Parse("""
        {"account.Count":{"type":"Collection","value":2},
         "account.Int":{"type":"System.Int32","value":42},
         "account.Guid":{"type":"System.Guid","value":"a17236b0-20c1-4f88-a453-cb07e80c6bcb"},
         "account.Enum":{"type":"Microsoft.Xrm.Sdk.Metadata.CascadeType","value":1},
         "account.Date":{"type":"DateTime","value":638000000000000000},
         "account.Kind":{"type":"DateTimeKind","value":1},
         "account.None":{"type":"null","value":null}}
        """);
        var result = Restore(document.RootElement);
        Assert.Equal(7, result.Count);
        Assert.Equal(2, Assert.IsType<int>(result["account.Count"].Value));
        Assert.Equal(42, Assert.IsType<int>(result["account.Int"].Value));
        Assert.Equal(Guid.Parse("a17236b0-20c1-4f88-a453-cb07e80c6bcb"), Assert.IsType<Guid>(result["account.Guid"].Value));
        Assert.Equal(1L, Assert.IsType<long>(result["account.Enum"].Value));
        Assert.Equal(638000000000000000L, Assert.IsType<long>(result["account.Date"].Value));
        Assert.Equal(1, Assert.IsType<int>(result["account.Kind"].Value));
        Assert.Null(result["account.None"].Value);
    }

    /// <summary>未知CLR型別、重複節點及偽null均拒絕，不能靜默覆蓋或捏造原完整基準。</summary>
    /// <param name="input">由測試控制的有限JSON，不含秘密或實機資料。</param>
    [Theory]
    [InlineData("""{"account.A":{"type":"Unknown.Type","value":1}}""")]
    [InlineData("""{"account.A":{"type":"null","value":null},"account.A":{"type":"null","value":null}}""")]
    [InlineData("""{"account.A":{"type":"null","value":42}}""")]
    public void CorruptScalarInput_IsRejected(string input)
    {
        using var document = JsonDocument.Parse(input);
        Assert.Throws<InvalidDataException>(() => Restore(document.RootElement));
    }

    /// <summary>真正舊label漂移只回路徑與型別，原值及新值都不可外洩；相等對照必須零差異。</summary>
    [Fact]
    public void OldLabelChange_ReportsSafePathWithoutScalarText()
    {
        var before = new Dictionary<string, UiMetadataPublicationGuard.Scalar>(StringComparer.Ordinal)
            { ["account.Description"] = new("System.String", "SYNTHETIC-PRIVATE-OLD") };
        var after = new Dictionary<string, UiMetadataPublicationGuard.Scalar>(StringComparer.Ordinal)
            { ["account.Description"] = new("System.String", "SYNTHETIC-PRIVATE-NEW") };
        Assert.Empty(BaselineNodeReplayProbe.Compare(before, before, default));
        var difference = Assert.Single(BaselineNodeReplayProbe.Compare(before, after, default));
        Assert.Equal("account.Description", difference.Path); Assert.Equal("Changed", difference.Kind);
        var json = JsonSerializer.Serialize(difference);
        Assert.DoesNotContain("SYNTHETIC-PRIVATE", json, StringComparison.Ordinal);
        Assert.Equal("System.String", difference.ExpectedType); Assert.Equal("System.String", difference.ActualType);
    }

    /// <summary>預取消不得回部分差異；取消來源由當案using擁有，沒有註冊或逃逸task。</summary>
    [Fact]
    public void Comparison_PreCanceledNeverPublishesPartialResult()
    {
        using var cancellation = new System.Threading.CancellationTokenSource(); cancellation.Cancel();
        var values = new Dictionary<string, UiMetadataPublicationGuard.Scalar>(StringComparer.Ordinal)
            { ["account.Description"] = new("System.String", "synthetic") };
        Assert.Throws<OperationCanceledException>(() => BaselineNodeReplayProbe.Compare(values, values, cancellation.Token));
    }

    /// <summary>注入可攜帶正文的非法路徑，診斷必須整批拒絕而非寫入報告。</summary>
    [Fact]
    public void UnsafePath_IsRejectedBeforeDiagnosticPublication()
    {
        var values = new Dictionary<string, UiMetadataPublicationGuard.Scalar>(StringComparer.Ordinal)
            { ["account.Label/PRIVATE"] = new("System.String", "synthetic") };
        Assert.Throws<InvalidDataException>(() => BaselineNodeReplayProbe.Compare(values, values, default));
    }

    /// <summary>透過反射檢查新契約存在；內部故障原型別重拋，禁止把缺實作的編譯錯誤當RED。</summary>
    /// <param name="values">當案JsonDocument借用的完整values物件。</param>
    /// <returns>脫離JSON文件且僅由當案持有的純值字典。</returns>
    private static IReadOnlyDictionary<string, UiMetadataPublicationGuard.Scalar> Restore(JsonElement values)
    {
        var type = typeof(BaselineNodeReplayContractTests).Assembly.GetType("SpeechMessage.Dynamics.Deployment.Tests.Ui.BaselineNodeReplayProbe");
        Assert.NotNull(type);
        var method = type.GetMethod("RestoreScalars", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        try { return Assert.IsAssignableFrom<IReadOnlyDictionary<string, UiMetadataPublicationGuard.Scalar>>(method.Invoke(null, new object[] { values })); }
        catch (TargetInvocationException failure) when (failure.InnerException is not null)
        { ExceptionDispatchInfo.Capture(failure.InnerException).Throw(); throw; }
    }
}
