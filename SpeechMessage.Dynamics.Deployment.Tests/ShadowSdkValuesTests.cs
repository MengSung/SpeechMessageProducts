/*
 * 檔案整體說明
 *
 * 目的與定位：以合成 metadata 保護 Deployment 影子情境單欄 JSON 轉換契約，
 * 驗證正式 SDK 值、精確十進位、日期行為及明確別名的信任邊界。
 * 主要功能與流程：先以反射斷言產品入口存在取得紅燈，再直接呼叫轉換器；
 * 注入錯型別、超量、未知選項、重複 marker、精度損失及來源突變，核對固定拒絕。
 * 使用邊界與維護：全部資料由單一案例擁有，不連網、不讀秘密、不宣稱實機驗收。
 * JSON 文件由 helper 在 finally 等價的 using 範圍 Dispose；回傳 SDK 值須獨立存活。
 * 平行案例核對無共享可變狀態，lookup 複本不持有來源名稱、key 或 RowVersion；
 * 無連線、計時器、背景工作或跨案例快取需要排空。
 */
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>離線核對欄位轉換的型別、容量、隔離與失敗契約；合成 metadata 不代表正式組織狀態。</summary>
public sealed class ShadowSdkValuesTests
{
    /// <summary>實作前入口不存在應明確紅燈，不以複製產品演算法充當測試。</summary>
    [Fact]
    public void Convert_EntryPointExists() => Assert.NotNull(Converter());

    /// <summary>文字與 memo 保留原文，並同時受到 metadata 與全域字元上限約束。</summary>
    [Fact]
    public void Convert_StringsEnforceBothBounds()
    {
        Assert.Equal("甲乙", Convert("\"甲乙\"", new StringAttributeMetadata { MaxLength = 2 }));
        Assert.Equal("甲乙", Convert("\"甲乙\"", new MemoAttributeMetadata { MaxLength = 2 }));
        Reject("\"甲乙丙\"", new StringAttributeMetadata { MaxLength = 2 });
        Reject("12", new StringAttributeMetadata { MaxLength = 2 });
        Reject(JsonSerializer.Serialize(new string('x', 65_537)), new MemoAttributeMetadata { MaxLength = 100_000 });
        Assert.Equal(new string('x', 65_536), Convert(JsonSerializer.Serialize(new string('x', 65_536)),
            new MemoAttributeMetadata { MaxLength = 100_000 }));
    }

    /// <summary>整數邊界及 bigint 超過 double 精度的值須正確保存，字串或小數不可冒充整數。</summary>
    [Fact]
    public void Convert_IntegerAndBigIntAreExact()
    {
        var integer = new IntegerAttributeMetadata { MinValue = -3, MaxValue = 3 };
        Assert.Equal(-3, Convert("-3", integer));
        Assert.Equal(3, Convert("3", integer));
        Reject("4", integer);
        Reject("1.1", integer);
        Reject("\"1\"", integer);
        var big = BigIntMetadata(long.MinValue, long.MaxValue);
        Assert.Equal(9_007_199_254_740_993L, Convert("9007199254740993", big));
        Assert.Equal(long.MinValue, Convert("-9223372036854775808", big));
        Assert.Equal(long.MaxValue, Convert("9223372036854775807", big));
        Reject("9223372036854775808", big);
        Reject("-9223372036854775809", big);
        Reject("3", BigIntMetadata(-2, 2));
    }

    /// <summary>精確數值可用不同指數及尾零拼字，超出 decimal 可表示精度或下溢必須拒絕。</summary>
    /// <param name="raw">不經 double 的原始 JSON 數值。</param>
    /// <param name="expected">精確預期十進位字串。</param>
    [Theory]
    [InlineData("12345678901234567890.12345678", "12345678901234567890.12345678")]
    [InlineData("1.230000000000000000000000000000000", "1.23")]
    [InlineData("123e-2", "1.23")]
    [InlineData("0.0000000000000000000000000001", "0.0000000000000000000000000001")]
    [InlineData("-0e-10", "0")]
    public void Convert_DecimalPreservesExactValue(string raw, string expected) =>
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), Convert(raw, DecimalMetadata()));

    /// <summary>框架可捨入成功的高精度輸入也須拒絕，固定例外不回顯數值。</summary>
    /// <param name="raw">注入捨入、溢位、下溢或無界指數。</param>
    [Theory]
    [InlineData("1.23456789012345678901234567891")]
    [InlineData("0.00000000000000000000000000001")]
    [InlineData("79228162514264337593543950336")]
    [InlineData("99999999999999999999999999999e-50")]
    [InlineData("1e2147483648")]
    [InlineData("\"1.2\"")]
    public void Convert_DecimalNeverRounds(string raw) => Reject(raw, DecimalMetadata());

    /// <summary>decimal 與 Money 只接受 metadata 閉區間；Money 每次回傳獨立的 SDK 包裝。</summary>
    [Fact]
    public void Convert_DecimalAndMoneyEnforceMetadataRange()
    {
        var number = new DecimalAttributeMetadata { MinValue = -2, MaxValue = 2 };
        Assert.Equal(2m, Convert("2", number));
        Reject("2.01", number);
        Reject("-2.01", number);
        var money = new MoneyAttributeMetadata { MinValue = -2, MaxValue = 2 };
        Assert.Equal(1.25m, Assert.IsType<Money>(Convert("1.25", money)).Value);
        Reject("2.01", money);
        Reject("1.23456789012345678901234567891", money);
    }

    /// <summary>布林值及選項必須符合 metadata，未知選項及數字布林不得自動轉型。</summary>
    [Fact]
    public void Convert_BooleanAndOptionsUseMetadata()
    {
        Assert.Equal(true, Convert("true", new BooleanAttributeMetadata()));
        Assert.Equal(false, Convert("false", new BooleanAttributeMetadata()));
        Reject("1", new BooleanAttributeMetadata());
        foreach (var metadata in new EnumAttributeMetadata[]
        {
            new PicklistAttributeMetadata(), new StateAttributeMetadata(), new StatusAttributeMetadata()
        })
        {
            metadata.OptionSet = new OptionSetMetadata();
            metadata.OptionSet.Options.Add(new OptionMetadata(new Label("合成選項", 1028), 7));
            Assert.Equal(7, Assert.IsType<OptionSetValue>(Convert("7", metadata)).Value);
            Reject("8", metadata);
            Reject("\"7\"", metadata);
        }
    }

    /// <summary>lookup 的兩種 marker 均須顯式解析；來源後續突變不可影響回傳身分。</summary>
    /// <param name="marker">允許的 alias marker 名稱。</param>
    [Theory]
    [InlineData("ref")]
    [InlineData("alias")]
    public void Convert_LookupUsesAliasAndReturnsOwnedIdentity(string marker)
    {
        var source = new EntityReference("contact", Guid.NewGuid()) { Name = "來源姓名", RowVersion = "1" };
        source.KeyAttributes["secret"] = "合成敏感值";
        var id = source.Id;
        var calls = 0;
        var result = Assert.IsType<EntityReference>(Convert(
            "{\"" + marker + "\":\"member\",\"entity\":\"contact\",\"name\":\"忽略\"}", Lookup(),
            (alias, entity) =>
            {
                Assert.Equal("member", alias);
                Assert.Equal("contact", entity);
                calls++;
                return source;
            }));
        source.Id = Guid.NewGuid();
        Assert.Equal(1, calls);
        Assert.Equal(id, result.Id);
        Assert.Equal("contact", result.LogicalName);
        Assert.Null(result.Name);
        Assert.Null(result.RowVersion);
        Assert.Empty(result.KeyAttributes);
        Assert.NotSame(source, result);
    }

    /// <summary>畸形或模糊 marker 不得呼叫 resolver；重複鍵不能採最後值覆蓋。</summary>
    /// <param name="json">注入未知欄位、重複欄位、原始 GUID 或錯誤型別。</param>
    [Theory]
    [InlineData("\"11111111-1111-1111-1111-111111111111\"")]
    [InlineData("{\"ref\":\"11111111-1111-1111-1111-111111111111\",\"entity\":\"contact\"}")]
    [InlineData("{\"ref\":\"member\",\"entity\":\"account\"}")]
    [InlineData("{\"ref\":\"member\",\"entity\":\"contact\",\"entity\":\"contact\"}")]
    [InlineData("{\"ref\":\"member\",\"ref\":\"member\",\"entity\":\"contact\"}")]
    [InlineData("{\"ref\":\"member\",\"alias\":\"member\",\"entity\":\"contact\"}")]
    [InlineData("{\"ref\":\"member\",\"entity\":\"contact\",\"unknown\":1}")]
    [InlineData("{\"ref\":\"member\",\"entity\":\"contact\",\"name\":3}")]
    [InlineData("{\"ref\":3,\"entity\":\"contact\"}")]
    [InlineData("{\"ref\":\"member\"}")]
    public void Convert_LookupRejectsBeforeResolution(string json) =>
        Reject(json, Lookup(), (_, _) => throw new Xunit.Sdk.XunitException("不應解析畸形 marker"));

    /// <summary>解析出的正式身分仍要核對實體及非空 ID，不能信任 resolver 的錯配輸出。</summary>
    [Fact]
    public void Convert_LookupRejectsInvalidResolvedIdentity()
    {
        const string marker = "{\"alias\":\"member\",\"entity\":\"contact\"}";
        Reject(marker, Lookup(), (_, _) => new EntityReference("account", Guid.NewGuid()));
        Reject(marker, Lookup(), (_, _) => new EntityReference("contact", Guid.Empty));
        Reject(marker, Lookup(), (_, _) => null!);
    }

    /// <summary>UserLocal 的顯式 offset 正規化為 UTC，不依本機時區猜測；日期及獨立時間維持未指定 Kind。</summary>
    [Fact]
    public void Convert_DateBehaviorsAreExplicit()
    {
        var local = new DateTimeAttributeMetadata { DateTimeBehavior = DateTimeBehavior.UserLocal };
        var utc = Assert.IsType<DateTime>(Convert("\"2026-11-14T10:20:30+08:00\"", local));
        Assert.Equal(new DateTime(2026, 11, 14, 2, 20, 30, DateTimeKind.Utc), utc);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(utc, Convert("\"2026-11-14T02:20:30Z\"", local));
        Reject("\"2026-11-14T02:20:30\"", local);
        Reject("\"2026-11-14\"", local);
        var date = new DateTimeAttributeMetadata { DateTimeBehavior = DateTimeBehavior.DateOnly };
        Assert.Equal(new DateTime(2026, 11, 14), Convert("\"2026-11-14\"", date));
        Reject("\"2026-11-14T00:00:00Z\"", date);
        Reject("\"2026-02-30\"", date);
        var independent = new DateTimeAttributeMetadata { DateTimeBehavior = DateTimeBehavior.TimeZoneIndependent };
        var wall = Assert.IsType<DateTime>(Convert("\"2026-11-14T02:20:30.1234567\"", independent));
        Assert.Equal(DateTimeKind.Unspecified, wall.Kind);
        Reject("\"2026-11-14T02:20:30+08:00\"", independent);
        Reject("\"2026-11-14T02:20:30Z\"", independent);
        Reject("\"2026-11-14T02:20:30.12345678\"", independent);
    }

    /// <summary>null 不能繞過 metadata 白名單或必要界限；未支援型別含 GUID 一律拒絕。</summary>
    [Fact]
    public void Convert_NullRequiresKnownUsableMetadata()
    {
        Assert.Null(Convert("null", new StringAttributeMetadata { MaxLength = 2 }));
        foreach (var metadata in new AttributeMetadata[]
        {
            new AttributeMetadata(), new UniqueIdentifierAttributeMetadata(), new DoubleAttributeMetadata(),
            new StringAttributeMetadata(), new IntegerAttributeMetadata(), new BigIntAttributeMetadata(),
            new DecimalAttributeMetadata { MinValue = 2, MaxValue = 1 },
            new MoneyAttributeMetadata { MinValue = double.NaN, MaxValue = 2 },
            new PicklistAttributeMetadata(), new LookupAttributeMetadata(), new DateTimeAttributeMetadata()
        })
            Reject("null", metadata);
        Reject("\"11111111-1111-1111-1111-111111111111\"", new UniqueIdentifierAttributeMetadata());
    }

    /// <summary>平行解析不同來源只回傳各自身分，文件已釋放仍可使用輸出，不保留跨呼叫 reference。</summary>
    [Fact]
    public async Task Convert_ConcurrentReferencesRemainIsolated()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            var id = Guid.NewGuid();
            var result = Assert.IsType<EntityReference>(Convert("{\"ref\":\"member\",\"entity\":\"contact\"}",
                Lookup(), (_, _) => new EntityReference("contact", id)));
            Assert.Equal(id, result.Id);
            return result;
        })));
        Assert.Equal(32, results.Select(item => item.Id).Distinct().Count());
    }

    /// <summary>重複選項或 targets 即使輸入為 null 仍不可發布，失敗後合法呼叫須回到乾淨基線。</summary>
    [Fact]
    public void Convert_DuplicateMetadataFailsClosedAndRetryIsClean()
    {
        var choice = new PicklistAttributeMetadata { OptionSet = new OptionSetMetadata() };
        choice.OptionSet.Options.Add(new OptionMetadata(new Label("甲", 1028), 1));
        choice.OptionSet.Options.Add(new OptionMetadata(new Label("乙", 1028), 1));
        Reject("null", choice);
        Reject("1", choice);
        choice.OptionSet.Options.RemoveAt(1);
        Assert.Equal(1, Assert.IsType<OptionSetValue>(Convert("1", choice)).Value);
        var duplicateTargets = new LookupAttributeMetadata { Targets = new[] { "contact", "contact" } };
        var invalidTarget = new LookupAttributeMetadata { Targets = new[] { "contact.bad" } };
        Reject("null", duplicateTargets);
        Reject("null", invalidTarget);
        Reject("null", new LookupAttributeMetadata { Targets = new string[129] });
        Reject("null", new DateTimeAttributeMetadata { DateTimeBehavior = "Unknown" });
        Reject("null", new MoneyAttributeMetadata { MinValue = -double.MaxValue, MaxValue = double.MaxValue });
    }

    /// <summary>每種已支援且完整的 metadata 都允許明確 null，且不解析 lookup；undefined 仍須拒絕。</summary>
    [Fact]
    public void Convert_ValidNullDoesNotResolveOrInventValues()
    {
        foreach (var metadata in new AttributeMetadata[]
        {
            new StringAttributeMetadata { MaxLength = 1 }, new MemoAttributeMetadata { MaxLength = 1 },
            new IntegerAttributeMetadata { MinValue = 0, MaxValue = 1 }, BigIntMetadata(0, 1),
            DecimalMetadata(), new MoneyAttributeMetadata { MinValue = 0, MaxValue = 1 },
            new BooleanAttributeMetadata(), Lookup(),
            new DateTimeAttributeMetadata { DateTimeBehavior = DateTimeBehavior.UserLocal }
        })
            Assert.Null(Convert("null", metadata));
        Assert.Throws<InvalidDataException>(() => ShadowSdkValues.Convert(default, DecimalMetadata(),
            (_, _) => throw new InvalidOperationException()));
    }

    /// <summary>真實 resolver 執行故障保留原實例交由外層告警，不能包裝成預期輸入拒絕而遺失證據。</summary>
    [Fact]
    public void Convert_ResolverFaultIsNotReclassified()
    {
        var fault = new InvalidOperationException("合成故障");
        Assert.Same(fault, Assert.Throws<InvalidOperationException>(() =>
            Convert("{\"alias\":\"member\",\"entity\":\"contact\"}", Lookup(), (_, _) => throw fault)));
    }

    /// <summary>運算預算先於任意精度整數配置，超長字面數值及錯型別容器必須固定拒絕。</summary>
    [Fact]
    public void Convert_RejectsOversizedNumbersAndContainers()
    {
        Reject("1." + new string('0', 300), DecimalMetadata());
        Reject("[]", DecimalMetadata());
        Reject("{}", new StringAttributeMetadata { MaxLength = 2 });
        Reject("\"" + new string('x', 257) + "\"", Lookup());
        Reject("{\"alias\":\"member\",\"entity\":\"contact\",\"name\":\"" + new string('x', 65_537) + "\"}", Lookup());
    }

    /// <summary>合成 decimal 的完整有效範圍，不預設捨入位數以便獨立測試框架精度。</summary>
    private static DecimalAttributeMetadata DecimalMetadata() => new() { MinValue = decimal.MinValue, MaxValue = decimal.MaxValue };

    /// <summary>只允許正式 contact 表的合成 lookup，不提供額外授權。</summary>
    private static LookupAttributeMetadata Lookup() => new() { Targets = new[] { "contact" } };

    /// <summary>
    /// 模擬 SDK 從伺服器反序列化的 bigint 唯讀界限，私有 setter 僅存在於離線測試，
    /// 產品仍直接讀權威 metadata，不以 CLR 全範圍取代缺少的伺服器定義。
    /// </summary>
    private static BigIntAttributeMetadata BigIntMetadata(long minimum, long maximum)
    {
        var result = new BigIntAttributeMetadata();
        typeof(BigIntAttributeMetadata).GetProperty(nameof(BigIntAttributeMetadata.MinValue))!.SetValue(result, minimum);
        typeof(BigIntAttributeMetadata).GetProperty(nameof(BigIntAttributeMetadata.MaxValue))!.SetValue(result, maximum);
        return result;
    }

    /// <summary>保留實作前的紅燈入口斷言；行為案例另以直接呼叫驗證，不透過此反射方法執行產品。</summary>
    private static MethodInfo Converter()
    {
        var type = typeof(Program).Assembly.GetType("SpeechMessage.Dynamics.Deployment.Shadow.ShadowSdkValues");
        Assert.NotNull(type);
        var method = type.GetMethod("Convert", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(method);
        return method;
    }

    /// <summary>每次獨占並釋放 JSON 文件，綠燈案例直接呼叫正式入口，避免反射或測試自行正規化輸入。</summary>
    private static object? Convert(string json, AttributeMetadata metadata, Func<string, string, EntityReference>? resolver = null)
    {
        using var document = JsonDocument.Parse(json);
        return ShadowSdkValues.Convert(document.RootElement, metadata,
            resolver ?? new Func<string, string, EntityReference>((_, _) => throw new InvalidOperationException()));
    }

    /// <summary>所有輸入拒絕皆須固定 InvalidDataException，不將敏感 JSON 或 metadata 值放入訊息。</summary>
    private static void Reject(string json, AttributeMetadata metadata, Func<string, string, EntityReference>? resolver = null) =>
        Assert.Equal("Shadow SDK field value is invalid.",
            Assert.Throws<InvalidDataException>(() => Convert(json, metadata, resolver)).Message);
}
