/*
檔案整體說明
目的與定位：驗證平台／產品層 views 定義只能引用同組織已載入 schema 的實體與欄位，且輸出穩定
FetchXml、LayoutXml 及公開檢視 QueryType。
主要流程：以每案例獨立的暫存 schema repo 載入分層設定，檢查允許欄位生成、未知欄位拒絕、
同實體第二個預設公用檢視拒絕，以及特殊檢視不能以 create 路徑新建。
依賴與隔離：只呼叫純 JSON parser／view generator；無 D365 SDK、User Secrets、網路、租戶列資料或
跨案例快取。Fixture 僅建立隨機暫存目錄，建立失敗與測試結束都由唯一 owner 清除。
決定性斷言：FetchXml 與 LayoutXml 欄位集合完全受 schema 白名單限制；非法欄位、預設檢視衝突及
QueryType 非 0 的建立請求均 fail closed，不能產生部分定義。
驗證限制：合成 schema 只證明離線產生器，不代表任何組織的 savedquery 已建立、啟用或發布。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SpeechMessage.Dynamics.Deployment.Schema;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>以 operation-local schema fixture 驗證檢視定義白名單與特殊檢視建立限制。</summary>
public sealed class ViewDefinitionTests
{
    /// <summary>FetchXml 與 LayoutXml 只能包含指定 organization schema 中已明確定義的欄位。</summary>
    [Fact]
    public void Views_GeneratedFetchXml_OnlyAllowListedAttributes()
    {
        using var fixture = ViewFixture.Create(
            """[{"entity":"account","name":"Synthetic Accounts","columns":["new_tenantcode"],"filter":[],"order":[],"isDefault":true}]""");
        var schema = SchemaFiles.LoadForOrganization(fixture.Root, "construction");
        var generated = ViewDefinitions.Generate(schema, Assert.Single(schema.Views));

        Assert.Equal(0, generated.QueryType);
        Assert.Contains("name=\"new_tenantcode\"", generated.FetchXml, StringComparison.Ordinal);
        Assert.Contains("name=\"new_tenantcode\"", generated.LayoutXml, StringComparison.Ordinal);
        Assert.DoesNotContain("new_unlisted", generated.FetchXml, StringComparison.Ordinal);
    }

    /// <summary>檢視表引用存在於資料庫但不在 schema 清單的欄位時，整個候選拒絕且不回傳部分 XML。</summary>
    [Fact]
    public void Views_UnknownAttribute_Rejected()
    {
        using var fixture = ViewFixture.Create(
            """[{"entity":"account","name":"Synthetic Accounts","columns":["new_unlisted"],"filter":[],"order":[],"isDefault":true}]""");

        Assert.Throws<InvalidDataException>(() =>
        {
            var schema = SchemaFiles.LoadForOrganization(fixture.Root, "construction");
            _ = ViewDefinitions.Generate(schema, Assert.Single(schema.Views));
        });
    }

    /// <summary>同一實體不能同時有兩個預設公用檢視；不能以清單順序或最後寫入覆蓋既有選擇。</summary>
    [Fact]
    public void Views_SecondDefaultPublicView_Refused()
    {
        using var fixture = ViewFixture.Create(
            """
            [
              {"entity":"account","name":"Synthetic Accounts A","columns":["new_tenantcode"],"filter":[],"order":[],"isDefault":true},
              {"entity":"account","name":"Synthetic Accounts B","columns":["new_tenantcode"],"filter":[],"order":[],"isDefault":true}
            ]
            """);

        Assert.Throws<InvalidDataException>(() =>
        {
            var schema = SchemaFiles.LoadForOrganization(fixture.Root, "construction");
            _ = ViewDefinitions.ValidateDefaults(schema.Views);
        });
    }

    /// <summary>建立新元件時不能指定 Quick Find 等特殊 QueryType；特殊檢視只允許以既有資料庫 ID 更新。</summary>
    [Fact]
    public void Views_SpecialViewCreate_Refused()
    {
        using var fixture = ViewFixture.Create(
            """[{"entity":"account","name":"Synthetic Quick Find","columns":["new_tenantcode"],"filter":[],"order":[],"isDefault":false}]""");
        var schema = SchemaFiles.LoadForOrganization(fixture.Root, "construction");
        var view = Assert.Single(schema.Views);

        Assert.Throws<InvalidDataException>(() => ViewDefinitions.Generate(schema, view, queryType: 4));
    }

    /// <summary>建立完整隔離 repo；任何 JSON 寫入故障都會清除只屬於本案例的隨機目錄。</summary>
    private sealed class ViewFixture : IDisposable
    {
        /// <summary>本案例唯一的 schema repository 根目錄。</summary>
        internal string Root { get; }

        /// <summary>建立固定 root 結構，路徑不含使用者或組織輸入。</summary>
        private ViewFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "3p-view-schema-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "d365", "schema", "platform"));
            File.WriteAllText(Path.Combine(Root, "SpeechMessageProducts.sln"), "", new UTF8Encoding(false));
        }

        /// <summary>寫入單一平台層 account schema 及 views JSON，供指定組織 loader 完整解析。</summary>
        internal static ViewFixture Create(string views)
        {
            var fixture = new ViewFixture();
            try
            {
                var schemaRoot = Path.Combine(fixture.Root, "d365", "schema");
                Write(Path.Combine(schemaRoot, "layers.json"),
                    "{\"schemaVersion\":1,\"organizations\":{\"construction\":[\"platform\"]},\"views\":{\"platform\":" +
                    views + "}}");
                Write(Path.Combine(schemaRoot, "platform", "00-core.schema.json"),
                    """{"schemaVersion":1,"activePhase":1,"solution":"SpeechMessageCore","publisher":"SpeechMessagePublisher","prefix":"new","entities":[{"logicalName":"account","displayName":"Synthetic account","pluralName":"Synthetic accounts","isNew":false,"audit":false,"fields":[{"logicalName":"new_tenantcode","displayName":"Tenant code","type":"string","phase":1,"maxLength":64}],"keys":[]}]}""");
                return fixture;
            }
            catch
            {
                fixture.Dispose();
                throw;
            }
        }

        /// <summary>以 UTF-8 無 BOM 與 CRLF 寫入合成輸入，不共用 repo 檔或其他案例輸出。</summary>
        private static void Write(string path, string value)
        {
            var text = value.Replace("\r\n", "\n", StringComparison.Ordinal)
                .TrimEnd('\n').Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n";
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        /// <summary>只刪除本 fixture 隨機建立的 repository root，確保失敗及成功路徑都釋放檔案。</summary>
        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
