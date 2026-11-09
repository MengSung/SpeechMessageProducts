/*
檔案整體說明
目的與定位：集中建立 GoodShepherd 產品的正式關鍵字 catalog，將已驗收的 P3-01
routing.json 對應到 GoodShepherdText.resx 的 Keyword.{意圖} 資源，供 Webhook
文字處理器與 Host DI 共用。

主要功能與流程：本檔從 I18nCheck 產生的 GoodShepherdTextData 建立 SystemTextTable，
再一次指定每個意圖的比對模式、穩定優先序及文化正規化策略。zh-TW／qps-ploc
保留舊路由的字面比較，以逐案符合 731 筆黃金；en 使用通用 NFKC、Unicode 空白
與 Invariant 大小寫策略，提供核定的六個英文 alias。建立完成後只回傳不可變
IKeywordCatalog，不保存 request、actor、tenant、credential 或呼叫輸入。

使用邊界與維護：本檔不讀檔、不連線、不建立背景工作、timer、queue 或 disposable
資源；固定資源由 DI 以 singleton 擁有，clock-only 相容入口則由呼叫端擁有回傳的
catalog。變更 alias 只能修改 resx 並以正式 I18nCheck 重生 generated C#；變更
模式或優先序須同步 P3-01 黃金逐案與雙目標測試，不能在 handler 或測試複製規則。
*/
using System;
using System.Collections.Generic;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Resources;

namespace SpeechMessageProducts.GoodShepherd.Core.Messaging;

/// <summary>
/// GoodShepherd 的唯一關鍵字 catalog factory；只組合固定生成資源與產品核定策略。
/// 回傳的 catalog 不保存任何 request、使用者、教會或認證資料。
/// </summary>
public static class GoodShepherdKeywordCatalog
{
    /// <summary>固定產品 catalog 使用的文化順序；只供本 factory 傳入建構器，不保存呼叫輸入。</summary>
    private static readonly IReadOnlyList<string> Cultures =
        Array.AsReadOnly(new[] { "zh-TW", "en", "qps-ploc" });

    /// <summary>
    /// 建立一次可長期唯讀共用的產品關鍵字 catalog。
    /// 產品 Host 應將結果註冊為 singleton；相容測試可自行擁有並在案例結束後丟棄
    /// 參照，因 catalog 本身沒有需要 Dispose 的資源。
    /// </summary>
    /// <returns>已驗證且不可變的 GoodShepherd 關鍵字 catalog。</returns>
    public static IKeywordCatalog Create()
    {
        var system = SystemTextTable.FromRows(GoodShepherdTextData.Cultures, GoodShepherdTextData.Rows);
        var modes = new Dictionary<string, KeywordMatchMode>(StringComparer.Ordinal)
        {
            ["Keyword.Approval"] = KeywordMatchMode.Prefix,
            ["Keyword.ClassAttendance"] = KeywordMatchMode.Contains,
            ["Keyword.Devotion"] = KeywordMatchMode.Prefix,
            ["Keyword.EnrollQuery"] = KeywordMatchMode.Prefix,
            ["Keyword.Enrollment"] = KeywordMatchMode.Prefix,
            ["Keyword.HR"] = KeywordMatchMode.Prefix,
            ["Keyword.Homework"] = KeywordMatchMode.Contains,
            ["Keyword.Journal"] = KeywordMatchMode.Contains,
            ["Keyword.NotifyLink"] = KeywordMatchMode.Exact,
            ["Keyword.PastorReply"] = KeywordMatchMode.Contains,
            ["Keyword.PrayerRequest"] = KeywordMatchMode.Prefix,
            ["Keyword.QrCode"] = KeywordMatchMode.Prefix,
            ["Keyword.QueryEnrollment"] = KeywordMatchMode.Prefix,
            ["Keyword.Registration"] = KeywordMatchMode.Prefix,
            ["Keyword.Rpg"] = KeywordMatchMode.Prefix,
            ["Keyword.SmallGroupAttendance"] = KeywordMatchMode.Contains,
            ["Keyword.StaffReport"] = KeywordMatchMode.Prefix,
            ["Keyword.SundayAttendance"] = KeywordMatchMode.Prefix,
            ["Keyword.Testimony"] = KeywordMatchMode.Prefix,
            ["Keyword.UserReply"] = KeywordMatchMode.Prefix
        };
        var priority = new[]
        {
            new TextKey("Keyword.Help"),
            new TextKey("Keyword.SignIn"),
            new TextKey("Keyword.RollCall"),
            new TextKey("Keyword.Equipment"),
            new TextKey("Keyword.QueryUserId"),
            new TextKey("Keyword.NotifyLink"),
            new TextKey("Keyword.Approval"),
            new TextKey("Keyword.Registration"),
            new TextKey("Keyword.UserReply"),
            new TextKey("Keyword.Testimony"),
            new TextKey("Keyword.EnrollQuery"),
            new TextKey("Keyword.Prayer247"),
            new TextKey("Keyword.QueryEnrollment"),
            new TextKey("Keyword.Devotion"),
            new TextKey("Keyword.PrayerRequest"),
            new TextKey("Keyword.HR"),
            new TextKey("Keyword.Journal"),
            new TextKey("Keyword.QrCode"),
            new TextKey("Keyword.Rpg"),
            new TextKey("Keyword.Enrollment"),
            new TextKey("Keyword.SundayAttendance"),
            new TextKey("Keyword.SmallGroupAttendance"),
            new TextKey("Keyword.StaffMeeting"),
            new TextKey("Keyword.ClassAttendance"),
            new TextKey("Keyword.StaffReport"),
            new TextKey("Keyword.PastorReply"),
            new TextKey("Keyword.Homework")
        };
        var normalizationModes = new Dictionary<string, KeywordNormalizationMode>(StringComparer.OrdinalIgnoreCase)
        {
            ["zh-TW"] = KeywordNormalizationMode.LegacyTrimOrdinal,
            ["qps-ploc"] = KeywordNormalizationMode.LegacyTrimOrdinal
        };
        return new KeywordCatalog(
            system,
            Cultures,
            modes,
            priority,
            normalizationModes);
    }
}
