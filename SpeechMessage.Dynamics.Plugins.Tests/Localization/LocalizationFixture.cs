/*
 * 檔案整體說明
 *
 * 目的與定位：P5-04 三個外殼共用的離線資料 owner，以真 RemoteExecutionContext 與
 * 本機 SDK 替身建立教會、會友與頻道；不使用需要私有反射的動態代理，供 CAS 案例共用。
 * 主要功能與流程：按正式主鍵回傳合成列，收集實際寫入及安全影子投影；合成待辦建立
 * 以唯一鍵衝突模擬資料庫原子 create-or-get，重複時回讀同列，不把記憶體鎖冒充實機證據。
 * 使用邊界與維護：每案獨占 fixture、字典、trace 與 factory，最多只建立本案有限輸入；
 * 不連網、不持有秘密、Session、timer、queue 或背景工作。呼叫同步完成才 Dispose，
 * 清除服務回呼、列與追蹤；平台借用服務不得被 Plugin 提早 Dispose。故障由案例顯式注入，
 * 斷言 owner 排空與新操作拒絕。這些合成結果不代表 Dataverse 授權或正式替代鍵驗收。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;

namespace SpeechMessage.Dynamics.Plugins.Tests.Localization;

/// <summary>單案合成資料及 SDK owner，亦可在受限域內建立；不使用跨案例狀態。</summary>
internal sealed class LocalizationFixture : IDisposable
{
    /// <summary>只在本次操作保留合成記錄；Dispose 無條件清空。</summary>
    private readonly Dictionary<Guid, Entity> rows = new();
    /// <summary>只保存本案已建立的待辦，不跨組織重用或覆寫既有狀態。</summary>
    private readonly List<Entity> tasks = new();
    /// <summary>建立各自獨立的教會／會友／頻道，未綁定案不需要讀教會或頻道。</summary>
    internal LocalizationFixture(bool bound = true, string code = "alpha", string language = "zh-TW")
    {
        ChurchId = Guid.NewGuid(); ContactId = Guid.NewGuid(); ChannelId = Guid.NewGuid();
        rows.Add(ChurchId, new Entity("account", ChurchId)
        {
            ["name"] = "Same synthetic church", ["accountnumber"] = code,
            ["new_linechannelid"] = new EntityReference("new_linechannel", ChannelId),
            ["new_defaultlanguage"] = "zh-TW", ["new_enabledlanguages"] = "zh-TW"
        });
        rows.Add(ContactId, new Entity("contact", ContactId)
        {
            ["parentcustomerid"] = new EntityReference("account", ChurchId),
            ["new_lineid"] = bound ? "U" + new string('a', 32) : string.Empty,
            ["new_preferredlanguage"] = language
        });
        rows.Add(ChannelId, new Entity("new_linechannel", ChannelId)
        { ["new_accesstokenencrypted"] = "invalid-envelope-must-never-be-decrypted" });
        Service.RetrieveHandler = (table, id, _) => rows.TryGetValue(id, out var row) && row.LogicalName == table
            ? row : throw new InvalidOperationException("Unexpected fixture identity.");
        Service.CreateHandler = row =>
        {
            if (tasks.Any(task => Equals(task["new_idempotencykey"], row["new_idempotencykey"])))
                throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault
                { ErrorCode = unchecked((int)0x80040237) });
            var id = Guid.NewGuid();
            var copy = new Entity(row.LogicalName, id);
            foreach (var pair in row.Attributes) copy[pair.Key] = pair.Value;
            tasks.Add(copy);
            return id;
        };
        Service.RetrieveMultipleHandler = query =>
        {
            if (query is not QueryExpression typed || typed.EntityName != "new_outboundtask" ||
                typed.Criteria.Conditions.Count != 1) throw new InvalidOperationException("Unexpected fixture query.");
            var key = typed.Criteria.Conditions[0].Values[0];
            var result = new EntityCollection();
            foreach (var task in tasks.Where(task => Equals(task["new_idempotencykey"], key))) result.Entities.Add(task);
            return result;
        };
    }
    /// <summary>正式合成教會 ID，顯示名稱相同也不得合併。</summary>
    internal Guid ChurchId { get; }
    /// <summary>正式合成會友 ID，不以 LINE 字串取代。</summary>
    internal Guid ContactId { get; }
    /// <summary>正式合成頻道 ID，讀回時必須符合 lookup。</summary>
    internal Guid ChannelId { get; }
    /// <summary>本案唯一 SDK owner；Plugin 只可借用。</summary>
    internal FakeOrganizationService Service { get; } = new();
    /// <summary>每次執行前清空；只用於本案安全投影斷言。</summary>
    internal FakeTracingService Trace { get; } = new();
    /// <summary>真正成功建立的合成待辦數，不把重複 Create 嘗試算成新資料。</summary>
    internal int TaskCount => tasks.Count;
    /// <summary>本案第一筆待辦內容供決定性欄位斷言，未建立時明確失敗。</summary>
    internal Entity TaskRow => tasks.Single();
    /// <summary>構造符合真註冊形狀的會友 Update，預設 depth 一，允許精確門檻故障注入。</summary>
    internal RemoteExecutionContext MemberContext(int depth = 1)
    {
        var context = Context("contact", ContactId, 40, 1, depth);
        context.InputParameters["Target"] = new Entity("contact", ContactId) { ["new_preferredlanguage"] = "zh-TW" };
        return context;
    }
    /// <summary>四欄完整前像與單欄補丁，測試可顯式清空或注入錯誤型別。</summary>
    internal RemoteExecutionContext ChurchContext(int stage = 40, int mode = 1, int depth = 1)
    {
        var context = Context("account", ChurchId, stage, mode, depth);
        context.InputParameters["Target"] = new Entity("account", ChurchId) { ["new_countrycode"] = "TW" };
        context.PreEntityImages["PreImage"] = new Entity("account", ChurchId)
        {
            ["new_defaultlanguage"] = "zh-TW", ["new_enabledlanguages"] = "zh-TW",
            ["new_timezone"] = "Asia/Taipei", ["new_countrycode"] = "TW"
        };
        return context;
    }
    /// <summary>以新組織與使用者 context 執行，factory 僅當次存活且驗證平台使用者。</summary>
    internal void Execute(IPlugin shell, RemoteExecutionContext context)
    {
        Trace.Entries.Clear();
        shell.Execute(new FakeServiceProvider().Add<IPluginExecutionContext>(context).Add<ITracingService>(Trace)
            .Add<IOrganizationServiceFactory>(new Factory(Service, context.UserId)));
    }
    /// <summary>只解析本案版本一影子投影，不將一般例外追蹤當作業務意圖。</summary>
    internal JObject[] Intents() => Trace.Entries.Where(line => line.StartsWith("SMV-SHADOW|1|", StringComparison.Ordinal))
        .Select(line => JObject.Parse(line.Substring("SMV-SHADOW|1|".Length))).ToArray();
    /// <summary>測試所有權排空，服務關閉後新入口拒絕；沒有非同步工作可被遺棄。</summary>
    public void Dispose()
    {
        Service.Dispose(); rows.Clear(); tasks.Clear(); Trace.Entries.Clear();
    }
    /// <summary>僅構造純 context，不借用父域或其他請求身分。</summary>
    private static RemoteExecutionContext Context(string table, Guid id, int stage, int mode, int depth) => new()
    {
        MessageName = "Update", PrimaryEntityName = table, PrimaryEntityId = id, Stage = stage, Mode = mode,
        Depth = depth, OrganizationId = Guid.NewGuid(), OrganizationName = "synthetic",
        UserId = Guid.NewGuid(), CorrelationId = Guid.NewGuid()
    };
    /// <summary>當次平台 factory；不 Dispose 外借 SDK，也不保存於外殼。</summary>
    private sealed class Factory(IOrganizationService service, Guid user) : IOrganizationServiceFactory
    {
        /// <summary>只接受當次 context 的使用者，不允許測試無意提升成 system user。</summary>
        public IOrganizationService CreateOrganizationService(Guid? userId) => userId == user
            ? service : throw new InvalidOperationException("Unexpected service user.");
    }
}
