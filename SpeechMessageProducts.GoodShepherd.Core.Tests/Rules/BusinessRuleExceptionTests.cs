/*
 * 檔案整體說明
 *
 * 目的與定位：保護業務拒絕例外與既有具名文字格式化器的契約，避免跨層傳遞例外時
 * 保存呼叫端可修改陣列或可變參數物件，導致診斷內容在驗證後遭改寫。
 * 主要功能與流程：使用純合成 TextArg、固定文字鍵驗證參數；刻意改寫輸入陣列及
 * 取回的陣列，斷言例外仍保留原始值，並注入可變參數物件確認拒絕。外殼使用系統
 * 字典的具名 formatter 消費同一份 TextArg 快照，本檔不虛構教會 scope。
 * 使用邊界與維護：每筆例外只由測試區域變數擁有，不建立靜態快取或外部連線，
 * 沒有背景工作或需 Dispose 的資源；這些離線斷言不代表 Plugin 實機驗收。
 */
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Rules;

/// <summary>驗證例外參數與 formatter 之間的具名純值快照。</summary>
public sealed class BusinessRuleExceptionTests
{
    /// <summary>修改原陣列及讀取結果都不得修改例外的內部狀態，TextArg 型別保持 formatter 相容。</summary>
    [Fact]
    public void BusinessRuleException_ArgumentsAreDetachedTextArgs()
    {
        var input = new[] { new TextArg("churchName", "Alpha") };
        var exception = new BusinessRuleException(new TextKey("Brand.SenderName"), input);
        input[0] = new TextArg("churchName", "mutated");
        var snapshot = Assert.IsType<TextArg[]>((object)exception.Arguments);
        Assert.Equal("Alpha", snapshot[0].Value);
        snapshot[0] = new TextArg("churchName", "changed-copy");
        var second = Assert.IsType<TextArg[]>((object)exception.Arguments);
        Assert.Equal("Alpha", second[0].Value);
        Assert.Equal("churchName", second[0].Name);
        var cultures = new[] { "zh-TW" };
        var rows = new[] { new string?[] { exception.TextKey.Value, "{churchName}助手" } };
        var table = SystemTextTable.FromRows(cultures, rows);
        Assert.Equal("Alpha助手", table.GetFormattedText("zh-TW", exception.TextKey, second));
    }

    /// <summary>可變物件會延長不明所有權生命週期，必須拒絕而不能延後執行其 ToString。</summary>
    [Fact]
    public void BusinessRuleException_MutableArgumentValue_Rejected()
    {
        Assert.Throws<ArgumentException>(() => new BusinessRuleException(new TextKey("Brand.SenderName"),
            new TextArg("churchName", new System.Text.StringBuilder("mutable"))));
    }
    /// <summary>參數名稱及容量與既有 formatter 相同採 fail-closed，不保留無法格式化的快照。</summary>
    [Fact]
    public void BusinessRuleException_InvalidNamesAndBudget_Rejected()
    {
        var key = new TextKey("Brand.SenderName");
        Assert.Throws<ArgumentException>(() => new BusinessRuleException(default));
        Assert.Throws<ArgumentException>(() => new BusinessRuleException(key, default(TextArg)));
        Assert.Throws<ArgumentException>(() => new BusinessRuleException(key,
            new TextArg("duplicate", 1), new TextArg("duplicate", 2)));
        Assert.Throws<ArgumentException>(() => new BusinessRuleException(key, new TextArg[257]));
        Assert.Throws<ArgumentException>(() => new BusinessRuleException(key, new TextArg("value", new byte[2])));
        Assert.Throws<ArgumentException>(() => new BusinessRuleException(key, new TextArg("value", DateTimeKind.Utc)));
    }
}
