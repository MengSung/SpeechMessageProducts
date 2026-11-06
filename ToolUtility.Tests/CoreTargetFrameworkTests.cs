using System.Reflection;
using System.Runtime.Versioning;
using System.Globalization;
using System.Xml.Linq;
using ToolUtilityNameSpace.FeeOperations;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.AppointmentOperations;
using ToolUtilityNameSpace.LessonsOperations;
using ToolUtilityNameSpace.MeetingStatisticsOperations;
using ToolUtilityNameSpace.OwnerOperations;
using ToolUtilityNameSpace.Interfaces;
using ToolUtilityNameSpace.AttributeOperations;
using ToolUtilityNameSpace.Extensions;
using ToolUtilityNameSpace.Constants;
using ToolUtilityNameSpace.AttachmentOperations;
using ToolUtilityNameSpace.ActivityOperations;
using ToolUtilityNameSpace.CollectionOperations;
using ToolUtilityNameSpace.ContactOperations;
using ToolUtilityNameSpace.ListOperations;
using ToolUtilityNameSpace.QueryOperations;
using Xunit;

namespace ToolUtility.Tests
{
    /// <summary>
    /// 反射實際載入產品組件，而非只讀csproj文字；Framework測試宿主可以net48，
    /// 但產品輸出必須仍是核定net462，不能誤載net48／host組件而假稱雙目標通過。
    /// </summary>
    public sealed class CoreTargetFrameworkTests
    {
        /// <summary>
        /// 比對FeeService所屬assembly的目標中繼資料；錯用任一產品目標即決定性失敗，
        /// 不接觸D365、不配置連線或其他需處置資源。
        /// </summary>
        [Fact]
        public void LoadedCore_UsesApprovedProductTarget()
        {
            var assembly = typeof(FeeService).Assembly;
            Assert.Same(assembly, typeof(EntityQueryService).Assembly);
            Assert.Same(assembly, typeof(EntityOptimizedQueryService).Assembly);
            Assert.Same(assembly, typeof(ToolUtilityNameSpace.EntityOperations.PagedResult<>).Assembly);
            Assert.Same(assembly, typeof(EntityRepository).Assembly);
            Assert.Same(assembly, typeof(IEntityRepository).Assembly);
            Assert.Same(assembly, typeof(AppointmentService).Assembly);
            Assert.Same(assembly, typeof(LessonsService).Assembly);
            Assert.Same(assembly, typeof(MeetingStatisticsService).Assembly);
            Assert.Same(assembly, typeof(EntityCrudService).Assembly);
            Assert.Same(assembly, typeof(OwnerManagementService).Assembly);
            Assert.Same(assembly, typeof(EntityAttributeUtilityService).Assembly);
            Assert.Same(assembly, typeof(IEntityAttributeUtilityService).Assembly);
            Assert.Same(assembly, typeof(OrganizationServiceExtensions).Assembly);
            Assert.Same(assembly, typeof(CrmEntityColumns).Assembly);
            Assert.Same(assembly, typeof(AttachmentService).Assembly);
            Assert.Same(assembly, typeof(IAttachmentService).Assembly);
            Assert.Same(assembly, typeof(AttachmentTransferLimits).Assembly);
            Assert.Same(assembly, typeof(ActivityService).Assembly);
            Assert.Same(assembly, typeof(IActivityService).Assembly);
            Assert.Same(assembly, typeof(CollectionQueryService).Assembly);
            Assert.Same(assembly, typeof(ICollectionQueryService).Assembly);
            Assert.Same(assembly, typeof(ToolUtilityNameSpace.CollectionOperations.PagedResult<>).Assembly);
            Assert.Same(assembly, typeof(AttributeServiceComposite).Assembly);
            Assert.Same(assembly, typeof(IAttributeService).Assembly);
            Assert.Same(assembly, typeof(BoolAttributeService).Assembly);
            Assert.Same(assembly, typeof(DateTimeAttributeService).Assembly);
            Assert.Same(assembly, typeof(DoubleAttributeService).Assembly);
            Assert.Same(assembly, typeof(FloatAttributeService).Assembly);
            Assert.Same(assembly, typeof(IntAttributeService).Assembly);
            Assert.Same(assembly, typeof(LookupAttributeService).Assembly);
            Assert.Same(assembly, typeof(MoneyAttributeService).Assembly);
            Assert.Same(assembly, typeof(OptionSetAttributeService).Assembly);
            Assert.Same(assembly, typeof(StringAttributeService).Assembly);
            Assert.Same(assembly, typeof(EntityAttributeHandler).Assembly);
            Assert.Same(assembly, typeof(IContactService).Assembly);
            Assert.Same(assembly, typeof(IListService).Assembly);
            Assert.Same(assembly, typeof(IMarketingListService).Assembly);
            Assert.Same(assembly, typeof(IComplexQueryService).Assembly);
            Assert.Same(assembly, typeof(IQueryService).Assembly);
            Assert.Equal("ToolUtility.Core", assembly.GetName().Name);
            var target = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
            Assert.NotNull(target);
#if NETFRAMEWORK
            Assert.Equal(".NETFramework,Version=v4.6.2", target!.FrameworkName);
#else
            Assert.Equal(".NETCoreApp,Version=v10.0", target!.FrameworkName);
#endif
        }

        /// <summary>
        /// 鎖定抽取前五份純介面的完整方法數、ref 計數及批次預設值。
        /// 搬移若漏掉任何舊多載、把 ref 變值傳遞或改變批次／取消預設值即失敗；
        /// 不建立實作、SDK 或背景工作，兩個產品測試宿主執行相同反射。
        /// </summary>
        [Theory]
        [InlineData(typeof(IContactService), 19)]
        [InlineData(typeof(IListService), 20)]
        [InlineData(typeof(IMarketingListService), 7)]
        [InlineData(typeof(IComplexQueryService), 16)]
        [InlineData(typeof(IQueryService), 5)]
        public void ExtractedContracts_PreserveMethodCountAndDefaults(System.Type contract, int expectedCount)
        {
            Assert.Equal(expectedCount, contract.GetMethods().Length);
            foreach (var method in contract.GetMethods())
                foreach (var parameter in method.GetParameters())
                {
                    if (parameter.Name == "batchSize")
                    {
                        Assert.True(parameter.IsOptional);
                        Assert.Equal(50, parameter.DefaultValue);
                    }
                    if (parameter.Name == "maxBatchSize")
                    {
                        Assert.True(parameter.IsOptional);
                        Assert.Equal(1000, parameter.DefaultValue);
                    }
                    if (parameter.ParameterType == typeof(System.Threading.CancellationToken))
                        Assert.True(parameter.IsOptional);
                }
            if (contract == typeof(IContactService))
            {
                var count = contract.GetMethods().Single(method => method.Name == "RetrieveByContactId"
                    && method.GetParameters().Length == 3).GetParameters()[2];
                Assert.Equal(typeof(int).MakeByRefType(), count.ParameterType);
                Assert.False(count.IsOut);
            }
        }

        /// <summary>
        /// 以搬移前來源簽章建立的固定 golden 核對五介面全部 67 個多載，而非只比方法數；
        /// 刪改參數型別／順序／名稱／ref 或預設值都會失敗。nullable 另驗，因其不改 CLR 簽章。
        /// 此測試不載入 host 或連線，兩個產品宿主使用完全相同 golden。
        /// </summary>
        [Fact]
        public void ExtractedContracts_MatchCompletePreMoveApiGolden()
        {
            var contracts = new[] { typeof(IContactService), typeof(IListService), typeof(IMarketingListService),
                typeof(IComplexQueryService), typeof(IQueryService) };
            var xml = new XElement("contracts", contracts.OrderBy(type => type.Name, System.StringComparer.Ordinal)
                .Select(type => new XElement("contract", new XAttribute("name", type.Name),
                    type.GetMethods().Select(method => TypeName(method.ReturnType) + " " + method.Name + "("
                        + string.Join(", ", method.GetParameters().Select(parameter =>
                            (parameter.ParameterType.IsByRef ? "ref " : "")
                            + TypeName(parameter.ParameterType.IsByRef
                                ? parameter.ParameterType.GetElementType()! : parameter.ParameterType)
                            + " " + parameter.Name + (parameter.IsOptional ? " = "
                                + (parameter.ParameterType == typeof(System.Threading.CancellationToken) ? "default"
                                    : System.Convert.ToString(parameter.DefaultValue, CultureInfo.InvariantCulture)) : "")))
                        + ");")
                    .OrderBy(signature => signature, System.StringComparer.Ordinal)
                    .Select(signature => new XElement("method", new XAttribute("signature", signature))))));
            TestHelpers.GoldenQueryAssertions.Equal("pure-contracts.xml", xml.ToString());
        }

        /// <summary>只將反射型別轉成搬移前 API 使用的 C# 拼字；不讀產品實作或建構預期 golden。</summary>
        private static string TypeName(System.Type type)
        {
            if (type == typeof(void)) return "void";
            if (type == typeof(string)) return "string";
            if (type == typeof(int)) return "int";
            if (type.IsGenericType)
                return type.Name.Split('`')[0] + "<" + string.Join(", ", type.GetGenericArguments().Select(TypeName)) + ">";
            return type.Name;
        }

#if !NETFRAMEWORK
        /// <summary>
        /// 實際反射抽取介面的公開 nullable 中繼資料，而非以檔頭是否有 pragma 判斷。
        /// Core strict 專案統一啟用 Nullable；若後續設定漂移成 oblivious，任一字串參數即決定性失敗。
        /// 屬性讀取的 attributeName 與附件四個原樣傳遞 metadata 依舊允許 null；
        /// Collection 的選用 pagingCookie 可為 null；其餘字串（包含附件 parent entityName）維持非 null。
        /// 不需修改產品公開 API 或新增重複 pragma 來配合靜態審查推測。
        /// </summary>
        [Theory]
        [InlineData(typeof(IAppointmentService))]
        [InlineData(typeof(ILessonsService))]
        [InlineData(typeof(IMeetingStatisticsService))]
        [InlineData(typeof(IEntityCrudService))]
        [InlineData(typeof(IEntityRepository))]
        [InlineData(typeof(IOwnerManagementService))]
        [InlineData(typeof(IEntityAttributeUtilityService))]
        [InlineData(typeof(IAttachmentService))]
        [InlineData(typeof(IActivityService))]
        [InlineData(typeof(ICollectionQueryService))]
        [InlineData(typeof(IAttributeService))]
        [InlineData(typeof(IContactService))]
        [InlineData(typeof(IListService))]
        [InlineData(typeof(IComplexQueryService))]
        [InlineData(typeof(IQueryService))]
        public void CoreInterfaces_EmitNonObliviousStringContracts(System.Type contract)
        {
            var context = new NullabilityInfoContext();
            var checkedParameters = 0;
            foreach (var method in contract.GetMethods())
                foreach (var parameter in method.GetParameters())
                    if (parameter.ParameterType == typeof(string))
                    {
                        var expected = (contract == typeof(IEntityAttributeUtilityService) && parameter.Name == "attributeName")
                            || (contract == typeof(IAttachmentService) && parameter.Name != "entityName")
                            || (contract == typeof(ICollectionQueryService) && parameter.Name == "pagingCookie")
                            ? NullabilityState.Nullable : NullabilityState.NotNull;
                        Assert.Equal(expected, context.Create(parameter).ReadState);
                        checkedParameters++;
                    }
            Assert.True(checkedParameters > 0);
        }

        /// <summary>
        /// 五個新搬介面的單筆查無結果必須標為 nullable，而非宣稱永不為 null；
        /// 集合與字串維持非 null。這是實際中繼資料斷言，不以檔頭文字替代。
        /// 同時驗證原 host 實作仍可指派至同 namespace 的 Core 契約，防止留下兩份同名介面。
        /// </summary>
        [Theory]
        [InlineData(typeof(IContactService), typeof(ContactService))]
        [InlineData(typeof(IListService), typeof(ListService))]
        [InlineData(typeof(IMarketingListService), typeof(MarketingListService))]
        [InlineData(typeof(IComplexQueryService), typeof(ComplexQueryService))]
        [InlineData(typeof(IQueryService), typeof(QueryService))]
        public void ExtractedContracts_PreserveHostAssignmentAndHonestNullability(
            System.Type contract, System.Type hostImplementation)
        {
            Assert.True(contract.IsAssignableFrom(hostImplementation));
            Assert.NotSame(contract.Assembly, hostImplementation.Assembly);
            var context = new NullabilityInfoContext();
            foreach (var method in contract.GetMethods())
                if (method.ReturnType.IsClass)
                    Assert.Equal(method.ReturnType == typeof(Microsoft.Xrm.Sdk.Entity)
                        ? NullabilityState.Nullable : NullabilityState.NotNull,
                        context.Create(method.ReturnParameter).ReadState);
        }
#endif
    }
}
