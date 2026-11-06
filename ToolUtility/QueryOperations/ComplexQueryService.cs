#nullable enable
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.Interfaces;

namespace ToolUtilityNameSpace.QueryOperations
{
    /// <summary>
    /// 保留舊ComplexQueryService建構/API的host薄adapter；所有十六個資料操作由Core單一實作提供。
    /// 只把已授權ICrmClient的三個讀取方法轉接到純契約，不搬連線、IsReady、credential或Dispose所有權。
    /// </summary>
    /// <remarks>
    /// 不保存logger。base Dispose解除adapter強參照且停止新入口；已入場操作持局部adapter可drain。
    /// host須排空操作後處置原client，本adapter不處置借入client，也不建立cache或背景工作。
    /// </remarks>
    public class ComplexQueryService : ComplexQueryDataService
    {
        /// <summary>完整保留舊建構簽章與logger可null語義；client只可來自同一已驗證使用者／租戶scope。</summary>
        /// <param name="logger">來源相容參數，不保存或反射。</param>
        /// <param name="crmClient">host擁有且已授權的client；不可為null。</param>
        public ComplexQueryService(object? logger, ICrmClient crmClient)
            : base(logger, new ClientExecutor(crmClient))
        {
        }

        /// <summary>最小只讀轉接只保留一個借用client；沒有額外SDK連線、結果cache或通知器。</summary>
        private sealed class ClientExecutor : ICrmQueryExecutor
        {
            private readonly ICrmClient _client;

            /// <summary>借入client，生命週期受base借用參照及已入場局部操作約束。</summary>
            public ClientExecutor(ICrmClient client) =>
                _client = client ?? throw new ArgumentNullException(nameof(client));

            /// <summary>原樣轉交表、正式ID與ColumnSet，不讀取或保存回傳內容。</summary>
            public Entity Retrieve(string entityName, Guid id, ColumnSet columns) => _client.Retrieve(entityName, id, columns);

            /// <summary>保留RetrieveMultiple原SDK路徑，完整性驗證由Core負責。</summary>
            public EntityCollection RetrieveMultiple(QueryBase query) => _client.RetrieveMultiple(query);

            /// <summary>保留Execute原SDK路徑及原Exception，型別驗證由Core負責。</summary>
            public OrganizationResponse Execute(OrganizationRequest request) => _client.Execute(request);
        }
    }
}
