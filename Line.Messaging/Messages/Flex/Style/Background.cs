/*
 * 檔案整體說明
 *
 * 目的與定位：Flex Box 線性漸層的傳輸資料，無資源與全域狀態；各訊息擁有自己的模型，建構後不要跨請求共用可變內容。
 *
 * 主要功能與流程：
 * 本檔宣告 Background。
 * - LINE 的背景種類；目前只支援 linearGradient，不是 BlockStyle 的背景色。
 * - 選用中間色位置百分比，例如 25%；此資料不改變 Box 內元件順序。
 * 資料／選項欄位包含 Background、Type、Angle、StartColor、CenterColor、EndColor、CenterPosition，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging.Tests/LineSdkFoundationTests.cs、Line.Messaging/Messages/Flex/Component/BoxComponent.cs、
 * Line.Messaging/Messages/Flex/Component/ButtonComponent.cs、
 * Line.Messaging/Messages/Flex/Component/ImageComponent.cs、
 * Line.Messaging/Messages/Flex/Extensions/BubbleContainerEntensions.cs 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging;

/// <summary>Flex Box 線性漸層的傳輸資料，無資源與全域狀態；各訊息擁有自己的模型，建構後不要跨請求共用可變內容。</summary>
public sealed class Background
{
    /// <summary>LINE 的背景種類；目前只支援 linearGradient，不是 BlockStyle 的背景色。</summary>
    public string Type => "linearGradient";
    /// <summary>漸層方向，採 LINE 格式，例如 90deg。</summary>
    public string Angle { get; set; }
    /// <summary>起點十六進位色碼，例如 #FF0000。</summary>
    public string StartColor { get; set; }
    /// <summary>選用中間點色碼；未指定時由序列化設定省略。</summary>
    public string CenterColor { get; set; }
    /// <summary>終點十六進位色碼，例如 #0000FF。</summary>
    public string EndColor { get; set; }
    /// <summary>選用中間色位置百分比，例如 25%；此資料不改變 Box 內元件順序。</summary>
    public string CenterPosition { get; set; }
}
