/*
 * 檔案整體說明
 *
 * 目的與定位：提供 P5-11 Plugin 與 Workflow 共用的撥款查詢待辦入列邊界。
 * 本服務只從 new_fee → contact.parentcustomerid(account) 解析教會，再以該教會
 * 的時區取得查詢日；不接受活動輸入或 caller 指定的教會作為授權來源。
 *
 * 主要功能與流程：先讀取最小關聯欄位取得正式教會 ID，再由 FeeAllotRecords 完整
 * 驗證收費單、會友、教會、付款欄位與 row version；若付款日位於教會今日之後，
 * 依 P4-03 修正不建立待辦。否則用 AllotTaskIdentity 建立 feeId/queryDate payload，
 * 交給 OutboundTaskWriter 的資料庫替代鍵原子建立或讀回。
 *
 * 使用邊界與維護：SDK 與 store 只由呼叫端／本方法當次借用；不保存 Entity、教會、
 * 時鐘、Session、credential、快取或背景工作。任一關聯錯誤、欄位錯誤或不確定寫入
 * 原樣交回平台，不自行重試；成功回傳的是資料庫待辦 ID，不是 feeId 或 sourceId。
 */
#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Outbound;
using ToolUtilityNameSpace.Outbound;

namespace SpeechMessageProducts.GoodShepherd.Core.Fees;

/// <summary>驗證收費單教會範圍並建立每日撥款查詢待辦。</summary>
public static class FeeAllotTaskService
{
    private const string FeeEntityName = "new_fee";
    private const string ContactEntityName = "contact";
    private const string ChurchEntityName = "account";
    private const string FeeContactField = "new_contact_new_fee";
    private const string MemberChurchField = "parentcustomerid";

    /// <summary>
    /// 解析 fee 的正式教會、以教會今日決定是否入列，並原子建立一筆 AllotQuery。
    /// 未來付款日不建立待辦，回傳 null；同 fee 同教會日由資料庫替代鍵保持冪等。
    /// </summary>
    /// <param name="sdk">當次平台授權的同步服務；只借用，不由本類 Dispose。</param>
    /// <param name="feeId">平台主要 new_fee 正式 ID。</param>
    /// <param name="clock">不保存教會狀態的時鐘；時區由 fee 的 account 欄位取得。</param>
    /// <returns>真正資料庫待辦 ID；未來付款日不入列時為 null。</returns>
    public static Guid? Enqueue(IOrganizationService sdk, Guid feeId, IChurchClock clock)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(sdk);
        ArgumentNullException.ThrowIfNull(clock);
#else
        if (sdk is null) throw new ArgumentNullException(nameof(sdk));
        if (clock is null) throw new ArgumentNullException(nameof(clock));
#endif
        if (feeId == Guid.Empty) throw new ArgumentException("A fee identity is required.", nameof(feeId));

        var churchId = ResolveChurchId(sdk, feeId);
        var record = FeeAllotRecords.Read(sdk, feeId, churchId, default);
        var churchToday = clock.TodayInChurch(record.TimeZoneId).Date;
        if (record.Snapshot.PayDate is DateTime payDate && payDate.Date > churchToday)
        {
            return null;
        }

        var sourceId = AllotTaskIdentity.SourceId(feeId, churchToday);
        using var store = new D365OutboundEnqueueStore(sdk);
        return new OutboundTaskWriter(store).Enqueue(
            OutboundTaskType.AllotQuery,
            churchId,
            null,
            sourceId,
            AllotTaskIdentity.Payload(feeId, churchToday));
    }

    /// <summary>只沿正式 fee→contact→account lookup 解析教會，不接受輸入參數補造歸屬。</summary>
    public static Guid ResolveChurchId(IOrganizationService sdk, Guid feeId)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(sdk);
#else
        if (sdk is null) throw new ArgumentNullException(nameof(sdk));
#endif
        if (feeId == Guid.Empty) throw new ArgumentException("A fee identity is required.", nameof(feeId));

        var fee = sdk.Retrieve(FeeEntityName, feeId, new ColumnSet(FeeContactField));
        RequireIdentity(fee, FeeEntityName, feeId);
        var contact = RequireLookup(fee, FeeContactField, ContactEntityName);
        var member = sdk.Retrieve(ContactEntityName, contact.Id, new ColumnSet(MemberChurchField));
        RequireIdentity(member, ContactEntityName, contact.Id);
        return RequireLookup(member, MemberChurchField, ChurchEntityName).Id;
    }

    /// <summary>沿同一正式 fee→contact lookup 取得通知目標，不能以 fee ID 冒充會友 ID。</summary>
    public static Guid ResolveContactId(IOrganizationService sdk, Guid feeId)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(sdk);
#else
        if (sdk is null) throw new ArgumentNullException(nameof(sdk));
#endif
        if (feeId == Guid.Empty) throw new ArgumentException("A fee identity is required.", nameof(feeId));
        var fee = sdk.Retrieve(FeeEntityName, feeId, new ColumnSet(FeeContactField));
        RequireIdentity(fee, FeeEntityName, feeId);
        return RequireLookup(fee, FeeContactField, ContactEntityName).Id;
    }

    private static void RequireIdentity(Entity? row, string logicalName, Guid id)
    {
        if (row is null || row.LogicalName != logicalName || row.Id != id || row.KeyAttributes.Count != 0)
            throw new InvalidOperationException("Fee source identity is invalid.");
    }

    private static EntityReference RequireLookup(Entity row, string field, string logicalName)
    {
        if (!row.Attributes.TryGetValue(field, out var value)
            || value is not EntityReference reference
            || reference.Id == Guid.Empty
            || reference.LogicalName != logicalName
            || reference.KeyAttributes.Count != 0)
            throw new InvalidOperationException("Fee church scope is unavailable.");
        return reference;
    }
}
