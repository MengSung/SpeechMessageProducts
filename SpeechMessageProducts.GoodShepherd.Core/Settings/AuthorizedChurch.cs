/*
 * 檔案整體說明
 *
 * 目的與定位：封裝 Host 已完成登入者權限判定後可操作的單一教會範圍，供 GoodShepherd.Core 設定服務使用。
 * 主要流程：受信任的應用層在驗證登入者與教會關聯後建立此不可變純值，再連同完整 TextContext 傳給設定服務。
 * 使用邊界與維護：建構值本身不執行授權；不得把 HTTP 欄位或未驗證識別直接包裝後當成權限。此型別不持有 request、
 * Session、連線、快取或可變狀態，生命週期只限呼叫端保留的純值參考。
 * 失敗與驗證：空識別立即拒絕；設定服務仍會比對此 ID 與 TextContext 及每筆資料的教會外鍵。離線核心測試驗證不一致時零寫入。
 */
using System;

namespace SpeechMessageProducts.GoodShepherd.Core.Settings;

/// <summary>封裝應用層已授權的教會識別；建立本型別不會自行授予權限。</summary>
public sealed class AuthorizedChurch
{
    /// <summary>由受信任應用層確認的正式 account 主鍵。</summary>
    public Guid Id { get; }

    /// <summary>建立不可變教會範圍；呼叫端必須先完成登入者權限及教會關聯判定。</summary>
    /// <param name="id">經伺服器授權流程確認的非空 account 主鍵，不可直接使用 request 值。</param>
    public AuthorizedChurch(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("An authorized church identity is required.", nameof(id));
        Id = id;
    }
}
