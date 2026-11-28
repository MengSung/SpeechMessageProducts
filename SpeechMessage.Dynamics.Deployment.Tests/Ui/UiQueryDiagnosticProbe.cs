/*
檔案整體說明
目的與定位：FS 實機根因調查的測試專屬唯讀查詢組裝器，供離線故障注入及明確標示 Live3P 的
對照測試共用；不接入產品命令，不執行完整 plan，也不授權任何結構或業務寫入。
主要流程：只允許 systemform／savedquery 的 account 範圍及正式主鍵，單頁至多一列；
比較一次／兩次相同主鍵排序，輸出固定階段、列數、例外型別及既有共用入口的有界數字碼。
回應形狀拒絕時只補表名匹配、欄位數、主鍵存在／型別／一致等布林，不輸出任何欄位值。
使用邊界：查詢委派、writer、取消來源及診斷 owner 全由呼叫端擁有；本檔不建立連線、檔案、
Task 或快取。真實使用前必須核對三個測試組織白名單與伺服器身分，不把委派當成授權。
資源與隔離：每次建立獨立 QueryExpression，結果只在同一呼叫驗證，不輸出 ID、XML、秘密、
個資或 SDK 錯誤本文。同步呼叫返回後檢查取消；已入場查詢由外層租約排空才 Dispose。
systemform 伺服器會額外回傳 objecttypecode／type；只接受兩欄成對、前者精確 account、
後者 SDK OptionSetValue 的形狀，不忽略其他欄位，也不將判別值輸出或保存至結果。
失敗與驗證：真正失敗將同一例外交給共用診斷委派一次；正常 caller 取消重新拋出、不通知。
合成測試驗查詢形狀、拒絕、無秘密輸出及取消；對照結果不是 FS plan／apply 的驗收。
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>
/// 為兩種 UI 管理表建立最小唯讀對照，不持有服務、身分、輸出或可跨測試共用的狀態。
/// 呼叫端負責組織核身及所有外部 owner；回傳值只表示這次小查詢是否通過。
/// </summary>
internal static class UiQueryDiagnosticProbe
{
    /// <summary>固定十個非業務 schema 名稱；不可變程序常數，不含任何組織、使用者或回應資料。</summary>
    private static readonly IReadOnlyList<string> ShapeColumns = Array.AsReadOnly(new[]
    {
        "formid", "formidunique", "objecttypecode", "type", "formactivationstate",
        "componentstate", "ismanaged", "solutionid", "organizationid", "overwritetime"
    });

    /// <summary>
    /// 執行一個固定 account 範圍的小查詢；重複排序只供故障對照，不能送入正式部署路徑。
    /// 回傳 false 時保留安全摘要及原例外上報，不把預期對照拒絕宣稱為正式操作成功。
    /// </summary>
    /// <param name="entity">精確白名單 systemform 或 savedquery。</param>
    /// <param name="repeatedOrder">是否注入第二個相同主鍵 ASC。</param>
    /// <param name="execute">呼叫端借出的同步唯讀查詢能力，不轉移連線所有權。</param>
    /// <param name="output">呼叫端有界證據 writer，不由本方法關閉。</param>
    /// <param name="report">共用診斷入口，接受原 Exception，不保存 client 或查詢。</param>
    /// <param name="cancellationToken">caller 正常取消；內部逾時不得冒用此分類。</param>
    /// <returns>小查詢成功才為 true；不能代表完整 metadata 或部署成功。</returns>
    internal static bool Run(string entity, bool repeatedOrder, Func<QueryExpression, EntityCollection> execute,
        TextWriter output, Action<Exception> report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(report);
        if (entity is not ("systemform" or "savedquery"))
            throw new ArgumentException("Only the fixed UI diagnostic tables are allowed.", nameof(entity));
        cancellationToken.ThrowIfCancellationRequested();
        var key = entity == "systemform" ? "formid" : "savedqueryid";
        var orders = repeatedOrder ? 2 : 1;
        var prefix = "FS-QUERY entity=" + entity + " orders=" + orders.ToString(CultureInfo.InvariantCulture);
        try
        {
            var query = new QueryExpression(entity)
            {
                ColumnSet = new ColumnSet(key),
                PageInfo = new PagingInfo { Count = 1, PageNumber = 1 }
            };
            query.Criteria.AddCondition(entity == "systemform" ? "objecttypecode" : "returnedtypecode",
                ConditionOperator.In, "account");
            query.AddOrder(key, OrderType.Ascending);
            if (repeatedOrder) query.AddOrder(key, OrderType.Ascending);
            var rows = execute(query);
            cancellationToken.ThrowIfCancellationRequested();
            if (rows is null || rows.Entities.Count > 1)
                throw new InvalidDataException("UI diagnostic response exceeds its single-row scope.");
            foreach (var row in rows.Entities)
            {
                // systemform 的伺服器判別欄不是 caller 擴大 ColumnSet；以精確三欄及範圍驗證，
                // 不能用「多回欄位就忽略」放行未知回應，更不能放寬正式 primary ID 的一致性。
                var discriminatorProjection = entity == "systemform" && row.Attributes.Count == 3
                    && row.Attributes.TryGetValue("objecttypecode", out var rawEntity) && rawEntity is string scope && scope == "account"
                    && row.Attributes.TryGetValue("type", out var rawType) && rawType is OptionSetValue;
                if (row.Id == Guid.Empty || row.LogicalName != entity || row.Attributes.Count != 1 && !discriminatorProjection
                    || !row.Attributes.TryGetValue(key, out var value) || value is not Guid id || id != row.Id)
                {
                    output.WriteLine(prefix + " shape tableMatch=" + (row.LogicalName == entity)
                        + " idPresent=" + (row.Id != Guid.Empty)
                        + " fields=" + row.Attributes.Count.ToString(CultureInfo.InvariantCulture)
                        + " keyPresent=" + row.Attributes.ContainsKey(key)
                        + " keyGuid=" + (row.Attributes.TryGetValue(key, out var rawKey) && rawKey is Guid)
                        + " keyMatches=" + (row.Attributes.TryGetValue(key, out var rawId) && rawId is Guid rowId && rowId == row.Id)
                        + " knownSchema=" + string.Join(",", ShapeColumns.Where(row.Attributes.ContainsKey)));
                    throw new InvalidDataException("UI diagnostic response has an invalid authoritative identity.");
                }
            }
            output.WriteLine(prefix + " result=OK rows=" + rows.Entities.Count.ToString(CultureInfo.InvariantCulture));
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            try { report(exception); }
            catch { Console.Error.WriteLine("[FS.QueryProbe] DiagnosticReportingFailed"); }
            output.WriteLine(prefix + " result=FAIL type=" + exception.GetType().Name);
            DeploymentCli.WriteSupplementalFailureDiagnostics(exception, output);
            return false;
        }
    }
}
