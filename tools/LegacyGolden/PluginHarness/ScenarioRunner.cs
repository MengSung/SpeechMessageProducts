/*
檔案整體說明

目的與定位：在 P5-02 離線 harness 中逐情境執行 Roslyn 已編譯的舊入口，將真實
Plugin／Activity 執行結果轉成受限 JSON；此執行器不連接 Dataverse、LINE 或其他外部服務。
主要功能與流程：先驗證有限的 cases/seed/trigger JSON，再為每個情境建立唯一 AppDomain
與 scratch。編譯組件位元組只在該域載入；IPlugin 使用案例獨占 FakeOrganizationService、
execution context、factory 與 tracing，CodeActivity 使用 WorkflowInvoker。唯一的 HTTP
出口是記錄 handler；結果包含實際寫入、LINE 請求、活動輸出或例外型別，完成後才卸載域、
Dispose 假服務並清除 scratch。
使用邊界與維護：呼叫端仍須先以 TransportRewriter 證明舊來源的傳輸與檔案 API 已被攔截，
並於整批成功後執行 OutputSecretScan 及原子發布。每案例資料、ID alias、capture 與暫存目錄
均獨立；逾時、無法卸載或清理失敗時停止整批，不重用不確定執行域，也不回傳部分黃金。
單次情境數、JSON 大小、執行時間與來源組件大小皆有上限。此處的 AppDomain 是舊程式隔離
與確定卸載邊界，不是對敵意 native code 的作業系統沙箱；編譯器須先拒絕 unsafe、P/Invoke、
dynamic、反射與所有未攔截外部 API。驗證以合成 Plugin／Activity 的寫入、訊息、輸出、
逾時、隔離與失敗清理測試為準。
*/
using System;
using System.Activities;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using LegacyGolden.PluginHarness;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;

namespace LegacyGolden.PluginHarness;

/// <summary>
/// 將已安全編譯的組件逐案例交給獨立 AppDomain 執行，並只回傳脫離 SDK 物件的 JSON 結果。
/// </summary>
public static class ScenarioRunner
{
    private const int MaxScenarioBytes = 1024 * 1024;
    private const int MaxCases = 16;
    private const int CaseTimeoutMilliseconds = 30000;

    /// <summary>
    /// 驗證 cases JSON，對每個情境建立、執行並卸載獨立 AppDomain；任何域生命週期失敗都停止整批。
    /// </summary>
    /// <param name="assemblyBytes">Roslyn emit 的舊入口組件；只複製到該案例執行域後載入。</param>
    /// <param name="entryType">Roslyn 語意分析確認的公開 Plugin 或 CodeActivity 型別。</param>
    /// <param name="scenariosJson">有界合成情境；不得含正式組織或真實憑證。</param>
    /// <returns>完整案例結果候選；不執行秘密掃描或檔案發布，責任由呼叫端持有。</returns>
    public static JObject Run(byte[] assemblyBytes, string entryType, string scenariosJson)
    {
        try
        {
            return RunCore(assemblyBytes, entryType, scenariosJson);
        }
        catch (CannotUnloadAppDomainException)
        {
            return Failure("ScenarioDomainUnloadFailed");
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return Failure("ScenarioExecutionInfrastructureFailed:" + exception.GetType().Name);
        }
    }

    /// <summary>建立完整候選；任何案例或 finally 清理失敗均由外層轉成無部分案例的固定失敗。</summary>
    private static JObject RunCore(byte[] assemblyBytes, string entryType, string scenariosJson)
    {
        if (assemblyBytes == null || assemblyBytes.Length == 0 || assemblyBytes.Length > 64 * 1024 * 1024)
        {
            return Failure("CompiledAssemblyInvalid");
        }

        if (string.IsNullOrWhiteSpace(entryType) || entryType.Length > 512
            || string.IsNullOrWhiteSpace(scenariosJson) || scenariosJson.Length > MaxScenarioBytes)
        {
            return Failure("ScenarioInputInvalid");
        }

        JObject document;
        try
        {
            document = JObject.Parse(scenariosJson);
        }
        catch (JsonException)
        {
            return Failure("ScenarioJsonInvalid");
        }

        if (document["cases"] is not JArray cases || cases.Count == 0 || cases.Count > MaxCases)
        {
            return Failure("ScenarioCaseCountInvalid");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var outputs = new JArray();
        foreach (var item in cases)
        {
            if (item is not JObject scenario || scenario["name"]?.Type != JTokenType.String)
            {
                return Failure("ScenarioCaseInvalid");
            }

            var name = scenario.Value<string>("name");
            if (name == null || string.IsNullOrWhiteSpace(name) || name.Length > 80 || !names.Add(name))
            {
                return Failure("ScenarioNameInvalid");
            }

            var scratch = CreateScratchDirectory();
            AppDomain? domain = null;
            Thread? executionThread = null;
            try
            {
                domain = CreateExecutionDomain(name);
                var runner = (ScenarioDomainWorker)domain.CreateInstanceAndUnwrap(
                    typeof(ScenarioDomainWorker).Assembly.FullName!,
                    typeof(ScenarioDomainWorker).FullName!);
                var caseJson = scenario.ToString(Formatting.None);
                var result = InvokeWithTimeout(runner, assemblyBytes, entryType, caseJson, scratch, out executionThread);
                outputs.Add(JObject.Parse(result));
            }
            catch (ScenarioDomainTimeoutException)
            {
                return Failure("ScenarioTimedOut");
            }
            catch (CannotUnloadAppDomainException)
            {
                return Failure("ScenarioDomainUnloadFailed");
            }
            catch (Exception exception) when (IsExpectedFailure(exception))
            {
                return Failure("ScenarioExecutionInfrastructureFailed:" + exception.GetType().Name);
            }
            finally
            {
                try
                {
                    if (domain != null)
                    {
                        AppDomain.Unload(domain);
                    }

                    DrainInvocation(executionThread);
                }
                finally
                {
                    DeleteScratchDirectory(scratch);
                }
            }
        }

        return new JObject
        {
            ["status"] = "Generated",
            ["diagnostics"] = new JArray(),
            ["cases"] = outputs
        };
    }

    /// <summary>由原始建置輸出目錄建立隔離域；測試主機 shadow copy 的 Location 不含完整相依，不能作 probing 根。</summary>
    private static AppDomain CreateExecutionDomain(string scenarioName)
    {
        var assemblyPath = new Uri(typeof(ScenarioRunner).Assembly.CodeBase).LocalPath;
        var setup = new AppDomainSetup
        {
            ApplicationBase = Path.GetDirectoryName(assemblyPath),
            ConfigurationFile = assemblyPath + ".config",
            ShadowCopyFiles = "false"
        };

        return AppDomain.CreateDomain(
            "LegacyGolden-" + StableName(scenarioName) + "-" + Guid.NewGuid().ToString("N"),
            null,
            setup);
    }

    /// <summary>以獨立背景執行緒執行遠端呼叫；逾時即卸載域且禁止執行後續案例。</summary>
    private static string InvokeWithTimeout(
        ScenarioDomainWorker runner,
        byte[] assemblyBytes,
        string entryType,
        string scenarioJson,
        string scratch,
        out Thread executionThread)
    {
        string? result = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = runner.RunCase(assemblyBytes, entryType, scenarioJson, scratch);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
            Name = "LegacyGoldenScenario"
        };
        executionThread = thread;
        thread.Start();

        if (!thread.Join(CaseTimeoutMilliseconds))
        {
            throw new ScenarioDomainTimeoutException();
        }

        if (failure != null)
        {
            throw failure;
        }

        return result ?? throw new InvalidDataException("Scenario runner returned no result.");
    }

    /// <summary>域卸載後等待遠端呼叫執行緒離開，未排空時禁止回傳候選或繼續下個案例。</summary>
    private static void DrainInvocation(Thread? thread)
    {
        if (thread != null && !thread.Join(5000))
        {
            throw new CannotUnloadAppDomainException("Scenario invocation did not drain.");
        }
    }

    /// <summary>建立專屬暫存目錄；名稱不使用情境文字、組織名稱或來源路徑。</summary>
    private static string CreateScratchDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "smv-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>卸載 AppDomain 後只刪除此 runner 建立的暫存目錄；清理失敗視為整批失敗。</summary>
    private static void DeleteScratchDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    /// <summary>將任意情境名稱化為固定域名片段，不把原始輸入或個資放入診斷。</summary>
    private static string StableName(string value)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value))).Replace("-", "").Substring(0, 12);
    }

    /// <summary>建立不含部分案例、路徑、例外文字或原始輸入的固定拒絕結果。</summary>
    private static JObject Failure(string diagnostic) => new()
    {
        ["status"] = "CompileFailed",
        ["diagnostics"] = new JArray(diagnostic)
    };

    /// <summary>只將預期的解析、載入、執行域及路徑錯誤轉為固定拒絕；不吞記憶體耗盡等致命錯誤。</summary>
    private static bool IsExpectedFailure(Exception exception) =>
        exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException
        || exception is InvalidDataException || exception is JsonException || exception is TypeLoadException
        || exception is FileNotFoundException || exception is BadImageFormatException || exception is TargetInvocationException;

    /// <summary>表示單一案例逾時；呼叫端不得重用該執行域或繼續批次。</summary>
    private sealed class ScenarioDomainTimeoutException : Exception
    {
    }
}

/// <summary>遠端執行域唯一入口；跨域只傳 byte[]、string，結果以 JSON 字串複製回主域。</summary>
public sealed class ScenarioDomainWorker : MarshalByRefObject
{
    private int invoked;

    /// <summary>停用 .NET Remoting 無限租約；物件生命週期由案例 AppDomain 卸載決定。</summary>
    public override object InitializeLifetimeService() => null!;

    /// <summary>在目前隔離域載入組件、建立假服務與 capture，並執行單一 Plugin 或 Activity 情境。</summary>
    public string RunCase(byte[] assemblyBytes, string entryType, string scenarioJson, string scratch)
    {
        if (Interlocked.Exchange(ref invoked, 1) != 0)
        {
            throw new InvalidOperationException("Scenario worker cannot be reused.");
        }

        var scenario = JObject.Parse(scenarioJson);
        var trigger = scenario["trigger"] as JObject ?? throw new InvalidDataException("Scenario trigger missing.");
        var aliases = new AliasMap(scenario);
        using var service = new FakeOrganizationService(aliases.ReadSeed());
        using var capture = new ExecutionCapture(scratch, aliases.RecipientAlias);
        WebRequest.DefaultWebProxy = new WebProxy(new Uri("http://127.0.0.1:9"));

        string? exceptionType = null;
        JObject? outputs = null;
        try
        {
            var assembly = Assembly.Load(assemblyBytes);
            var type = assembly.GetType(entryType, throwOnError: true, ignoreCase: false)!;
            if (trigger.Value<bool?>("activity") == true)
            {
                outputs = InvokeActivity(type, trigger["inputs"] as JObject);
            }
            else
            {
                InvokePlugin(type, trigger, aliases, service);
            }
        }
        catch (Exception exception)
        {
            var cause = Unwrap(exception);
            if (cause is FileLoadException || cause is FileNotFoundException || cause is TypeLoadException
                || cause is BadImageFormatException)
            {
                throw new InvalidDataException("Scenario dependency unavailable.");
            }
            exceptionType = cause.GetType().Name;
        }

        return SerializeCase(scenario.Value<string>("name") ?? "", aliases, service, capture, outputs, exceptionType);
    }

    /// <summary>建立案例專屬 context/factory/tracing，並呼叫舊 IPlugin.Execute。</summary>
    private static void InvokePlugin(Type type, JObject trigger, AliasMap aliases, FakeOrganizationService service)
    {
        if (!typeof(IPlugin).IsAssignableFrom(type) || type.IsAbstract)
        {
            throw new InvalidDataException("Plugin entrypoint required.");
        }

        var entityName = Required(trigger, "entity");
        var targetAlias = Required(trigger, "target");
        var target = new Entity(entityName, aliases.IdFor(targetAlias));
        AddFields(target, trigger["fields"] as JObject, aliases);
        var depth = trigger.Value<int?>("depth") ?? 1;
        if (depth < 1 || depth > 100)
        {
            throw new InvalidDataException("Scenario depth invalid.");
        }

        var context = new FakePluginExecutionContext(depth, Required(trigger, "message"), entityName, target.Id);
        context.Object.InputParameters["Target"] = target;
        AddImages(context.Object.PreEntityImages, trigger["images"]?["pre"] as JObject, target, aliases);
        AddImages(context.Object.PostEntityImages, trigger["images"]?["post"] as JObject, target, aliases);
        if (trigger["preImage"] is JObject preImage)
        {
            context.Object.PreEntityImages["PresentRecordLineImage"] = CreateEntity(preImage, aliases);
        }

        if (trigger["postImage"] is JObject postImage)
        {
            context.Object.PostEntityImages["PresentRecordLineImage"] = CreateEntity(postImage, aliases);
        }

        var provider = new FakeServiceProvider()
            .Add<IPluginExecutionContext>(context.Object)
            .Add<IOrganizationServiceFactory>(new ScenarioOrganizationServiceFactory(service))
            .Add<ITracingService>(new FakeTracingService());
        var plugin = (IPlugin)(Activator.CreateInstance(type) ?? throw new InvalidDataException("Plugin activation failed."));
        plugin.Execute(provider);
    }

    /// <summary>依05A情境的具名影像建立獨立Entity；主鍵承接Target，欄位只來自明列影像而非後狀態seed。</summary>
    private static void AddImages(EntityImageCollection destination, JObject? images, Entity target, AliasMap aliases)
    {
        if (images == null)
        {
            return;
        }

        if (images.Count > 16)
        {
            throw new InvalidDataException("Scenario image capacity exceeded.");
        }

        foreach (var property in images.Properties())
        {
            if (property.Value is not JObject fields || string.IsNullOrWhiteSpace(property.Name) || property.Name.Length > 256)
            {
                throw new InvalidDataException("Scenario image invalid.");
            }

            var image = new Entity(target.LogicalName, target.Id);
            AddFields(image, fields, aliases);
            destination.Add(property.Name, image);
        }
    }

    /// <summary>使用 WorkflowInvoker 執行真實 CodeActivity，輸入值依公開 InArgument 型別轉換。</summary>
    private static JObject InvokeActivity(Type type, JObject? input)
    {
        if (!typeof(CodeActivity).IsAssignableFrom(type) || type.IsAbstract)
        {
            throw new InvalidDataException("Activity entrypoint required.");
        }

        var activity = (CodeActivity)(Activator.CreateInstance(type) ?? throw new InvalidDataException("Activity activation failed."));
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        if (input != null)
        {
            foreach (var property in input.Properties())
            {
                var argument = type.GetProperty(property.Name, BindingFlags.Instance | BindingFlags.Public);
                if (argument == null || !argument.PropertyType.IsGenericType
                    || argument.PropertyType.GetGenericTypeDefinition() != typeof(InArgument<>))
                {
                    throw new InvalidDataException("Activity input name invalid.");
                }

                var argumentType = argument.PropertyType.GetGenericArguments()[0];
                values[property.Name] = property.Value.ToObject(argumentType)
                    ?? throw new InvalidDataException("Activity input value invalid.");
            }
        }

        var result = WorkflowInvoker.Invoke(activity, values);
        var outputs = new JObject();
        foreach (var value in result.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            outputs[value.Key] = SafeToken(value.Value, null);
        }

        return outputs;
    }

    /// <summary>以正式結果只保存寫入呼叫的 patch 與穩定 alias，不序列化 SDK Entity 內部物件。</summary>
    private static string SerializeCase(
        string name,
        AliasMap aliases,
        FakeOrganizationService service,
        ExecutionCapture capture,
        JObject? outputs,
        string? exceptionType)
    {
        var writes = new JArray();
        foreach (var record in service.WriteRecords)
        {
            var row = new JObject
            {
                ["operation"] = record.Operation,
                ["entity"] = record.EntityName,
                ["alias"] = aliases.AliasFor(record.Id)
            };
            var fields = new JObject();
            if (record.Entity is Entity entity)
            {
                foreach (var attribute in entity.Attributes.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    fields[attribute.Key] = SafeToken(attribute.Value, aliases);
                }
            }

            row["fields"] = fields;
            writes.Add(row);
        }

        return new JObject
        {
            ["name"] = name,
            ["writes"] = writes,
            ["lineCalls"] = capture.Calls.DeepClone(),
            ["outputs"] = outputs ?? new JObject(),
            ["exception"] = exceptionType == null ? JValue.CreateNull() : new JValue(exceptionType)
        }.ToString(Formatting.None);
    }

    /// <summary>轉成不含 SDK 或未驗證 GUID 的 JSON scalar/alias；未知型別拒絕，不能靜默丟欄位。</summary>
    private static JToken SafeToken(object? value, AliasMap? aliases)
    {
        switch (value)
        {
            case null: return JValue.CreateNull();
            case string text: return new JValue(text);
            case bool flag: return new JValue(flag);
            case byte or short or int or long or float or double or decimal: return new JValue(value);
            case Guid id: return new JValue(aliases?.AliasFor(id) ?? throw new InvalidDataException("Unknown output identity."));
            case DateTime date: return new JValue(date.ToString("O", CultureInfo.InvariantCulture));
            case DateTimeOffset date: return new JValue(date.ToString("O", CultureInfo.InvariantCulture));
            case Money money: return new JValue(money.Value);
            case OptionSetValue option: return new JValue(option.Value);
            case EntityReference reference:
                return new JObject
                {
                    ["entity"] = reference.LogicalName,
                    ["alias"] = aliases?.AliasFor(reference.Id) ?? throw new InvalidDataException("Unknown reference identity."),
                    ["name"] = reference.Name
                };
            case EntityCollection collection:
                var rows = new JArray();
                foreach (var entity in collection.Entities)
                {
                    rows.Add(new JObject
                    {
                        ["entity"] = entity.LogicalName,
                        ["alias"] = aliases?.AliasFor(entity.Id) ?? throw new InvalidDataException("Unknown collection identity.")
                    });
                }

                return rows;
            default:
                throw new InvalidDataException("Unsupported output value.");
        }
    }

    /// <summary>建立情境指定 Entity；每個 Entity 必須使用已宣告 alias 與 schema 名稱。</summary>
    private static Entity CreateEntity(JObject data, AliasMap aliases)
    {
        var logicalName = Required(data, "entity");
        var alias = Required(data, "alias");
        var entity = new Entity(logicalName, aliases.IdFor(alias));
        AddFields(entity, data["fields"] as JObject, aliases);
        return entity;
    }

    /// <summary>遞迴投影欄位值；只接受合成 alias、primitive 與明列 SDK 包裝物。</summary>
    private static void AddFields(Entity entity, JObject? fields, AliasMap aliases)
    {
        if (fields == null)
        {
            return;
        }

        if (fields.Properties().Count() > 256)
        {
            throw new InvalidDataException("Scenario field count exceeded.");
        }

        foreach (var field in fields.Properties())
        {
            entity[field.Name] = ReadValue(field.Value, aliases);
        }
    }

    /// <summary>將明列合成 JSON 值轉成 SDK 欄位；不依欄位名稱猜型別或主鍵。</summary>
    private static object? ReadValue(JToken token, AliasMap aliases)
    {
        if (token is JValue scalar)
        {
            return scalar.Value;
        }

        if (token is JArray array)
        {
            if (array.Count > 256) throw new InvalidDataException("Scenario array count exceeded.");
            return array.Select(item => ReadValue(item, aliases)).ToArray();
        }

        if (token is JObject value)
        {
            if (value["ref"]?.Type == JTokenType.String)
            {
                return new EntityReference(Required(value, "entity"), aliases.IdFor(Required(value, "ref")))
                {
                    Name = value.Value<string>("name")
                };
            }

            if (value.Count == 1 && value["line"]?.Type == JTokenType.String)
            {
                return aliases.LineIdFor(Required(value, "line"));
            }

            if (value["reference"] is JObject reference)
            {
                var entity = Required(reference, "entity");
                var alias = Required(reference, "alias");
                return new EntityReference(entity, aliases.IdFor(alias))
                {
                    Name = reference.Value<string>("name")
                };
            }

            if (value.Count == 1 && value["money"] != null)
            {
                return new Money(value["money"]!.Value<decimal>());
            }

            if (value.Count == 1 && value["option"] != null)
            {
                return new OptionSetValue(value["option"]!.Value<int>());
            }

            throw new InvalidDataException("Scenario object value requires an explicit type marker.");
        }

        throw new InvalidDataException("Scenario value invalid.");
    }

    /// <summary>讀取必填字串欄位並限制長度，避免把任意 JSON 值當成 schema 或入口名稱。</summary>
    private static string Required(JObject source, string name)
    {
        var value = source.Value<string>(name);
        if (value == null || string.IsNullOrWhiteSpace(value) || value.Length > 256)
        {
            throw new InvalidDataException("Scenario required value missing.");
        }

        return value;
    }

    /// <summary>只取最內層例外型別，不回傳原始訊息或 stack trace。</summary>
    private static Exception Unwrap(Exception exception)
    {
        while (exception.InnerException != null
            && (exception is TargetInvocationException || exception is TypeInitializationException
                || exception is System.Reflection.ReflectionTypeLoadException))
        {
            exception = exception.InnerException!;
        }

        return exception;
    }

    /// <summary>提供 IOrganizationServiceFactory 的案例獨占轉接；不建立或保存連線。</summary>
    private sealed class ScenarioOrganizationServiceFactory : IOrganizationServiceFactory
    {
        private readonly IOrganizationService service;

        /// <summary>以單一案例 service 建立 factory，所有 userId 都只在本案例域有效。</summary>
        internal ScenarioOrganizationServiceFactory(IOrganizationService service) => this.service = service;

        /// <summary>回傳本案例 fake；不建立 D365 client 或跨案例狀態。</summary>
        public IOrganizationService CreateOrganizationService(Guid? userId) => service;
    }

    /// <summary>單一案例 alias 對照；ID 與 LINE 測試值只在目前 AppDomain 存活。</summary>
    private sealed class AliasMap
    {
        private const int MaximumSeedCount = 4096;
        private readonly Dictionary<string, Guid> idsByAlias = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, string> aliasesById = new();
        private readonly Dictionary<string, string> lineAliasesById = new(StringComparer.Ordinal);
        private readonly List<(string Alias, Entity Entity)> seeds = new();

        /// <summary>解析 seed alias 並建立固定 ID 對照；未知 schema 或重複 alias 立即拒絕。</summary>
        internal AliasMap(JObject scenario)
        {
            if (scenario["seed"] is not JArray seed || seed.Count > MaximumSeedCount)
            {
                throw new InvalidDataException("Scenario seed invalid.");
            }

            foreach (var item in seed)
            {
                if (item is not JObject record)
                {
                    throw new InvalidDataException("Scenario seed row invalid.");
                }

                var alias = Required(record, "alias");
                if (idsByAlias.ContainsKey(alias))
                {
                    throw new InvalidDataException("Scenario alias duplicated.");
                }

                var id = DeterministicId(alias);
                idsByAlias.Add(alias, id);
                aliasesById.Add(id, alias);
                seeds.Add((alias, new Entity(Required(record, "entity"), id)));
            }

            foreach (var item in seed.OfType<JObject>())
            {
                var alias = Required(item, "alias");
                var entity = seeds.Single(row => row.Alias == alias).Entity;
                AddFields(entity, item["fields"] as JObject, this);
            }
        }

        /// <summary>回傳 seed 複本，FakeOrganizationService 由本次案例擁有並再做深層複製。</summary>
        internal Entity[] ReadSeed() => seeds.Select(item => FakeOrganizationService.CloneEntity(item.Entity, 0)).ToArray();

        /// <summary>解析情境 alias；不得因顯示名稱相同或缺漏而生成新識別。</summary>
        internal Guid IdFor(string alias) => idsByAlias.TryGetValue(alias, out var id)
            ? id
            : throw new InvalidDataException("Scenario alias unknown.");

        /// <summary>把實際 SDK GUID 轉回情境 alias；本次新建記錄以穩定順序配置合成名稱。</summary>
        internal string AliasFor(Guid id)
        {
            if (aliasesById.TryGetValue(id, out var alias))
            {
                return alias;
            }

            var generated = "created-" + (aliasesById.Count(item => item.Value.StartsWith("created-", StringComparison.Ordinal)) + 1)
                .ToString(CultureInfo.InvariantCulture);
            aliasesById[id] = generated;
            return generated;
        }

        /// <summary>只在案例記憶體中產生 LINE 假 ID，並保留反向 alias 供記錄輸出使用。</summary>
        internal string LineIdFor(string alias)
        {
            var lineId = "U" + IdFor(alias).ToString("N");
            lineAliasesById[lineId] = alias;
            return lineId;
        }

        /// <summary>只把已登錄合成身分映回alias；單contact測試僅接受固定全零測試ID，未知舊開發者ID必須拒絕。</summary>
        internal string RecipientAlias(string recipient)
        {
            if (lineAliasesById.TryGetValue(recipient, out var alias))
            {
                return alias;
            }

            if (string.Equals(recipient, "U00000000000000000000000000000001", StringComparison.Ordinal))
            {
                var contacts = seeds.Where(item => item.Entity.LogicalName == "contact").Select(item => item.Alias).ToArray();
                if (contacts.Length == 1)
                {
                    return contacts[0];
                }
            }

            throw new InvalidDataException("Scenario LINE recipient alias unknown.");
        }

        /// <summary>由 alias 的 SHA-256 前綴建立固定 GUID，讓重跑結果不受隨機值影響。</summary>
        private static Guid DeterministicId(string alias)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(alias));
            var id = new Guid(bytes.Take(16).ToArray());
            return id == Guid.Empty ? new Guid("00000000-0000-0000-0000-000000000001") : id;
        }
    }
}
