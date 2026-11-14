/*
檔案整體說明
目的與定位：以記憶體 ZIP 與合成 Dynamics 解決方案 XML，驗證 P5-03b 匯出入共用的封裝邊界，並非實機匯出結果。
主要功能與流程：核對共用實體表單移除但檢視與自有實體表單保留；注入越界路徑、大小寫重複、壓縮膨脹、DTD 及錯誤方案身分，驗證完整候選失敗而非發布部分資料。
使用邊界與擁有權：每個案例獨占記憶體串流與 ZIP，using 在斷言失敗時亦釋放；資料只含合成 GUID、固定名稱與測試內容，不讀 User Secrets、不接 SDK、不建立背景工作。往返測試以實際封裝內容而非替身呼叫次數驗證。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>保護解決方案 ZIP 的來源完整性、共用元件界線及有界記憶體契約；所有輸入皆為離線合成資料。</summary>
public sealed class UiSolutionArchiveTests
{
    private static readonly string[] NonWhitelistedImportArguments =
        { "ui-import", "--org", "production", "--from", "outside.zip" };

    /// <summary>共用 account／contact 表單及其根參照必須移除；相同集合中的檢視及本產品實體表單必須完整保留。</summary>
    [Fact]
    public void UiExport_SharedEntityForm_NotPackaged()
    {
        var result = UiSolutionArchive.Prepare(Archive());
        var customizations = XDocument.Parse(Encoding.UTF8.GetString(result["customizations.xml"]));
        var entities = customizations.Descendants("Entity").ToArray();
        Assert.Equal(3, entities.Length);
        foreach (var entity in entities.Where(x => x.Element("Name")!.Value is "account" or "contact"))
        {
            Assert.Null(entity.Element("FormXml"));
            Assert.NotNull(entity.Element("SavedQueries"));
        }
        Assert.NotNull(entities.Single(x => x.Element("Name")!.Value == "new_example").Element("FormXml"));
        var solution = XDocument.Parse(Encoding.UTF8.GetString(result["solution.xml"]));
        var roots = solution.Descendants("RootComponent").ToArray();
        Assert.Single(roots.Where(x => (string?)x.Attribute("type") == "60"));
        Assert.All(roots.Where(x => (string?)x.Attribute("type") == "1"),
            x => Assert.Equal("2", (string?)x.Attribute("behavior")));
        Assert.Equal(new byte[] { 1, 2, 3 }, result["WebResources/smv_icon"]);
    }

    /// <summary>壓縮檔的父目錄、絕對路徑、Windows 別名與反斜線均不能成為輸出路徑；拒絕發生於任何檔案發布之前。</summary>
    [Theory]
    [InlineData("../escape.xml")]
    [InlineData("/escape.xml")]
    [InlineData("C:/escape.xml")]
    [InlineData("folder\\escape.xml")]
    [InlineData("folder/../escape.xml")]
    [InlineData("folder/CON.xml")]
    [InlineData("folder/name. ")]
    public void UiImport_ZipEntryOutsideRoot_Rejected(string name)
    {
        Assert.Throws<InvalidDataException>(() => UiSolutionArchive.Prepare(Archive(extra: name)));
    }

    /// <summary>單一高度可壓縮大檔不能繞過解壓後上限；測試只配置比單檔預算多一個位元組的有限候選。</summary>
    [Fact]
    public void UiImport_OversizedArchive_Rejected()
    {
        Assert.Throws<InvalidDataException>(() => UiSolutionArchive.Prepare(
            Archive(extra: "WebResources/large", content: new byte[8 * 1024 * 1024 + 1])));
    }

    /// <summary>Windows 相同身分的兩個路徑必須拒絕，不能用字典最後寫入覆蓋來隱藏衝突。</summary>
    [Fact]
    public void DuplicateArchiveIdentity_IsRejected()
    {
        Assert.Throws<InvalidDataException>(() => UiSolutionArchive.Prepare(Archive(extra: "SOLUTION.XML")));
    }

    /// <summary>外部實體與錯誤方案／受管理封裝不可進入匯入候選；解析例外不得攜帶原 XML 本文。</summary>
    [Theory]
    [InlineData("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///private'>]><ImportExportXml>&e;</ImportExportXml>")]
    [InlineData("<ImportExportXml><SolutionManifest><UniqueName>Other</UniqueName><Managed>0</Managed></SolutionManifest></ImportExportXml>")]
    [InlineData("<ImportExportXml><SolutionManifest><UniqueName>SpeechMessageUi</UniqueName><Managed>1</Managed></SolutionManifest></ImportExportXml>")]
    public void InvalidSolutionXml_IsRejected(string solution)
    {
        var error = Assert.Throws<InvalidDataException>(() => UiSolutionArchive.Prepare(Archive(solution: solution)));
        Assert.DoesNotContain("private", error.ToString(), StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    /// <summary>經過真 ZIP 重壓與再驗證後，剩餘路徑與每個位元組必須一致，不得遺失非 XML 資源。</summary>
    [Fact]
    public void ArchiveRoundTrip_PreservesEveryRetainedComponent()
    {
        var expected = UiSolutionArchive.Prepare(Archive());
        var actual = UiSolutionArchive.Prepare(UiSolutionArchive.Pack(expected));
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach (var item in expected) Assert.Equal(item.Value, actual[item.Key]);
    }

    /// <summary>UI 匯入沿用唯一組織白名單；非 goodshepherdback 必須在建立租約前拒絕且不觸發 connector。</summary>
    [Fact]
    public void UiImport_NonWhitelistedOrganization_Rejected()
    {
        using var output = new StringWriter();
        var reports = new List<Exception>();
        var result = DeploymentCli.Run(
            NonWhitelistedImportArguments,
            new RejectingConnector(), output, reports.Add);
        Assert.Equal(2, result);
        Assert.Empty(reports);
    }

    /// <summary>建立標準三個根檔及自有資源；可注入單一惡意 entry 或 XML，所有串流離開方法前確定關閉。</summary>
    internal static byte[] Archive(string? extra = null, byte[]? content = null, string? solution = null)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "solution.xml", Encoding.UTF8.GetBytes(solution ?? Solution));
            Add(zip, "customizations.xml", Encoding.UTF8.GetBytes(Customizations));
            Add(zip, "[Content_Types].xml", Encoding.UTF8.GetBytes("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\" />"));
            Add(zip, "WebResources/smv_icon", new byte[] { 1, 2, 3 });
            if (extra is not null) Add(zip, extra, content ?? new byte[] { 4 });
        }
        return stream.ToArray();
    }

    /// <summary>寫入本案例的單一 ZIP 成員，entry stream 不外借或跨案例保留。</summary>
    private static void Add(ZipArchive zip, string name, byte[] bytes)
    {
        using var output = zip.CreateEntry(name).Open();
        output.Write(bytes);
    }

    /// <summary>白名單拒絕案例的 connector；若 parser 越過拒絕邊界，立即以固定例外暴露錯誤。</summary>
    private sealed class RejectingConnector : IOrganizationConnector
    {
        /// <summary>非白名單命令不應取得 organization lease。</summary>
        public OrganizationLease Connect(string organization) =>
            throw new InvalidOperationException("Connector must not be called.");
    }

    /// <summary>標準未受管理解決方案的合成根參照；三個固定 formid 分別對應下方三個實體。</summary>
    private const string Solution = """
        <ImportExportXml><SolutionManifest><UniqueName>SpeechMessageUi</UniqueName><Managed>0</Managed>
        <RootComponents>
        <RootComponent type="1" schemaName="account" behavior="0" />
        <RootComponent type="1" schemaName="contact" behavior="0" />
        <RootComponent type="60" id="{10000000-0000-0000-0000-000000000001}" />
        <RootComponent type="60" id="{10000000-0000-0000-0000-000000000002}" />
        <RootComponent type="60" id="{10000000-0000-0000-0000-000000000003}" />
        </RootComponents></SolutionManifest></ImportExportXml>
        """;

    /// <summary>表單之外仍有檢視，避免測試以整個刪掉共用實體而錯誤通過。</summary>
    private const string Customizations = """
        <ImportExportXml><Entities>
        <Entity><Name>account</Name><FormXml><forms><systemform><formid>{10000000-0000-0000-0000-000000000001}</formid><form /></systemform></forms></FormXml><SavedQueries /></Entity>
        <Entity><Name>contact</Name><FormXml><forms><systemform><formid>{10000000-0000-0000-0000-000000000002}</formid><form /></systemform></forms></FormXml><SavedQueries /></Entity>
        <Entity><Name>new_example</Name><FormXml><forms><systemform><formid>{10000000-0000-0000-0000-000000000003}</formid><form /></systemform></forms></FormXml></Entity>
        </Entities></ImportExportXml>
        """;
}
