/*
 * 檔案整體說明
 * 目的與定位：F8與產品組合根之間的短命SDK借用契約，讓共用引擎不反向參考ToolUtility或產品。
 * 主要功能與流程：產品adapter從已綁定組織的manager取得租約，公開service並轉送fault與Dispose；每次權限或稽核操作獨立取得，不把Actor當連線路由。
 * 使用邊界與維護：唯一擁有者為當次AccessDataverseExecutor，必須等待所有同步SDK工作結束再標記故障、歸還；連線池最終關閉仍屬Host。實作者不得保留請求、Session或使用者憑證，組合根須提供有界池與SDK傳輸逾時，不能共用有未完成工作之client。
 */
using Microsoft.Xrm.Sdk;

namespace SpeechMessage.Access;

/// <summary>單次借用的組織服務；實作者轉送既有受管池，不得建立未受管常駐client。</summary>
public interface IAccessDataverseLease : IDisposable
{
    /// <summary>只在目前租約內有效的SDK服務，不能由呼叫端保存至其他請求。</summary>
    IOrganizationService Service { get; }

    /// <summary>將故障或取消不確定的服務標記不可重用；仍須由同一owner確定Dispose。</summary>
    void MarkFaulted();
}
