/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P5-10 真實 Core adapter 的欄位投影、重跑與資料隔離契約。
 * 本檔使用嚴格投影的同步 SDK 替身，避免全欄回應掩蓋產品漏讀既有值的問題；
 * 不是退役 DLL 執行或 goodshepherdback 的影子比對證據。
 *
 * 主要流程：合成正式 contact、account 及 list 主鍵，每次 Retrieve 只回傳要求的
 * ColumnSet；Update 實際套用到案例資料，再次執行服務必須沒有第二次寫入。
 * 故障注入包含錯誤 logical name、原始 Update 例外及 A/B/A 順序交錯；
 * 斷言完整寫入次數、日期不推進、錯誤原實例及個案互不保留。
 *
 * 使用邊界與維護：替身由每個案例 using 擁有，Dispose 排空資料與寫入紀錄，
 * 產品僅借用 SDK，不得提前 Dispose。沒有網路、秘密、背景工作、timer 或共享快取。
 * 所有日期取教會時區，案例執行當日只用作同次服務觀察；跨日純規則另用固定值測試。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Care;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Care;

/// <summary>保護服務真正讀寫資料的契約，替身不代算任何生日、年資或分組規則。</summary>
public sealed class CareServiceTests
{
    /// <summary>
    /// 逐案執行真服務，以情境的固定 UTC 及正式教會時區核對全部 patch；
    /// 期望來自舊原始碼閱讀，不從產品執行結果重生，非閏年例外不得留下部分更新。
    /// </summary>
    [Fact]
    public void Care_Scenarios_MatchExpected()
    {
        var scenarios = Read("scenarios");
        var expected = Read("expected");
        Assert.Equal("reading", (string?)expected["basis"]);
        var cases = (JArray)scenarios["cases"]!;
        Assert.InRange(cases.Count, 3, 6);
        Assert.Equal(cases.Count, ((JArray)expected["cases"]!).Count);
        for (var index = 0; index < cases.Count; index++)
        {
            var scenario = cases[index];
            var contract = expected["cases"]![index]!;
            using var sdk = new CareSdk();
            foreach (var seed in (JArray)scenario["seed"]!)
            {
                var row = (string?)seed["alias"] switch
                {
                    "record" => sdk.Contact,
                    "church-a" => sdk.Church,
                    "group" => sdk.Group,
                    _ => throw new InvalidDataException("Unknown Care seed identity.")
                };
                foreach (var field in ((JObject)seed["fields"]!).Properties())
                    row[field.Name] = Parse(field.Value, sdk);
            }
            var clock = new ChurchClock(new FixedClock(DateTimeOffset.Parse((string)scenario["now"]!, CultureInfo.InvariantCulture)));
            var failure = Record.Exception(() =>
            {
                if ((string?)scenario["trigger"]!["project"] == "NewPersonCareWorkflow")
                    NewPersonCareAssignmentService.Execute(sdk, sdk.Contact.Id, clock);
                else ContactAnniversaryService.Execute(sdk, sdk.Contact.Id, clock,
                    (string?)scenario["trigger"]!["project"] == "RecursiveWorkflow"
                        ? ContactAnniversaryKind.Onboarding : ContactAnniversaryKind.Birthday);
            });
            Assert.Equal((string?)scenario["name"], (string?)contract["name"]);
            Assert.Equal((string?)contract["exception"], failure?.GetType().Name);
            var writes = new JArray();
            foreach (var update in sdk.Updates)
            {
                var fields = new JObject();
                foreach (var field in update.Attributes)
                    fields[field.Key] = field.Value switch
                    {
                        DateTime date => date.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                        OptionSetValue option => new JValue(option.Value),
                        _ => JToken.FromObject(field.Value)
                    };
                writes.Add(new JObject { ["operation"] = "Update", ["entity"] = "contact", ["alias"] = "record", ["fields"] = fields });
            }
            Assert.True(JToken.DeepEquals(contract["writes"], writes), "Care reading writes differ: " + (string?)scenario["name"]);
            Assert.Empty((JArray)contract["lineCalls"]!);
        }
    }

    /// <summary>情境僅允許本測試明列的 SDK 純值型別與正式 lookup，未知輸入拒絕。</summary>
    private static object Parse(JToken value, CareSdk sdk)
    {
        if (value is JObject lookup)
            return (string?)lookup["ref"] switch
            {
                "church-a" => sdk.Church.ToEntityReference(),
                "group" => sdk.Group.ToEntityReference(),
                _ => throw new InvalidDataException("Unknown Care lookup.")
            };
        if (value.Type == JTokenType.String && DateTime.TryParseExact((string)value!, "yyyy-MM-ddTHH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
        return ((JValue)value).Value ?? throw new InvalidDataException("Null Care seed.");
    }

    /// <summary>嵌入 JSON 每次獨立讀取，容量最多一 MiB，reader/stream 同步 using 關閉。</summary>
    private static JObject Read(string kind)
    {
        var assembly = typeof(CareServiceTests).Assembly;
        var name = assembly.GetManifestResourceNames().Single(x => x.EndsWith(".care." + kind + ".json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("Missing Care golden.");
        if (stream.Length > 1048576) throw new InvalidDataException("Care golden exceeds capacity.");
        using var text = new StreamReader(stream, new UTF8Encoding(false, true));
        using var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None, MaxDepth = 32 };
        var result = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        if (reader.Read()) throw new InvalidDataException("Trailing Care content.");
        return result;
    }

    /// <summary>每案不可變 UTC 來源，僅被當次 ChurchClock 借用，沒有跨案例狀態或資源。</summary>
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        /// <summary>提供情境指定的瞬間，時區轉換仍執行正式 ChurchClock。</summary>
        public DateTimeOffset UtcNow { get; } = now;
    }
    /// <summary>真實投影及回寫後再跑，必須零重複更新且下一次年資更新日不再前進。</summary>
    [Fact]
    public void Care_Anniversary_ServiceRerun_NoDuplicateWrite()
    {
        using var sdk = new CareSdk();
        sdk.Contact["birthdate"] = new DateTime(1990, 1, 1);
        sdk.Contact["new_onboard_date"] = new DateTime(2024, 1, 1);
        ContactAnniversaryService.Execute(sdk, sdk.Contact.Id);
        var next = sdk.Contact.GetAttributeValue<DateTime>("new_next_update_onboard_date");
        Assert.Single(sdk.Updates);
        ContactAnniversaryService.Execute(sdk, sdk.Contact.Id);
        Assert.Single(sdk.Updates);
        Assert.Equal(next, sdk.Contact.GetAttributeValue<DateTime>("new_next_update_onboard_date"));
    }

    /// <summary>分組服務必須讀回已寫的日期與歷程；第二次執行不得產生另一筆 Update。</summary>
    [Fact]
    public void Care_NewPerson_ServiceRerun_NoDuplicateWrite()
    {
        using var sdk = new CareSdk();
        NewPersonCareAssignmentService.Execute(sdk, sdk.Contact.Id);
        Assert.Single(sdk.Updates);
        NewPersonCareAssignmentService.Execute(sdk, sdk.Contact.Id);
        Assert.Single(sdk.Updates);
    }

    /// <summary>相同 ID 的其他表不代表合法 contact，必須在後續查詢與寫入前拒絕。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Care_ServiceWrongLogicalName_RejectsBeforeWrite(bool anniversary)
    {
        using var sdk = new CareSdk();
        sdk.WrongContactType = true;
        Assert.Throws<InvalidOperationException>(() =>
        {
            if (anniversary) ContactAnniversaryService.Execute(sdk, sdk.Contact.Id);
            else NewPersonCareAssignmentService.Execute(sdk, sdk.Contact.Id);
        });
        Assert.Equal(1, sdk.Reads);
        Assert.Empty(sdk.Updates);
    }

    /// <summary>失敗保留原始例外，A/B/A 執行不把上一個來源寫入或例外狀態帶給下一個來源。</summary>
    [Fact]
    public void Care_ServiceFaultThenOtherScope_NoRetainedState()
    {
        using var first = new CareSdk();
        using var second = new CareSdk();
        var failure = new InvalidOperationException("Synthetic update failure.");
        first.UpdateFailure = failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            ContactAnniversaryService.Execute(first, first.Contact.Id)));
        ContactAnniversaryService.Execute(second, second.Contact.Id);
        Assert.Equal(second.Contact.Id, Assert.Single(second.Updates).Id);
        first.UpdateFailure = null;
        ContactAnniversaryService.Execute(first, first.Contact.Id);
        Assert.Equal(first.Contact.Id, Assert.Single(first.Updates).Id);
    }

    /// <summary>每案唯一資料擁有者；只接受精確主鍵及要求欄位，未知 SDK 操作拒絕。</summary>
    private sealed class CareSdk : IOrganizationService, IDisposable
    {
        private readonly Entity church = new("account", Guid.NewGuid()) { ["new_timezone"] = "Asia/Taipei" };
        private readonly Entity list = new("list", Guid.NewGuid()) { ["listname"] = "合成關懷小組" };
        /// <summary>當案正式教會，僅供合成種子設定時區，不能跨案例共享。</summary>
        internal Entity Church => church;
        /// <summary>當案正式名單，僅供合成種子設定業務分類。</summary>
        internal Entity Group => list;
        /// <summary>本案例權威 contact，僅由測試設種子與 Update 改變。</summary>
        internal Entity Contact { get; }
        /// <summary>當案成功寫入的 detached patch，Dispose 清空。</summary>
        internal List<Entity> Updates { get; } = new();
        /// <summary>讀取次數用來證明錯誤主表身分立即拒絕。</summary>
        internal int Reads { get; private set; }
        /// <summary>注入同 ID 不同表回應，不能因為 GUID 相同就信任。</summary>
        internal bool WrongContactType { get; set; }
        /// <summary>只在本案例存活的寫入故障，不影響另一個 SDK。</summary>
        internal Exception? UpdateFailure { get; set; }

        /// <summary>建立有正式教會與名單 lookup 的合成 contact，不以顯示名稱授權。</summary>
        internal CareSdk() => Contact = new Entity("contact", Guid.NewGuid())
        {
            ["parentcustomerid"] = church.ToEntityReference(),
            ["new_cell_list_contact"] = list.ToEntityReference()
        };

        /// <summary>嚴格最小投影；未讀取欄位不會因測試便利而出現在回應。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            Reads++;
            Assert.False(columnSet.AllColumns);
            var source = entityName == "contact" && id == Contact.Id ? Contact
                : entityName == "account" && id == church.Id ? church
                : entityName == "list" && id == list.Id ? list
                : throw new InvalidOperationException("Unknown synthetic identity.");
            var result = new Entity(WrongContactType && source == Contact ? "account" : source.LogicalName, id);
            foreach (var name in columnSet.Columns)
                if (source.Contains(name)) result[name] = Copy(source[name]);
            return result;
        }

        /// <summary>先故障再發布；成功後把獨立 patch 套用來源，以驗證第二次真實讀回。</summary>
        public void Update(Entity entity)
        {
            if (UpdateFailure is not null) throw UpdateFailure;
            Assert.Equal("contact", entity.LogicalName);
            Assert.Equal(Contact.Id, entity.Id);
            var patch = new Entity(entity.LogicalName, entity.Id);
            foreach (var pair in entity.Attributes)
            {
                Contact[pair.Key] = Copy(pair.Value);
                patch[pair.Key] = Copy(pair.Value);
            }
            Updates.Add(patch);
        }

        /// <summary>複製案例支援的可變 SDK 值，不讓產品持有來源字典內的 option 或 lookup。</summary>
        private static object Copy(object value) => value switch
        {
            EntityReference reference => new EntityReference(reference.LogicalName, reference.Id),
            OptionSetValue option => new OptionSetValue(option.Value),
            _ => value
        };
        /// <summary>案例結束清空所有可變來源、故障與記錄；無借用資源需要關閉。</summary>
        public void Dispose() { Updates.Clear(); Contact.Attributes.Clear(); church.Attributes.Clear(); list.Attributes.Clear(); UpdateFailure = null; }
        /// <summary>關懷欄位服務不建立資料列。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>關懷欄位服務不刪除資料列。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>關懷欄位服務不執行其他 SDK 訊息。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();
        /// <summary>關懷欄位服務不需集合查詢。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new NotSupportedException();
        /// <summary>關懷欄位服務不新增關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>關懷欄位服務不移除關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}
