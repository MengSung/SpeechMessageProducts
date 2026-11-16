/*
 * 檔案整體說明
 * 目的與定位：P5-09 六個 reading 黃金情境的雙宿主整合驗證，從嵌入 scenarios
 * 與人工 expected 讀取正式合成 ID，實際執行 Core SDK 協調，不在測試重寫業務計數。
 * 主要流程：每案建立獨立 SDK 替身，逐欄載入種子、呼叫真待辦或計數服務，再比較
 * 完整觀察寫入及錯誤型別。待辦僅投影教會、會友與完整 payload，狀態/唯一鍵另由
 * 真 store 契約及 QrImageTaskTests 保護；不把預期檔當作服務回應。
 * 使用邊界與維護：沒有 D365、LINE、圖形、檔案分享或秘密；SDK 以精確表及正式 ID
 * 讀取，QueryExpression 只接受明列的等值與非空條件，未知語法拒絕。每案 owner
 * 在 finally Dispose 清空種子及觀察寫入；嵌入 stream/reader 使用 using 關閉。
 * 候選至多六案且每份 JSON 至多 1 MiB，不快取 JSON/SDK，不建立 timer/背景工作。
 * 驗證只代表新規則符合有來源行號的閱讀期望，不冒稱舊 DLL、實機影子或像素比對。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpeechMessageProducts.GoodShepherd.Core.QrCodes;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.QrCodes;

/// <summary>真 Core SDK 鏈與獨立閱讀期望的逐值核對，錯誤時不得有部分發布。</summary>
public sealed class QrCodeGoldenTests
{
    /// <summary>六案包含正常聚會/個人、空姓名、同名不同列計數、空集合及缺教會拒絕。</summary>
    [Fact]
    public void QrCodes_Scenarios_MatchExpected()
    {
        var scenarios = Read("scenarios"); var expected = Read("expected");
        Assert.Equal("reading", (string?)expected["basis"]);
        var cases = (JArray)scenarios["cases"]!;
        Assert.InRange(cases.Count, 3, 6);
        Assert.Equal(cases.Count, ((JArray)expected["cases"]!).Count);
        for (var i = 0; i < cases.Count; i++)
        {
            using var sdk = new GoldenSdk((JArray)cases[i]["seed"]!);
            var trigger = cases[i]["trigger"]!;
            var id = LessonLinkGoldenFixture.Id("record");
            var contract = expected["cases"]![i]!;
            Assert.Equal((string?)cases[i]["name"], (string?)contract["name"]);
            Exception? failure = null;
            try
            {
                switch ((string?)trigger["project"])
                {
                    case "SundayQrCodeWorkflow": _ = QrImageTaskService.EnqueueMeeting(sdk, id); break;
                    case "PersonalQrCodeWorkflow": _ = QrImageTaskService.EnqueuePersonal(sdk, id); break;
                    case "CalculateQrCodeScanNumberWorkflow": new MeetingScanCountService().Execute(sdk, id); break;
                    default: throw new InvalidDataException("Unknown golden trigger.");
                }
            }
            catch (Exception error) { failure = error; }
            Assert.Equal((string?)contract["exception"], failure?.GetType().Name);
            Assert.True(JToken.DeepEquals(contract["writes"], JArray.Parse(sdk.Writes.ToString(Formatting.None))), "Golden writes differ: " + (string?)cases[i]["name"]);
            Assert.Empty((JArray)contract["lineCalls"]!);
        }
    }

    /// <summary>只讀固定嵌入資源；每次建立獨立 JSON 樹，拒絕重複鍵與超預算資料。</summary>
    private static JObject Read(string kind)
    {
        var assembly = typeof(QrCodeGoldenTests).Assembly;
        var name = assembly.GetManifestResourceNames().Single(x => x.EndsWith(".qrcodes." + kind + ".json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("Missing QR golden.");
        if (stream.Length > 1048576) throw new InvalidDataException("QR golden exceeds capacity.");
        using var text = new StreamReader(stream, new UTF8Encoding(false, true));
        using var reader = new JsonTextReader(text) { MaxDepth = 32, DateParseHandling = DateParseHandling.None };
        var result = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        if (reader.Read()) throw new InvalidDataException("Trailing QR golden content.");
        return result;
    }

    /// <summary>單案 SDK owner；只解讀查詢條件與投影，沒有任何 QR 或統計業務演算法。</summary>
    private sealed class GoldenSdk : IOrganizationService, IDisposable
    {
        private readonly Dictionary<Guid, Entity> rows = new();
        private readonly Dictionary<Guid, string> aliases = new();
        /// <summary>操作局部寫入投影；測試結束清空，不保留 caller Entity。</summary>
        internal JArray Writes { get; } = new();

        /// <summary>每筆種子以正式合成主鍵載入；相同姓名不作字典鍵或合併依據。</summary>
        internal GoldenSdk(JArray seed)
        {
            foreach (var item in seed)
            {
                var alias = (string)item["alias"]!;
                var row = new Entity((string)item["entity"]!, LessonLinkGoldenFixture.Id(alias));
                aliases.Add(row.Id, alias);
                foreach (var field in ((JObject)item["fields"]!).Properties())
                    row[field.Name] = field.Value is JObject reference
                        ? new EntityReference((string)reference["entity"]!, LessonLinkGoldenFixture.Id((string)reference["ref"]!))
                        : field.Value.Type == JTokenType.Null ? null : ((JValue)field.Value).Value;
                rows.Add(row.Id, row);
            }
        }

        /// <summary>精確讀取及 detached 投影；不把可修改種子交給產品再回寫。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) =>
            rows.TryGetValue(id, out var row) && row.LogicalName == entityName
                ? Project(row, columnSet) : throw new InvalidOperationException("Synthetic record missing.");

        /// <summary>按正式 lookup 或值過濾，支援產品實際使用的有界單頁 QueryExpression。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var expression = query as QueryExpression ?? throw new NotSupportedException("Unknown golden query.");
            if (expression.LinkEntities.Count != 0 || expression.Criteria.Filters.Count != 0)
                throw new NotSupportedException("Unknown golden filter graph.");
            var matches = rows.Values.Where(r => r.LogicalName == expression.EntityName)
                .Where(r => expression.Criteria.Conditions.All(c => Matches(r, c)))
                .OrderBy(r => r.Id).Select(r => Project(r, expression.ColumnSet)).ToArray();
            return new EntityCollection(matches);
        }

        /// <summary>只模擬 schema2 待辦持久化觀察；唯一鍵競爭在專用 store 測試另行驗證。</summary>
        public Guid Create(Entity entity)
        {
            if (entity.LogicalName != "new_outboundtask") throw new NotSupportedException("Unknown golden create.");
            Writes.Add(new JObject
            {
                ["operation"] = "Create", ["entity"] = entity.LogicalName, ["alias"] = "qr-task",
                ["fields"] = new JObject
                {
                    ["new_accountid"] = Reference(entity.GetAttributeValue<EntityReference>("new_accountid")),
                    ["new_contactid"] = entity.GetAttributeValue<EntityReference>("new_contactid") is { } contact ? Reference(contact) : JValue.CreateNull(),
                    ["new_payload"] = entity.GetAttributeValue<string>("new_payload")
                }
            });
            return Guid.NewGuid();
        }

        /// <summary>複製完整最小 patch 的所有欄位，新增非預期欄位也會使黃金失敗。</summary>
        public void Update(Entity entity)
        {
            var fields = new JObject();
            foreach (var field in entity.Attributes) fields.Add(field.Key, JToken.FromObject(field.Value));
            Writes.Add(new JObject { ["operation"] = "Update", ["entity"] = entity.LogicalName,
                ["alias"] = aliases[entity.Id], ["fields"] = fields });
        }

        /// <summary>未列入當包讀寫圖的 SDK 訊息明確拒絕，不以預設成功遮蔽新增行為。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();
        /// <summary>本包不刪來源或待辦；任何呼叫均為測試失敗。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>本包不新增關聯；任何呼叫均為測試失敗。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>本包不移除關聯；任何呼叫均為測試失敗。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>同步產品呼叫排空後移除所有種子與寫入，避免測試間保留會員或教會狀態。</summary>
        public void Dispose() { rows.Clear(); aliases.Clear(); Writes.Clear(); }

        /// <summary>正式 ID 僅正規化為明列 seed alias；不依姓名或顯示內容尋找對象。</summary>
        private JObject Reference(EntityReference value) => new() { ["ref"] = aliases[value.Id], ["entity"] = value.LogicalName };

        /// <summary>不實作業務規則，只處理 SDK 精確等值與非空條件；未知操作 fail closed。</summary>
        private static bool Matches(Entity row, ConditionExpression condition)
        {
            row.Attributes.TryGetValue(condition.AttributeName, out var actual);
            if (actual is EntityReference reference) actual = reference.Id;
            return condition.Operator switch
            {
                ConditionOperator.Equal => condition.Values.Count == 1 && Equals(actual, condition.Values[0]),
                ConditionOperator.NotNull => actual is not null,
                _ => throw new NotSupportedException("Unknown golden condition.")
            };
        }

        /// <summary>測試只含純值與 lookup，每次回傳獨立列及 lookup，不共享可變參照。</summary>
        private static Entity Project(Entity source, ColumnSet columns)
        {
            var copy = new Entity(source.LogicalName, source.Id);
            foreach (var field in source.Attributes)
                if (columns.AllColumns || columns.Columns.Contains(field.Key))
                    copy[field.Key] = field.Value is EntityReference reference
                        ? new EntityReference(reference.LogicalName, reference.Id) : field.Value;
            return copy;
        }
    }
}
