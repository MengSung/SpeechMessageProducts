/*
檔案整體說明
目的與定位：FS savedquery 建立的 D365 9.1 執行期版面契約回歸，防止只通過匯出格式的
寬鬆 XSD，卻在真正 Create 時以 0x80040216 拒絕。2026-11-28 唯讀檢查 D365APP01
已安裝 LayoutXmlValidator 的內嵌 XSD，確認 grid 的 icon／jump 等屬性為必填。
主要流程：以純合成 schema 呼叫真正 ViewDefinitions；本測試自行維護的最小 XSD
只重現該產生器使用的 grid／row／cell 子集與執行期必填屬性，不複製供應商組件或載入 DLL。
故障注入與斷言：逐一刪除必填屬性時，合成 Create 邊界轉成真實 SDK fault 型別及固定
錯碼；正常產物必須通過相同邊界，jump 只能使用已核准的顯示欄位，輸出重跑保持一致。
資源所有權與隔離：無真實 SDK client、網路、User Secrets、業務資料、快取或背景工作。
XML、schema set 與純值候選僅存活於本案；StringReader／XmlReader 由本案 using 清理，
禁止 DTD 與外部解析。這是離線同型故障回歸，不代表實機 Create、發布或 FS 驗收成功。
*/
using System;
using System.IO;
using System.ServiceModel;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Deployment.Schema;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>隔離驗證正式產生器與執行期版面必填契約，不執行任何組織寫入。</summary>
public sealed class UiRuntimeLayoutContractTests
{
    /// <summary>
    /// 獨立合成執行期支援子集；不是以產生結果反推期望，六個必填 grid 屬性來自
    /// 已安裝驗證器常數。保留布林／整數型別、單一 row 與至少一個 cell 的拒絕規則。
    /// </summary>
    private const string RuntimeSchema = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="grid"><xs:complexType><xs:sequence>
            <xs:element name="row"><xs:complexType><xs:sequence>
              <xs:element name="cell" minOccurs="1" maxOccurs="unbounded"><xs:complexType>
                <xs:attribute name="name" type="xs:string" use="required"/>
                <xs:attribute name="width" type="xs:integer"/>
              </xs:complexType></xs:element>
            </xs:sequence><xs:attribute name="name" type="xs:string" use="required"/>
              <xs:attribute name="id" type="xs:string" use="required"/>
            </xs:complexType></xs:element>
          </xs:sequence>
            <xs:attribute name="name" type="xs:string" use="required"/>
            <xs:attribute name="object" type="xs:integer" use="required"/>
            <xs:attribute name="select" type="xs:boolean" use="required"/>
            <xs:attribute name="preview" type="xs:boolean" use="required"/>
            <xs:attribute name="icon" type="xs:boolean" use="required"/>
            <xs:attribute name="jump" type="xs:string" use="required"/>
          </xs:complexType></xs:element>
        </xs:schema>
        """;

    /// <summary>正式產物應通過同型 Create 驗證；修正前缺 icon／jump，必以實際故障型別轉紅。</summary>
    [Fact]
    public void Generated_view_passes_the_runtime_savedquery_create_boundary()
    {
        var generated = Generate();
        ValidateCreateBoundary(generated.LayoutXml);
        var grid = XElement.Parse(generated.LayoutXml);
        Assert.Equal("new_title", (string?)grid.Attribute("jump"));
        Assert.Equal("1", (string?)grid.Attribute("icon"));
        Assert.Equal(Generate(), generated);
    }

    /// <summary>
    /// 逐一刪除 runtime 必填值；即使匯出 XSD 允許缺省，Create 邊界仍須保留相同型別、
    /// 0x80040216 與固定訊息特徵，不能把非預期故障當成成功。
    /// </summary>
    [Theory]
    [InlineData("icon")]
    [InlineData("jump")]
    [InlineData("object")]
    [InlineData("select")]
    [InlineData("preview")]
    [InlineData("name")]
    public void Missing_runtime_attribute_reproduces_the_savedquery_fault(string attribute)
    {
        var grid = XElement.Parse(Generate().LayoutXml);
        grid.SetAttributeValue("icon", "1");
        grid.SetAttributeValue("jump", "new_title");
        grid.Attribute(attribute)!.Remove();
        var fault = Assert.Throws<FaultException<OrganizationServiceFault>>(
            () => ValidateCreateBoundary(grid.ToString(SaveOptions.DisableFormatting)));
        Assert.Equal(unchecked((int)0x80040216), fault.Detail.ErrorCode);
        Assert.StartsWith("Invalid layout xml found.", fault.Detail.Message, StringComparison.Ordinal);
    }

    /// <summary>建立案例私有的純值 schema，不持有 SDK、組織快照或可跨案例保留的集合。</summary>
    private static GeneratedView Generate()
    {
        var view = new SchemaView
        {
            Entity = "new_example", Name = "Synthetic view", Columns = new[] { "new_title" }
        };
        var schema = new SchemaDefinition
        {
            Entities = new[]
            {
                new SchemaEntity
                {
                    LogicalName = "new_example", PrimaryName = "new_title",
                    Fields = new[] { new SchemaField { LogicalName = "new_title", Type = "string" } }
                }
            },
            Views = new[] { view }
        };
        return ViewDefinitions.Generate(schema, view, primaryId: "new_exampleid", objectTypeCode: 10001);
    }

    /// <summary>
    /// 合成實機 Create 的純驗證邊界；保留 XmlSchemaValidationException 到 SDK fault
    /// 的失敗特徵，沒有寫入或假造成功 ID。所有 reader 於離開本方法確定關閉。
    /// </summary>
    private static void ValidateCreateBoundary(string layout)
    {
        using var schemaText = new StringReader(RuntimeSchema);
        using var schemaReader = XmlReader.Create(schemaText, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null
        });
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, ValidationType = ValidationType.Schema,
            MaxCharactersInDocument = 64 * 1024
        };
        settings.Schemas.Add(null, schemaReader);
        using var input = new StringReader(layout);
        using var reader = XmlReader.Create(input, settings);
        try
        {
            while (reader.Read()) { }
        }
        catch (XmlSchemaValidationException)
        {
            throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault
            {
                ErrorCode = unchecked((int)0x80040216), Message = "Invalid layout xml found."
            });
        }
    }
}
