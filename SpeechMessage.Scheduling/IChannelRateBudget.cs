/*
檔案整體說明
目的與定位：提供三大產品各Channel建立及刪除宮格的獨立每小時速率契約，防止多程序競爭繞過供應商限制。
主要功能與流程：實作以完整產品、Channel、操作及UTC小時作SQL原子計數；richmenu-create與richmenu-delete各100次。工作內取得false時handler須立即返回，dispatcher依同一scoped延期狀態把工作延至下一小時。
使用邊界與維護：本契約不發送LINE、不保存token或使用者身分；Channel識別不是授權。只借用Host儲存與時鐘，SQL操作有界關閉；預算耗盡是正常延期，不消耗失敗重試或丟棄工作。
*/
namespace SpeechMessage.Scheduling;

/// <summary>有界持久化速率預算；每次true對應一次允許的宮格操作，外部副作用由handler擁有。</summary>
public interface IChannelRateBudget
{
    /// <summary>原子取得一個額度；false時本次工作立即停止操作並在下一UTC小時重排。</summary>
    Task<bool> TryAcquireAsync(string product, string channelKey, string operation, CancellationToken ct);
}
