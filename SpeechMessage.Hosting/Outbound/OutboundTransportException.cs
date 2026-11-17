/*
 * 檔案整體說明
 * 目的與定位：Host對外待辦已確認無HTTP回應的可重試傳輸故障，提供跨產品固定分類。
 * 主要功能與流程：adapter保留真原始故障於inner，worker依本型別排既有有界退避，
 * 不假造HTTP503／Timeout，不把任意HttpRequestException一律視為可重試。
 * 資源與隔離：只保留當次故障至worker結算或告警返回，不持有SDK、key或request；
 * 不建立Task／timer／cache，不輸出inner訊息，生命期由單筆工作owner控制。
 * 驗證方式：代送Host故障注入核對worker Pending／Transport、原inner及scope排空。
 */
using System;
namespace SpeechMessage.Hosting.Outbound;

/// <summary>只供已證明無回應的傳輸故障，worker保留真inner並採既有五次有界重試。</summary>
public sealed class OutboundTransportException : Exception
{
    /// <summary>固定安全訊息，不記錄供應商或credential；原錯誤仍交終端共用告警。</summary>
    /// <param name="original">已分類的真實故障，不是用來捏造成功或HTTP狀態的文字。</param>
    public OutboundTransportException(Exception original) : base("Outbound transport failed without a response.", original ?? throw new ArgumentNullException(nameof(original))) { }
}
