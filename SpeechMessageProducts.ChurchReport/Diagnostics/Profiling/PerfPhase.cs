/*
 * 檔案整體說明
 *
 * 目的與定位：提供控制器階段計時scope，Debug量測階段與門檻，Release保留可被JIT消除的空殼；scope由呼叫區塊確定結束，不把個別請求資訊保存為全域分析狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 PerfPhase、PerfScope。
 * 主要操作入口包含 Measure、Dispose。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/EquipmentController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/FeeManagementController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using Microsoft.AspNetCore.Http;

namespace ChurchReport.Diagnostics.Profiling
{
    /// <summary>
    /// Call-site wrapper for controller phase timing.
    /// Release: Measure returns default(PerfScope) (readonly struct, empty Dispose elided by the JIT),
    ///          and there is NO Func overload, so call sites allocate no closure -> truly zero overhead.
    /// Debug  : delegates to RequestProfiler.MeasurePhase (still gated by ProfilingSwitch).
    /// Usage is always a using block: using (PerfPhase.Measure(HttpContext, "Name")) { ... }
    /// </summary>
    public static class PerfPhase
    {
        public static PerfScope Measure(HttpContext context, string name)
        {
#if DEBUG
            return new PerfScope(RequestProfiler.MeasurePhase(context, name));
#else
            return default;
#endif
        }
    }

    /// <summary>Phase-timing scope. Release: empty-shell struct (zero allocation, Dispose elided).</summary>
    public readonly struct PerfScope : IDisposable
    {
#if DEBUG
        private readonly IDisposable _inner;
        internal PerfScope(IDisposable inner) { _inner = inner; }
        public void Dispose() => _inner?.Dispose();
#else
        public void Dispose() { }
#endif
    }
}
