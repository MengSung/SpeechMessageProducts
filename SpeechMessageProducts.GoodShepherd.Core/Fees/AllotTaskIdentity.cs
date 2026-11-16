/*
 * 檔案整體說明
 *
 * 目的與定位：定義 P5-11 撥款查詢待辦的穩定來源識別與封閉 payload 契約，供
 * Plugin、Workflow、Host 與測試共用。輸入只包含正式收費單 ID 及教會當地的查詢日，
 * 不讀取 SDK、時鐘、Session、token 或任何可變狀態。
 *
 * 主要功能與流程：先驗證非空收費單 ID 及有效的曆日，再以固定 purpose、feeId 與
 * yyyyMMdd 查詢日呼叫 DeterministicGuid，建立跨 net462／net10 一致的 sourceId；
 * payload 固定只有 feeId 與 queryDate 兩欄。呼叫端仍須先由伺服器驗證教會歸屬，
 * 本檔的 GUID 不取代 Dataverse 唯一鍵或授權。
 *
 * 使用邊界與維護：本檔是純同步值規則，沒有連線、背景工作、快取、計時器或可處置
 * 資源；輸入與產出只活在單次操作。任何不合法日期或 ID 立即拒絕，避免建立無法
 * 重試或跨教會混用的待辦。雙目標固定向量測試驗證格式與穩定性。
 */
#nullable enable
using System;
using System.Collections.Generic;
using SpeechMessageProducts.GoodShepherd.Core.Rules;

namespace SpeechMessageProducts.GoodShepherd.Core.Fees;

/// <summary>以收費單正式 ID 與教會當地查詢日建立每日唯一的撥款待辦識別。</summary>
public static class AllotTaskIdentity
{
    private const string Purpose = "GoodShepherd.Fees.AllotQuery";

    /// <summary>由 feeId 與純曆日計算固定 sourceId；不依賴主機時區或當下時間。</summary>
    public static Guid SourceId(Guid feeId, DateTime queryDate)
    {
        Validate(feeId, queryDate);
        var date = queryDate.Date;
        return DeterministicGuid.Create(Purpose, feeId, date.Year, date.Month, date.Day);
    }

    /// <summary>建立精確包含 feeId 與 queryDate 的待辦 payload。</summary>
    public static IReadOnlyDictionary<string, object> Payload(Guid feeId, DateTime queryDate)
    {
        Validate(feeId, queryDate);
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["feeId"] = feeId,
            ["queryDate"] = ToQueryDate(queryDate)
        };
    }

    /// <summary>將純曆日轉成核定的 yyyyMMdd 整數欄位。</summary>
    public static int ToQueryDate(DateTime queryDate)
    {
        ValidateDate(queryDate);
        return checked(queryDate.Year * 10000 + queryDate.Month * 100 + queryDate.Day);
    }

    /// <summary>將 payload 的 yyyyMMdd 還原為未指定時區的純曆日。</summary>
    public static DateTime FromQueryDate(int queryDate)
    {
        var year = queryDate / 10000;
        var month = queryDate / 100 % 100;
        var day = queryDate % 100;
        try
        {
            var value = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);
#if NET8_0_OR_GREATER
            // 使用 .NET 8+ 的集中檢查 API，避免 CA1512；net462 沒有此 API，保留明確擲回。
            ArgumentOutOfRangeException.ThrowIfNotEqual(ToQueryDate(value), queryDate, nameof(queryDate));
#else
            if (ToQueryDate(value) != queryDate) throw new ArgumentOutOfRangeException(nameof(queryDate));
#endif
            return value;
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentOutOfRangeException(nameof(queryDate), "The allot query date is invalid.");
        }
    }

    /// <summary>驗證 ID 與日期不含空值、時間或不可表達的範圍。</summary>
    public static void Validate(Guid feeId, DateTime queryDate)
    {
        if (feeId == Guid.Empty) throw new ArgumentException("A fee identity is required.", nameof(feeId));
        ValidateDate(queryDate);
    }

    private static void ValidateDate(DateTime value)
    {
        if (value.Kind != DateTimeKind.Unspecified || value.TimeOfDay != TimeSpan.Zero)
            throw new ArgumentException("The allot query date must be an unspecified calendar date.", nameof(value));
        if (value.Year < 1 || value.Year > 9999)
            throw new ArgumentOutOfRangeException(nameof(value));
    }
}
