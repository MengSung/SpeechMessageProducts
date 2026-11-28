/*
檔案整體說明
目的與定位：以純合成委派驗證 FS 小查詢診斷的範圍與安全輸出，不載入 User Secrets、
不連 D365、不建立 Host，也不能把本檔通過當成實機 plan 或故障根因已確認。
主要流程：獨立建立正式管理 ID、SDK 回應、writer 與取消來源，攔截一次／兩次排序的
查詢，驗精確表名、唯一欄位、account 範圍、第一頁一列與無續頁，再注入 SDK 故障及壞回應。
決定性斷言：未知表在委派之前拒絕；原例外只上報一次，訊息／TraceText 的秘密哨兵不外洩；
caller 取消沒有查詢及通知，內部逾時仍屬失敗。錯正式 ID 或多列不得被假成功掩蓋。
資源與隔離：每案擁有 StringWriter／CancellationTokenSource，using 結束時同步 Dispose；
委派與 SDK 候選僅存活於單一測試，沒有連線、檔案、背景工作、快取或跨組織可變狀態。
另外以只讀 SDK 替身對重複排序拋出 FaultException，先證明舊查詢演算法會失敗，
再要求正式 UiViewDeployment 的兩來源 reader 通過；此合成對照不倒推歷史服務端原因。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.ServiceModel;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Schema;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>診斷子查詢的純離線契約；全部故障由本案委派注入，沒有實機依賴。</summary>
public sealed class UiQueryDiagnosticProbeTests
{
    /// <summary>每種固定表及排序版本只讀正式主鍵，完整查詢形狀在送出前由獨立斷言核對。</summary>
    /// <param name="entity">兩種 UI 管理表之一。</param>
    /// <param name="repeated">是否刻意使用重複排序對照。</param>
    [Theory]
    [InlineData("systemform", false)]
    [InlineData("systemform", true)]
    [InlineData("savedquery", false)]
    [InlineData("savedquery", true)]
    public void Run_FixedShape_UsesFreshOneRowQueryWithoutPrivateValues(string entity, bool repeated)
    {
        using var output = new StringWriter();
        var failures = new List<Exception>();
        QueryExpression? previous = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var success = UiQueryDiagnosticProbe.Run(entity, repeated, query =>
            {
                Assert.NotSame(previous, query);
                previous = query;
                Assert.Equal(entity, query.EntityName);
                var key = entity == "systemform" ? "formid" : "savedqueryid";
                Assert.False(query.ColumnSet.AllColumns);
                Assert.Equal(key, Assert.Single(query.ColumnSet.Columns));
                Assert.Equal(repeated ? 2 : 1, query.Orders.Count);
                Assert.All(query.Orders, order =>
                {
                    Assert.Equal(key, order.AttributeName);
                    Assert.Equal(OrderType.Ascending, order.OrderType);
                });
                Assert.Equal(1, query.PageInfo.PageNumber);
                Assert.Equal(1, query.PageInfo.Count);
                Assert.Null(query.PageInfo.PagingCookie);
                Assert.Empty(query.LinkEntities);
                var condition = Assert.Single(query.Criteria.Conditions);
                Assert.Equal(entity == "systemform" ? "objecttypecode" : "returnedtypecode", condition.AttributeName);
                Assert.Equal(ConditionOperator.In, condition.Operator);
                Assert.Equal("account", Assert.Single(condition.Values));
                var row = new Entity(entity, new Guid("4df79d0d-f1ce-4ede-a848-c6d18bf55e21"));
                row[key] = row.Id;
                return new EntityCollection([row]);
            }, output, failures.Add, default);
            Assert.True(success);
        }
        Assert.Empty(failures);
        Assert.Contains("result=OK rows=1", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("4df79d0d", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>含秘密哨兵的 SDK fault 必須原件上報一次，輸出僅保留型別及有界數字碼。</summary>
    [Fact]
    public void Run_SdkFault_PreservesOriginalAndSuppressesMessage()
    {
        using var output = new StringWriter();
        var fault = new FaultException<OrganizationServiceFault>(new OrganizationServiceFault
        {
            ErrorCode = unchecked((int)0x80041103),
            Message = "private-fault-message",
            TraceText = "private-fault-trace"
        }, "private-fault-reason");
        var reports = new List<Exception>();
        Assert.False(UiQueryDiagnosticProbe.Run("systemform", true, _ => throw fault, output, reports.Add, default));
        Assert.Same(fault, Assert.Single(reports));
        Assert.Contains("0x80041103", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-fault", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// 重現實機 systemform 單欄查詢仍自動回傳 objecttypecode／type 的合法 SDK 形狀；
    /// 必須保持正式 ID 及 account 範圍，不印附加值，且不能因新診斷的單欄假設誤拒。
    /// </summary>
    [Fact]
    public void Run_SystemFormServerDiscriminators_PreservesIdentityAndScope()
    {
        using var output = new StringWriter();
        var row = new Entity("systemform", Guid.NewGuid());
        row["formid"] = row.Id;
        row["objecttypecode"] = "account";
        row["type"] = new OptionSetValue(2);
        var reports = new List<Exception>();
        Assert.True(UiQueryDiagnosticProbe.Run("systemform", false, _ => new EntityCollection([row]),
            output, reports.Add, default));
        Assert.Empty(reports);
        Assert.Contains("result=OK rows=1", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(row.Id.ToString(), output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("account", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>附加欄只允許精確範圍與強型別判別欄；故障注入不可被相容處理吞掉。</summary>
    /// <param name="fault">錯範圍、錯型別、額外欄位或缺半組判別欄。</param>
    [Theory]
    [InlineData("scope")]
    [InlineData("type")]
    [InlineData("extra")]
    [InlineData("partial")]
    public void Run_SystemFormUnexpectedProjection_StillFailsClosed(string fault)
    {
        using var output = new StringWriter();
        var row = new Entity("systemform", Guid.NewGuid());
        row["formid"] = row.Id;
        row["objecttypecode"] = fault == "scope" ? "contact" : "account";
        row["type"] = fault == "type" ? "private-type" : new OptionSetValue(2);
        if (fault == "extra") row["private-field"] = "private-value";
        if (fault == "partial") row.Attributes.Remove("type");
        var reports = new List<Exception>();
        Assert.False(UiQueryDiagnosticProbe.Run("systemform", false, _ => new EntityCollection([row]),
            output, reports.Add, default));
        Assert.IsType<InvalidDataException>(Assert.Single(reports));
        Assert.DoesNotContain("private-", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("contact", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>未知表不送查詢；拒絕訊息不回顯原始輸入，避免把診斷擴大為業務查詢。</summary>
    [Fact]
    public void Run_UnknownTable_RejectsBeforeDelegate()
    {
        using var output = new StringWriter();
        var calls = 0;
        var failure = Assert.Throws<ArgumentException>(() => UiQueryDiagnosticProbe.Run("contact", false,
            _ => { calls++; return new EntityCollection(); }, output, _ => throw new InvalidOperationException(), default));
        Assert.Equal(0, calls);
        Assert.DoesNotContain("contact", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>caller 取消在入場前重新拋出；只有本案 token 是正常取消依據，不產生查詢或通知。</summary>
    [Fact]
    public void Run_CallerCancelled_NoQueryOrNotification()
    {
        using var output = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;
        var reports = 0;
        Assert.Throws<OperationCanceledException>(() => UiQueryDiagnosticProbe.Run("savedquery", false,
            _ => { calls++; return new EntityCollection(); }, output, _ => reports++, cancellation.Token));
        Assert.Equal(0, calls);
        Assert.Equal(0, reports);
    }

    /// <summary>同步傳輸內部取消不等於 caller 取消；必須失敗且上報原件，不能假稱正常結束。</summary>
    [Fact]
    public void Run_InternalTimeout_ReportsOriginalFailure()
    {
        using var output = new StringWriter();
        var failure = new OperationCanceledException("private-timeout");
        var reports = new List<Exception>();
        Assert.False(UiQueryDiagnosticProbe.Run("savedquery", false, _ => throw failure, output, reports.Add, default));
        Assert.Same(failure, Assert.Single(reports));
        Assert.DoesNotContain("private-timeout", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>服務多回一列或偽主鍵時整次拒絕，不印值、不截斷成一筆假成功。</summary>
    /// <param name="multiple">true 注入兩列；false 注入回傳主鍵與 Entity.Id 不符。</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Run_InvalidResponse_FailsClosed(bool multiple)
    {
        using var output = new StringWriter();
        var row = new Entity("savedquery", Guid.NewGuid());
        row["savedqueryid"] = multiple ? row.Id : Guid.NewGuid();
        var rows = multiple ? new EntityCollection([row, row]) : new EntityCollection([row]);
        var reports = new List<Exception>();
        Assert.False(UiQueryDiagnosticProbe.Run("savedquery", false, _ => rows, output, reports.Add, default));
        Assert.IsType<InvalidDataException>(Assert.Single(reports));
        Assert.DoesNotContain("result=OK", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(row.Id.ToString(), output.ToString(), StringComparison.Ordinal);
        if (!multiple)
            Assert.Contains("shape tableMatch=True idPresent=True fields=1 keyPresent=True keyGuid=True keyMatches=False",
                output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>診斷入口自己故障仍保留原失敗摘要，不印 reporter 例外本文或遞迴通知。</summary>
    [Fact]
    public void Run_ReporterFailure_DoesNotExposeEitherPrivateMessage()
    {
        using var output = new StringWriter();
        var original = new InvalidOperationException("private-operation");
        var reports = 0;
        Assert.False(UiQueryDiagnosticProbe.Run("systemform", false, _ => throw original, output, exception =>
        {
            Assert.Same(original, exception);
            reports++;
            throw new IOException("private-reporter");
        }, default));
        Assert.Equal(1, reports);
        Assert.DoesNotContain("private-", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("type=InvalidOperationException", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// 將舊版第二次無條件 AddOrder 的查詢形狀送入同型 SDK fault 替身，必須先拒絕；
    /// 正式 ReadPublicationScope 再讀兩種表的已發布／未發布來源，全程單排序且零寫入。
    /// 此 RED／GREEN 僅證明排序相容缺陷，不宣稱就是缺乏服務端錯碼的歷史故障根因。
    /// </summary>
    [Fact]
    public void PublicationReader_DuplicateOrderFault_LegacyFailsAndCurrentReaderPasses()
    {
        var service = new DuplicateOrderService();
        var legacy = new QueryExpression("systemform") { ColumnSet = new ColumnSet("formid") };
        legacy.AddOrder("formid", OrderType.Ascending);
        legacy.AddOrder("formid", OrderType.Ascending);
        Assert.Throws<FaultException<OrganizationServiceFault>>(() => service.RetrieveMultiple(legacy));
        var scope = UiViewDeployment.ReadPublicationScope(service, ["account"], default);
        Assert.Empty(scope.Published);
        Assert.Empty(scope.Unpublished);
        Assert.Equal(5, service.Reads);
    }

    /// <summary>
    /// 純記憶體唯讀 SDK 邊界；唯一狀態是單案呼叫計數，重複排序注入原生 fault，
    /// 所有寫入／其他能力立即拒絕。沒有連線、timer 或可 Dispose 資源。
    /// </summary>
    private sealed class DuplicateOrderService : IOrganizationService
    {
        /// <summary>本案唯一的讀取計數，不跨測試或組織重用。</summary>
        internal int Reads { get; private set; }

        /// <summary>只接受未發布讀取並保留相同 Query 供排序斷言；不處理任何寫入訊息。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) =>
            request is RetrieveUnpublishedMultipleRequest { Query: QueryExpression query }
                ? new RetrieveUnpublishedMultipleResponse { Results = { ["EntityCollection"] = RetrieveMultiple(query) } }
                : throw new InvalidOperationException("Unexpected diagnostic capability.");

        /// <summary>對第二個相同主鍵排序注入 SDK fault，合法單排序回空頁，避免依賴任何資料列。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            Reads++;
            if (query is not QueryExpression expression)
                throw new InvalidOperationException("Unexpected diagnostic query.");
            if (expression.Orders.Count != 1)
                throw new FaultException<OrganizationServiceFault>(
                    new OrganizationServiceFault { ErrorCode = unchecked((int)0x80041103) }, "synthetic duplicate order");
            return new EntityCollection();
        }

        /// <summary>本替身不授權按 ID 讀取任何資料。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new InvalidOperationException();
        /// <summary>拒絕新增；診斷及正式 reader 都不應觸發此能力。</summary>
        public Guid Create(Entity entity) => throw new InvalidOperationException();
        /// <summary>拒絕修改，避免只讀測試掩蓋額外副作用。</summary>
        public void Update(Entity entity) => throw new InvalidOperationException();
        /// <summary>拒絕刪除，沒有測試業務資料可供清除。</summary>
        public void Delete(string entityName, Guid id) => throw new InvalidOperationException();
        /// <summary>拒絕關聯寫入，不可藉診斷觸發 Plugin。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship,
            EntityReferenceCollection relatedEntities) => throw new InvalidOperationException();
        /// <summary>拒絕解除關聯，維持純唯讀能力範圍。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship,
            EntityReferenceCollection relatedEntities) => throw new InvalidOperationException();
    }
}
