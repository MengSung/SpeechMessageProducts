/*
 * 檔案整體說明
 *
 * 目的與定位：P6-07 的可見性相容契約，在型別改 internal 之前鎖住主機公開
 * 控制器的路由宣告與所有產品類別／結構的雙序列化器屬性形狀；不是網站實機驗收。
 * 主要功能與流程：由已建置的 ChurchReport 組件讀取型別及框架屬性，不建立模型、
 * 不呼叫 action 或屬性 getter。路由保留繼承自產品基底的方法，排除 NonAction、
 * 特殊方法與框架基底；形狀以 Newtonsoft DefaultContractResolver 及 System.Text.Json
 * 預設 metadata 解析器取得名稱，不記可見性。依完整名稱以 Ordinal 排序並比較原始位元組。
 * 使用邊界與維護：只有本機明確設定 SPEECHMESSAGE_R2_WRITE_GOLDEN=1 才寫檔；
 * 正常測試、guard 與 CI 唯讀，缺檔或任何形狀差異都失敗。Release 與 Debug 各自保存
 * 黃金，不以一種組態掩蓋另一種。來源根由最近方案定位，禁止讀到父工作樹。
 * 不讀設定、秘密或個資，不連 D365／LINE，不啟動 Host。反射資料與解析器只由單次
 * 測試擁有，大小限於產品組件型別集合，檔案存取立即關閉，沒有背景作業或資源 owner。
 */
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Architecture;

/// <summary>以改動前提交的固定黃金逐位元組保護 MVC 公開入口與兩種既有 JSON 形狀。</summary>
public sealed class R2VisibilityContractTests
{
    /// <summary>
    /// 實際反射公開非抽象控制器及產品宣告的可執行公開實例方法，包含產品基底繼承。
    /// 任一 action、參數型別、HTTP 方法／範本、Area 或類別／方法 Route 更動都必須失敗。
    /// </summary>
    [Fact]
    public void R2_RouteTable_UnchangedAfterVisibilityChange()
    {
        var assembly = typeof(global::ChurchReport.Program).Assembly;
        var rows = new List<string>();
        foreach (var controller in assembly.GetTypes().Where(type => type.IsVisible && !type.IsAbstract
            && typeof(ControllerBase).IsAssignableFrom(type)))
        {
            var classRoutes = controller.GetCustomAttributes<RouteAttribute>(true)
                .Select(route => route.Template).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            var areas = controller.GetCustomAttributes<AreaAttribute>(true)
                .Select(area => area.RouteValue).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => !method.IsSpecialName && method.DeclaringType?.Assembly == assembly
                    && !method.IsDefined(typeof(NonActionAttribute), true)))
            {
                var http = method.GetCustomAttributes<HttpMethodAttribute>(true)
                    .Select(attribute => string.Join(",", attribute.HttpMethods.OrderBy(value => value, StringComparer.Ordinal))
                        + ":" + JsonSerializer.Serialize(attribute.Template))
                    .OrderBy(value => value, StringComparer.Ordinal).ToArray();
                var routes = method.GetCustomAttributes<RouteAttribute>(true)
                    .Select(route => route.Template).OrderBy(value => value, StringComparer.Ordinal).ToArray();
                rows.Add(FullName(controller) + "|" + method.Name + "|parameters="
                    + string.Join(",", method.GetParameters().Select(parameter => FullName(parameter.ParameterType)))
                    + "|http=" + JsonSerializer.Serialize(http)
                    + "|classRoute=" + JsonSerializer.Serialize(classRoutes)
                    + "|methodRoute=" + JsonSerializer.Serialize(routes)
                    + "|area=" + JsonSerializer.Serialize(areas)
                    + "|methodArea=" + JsonSerializer.Serialize(method.GetCustomAttributes<AreaAttribute>(true)
                        .Select(area => area.RouteValue).OrderBy(value => value, StringComparer.Ordinal).ToArray()));
            }
        }
        Assert.NotEmpty(rows);
        MatchGolden("r2-route-table", ".txt", string.Join("\n", rows.OrderBy(value => value, StringComparer.Ordinal)));
    }

    /// <summary>
    /// 列出非編譯器生成、非靜態、非泛型定義的產品類別與結構，解析屬性名稱而不建立實例。
    /// 預設 System.Text.Json 拒絕的型別記固定例外型別；型別改 internal 不得讓任一欄位增減。
    /// </summary>
    [Fact]
    public void R2_SerializationShape_UnchangedAfterVisibilityChange()
    {
        var resolver = new DefaultContractResolver();
        var shapes = new List<Shape>();
        foreach (var type in typeof(global::ChurchReport.Program).Assembly.GetTypes()
            .Where(type => (type.IsClass || (type.IsValueType && !type.IsEnum))
                && !(type.IsAbstract && type.IsSealed) && !type.IsGenericTypeDefinition
                && !type.IsDefined(typeof(CompilerGeneratedAttribute), false))
            .OrderBy(FullName, StringComparer.Ordinal))
        {
            var contract = resolver.ResolveContract(type);
            var newtonsoft = contract is JsonObjectContract objectContract
                ? objectContract.Properties.Select(property => property.PropertyName ?? string.Empty)
                    .OrderBy(value => value, StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();
            string[] systemTextJson;
            try
            {
                systemTextJson = JsonSerializerOptions.Default.GetTypeInfo(type).Properties
                    .Select(property => property.Name).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            }
            catch (Exception exception)
            {
                // 只記metadata解析拒絕的型別，不能保存例外訊息或將產品執行例外當成形狀。
                systemTextJson = new[] { "unsupported:" + FullName(exception.GetType()) };
            }
            shapes.Add(new Shape(FullName(type), newtonsoft, systemTextJson));
        }
        Assert.NotEmpty(shapes);
        MatchGolden("r2-serialization-shape", ".json", JsonSerializer.Serialize(shapes,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>不寫可見性或真實值，僅保存型別身分與兩個預設解析器的屬性名稱序列。</summary>
    /// <param name="Type">完整產品型別名稱。</param>
    /// <param name="Newtonsoft">Newtonsoft預設契約中的名稱，保留所有解析結果。</param>
    /// <param name="SystemTextJson">System.Text.Json預設名稱或unsupported例外型別。</param>
    private sealed record Shape(string Type, string[] Newtonsoft, string[] SystemTextJson);

    /// <summary>穩定顯示陣列、泛型與ref型別，不把組件版本或記憶體身分寫入黃金。</summary>
    private static string FullName(Type type)
    {
        if (type.IsByRef) return FullName(type.GetElementType()!) + "&";
        if (type.IsArray) return FullName(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        if (type.IsGenericType)
            return type.GetGenericTypeDefinition().FullName + "["
                + string.Join(",", type.GetGenericArguments().Select(FullName)) + "]";
        return type.FullName ?? type.Name;
    }

    /// <summary>
    /// 唯一寫入接點，固定檔名只由測試傳入；產生開關未明確啟用就只讀並比較原始位元組。
    /// 行尾正規化只處理產生的metadata文字，來源／產品與既有黃金不會在比對模式被重寫。
    /// </summary>
    private static void MatchGolden(string name, string extension, string content)
    {
#if DEBUG
        const string suffix = ".debug";
#else
        const string suffix = "";
#endif
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SpeechMessageProducts.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var path = Path.Combine(root!.FullName, "ChurchReport.MemberInfo.Tests", "Golden", name + suffix + extension);
        var bytes = new UTF8Encoding(false).GetBytes(content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd('\n').Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n");
        if (string.Equals(Environment.GetEnvironmentVariable("SPEECHMESSAGE_R2_WRITE_GOLDEN"), "1", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        Assert.True(File.Exists(path), "R2 golden must be captured and committed before visibility changes: " + Path.GetFileName(path));
        Assert.Equal(File.ReadAllBytes(path), bytes);
    }
}
