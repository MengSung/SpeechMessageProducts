/*
檔案整體說明
目的與定位：P7流程活動精準改寫的離線安全契約，合成XAML同時包含有版本參照、CLR節點與同名正文。
主要流程：以既有ActivityProbe入口驗證共用改寫，只能改核定活動的型別與組件，不能改正文或其他活動。
信任邊界：不載入活動DLL、不執行XAML、不連D365；DTD、過深XML及未命中參照都必須fail closed。
資源生命週期：字串及XML reader只屬案例與單次函式；沒有測試外狀態或資源，斷言改寫與保留雙向成立。
*/
using System;
using System.IO;
using Xunit;
namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>真解析器的合成故障測試，保護活動引用與非引用內容的界線。</summary>
public sealed class WorkflowRepointerTests
{
    /// <summary>真XAML帶版本／簽章與不同assembly大小寫；只改屬性和CLR活動節點，正文及其他屬性保留。</summary>
    [Fact]
    public void Switch_WorkflowRepoint_RewritesOnlyActivityReference()
    {
        const string source = """
            <Activity xmlns:o="clr-namespace:Old;assembly=old.assembly">
              <o:Activity Other="Old.Activity, Old.Assembly" />
              <Node AssemblyQualifiedName="Old.Activity, OLD.Assembly, Version=1.0.0.0, Culture=neutral, PublicKeyToken=1234567890abcdef" />
              <Node AssemblyQualifiedName="Other.Activity, Other.Assembly" />
              <Text>Old.Activity, Old.Assembly</Text>
            </Activity>
            """;
        var result = ActivityProbe.RewriteOnlyActivityReference(source, "Old.Assembly", "Old.Activity", "New.Assembly", "New.Activity");
        Assert.Contains("clr-namespace:New;assembly=New.Assembly", result, StringComparison.Ordinal);
        Assert.Contains("AssemblyQualifiedName=\"New.Activity, New.Assembly\"", result, StringComparison.Ordinal);
        Assert.Contains("Other=\"Old.Activity, Old.Assembly\"", result, StringComparison.Ordinal);
        Assert.Contains("<Text>Old.Activity, Old.Assembly</Text>", result, StringComparison.Ordinal);
        Assert.Contains("Other.Activity, Other.Assembly", result, StringComparison.Ordinal);
    }

    /// <summary>XML超過128層時拒絕，而非繼續建構無界物件圖；不執行任何內嵌內容。</summary>
    [Fact]
    public void DeepWorkflow_IsRejected()
    {
        var source = string.Concat(System.Linq.Enumerable.Repeat("<x>", 130))
            + "<Node AssemblyQualifiedName=\"Old.Activity, Old.Assembly\"/>"
            + string.Concat(System.Linq.Enumerable.Repeat("</x>", 130));
        Assert.Throws<InvalidDataException>(() => ActivityProbe.RewriteOnlyActivityReference(source, "Old.Assembly", "Old.Activity", "New.Assembly", "New.Activity"));
    }
}
