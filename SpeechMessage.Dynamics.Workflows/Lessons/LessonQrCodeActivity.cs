/*
 * 檔案整體說明
 * 目的與定位：P5-07舊QrCodeWorkflow.QrCodeWorkflow的薄活動對應，保留可供既有
 * 流程遷移的同名同型參數；不能以Plugin取代活動而遺失原本多產生問卷的入口。
 * 主要流程：平台ExecuteWorkflow／Create／Update的主要課程交Core驗正式來源歸屬，
 * 建立包含15堂簽到／簽退、報名及問卷的單筆圖片組待辦，不在沙箱畫圖或寫UNC。
 * 舊InputParameter未消費、OutputResult未設定，新版保留相同行為與參數名稱／型別，顯示標籤使用固定 ASCII，
 * 只另有共用ExecutionMode；舊兩深度分支都執行，本活動不增加深度限制。
 * 使用邊界與維護：只保存WF引數運算式，不保存SDK、context、已評估引數或Entity。
 * SDK由平台借用；當次store與Shadow recorder由各自owner同步finally／using排空，
 * 無Session、cache、背景Task或timer。失敗原樣交共用基底，不讀token、不連外。
 * 離線真活動驗完整意圖及問卷差異，不宣稱實機影子驗收或已發布圖片。
 */
using System.Activities;
using Microsoft.Xrm.Sdk.Workflow;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Workflows.Infrastructure;
using SpeechMessageProducts.GoodShepherd.Core.Lessons;

namespace SpeechMessage.Dynamics.Workflows.Lessons;

/// <summary>舊課程QR活動的參數相容入口，與Plugin共用規則並保留問卷差異。</summary>
public sealed class LessonQrCodeActivity : ActivityShellBase
{
    /// <summary>舊活動必要字串參數；保留運算式宣告，但不消費或拿來指定教會。</summary>
    [RequiredArgument]
    [Input("InputParameter")]
    public InArgument<string> InputParameter { get; set; } = new();
    /// <summary>舊活動未設定的整數輸出，維持WF預設值，不捏造成功碼。</summary>
    [Output("OutputResult")]
    public OutArgument<int> OutputResult { get; set; } = new();

    /// <summary>只讀平台來源識別，問卷旗標固定為活動原本的true，不受caller字串操控。</summary>
    internal override void ExecuteRule(ActivityInput input, PluginServices services)
    {
        if (input.PrimaryEntityName != "new_disciple_lessons" || input.MessageName is not ("ExecuteWorkflow" or "Create" or "Update")) return;
        _ = LessonLinkService.Enqueue(services.Organization, input.PrimaryEntityId, includePoll: true);
    }
}
