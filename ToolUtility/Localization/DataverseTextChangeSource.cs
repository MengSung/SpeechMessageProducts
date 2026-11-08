/*
 * 檔案整體說明
 *
 * 目的與定位：P2-10 的 net10 D365 改字時間來源，讓 Hosting 不反向依賴 SDK 或
 * ToolUtility；借用組合根已驗證固定組織／產品的 manager，不接受 request 路由選組織。
 * 主要功能與流程：一次 lease 查 account.new_textchangedon > UTC cutoff，僅取改字
 * 時刻並依正式主鍵完整分頁；先驗全部列的 ID、時間及重複身分，再發布 detached 純值。
 * 使用邊界與維護：每次讀取最多五千列，沒有教會、credential、SDK 或快照快取。
 * manager 由 Host 擁有，先停止輪詢並排空查詢才處置 manager；同步 SDK 必須有主機
 * transport deadline，caller 取消在 I/O 前後檢查，不以 Task.Run 遺留連線。
 * 故障與取消皆標記 lease 不可重用並歸還；原例外交 Hosting 共用告警，不另發 LINE。
 */
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Hosting.Localization;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Extensions;

namespace ToolUtilityNameSpace.Localization;

/// <summary>固定部署 manager 的改字來源；本類不擁有或處置 manager。</summary>
public sealed class DataverseTextChangeSource : ITextChangeSource
{
    private readonly IDataverseConnectionManager manager;

    /// <summary>組合根必須核對 manager endpoint 與輪詢器固定部署組織／產品相同。</summary>
    public DataverseTextChangeSource(IDataverseConnectionManager manager) =>
        this.manager = manager ?? throw new ArgumentNullException(nameof(manager));

    /// <summary>完整讀取與驗證後才發布純值；任何部分頁失敗不回傳前頁內容。</summary>
    public async Task<IReadOnlyList<TextChangedChurch>> ReadAsync(DateTime sinceUtc, CancellationToken cancellationToken)
    {
        if (sinceUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A UTC text-change cutoff is required.", nameof(sinceUtc));
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = manager.Acquire(cancellationToken);
        try
        {
            var query = new QueryExpression("account") { ColumnSet = new ColumnSet("new_textchangedon") };
            query.Criteria.AddCondition("new_textchangedon", ConditionOperator.GreaterThan, sinceUtc);
            query.AddOrder("accountid", OrderType.Ascending);
            var rows = await lease.Service.RetrieveAllAsync(query, cancellationToken: cancellationToken).ConfigureAwait(false);
            var identities = new HashSet<Guid>();
            var candidate = new List<TextChangedChurch>(rows.Count);
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row.Id == Guid.Empty || !string.Equals(row.LogicalName, "account", StringComparison.Ordinal) ||
                    !identities.Add(row.Id) ||
                    !row.Attributes.TryGetValue("new_textchangedon", out var value) || value is not DateTime changed ||
                    changed.Kind != DateTimeKind.Utc || changed <= sinceUtc)
                    throw new InvalidOperationException("Invalid text-change candidate identity or UTC version.");
                candidate.Add(new TextChangedChurch(row.Id, changed));
            }
            return candidate.AsReadOnly();
        }
        catch
        {
            // 已入場 SDK 全部返回後才標記及歸還；不能把取消後不確定的 transport 給下一輪。
            lease.MarkFaulted();
            throw;
        }
    }
}
