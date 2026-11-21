/*
檔案整體說明
目的與定位：驗證 Deployment 對停用舊流程 XAML 的離線結構脫敏投影契約；本測試不連線 D365，也不把合成資料當作平台實測。
主要流程：以含固定值、主要記錄欄位繫結、輸出參數欄位、活動型別及流程識別的合成 XAML，呼叫正式投影器，檢查只有輸入參數會產生繫結分類，輸出參數不會冒充輸入 shape，且投影不含來源值、流程名稱或識別碼。
依賴與邊界：輸入為測試內合成 XML 字串，輸出由待測產品投影器產生；本檔不讀取正式組織、秘密、User Secrets、真實流程或外部檔案。
資源與隔離：投影器須拒絕無效 XML、缺少或多個自訂活動參照及超出限制的輸入；測試資料為方法區域短生命週期字串，無 SDK、連線、背景工作或需額外 Dispose 的資源。
驗證方式：斷言投影保留可用於樣板比對的命名空間與 binding 節點形狀，同時移除業務值、名稱及識別；離線結果不能取代 goodshepherdback 唯讀取樣或 9.1 實機驗收。
*/
using System;
using System.IO;
using SpeechMessage.Dynamics.Deployment.Live;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>保護自訂流程 XAML 投影只輸出結構而不保留流程或業務內容的契約。</summary>
public sealed class WorkflowActivityShapeProjectorTests
{
    private const string Source = """
        <Activity x:Class="Legacy.Workflow.SecretName" xmlns="urn:test:workflow" xmlns:x="urn:test:xaml" Id="workflow-secret-id">
          <Sequence DisplayName="secret process">
            <ActivityReference AssemblyQualifiedName="SpeechMessage.Dynamics.Workflows.QrCodes.MeetingScanCountActivity, SpeechMessage.Dynamics.Workflows, Version=1.0.0.0">
              <ActivityReference.Arguments>
                <InArgument x:TypeArguments="x:String" x:Key="InputParameter">business-secret-value</InArgument>
                <InArgument x:TypeArguments="x:String" x:Key="RecordId">[record.new_secretfield]</InArgument>
              </ActivityReference.Arguments>
            </ActivityReference>
          </Sequence>
        </Activity>
        """;

    /// <summary>投影保留 ActivityReference 與引數樹形狀，固定值和欄位內容均不得洩漏。</summary>
    [Fact]
    public void Project_RetainsActivityAndBindingShape_WithoutSourceValues()
    {
        var projection = WorkflowActivityShapeProjector.Project(Source);

        Assert.StartsWith("<Activity ", projection, StringComparison.Ordinal);
        Assert.Contains("Sequence", projection, StringComparison.Ordinal);
        Assert.Contains("ActivityReference", projection, StringComparison.Ordinal);
        Assert.Contains("ActivityReference.Arguments", projection, StringComparison.Ordinal);
        Assert.Contains("InArgument", projection, StringComparison.Ordinal);
        Assert.Contains("TypeArguments", projection, StringComparison.Ordinal);
        Assert.Contains("InputParameter", projection, StringComparison.Ordinal);
        Assert.Contains("AssemblyQualifiedName=\"[ACTIVITY_TYPE], [ASSEMBLY], Version=[VERSION], Culture=[CULTURE], PublicKeyToken=[TOKEN]\"", projection, StringComparison.Ordinal);
        Assert.Contains("[FIXED_VALUE]", projection, StringComparison.Ordinal);
        Assert.Contains("[RECORD_FIELD]", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("business-secret-value", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("new_secretfield", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("secret process", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("workflow-secret-id", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("Legacy.Workflow.SecretName", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("MeetingScanCountActivity", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("SpeechMessage.Dynamics.Workflows", projection, StringComparison.Ordinal);
    }

    /// <summary>Value 屬性形式的 XAML literal 也需分類為固定輸入，且不得將屬性原值帶入投影。</summary>
    [Fact]
    public void Project_FixedValueAttribute_IsClassifiedAndRedacted()
    {
        const string source = """
            <Activity xmlns:x="urn:test:xaml">
              <ActivityReference AssemblyQualifiedName="Old.Activity, Old.Assembly">
                <ActivityReference.Arguments>
                  <InArgument x:TypeArguments="x:String" x:Key="InputParameter" Value="credential-shaped-sensitive-value" />
                  <InArgument x:TypeArguments="x:String" x:Key="RecordId" Value="[record.accountid]" />
                </ActivityReference.Arguments>
              </ActivityReference>
            </Activity>
            """;

        var projection = WorkflowActivityShapeProjector.Project(source);

        Assert.Contains("[FIXED_VALUE]", projection, StringComparison.Ordinal);
        Assert.Contains("[RECORD_FIELD]", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("credential-shaped-sensitive-value", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("Value=\"[REDACTED]\"", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("Value=\"[record.accountid]\"", projection, StringComparison.Ordinal);
    }

    /// <summary>輸入引數子樹內巢狀節點的 Value 仍須分類並遮蔽，避免漏掉序列化 literal。</summary>
    [Fact]
    public void Project_NestedInputValueAttribute_IsClassifiedAndRedacted()
    {
        const string source = """
            <Activity xmlns:x="urn:test:xaml">
              <ActivityReference AssemblyQualifiedName="Old.Activity, Old.Assembly">
                <ActivityReference.Arguments>
                  <InArgument x:TypeArguments="x:String" x:Key="InputParameter">
                    <Literal Value="sensitive-literal-value" />
                  </InArgument>
                  <InArgument x:TypeArguments="x:String" x:Key="RecordId">
                    <Literal Value="[record.sensitivefield]" />
                  </InArgument>
                </ActivityReference.Arguments>
              </ActivityReference>
            </Activity>
            """;

        var projection = WorkflowActivityShapeProjector.Project(source);

        Assert.Contains("Value=\"[FIXED_VALUE]\"", projection, StringComparison.Ordinal);
        Assert.Contains("Value=\"[RECORD_FIELD]\"", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-literal-value", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitivefield", projection, StringComparison.Ordinal);
    }

    /// <summary>只有輸入參數能證明樣板輸入繫結；輸出欄位值不得誤充主要記錄輸入。</summary>
    [Fact]
    public void Project_OutputArgumentRecordField_IsNotClassifiedAsInputBinding()
    {
        const string source = """
            <Activity xmlns:x="urn:test:xaml">
              <ActivityReference AssemblyQualifiedName="Old.Activity, Old.Assembly">
                <ActivityReference.Arguments>
                  <InArgument x:TypeArguments="x:String" x:Key="InputParameter" Value="fixed-input" />
                  <OutArgument x:TypeArguments="x:Int32" x:Key="OutputResult">[record.new_secretfield]</OutArgument>
                </ActivityReference.Arguments>
              </ActivityReference>
            </Activity>
            """;

        var projection = WorkflowActivityShapeProjector.Project(source);

        Assert.Contains("[FIXED_VALUE]", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("[RECORD_FIELD]", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("new_secretfield", projection, StringComparison.Ordinal);
    }

    /// <summary>資料量超限時必須在解析前拒絕，避免不受控配置與保留原始 XAML。</summary>
    [Fact]
    public void Project_OversizedInput_IsRejected()
    {
        var oversized = new string('x', WorkflowActivityShapeProjector.MaximumInputCharacters + 1);

        Assert.Throws<InvalidDataException>(() => WorkflowActivityShapeProjector.Project(oversized));
    }

    /// <summary>投影只接受恰好一個自訂活動參照，避免把未知副作用混入新樣板。</summary>
    [Fact]
    public void Project_MultipleActivityReferences_AreRejected()
    {
        var source = Source.Replace("</Sequence>",
            "<ActivityReference AssemblyQualifiedName=\"Other.Activity, Other.Assembly\" /></Sequence>",
            StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => WorkflowActivityShapeProjector.Project(source));
    }

    /// <summary>DTD 外部實體不得解析，固定錯誤也不可回顯攻擊者提供的內容。</summary>
    [Fact]
    public void Project_DtdWithExternalEntity_IsRejectedWithoutEcho()
    {
        const string source = "<!DOCTYPE x [<!ENTITY secret 'sensitive-marker'>]><x><ActivityReference>&secret;</ActivityReference></x>";

        var failure = Assert.Throws<InvalidDataException>(() => WorkflowActivityShapeProjector.Project(source));

        Assert.DoesNotContain("sensitive-marker", failure.Message, StringComparison.Ordinal);
    }
}
