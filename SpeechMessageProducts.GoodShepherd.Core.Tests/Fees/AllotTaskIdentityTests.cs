/*
 * 檔案整體說明
 *
 * 目的與定位：以雙目標離線固定案例驗證 P5-11 撥款待辦的每日 sourceId 與 payload 契約。
 * 測試只使用純值，不連線 D365、永豐、LINE 或讀取任何秘密；每個案例的輸入與斷言都在
 * 方法範圍內建立，沒有共享可變 owner、背景工作或資源。
 *
 * 主要功能與流程：建立固定 fee 與教會曆日，呼叫 sourceId／payload 純規則，再以
 * 確定性斷言驗證同日、跨日、格式及錯誤輸入，不建立任何 SDK 或外部資源。
 *
 * 主要保護契約：同一 fee 同一教會日重送得到相同 GUID；跨日得到不同 GUID；queryDate
 * 必須是無時區的有效曆日，payload 必須精確含 feeId 與 yyyyMMdd。錯誤輸入必須在 SDK
 * 前被拒絕，讓跨程序唯一鍵繼續由資料庫負責。
 *
 * 使用邊界與維護：測試只持有案例區域純值，不建立連線、背景工作或可處置資源；
 * 雙目標執行結果只能證明離線規則契約，不代表 D365 或付款供應商實機結果。
 */
using System;
using System.Linq;
using SpeechMessageProducts.GoodShepherd.Core.Fees;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Fees;

/// <summary>驗證收費待辦的每日穩定身分、日期格式及封閉 payload。</summary>
public sealed class AllotTaskIdentityTests
{
    private static readonly Guid FeeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    /// <summary>相同 fee 與當地日重送必須得到相同 sourceId，隔日必須得到不同 sourceId。</summary>
    [Fact]
    public void SameFeeSameDay_IsIdempotent_AndNextDayDiffers()
    {
        var day = new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Unspecified);
        Assert.Equal(AllotTaskIdentity.SourceId(FeeId, day), AllotTaskIdentity.SourceId(FeeId, day));
        Assert.NotEqual(AllotTaskIdentity.SourceId(FeeId, day), AllotTaskIdentity.SourceId(FeeId, day.AddDays(1)));
    }

    /// <summary>payload 只能包含 feeId 與 queryDate，且 queryDate 以 yyyyMMdd 整數保存。</summary>
    [Fact]
    public void Payload_ContainsExactFeeAndQueryDate()
    {
        var day = new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Unspecified);
        var payload = AllotTaskIdentity.Payload(FeeId, day);
        Assert.Equal(2, payload.Count);
        Assert.Equal(FeeId, payload["feeId"]);
        Assert.Equal(20261113, payload["queryDate"]);
        Assert.Equal(day, AllotTaskIdentity.FromQueryDate(20261113));
    }

    /// <summary>帶時間、Local 或非法日期的輸入一律 fail closed，不建立模糊的待辦識別。</summary>
    [Fact]
    public void InvalidDate_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => AllotTaskIdentity.SourceId(FeeId, new DateTime(2026, 11, 13, 1, 0, 0, DateTimeKind.Unspecified)));
        Assert.Throws<ArgumentException>(() => AllotTaskIdentity.SourceId(FeeId, new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Local)));
        Assert.Throws<ArgumentOutOfRangeException>(() => AllotTaskIdentity.FromQueryDate(20260230));
    }
}
