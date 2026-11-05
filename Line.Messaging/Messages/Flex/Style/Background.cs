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
