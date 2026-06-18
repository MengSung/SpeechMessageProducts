#if DEBUG
namespace ChurchReport.Diagnostics.Profiling
{
    /// <summary>
    /// 剖析總開關。僅 Debug 編譯存在；再加 runtime 旗標（預設關）雙重保險，
    /// 即使正式機誤以 Debug 部署，未開設定就不剖析。由 Startup 從 Profiling:Enabled 設定。
    /// </summary>
    public static class ProfilingSwitch
    {
        public static volatile bool Enabled = false;
    }
}
#endif
