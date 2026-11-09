/*
檔案整體說明

目的與定位：提供 GoodShepherd Webhook 使用的合併文字 catalog，讓產品回覆資源與
共用 sender／letter 資源都遵守同一套 TextContext、教會覆寫及格式化契約。

主要功能與流程：建立固定的 GoodShepherd 資源表與共用 SharedText 表；每次 GetText
先依穩定 key 判斷產品資源，再交給對應的 TextCatalog 使用本次已授權 scope、會員語系
及 bounded 具名參數格式化。兩套表都借用相同的不可變覆寫 snapshot provider，不把
使用者輸入、教會資料、request、token 或查詢結果保存到 catalog。

使用邊界與維護：本類是無狀態、無 I/O、無 timer、無背景工作及無 request cache 的
singleton 候選；覆寫 provider 由 Host 擁有並負責清理。產品資源由 I18nCheck 產生，
不得手改 GoodShepherdText.g.cs。缺 key、參數不符或 scope 不符時原例外向 dispatcher
及 Hosting 共用失敗入口傳遞，不以另一教會或固定語系補值。
*/
using System;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Resources;

namespace SpeechMessageProducts.GoodShepherd.Core.Messaging;

/// <summary>合併產品與共用固定資源的無狀態文字 catalog。</summary>
public sealed class GoodShepherdTextCatalog : ITextCatalog
{
    private readonly SystemTextTable productSystem;
    private readonly TextCatalog product;
    private readonly TextCatalog shared;

    /// <summary>
    /// 建立只借用覆寫 provider 的 catalog；不接管 provider 的生命週期，也不保存任何 request 值。
    /// </summary>
    /// <param name="overrides">Host 擁有的已授權 scope snapshot provider。</param>
    public GoodShepherdTextCatalog(IChurchTextOverrideSnapshotProvider overrides)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(overrides);
#else
        if (overrides is null) throw new ArgumentNullException(nameof(overrides));
#endif
        productSystem = SystemTextTable.FromRows(GoodShepherdTextData.Cultures, GoodShepherdTextData.Rows);
        product = new TextCatalog(productSystem, overrides);
        shared = new TextCatalog(new SystemTextTable(), overrides);
    }

    /// <summary>依穩定 key 選擇產品或共用資源，再以本次 scope 格式化具名參數。</summary>
    /// <param name="context">已由 Host 授權的組織／教會／產品與語系範圍。</param>
    /// <param name="key">固定資源鍵。</param>
    /// <param name="args">本次呼叫的 bounded 具名參數。</param>
    /// <returns>與呼叫端脫離的格式化文字。</returns>
    public string GetText(TextContext context, TextKey key, params TextArg[] args)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(context);
#else
        if (context is null) throw new ArgumentNullException(nameof(context));
#endif
        if (productSystem.TryGetOriginalText(context.TextCulture, key, out _) ||
            productSystem.TryGetOriginalText("zh-TW", key, out _))
            return product.GetText(context, key, args);
        return shared.GetText(context, key, args);
    }
}
