using System.Reflection;
using System.Runtime.Versioning;
using ToolUtilityNameSpace.FeeOperations;
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
            Assert.Equal("ToolUtility.Core", assembly.GetName().Name);
            var target = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
            Assert.NotNull(target);
#if NETFRAMEWORK
            Assert.Equal(".NETFramework,Version=v4.6.2", target!.FrameworkName);
#else
            Assert.Equal(".NETCoreApp,Version=v10.0", target!.FrameworkName);
#endif
        }
    }
}
