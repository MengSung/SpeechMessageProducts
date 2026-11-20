/*
 * 檔案整體說明
 *
 * 目的與定位：集中定義 GoodShepherd 教會可修改的顯示詞彙鍵，避免設定頁、API 與資料服務各自維護白名單。
 * 主要流程：以固定、唯讀且採序數比較的集合提供 11 個 Role.* 鍵及 Fees.Notify.Template；所有查詢均為純記憶體操作。
 * 使用邊界與維護：清單只涵蓋顯示文字，不代表授權，也不接受 Keyword.*、Webhook.Route.* 或任意相似前綴。
 * 集合只隨程式版本存活，不保存教會輸入、request 或外部資源；新增鍵須同步核對產品資源、模板參數與契約測試。
 * 失敗與驗證：未知鍵回傳 false，不做前綴或大小寫寬鬆比對；核心測試鎖住精確 12 個鍵及拒絕案例。
 */
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SpeechMessageProducts.GoodShepherd.Core.Settings;

/// <summary>提供精確且唯讀的教會詞彙覆寫鍵清單。</summary>
public sealed class ChurchTermWhitelist
{
    private static readonly ReadOnlyCollection<string> AllowedKeys = Array.AsReadOnly(new[]
    {
        "Role.PastorCouple",
        "Role.ZoneShepherd",
        "Role.ZoneLeader",
        "Role.GroupLeader",
        "Role.AssistantGroupLeader",
        "Role.CoreWorker",
        "Role.Member",
        "Role.NotInGroup",
        "Role.Newcomer",
        "Role.OtherChurch",
        "Role.Closed",
        "Fees.Notify.Template"
    });
    private readonly ReadOnlyCollection<string> allowedKeys = AllowedKeys;

    /// <summary>取得固定詞彙鍵；集合不可由呼叫端修改。</summary>
    public IReadOnlyList<string> Keys => allowedKeys;

    /// <summary>以序數完全相同比較檢查單一鍵，不接受前綴、路由或關鍵字。</summary>
    /// <param name="key">待查穩定資源鍵。</param>
    /// <returns>只有精確列於清單時為 true。</returns>
    public bool Contains(string key)
    {
        if (key is null) return false;
        for (var index = 0; index < allowedKeys.Count; index++)
            if (string.Equals(allowedKeys[index], key, StringComparison.Ordinal)) return true;
        return false;
    }
}
