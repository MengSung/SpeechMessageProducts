/*
檔案整體說明
目的與定位：集中三產品 Channel 池的精確產品字串，讓純規則和 Plugin 不以顯示名稱、大小寫轉換或 caller 提示選擇產品。
主要功能與流程：提供三個固定字串與 ordinal 白名單檢查；這些值對應 new_product 字串欄位，不是 choice 數值，且不包含舊版好牧人的後備產品。
使用邊界與維護：純常數不保存組織、租戶、認證或可變狀態；真正權威來自 Plugin 執行組織與部署設定映射。本檔不查資料或持有資源，呼叫端必須另驗證授權；測試應拒絕近似拼字及未知產品。
*/
using System;
namespace LineMessagingProcessor.Core.ChannelPool;

/// <summary>三產品名額池的持久字串白名單；此常數不代表 caller 有權存取該產品。</summary>
public static class PoolProduct
{
    /// <summary>好牧人二代的精確產品值，不能與舊版 GoodShepherd 混用。</summary>
    public const string GoodShepherd2 = "GoodShepherd2";
    /// <summary>好管家產品池，只能由對應組織設定選取。</summary>
    public const string GoodButler = "GoodButler";
    /// <summary>好學會產品池，與其他兩池完全隔離。</summary>
    public const string GoodGuild = "GoodGuild";
    /// <summary>以 ordinal 驗證精確持久字串，不修剪、不忽略大小寫，也不回落到預設產品。</summary>
    public static bool IsSupported(string? product) =>
        string.Equals(product, GoodShepherd2, StringComparison.Ordinal) ||
        string.Equals(product, GoodButler, StringComparison.Ordinal) ||
        string.Equals(product, GoodGuild, StringComparison.Ordinal);
}
