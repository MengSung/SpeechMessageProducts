/*
 * 檔案整體說明
 * 目的與定位：P5-11 雙目標 Core 與 net48 外殼共用的 reading 合成情境 owner。
 * 固定兩份內嵌 JSON 由人工閱讀舊來源建立，不從產品輸出生成，亦不載入舊 DLL。
 * 主要流程：種子只支援純值、正式 lookup、OptionSet、Money 與 UTC 日期，按所求
 * 欄位建立 detached 回應；首次故障及第二次會友讀取教會變動明確注入。SDK 一律
 * 拒絕寫入／查詢／未知操作，最多十六次精確讀取且任何 token 欄位皆拒絕。
 * 使用邊界與維護：每次 Read 返回獨立 JSON，fixture 獨占列、故障與有限通知摘要，
 * using 同步排空後 Dispose 清除所有集合與參照，無網路、Session、Timer、秘密
 * 或背景工作。CAS 子域可直接使用，斷言以固定 InvalidOperationException 回報，
 * 不依賴 Moq／動態代理。這是離線規則與外殼證據，不宣稱 Dataverse 實機送達。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests;

/// <summary>兩測試宿主共用的單案 owner；清理後禁止重新讀取或記錄。</summary>
internal sealed class FeesGoldenFixture : IOrganizationService, IShadowPushSink, IDisposable
{
    private readonly Dictionary<Guid, Entity> rows = new();
    private readonly string? fault;
    private int memberReads;
    private bool disposed;

    /// <summary>選定固定情境並建立獨立正式 ID 種子，不保留呼叫者 JSON 參照。</summary>
    internal FeesGoldenFixture(JToken scenario)
    {
        fault = (string?)scenario["fault"];
        Message = (string)scenario["trigger"]!["message"]!;
        foreach (var seed in (JArray)scenario["seed"]!)
        {
            var row = new Entity((string)seed["entity"]!, Id((string)seed["alias"]!));
            foreach (var field in ((JObject)seed["fields"]!).Properties())
                row[field.Name] = Parse(field.Value);
            rows.Add(row.Id, row);
        }
        ReadFailure = fault == "first-read" ? new InvalidOperationException("Synthetic private fee failure.") : null;
    }

    /// <summary>平台訊息只用於選擇舊可達分支，不提供授權。</summary>
    internal string Message { get; }
    /// <summary>同一故障實例須穿越 Core／WF，不許被字串或新例外取代。</summary>
    internal Exception? ReadFailure { get; private set; }
    /// <summary>本案讀取次數；可檢查正常拒絕先於頻道讀取。</summary>
    internal int Reads { get; private set; }
    /// <summary>任何真寫入嘗試均先計數再拒絕，不能只看成功寫入。</summary>
    internal int WriteAttempts { get; private set; }
    /// <summary>僅保存當次一則安全意圖，由 owner 清除。</summary>
    internal JArray Calls { get; } = new();

    /// <summary>固定資源每次獨立讀取，限制容量、深度及日期／decimal 解析。</summary>
    internal static JObject Read(string kind)
    {
        if (kind is not ("scenarios" or "expected")) throw new ArgumentException("Unknown fee fixture.", nameof(kind));
        var assembly = typeof(FeesGoldenFixture).Assembly;
        var name = assembly.GetManifestResourceNames().Single(x => x.EndsWith(".fees." + kind + ".json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("Missing fee fixture.");
        if (stream.Length > 1048576) throw new InvalidDataException("Fee fixture exceeds capacity.");
        using var text = new StreamReader(stream, new UTF8Encoding(false, true));
        using var reader = new JsonTextReader(text) { MaxDepth = 32, DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal };
        return JObject.Load(reader);
    }

    /// <summary>別名只供離線種子；固定 SHA 前十六 bytes，不以產品事件鍵產生器自我比對。</summary>
    internal static Guid Id(string alias)
    {
        if (string.IsNullOrEmpty(alias) || alias.Length > 128) throw new ArgumentException("Invalid fee alias.", nameof(alias));
#if NET10_0_OR_GREATER
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(alias));
#else
        using var hash = SHA256.Create();
        var digest = hash.ComputeHash(Encoding.UTF8.GetBytes(alias));
#endif
        var bytes = new byte[16]; Array.Copy(digest, bytes, bytes.Length);
        return new Guid(bytes);
    }

    /// <summary>只解析固定情境所需型別，未知巢狀資料一律拒絕。</summary>
    private static object Parse(JToken value)
    {
        if (value is JObject row)
        {
            if (row["ref"] is not null) return new EntityReference((string)row["entity"]!, Id((string)row["ref"]!));
            if (row["option"] is not null) return new OptionSetValue((int)row["option"]!);
            if (row["money"] is not null) return new Microsoft.Xrm.Sdk.Money((decimal)row["money"]!);
            throw new InvalidDataException("Unknown fee seed value.");
        }
        var text = (string)value!;
        return DateTime.TryParseExact(text, "yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date) ? date : text;
    }

    /// <summary>最小有界讀取，只回傳本次所求欄位的獨立純值，不返回來源 Entity。</summary>
    public Entity Retrieve(string entityName, Guid id, ColumnSet columns)
    {
        Require(!disposed && ++Reads <= 16 && !columns.AllColumns &&
            columns.Columns.All(column => column.IndexOf("token", StringComparison.OrdinalIgnoreCase) < 0),
            "bounded token-free retrieve");
        if (ReadFailure is not null) throw ReadFailure;
        Require(rows.TryGetValue(id, out var row) && row.LogicalName == entityName, "exact authoritative identity");
        var result = new Entity(entityName, id);
        foreach (var field in columns.Columns)
        {
            if (row!.Attributes.TryGetValue(field, out var value))
                result[field] = value switch
                {
                    EntityReference reference => new EntityReference(reference.LogicalName, reference.Id),
                    OptionSetValue option => new OptionSetValue(option.Value),
                    Microsoft.Xrm.Sdk.Money money => new Microsoft.Xrm.Sdk.Money(money.Value),
                    _ => value
                };
        }
        if (entityName == "contact" && ++memberReads == 2 && fault == "recipient-church-change")
            result["parentcustomerid"] = new EntityReference("account", Id("other-church"));
        return result;
    }

    /// <summary>把安全投影正規化回情境別名後保存，最多一人一則，不保存訊息原文或 LINE ID。</summary>
    public void Record(Guid contactId, string templateKey, Guid retryKey, int messageCount, string contentSha256)
    {
        Require(!disposed && Calls.Count == 0 && contactId == Id("member") && retryKey == Id("record"), "single authoritative push");
        Calls.Add(new JObject
        {
            ["method"] = "POST", ["endpoint"] = "/v2/bot/message/push",
            ["recipients"] = new JArray("member"), ["messageCount"] = messageCount,
            ["shadow"] = new JObject { ["retryKey"] = "record", ["templateKey"] = templateKey, ["contentSha256"] = contentSha256 }
        });
    }

    /// <summary>回報固定契約名稱，不在例外放來源個資或設定值。</summary>
    internal static void Require(bool condition, string contract)
    { if (!condition) throw new InvalidOperationException("Fee fixture mismatch: " + contract); }

    /// <summary>清除所有當案資料、故障與安全摘要；重複 Dispose 無害，不處置外部資源。</summary>
    public void Dispose()
    {
        disposed = true;
        foreach (var row in rows.Values) row.Attributes.Clear();
        rows.Clear(); Calls.Clear(); ReadFailure = null; Reads = 0; memberReads = 0;
    }

    /// <summary>通知模組絕不真寫入；違規嘗試不能被當成無副作用成功。</summary>
    private InvalidOperationException RefuseWrite() { WriteAttempts++; return new InvalidOperationException("Unexpected fee notification write."); }
    /// <summary>拒絕建立資料列。</summary>
    public Guid Create(Entity entity) => throw RefuseWrite();
    /// <summary>拒絕更新資料列。</summary>
    public void Update(Entity entity) => throw RefuseWrite();
    /// <summary>拒絕刪除資料列。</summary>
    public void Delete(string entityName, Guid id) => throw RefuseWrite();
    /// <summary>拒絕其他訊息及穿透的隱性寫入。</summary>
    public OrganizationResponse Execute(OrganizationRequest request) => throw RefuseWrite();
    /// <summary>通知不需集合掃描，未知查詢立即拒絕。</summary>
    public EntityCollection RetrieveMultiple(QueryBase query) => throw new InvalidOperationException("Unexpected fee notification query.");
    /// <summary>拒絕新增關聯。</summary>
    public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw RefuseWrite();
    /// <summary>拒絕移除關聯。</summary>
    public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw RefuseWrite();
}
