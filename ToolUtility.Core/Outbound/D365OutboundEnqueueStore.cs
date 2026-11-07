/*
 * 檔案整體說明
 *
 * 目的與定位：把對外待辦純值命令寫入 A.4 的 new_outboundtask，透過資料庫替代鍵
 * 保證多程序同事件只一列，不以程序鎖或查完再寫作為唯一性保證。
 * 主要功能與流程：先嘗試建立具完整預設狀態的新列；只有已知唯一鍵衝突才按精確鍵
 * 讀回並核對全部不可變內容，回原正式 ID。絕不盲目 Upsert 覆寫既有完成狀態、
 * 重試次數、payload 或教會；差異視為事件衝突並 fail closed。
 * 使用邊界與維護：SDK 由呼叫端授權固定組織並有界擁有，本 store 只借用，不建池、
 * 不 Dispose 外借連線、不保留 Entity／查詢結果。Dispose 清除借用參照並拒絕新入口；
 * 已入場操作保留局部借用，故呼叫端必須先排空再關閉 SDK。同步 I/O 入場後須等返回才能
 * 釋放 owner；不自動重試逾時或其他故障。回復與通知由最終 Host 負責，例外不含本文。
 * 離線 SDK 契約覆蓋並行唯一鍵、既有資料不變及故障傳遞；實機仍集中於 P2-12。
 */
using System;
using System.ServiceModel;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.Outbound;

/// <summary>單次授權儲存適配器；真正唯一性由已啟用的 new_idempotencykey 替代鍵負責。</summary>
public sealed class D365OutboundEnqueueStore : IOutboundEnqueueStore, IDisposable
{
    private IOrganizationService? service;

    /// <summary>借用呼叫端已授權、已設定 transport timeout 的 SDK；不得跨組織／使用者重用 store。</summary>
    /// <param name="service">此操作 owner 持有的 SDK；store 不接管其 Dispose。</param>
    public D365OutboundEnqueueStore(IOrganizationService service) =>
        this.service = service ?? throw new ArgumentNullException(nameof(service));

    /// <summary>
    /// 原子建立或核對既有事件後回正式 ID；成功建立後保留成功結果，不因後到取消
    /// 隱藏已確認的寫入。僅明確唯一鍵衝突可以讀回，其他 SDK 例外保持原身分。
    /// </summary>
    /// <param name="command">已凍結的單次事件命令。</param>
    /// <param name="cancellationToken">入場前及重複鍵讀回前檢查；不能提前歸還仍入場的 SDK。</param>
    /// <returns>此事件真正的資料庫 ID；缺失或衝突不能以來源 ID 補洞。</returns>
    public Guid CreateOrGet(OutboundTaskCommand command, CancellationToken cancellationToken)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(command);
#else
        if (command is null) throw new ArgumentNullException(nameof(command));
#endif
        cancellationToken.ThrowIfCancellationRequested();
        var client = Volatile.Read(ref service) ?? throw new ObjectDisposedException(nameof(D365OutboundEnqueueStore));
        var row = new Entity("new_outboundtask")
        {
            ["new_name"] = command.Type + "|" + command.IdempotencyKey,
            ["new_idempotencykey"] = command.IdempotencyKey,
            ["new_type"] = new OptionSetValue((int)command.Type),
            ["new_taskstatus"] = new OptionSetValue(100000000),
            ["new_accountid"] = new EntityReference("account", command.ChurchId),
            ["new_payload"] = command.PayloadJson,
            ["new_attempts"] = 0
        };
        if (command.ContactId.HasValue) row["new_contactid"] = new EntityReference("contact", command.ContactId.Value);
        try
        {
            var id = client.Create(row);
            if (id == Guid.Empty) throw new InvalidOperationException("Outbound create did not return a database identity.");
            return id;
        }
        catch (FaultException<OrganizationServiceFault> exception)
            when (exception.Detail?.ErrorCode == unchecked((int)0x80040237))
        {
            // 只有已證明的唯一鍵衝突可轉為讀回；不確定逾時或其他 fault 不可假定已存在。
            cancellationToken.ThrowIfCancellationRequested();
            var query = new QueryExpression("new_outboundtask")
            {
                TopCount = 2,
                ColumnSet = new ColumnSet("new_accountid", "new_contactid", "new_idempotencykey", "new_payload", "new_type")
            };
            query.Criteria.AddCondition("new_idempotencykey", ConditionOperator.Equal, command.IdempotencyKey);
            var rows = client.RetrieveMultiple(query);
            cancellationToken.ThrowIfCancellationRequested();
            if (rows is null || rows.MoreRecords || rows.Entities.Count != 1)
                throw new InvalidOperationException("Outbound idempotency identity is ambiguous.");
            var existing = rows.Entities[0];
            var church = existing.GetAttributeValue<EntityReference>("new_accountid");
            var contact = existing.GetAttributeValue<EntityReference>("new_contactid");
            if (existing.Id == Guid.Empty || !string.Equals(existing.LogicalName, "new_outboundtask", StringComparison.Ordinal)
                || church is null || church.Id != command.ChurchId || !string.Equals(church.LogicalName, "account", StringComparison.Ordinal)
                || contact?.Id != command.ContactId
                || contact is not null && !string.Equals(contact.LogicalName, "contact", StringComparison.Ordinal)
                || existing.GetAttributeValue<OptionSetValue>("new_type")?.Value != (int)command.Type
                || !string.Equals(existing.GetAttributeValue<string>("new_idempotencykey"), command.IdempotencyKey, StringComparison.Ordinal)
                || !string.Equals(existing.GetAttributeValue<string>("new_payload"), command.PayloadJson, StringComparison.Ordinal))
                throw new InvalidOperationException("Outbound idempotency content conflicts with the existing event.");
            return existing.Id;
        }
    }

    /// <summary>清除借用參照並拒絕新操作；不 Dispose 外借 SDK，呼叫端仍須先排空 I/O 再釋放連線。</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref service, null);
    }
}
