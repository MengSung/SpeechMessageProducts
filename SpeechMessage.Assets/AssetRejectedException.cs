/*
檔案整體說明
目的與定位：F7不可信圖片或容量滿載的預期拒絕型別，讓呼叫端區分正常輸入拒絕與必須交共用告警的I/O、權限或其他真正故障。
主要功能與流程：只攜帶實作指定的固定英文原因代碼，不複製原始解碼器訊息、上傳內容、租戶、路徑或個資；不作重試、不替呼叫端產生HTTP回應。
使用邊界與維護：此例外為單次操作純值，不保存來源、影像、inner Exception或資源owner；不能用它包裝任意故障掩蓋原錯，取消亦須保留OperationCanceledException。
*/
namespace SpeechMessage.Assets;

/// <summary>預期的圖片或容量拒絕；固定訊息不攜帶來源內容，也不取代真正故障的原始例外。</summary>
public sealed class AssetRejectedException : Exception
{
    /// <summary>只允許本模組以固定原因代碼建立，禁止從decoder或呼叫者原文組成訊息。</summary>
    internal AssetRejectedException(string code) : base("Public asset input rejected.") => Code = code;
    /// <summary>固定拒絕代碼；呼叫端可據此提供對應訊息，但不能把它當授權結果或重試保證。</summary>
    public string Code { get; }
}
