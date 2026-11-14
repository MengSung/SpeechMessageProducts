/*
 * 檔案整體說明
 *
 * 目的與定位：保護 Deployment observe 使用 Plugin 同源安全投影的離線契約，
 * 防止機密、LINE 識別、訊息正文及短個資因第二份欄位規則分歧而流出。
 * 主要功能與流程：以合成 SDK Entity 注入敏感值、精度、參照與超量資料，
 * 呼叫正式 Project 入口，逐欄斷言雜湊、日期偏移及身分；實作前先以反射解析入口
 * 取得缺少型別的斷言紅燈，正式案例直接呼叫產品，不另寫投影演算法。
 * 使用邊界與維護：不建立 SDK 連線、不讀秘密或接觸實機。每例擁有輸入，
 * 每次投影應自行 Dispose recorder 與 JSON 文件；回傳值須在文件釋放後可讀。
 * 平行同身分案例及來源突變驗證呼叫隔離；容量故障後重試須回到空白基線，
 * 不持有跨案例服務、快取、計時器、檔案或背景工作。
 */
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>
/// 以真實 SDK 純資料驗證安全輸出與呼叫生命週期；任何網路路徑均應由產品拒絕服務阻斷。
/// 所有資料為案例區域合成值，測試成功不代表實機授權或部署驗收。
/// </summary>
public sealed class ShadowSdkProjectionTests
{
    /// <summary>
    /// 注入帶前綴秘密欄位及藏在普通欄位的 LINE ID／Bearer，
    /// 決定性斷言為欄位完全省略，同時保留普通業務值及原始正式身分。
    /// </summary>
    [Fact]
    public void Project_ExcludesSecretsLineIdsAndSubject()
    {
        var row = Row();
        row["new_token"] = "synthetic-secret";
        row["new_line_id"] = "synthetic-recipient";
        row["subject"] = "synthetic-body";
        row["ordinary"] = "prefix U" + new string('a', 32) + " suffix";
        row["authorization"] = "Bearer synthetic";
        row["statuscode"] = new OptionSetValue(2);

        var result = Project(row);

        Assert.Equal("contact", result.Entity);
        Assert.Equal(row.Id.ToString("D", CultureInfo.InvariantCulture), result.Id);
        Assert.Single(result.Fields.EnumerateObject());
        Assert.Equal(2, result.Fields.GetProperty("statuscode").GetInt32());
    }

    /// <summary>
    /// 注入一般與歷史自訂個資欄位，即使短值也只能保留長度及獨立計算的 SHA-256。
    /// 若省略舊拼字或改成原文輸出，物件型別及摘要斷言都會失敗。
    /// </summary>
    /// <param name="field">既有 recorder 定義的個資 schema，測試資料不成為產品規則表。</param>
    [Theory]
    [InlineData("fullname")]
    [InlineData("emailaddress1")]
    [InlineData("new_personal_id")]
    [InlineData("new_cell_hpone")]
    [InlineData("company")]
    [InlineData("new_last_six_digit")]
    [InlineData("new_fb_account")]
    [InlineData("new_ig_account")]
    public void Project_ShortPersonalValuesAreHashed(string field)
    {
        var row = Row();
        row[field] = "短值";
        AssertHash(Project(row).Fields.GetProperty(field), "短值");
    }

    /// <summary>普通長值同樣只能輸出摘要，121 字元跨過既有 writer 的原文保留界線。</summary>
    [Fact]
    public void Project_LongValuesAreHashed()
    {
        var row = Row();
        var text = new string('x', 121);
        row["category"] = text;
        AssertHash(Project(row).Fields.GetProperty("category"), text);
    }

    /// <summary>
    /// 注入超過 double 有效位數的 Money 與帶負偏移日期，
    /// 必須保留 decimal 完整數值與日期字串偏移，不受主機時區改寫。
    /// </summary>
    [Fact]
    public void Project_PreservesDecimalAndDateOffset()
    {
        var row = Row();
        const decimal amount = 12345678901234567890.12345678m;
        var time = new DateTimeOffset(2026, 11, 14, 10, 20, 30, TimeSpan.FromHours(-5));
        row["amount"] = new Money(amount);
        row["scheduledend"] = time;

        var fields = Project(row).Fields;

        Assert.Equal(amount, fields.GetProperty("amount").GetDecimal());
        Assert.Equal("2026-11-14T10:20:30-05:00", fields.GetProperty("scheduledend").GetString());
    }

    /// <summary>
    /// 注入含顯示姓名及敏感 alternate key 的 reference；只允許實體與正式 ID，
    /// key 仍沿用同源隱私政策，不得透過巢狀資料繞過省略或雜湊。
    /// </summary>
    [Fact]
    public void Project_ReferenceOmitsNameAndFiltersKeys()
    {
        var row = Row();
        var reference = new EntityReference("account", Guid.NewGuid()) { Name = "不應出現的姓名" };
        reference.KeyAttributes["new_token"] = "synthetic-secret";
        reference.KeyAttributes["emailaddress1"] = "a@b";
        row["parentcustomerid"] = reference;

        var fields = Project(row).Fields.GetProperty("parentcustomerid");

        Assert.Equal(reference.Id.ToString("D", CultureInfo.InvariantCulture), fields.GetProperty("id").GetString());
        Assert.Equal("account", fields.GetProperty("entity").GetString());
        Assert.False(fields.TryGetProperty("name", out _));
        var keys = fields.GetProperty("keys");
        Assert.False(keys.TryGetProperty("new_token", out _));
        AssertHash(keys.GetProperty("emailaddress1"), "a@b");
    }

    /// <summary>
    /// 分別注入欄位超量、字串預算超量及摘要容量超量，全部拒絕而非發布部分結果。
    /// 隨後正常呼叫必須只含本次值，證明失敗 recorder 不污染重試。
    /// </summary>
    /// <param name="kind">注入的容量故障類別。</param>
    [Theory]
    [InlineData("fields")]
    [InlineData("units")]
    [InlineData("projection")]
    public void Project_RejectsOverflowAndAllowsCleanRetry(string kind)
    {
        var row = Row();
        if (kind == "fields")
            for (var index = 0; index < 129; index++)
                row["field" + index.ToString(CultureInfo.InvariantCulture)] = index;
        else if (kind == "units")
            row["category"] = new string('x', 65_537);
        else
            for (var index = 0; index < 128; index++)
                row["field" + index.ToString(CultureInfo.InvariantCulture)] = new string('x', 120);

        if (kind == "projection")
            Assert.Throws<InvalidDataException>(() => Project(row));
        else
            Assert.Throws<NotSupportedException>(() => Project(row));
        var clean = Row();
        clean["value"] = 3;
        var result = Project(clean);
        Assert.Single(result.Fields.EnumerateObject());
        Assert.Equal(3, result.Fields.GetProperty("value").GetInt32());
    }

    /// <summary>
    /// 同一正式身分平行投影六十四份不同資料，並在返回後突變 SDK Money／reference。
    /// 每筆只含自己的值且不因來源變動而改寫；結果持續可讀亦驗證文件已 detached。
    /// </summary>
    [Fact]
    public async Task Project_ConcurrentCallsAndSourceMutationStayDetached()
    {
        var id = Guid.NewGuid();
        var projections = await Task.WhenAll(Enumerable.Range(1, 64).Select(index => Task.Run(() =>
        {
            var row = new Entity("contact", id);
            var amount = new Money(index);
            var reference = new EntityReference("account", Guid.NewGuid());
            var referenceId = reference.Id;
            row["amount"] = amount;
            row["parentcustomerid"] = reference;
            var result = Project(row);
            amount.Value = -1;
            reference.Id = Guid.NewGuid();
            row.Attributes.Clear();
            Assert.Equal(index, result.Fields.GetProperty("amount").GetDecimal());
            Assert.Equal(referenceId.ToString("D", CultureInfo.InvariantCulture),
                result.Fields.GetProperty("parentcustomerid").GetProperty("id").GetString());
            return result;
        })));

        Assert.Equal(64, projections.Select(row => row.Fields.GetProperty("amount").GetDecimal()).Distinct().Count());
    }

    /// <summary>缺正式身分或 null 輸入必須先拒絕，不得生成 draft key 或假造資料列。</summary>
    [Fact]
    public void Project_RejectsNullAndMissingDatabaseIdentity()
    {
        Assert.Throws<ArgumentNullException>(() => Project(null!));
        Assert.Throws<ArgumentException>(() => Project(new Entity("contact")));
    }

    /// <summary>每例產生獨立正式身分，不以顯示值或測試索引當主鍵。</summary>
    private static Entity Row() => new("contact", Guid.NewGuid());

    /// <summary>
    /// 直接委派需求指定的正式入口，不攔截產品例外或重寫安全規則。
    /// 每次輸入及回傳值均屬目前案例，沒有跨呼叫可變狀態。
    /// </summary>
    private static ShadowRow Project(Entity row) => ShadowSdkProjection.Project(row);

    /// <summary>以框架 SHA-256 獨立核對摘要，防止僅斷言屬性存在而放過錯誤或固定 hash。</summary>
    private static void AssertHash(JsonElement actual, string value)
    {
        Assert.Equal(value.Length, actual.GetProperty("length").GetInt32());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value))),
            actual.GetProperty("sha256").GetString());
    }
}
