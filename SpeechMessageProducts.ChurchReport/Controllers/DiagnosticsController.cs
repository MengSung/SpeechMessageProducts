// ============================================================================
// 檔案：SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs
// 目的：僅在 DEBUG 組態提供最小、無持久化的 index、Session 與 process 效能診斷。
//
// 安全與資源設計：
// 1. 舊 ADFS authorization 與 direct Web API identity probe 已退休；本 controller 不讀 Dynamics 設定、不交換 token、
//    不建立 HTTP request，也不提供任何替代網路 probe。正式相容性只走網站 → Gateway → 官方 worker → Organization Service。
// 2. 所有 action 都受 diagnostics-operator 政策保護，且回應強制 private, no-store，避免瀏覽器或 shared cache 保存診斷訊號。
// 3. 回應不包含 Session ID、使用者識別、credential、endpoint、token、process user 或 command line。
// 4. index 與 Session action 不建立 disposable resource；效能 action 是 Process handle 的唯一 owner，並在 using 結束時釋放。
// 5. controller 沒有設定、HTTP client、cache、timer、subscription 或背景工作 dependency，因此 request 結束後不保留狀態。
// ============================================================================

using System.Collections.Generic;
using System.Diagnostics;
using ChurchReport.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChurchReport.Controllers
{
#if DEBUG
    /// <summary>
    /// DEBUG-only 操作員診斷控制器，只回傳目前網站 process 的有界、非敏感狀態。舊 ADFS authorization 與 direct
    /// Web API 診斷已退休，不能再作為 Gateway readiness、Phase 4 或相容性驗證入口；正式驗證必須經由網站、Gateway、
    /// ControlPlane／WorkerSupervisor 與版本固定的官方 worker 到 Organization Service。此型別沒有設定或網路 dependency，
    /// 不建立 token、connection pool、timer、cache、Session state 或背景工作；Release 編譯不包含此型別。
    /// </summary>
    [Authorize(Policy = DiagnosticsOperatorAuthorization.PolicyName)]
    [Route("diagnostics")]
    public sealed class DiagnosticsController : Controller
    {
        /// <summary>
        /// 回傳最小診斷入口狀態，並明確標示舊 ADFS 診斷為 unavailable／retired，避免操作員把已刪除路由誤認為
        /// 可用的相容性工具。結果只包含固定 stage 與 Session 可用性，不讀取部署設定、Session ID 或使用者資料；
        /// action 不配置外部資源，response header 由目前 request 唯一擁有。
        /// </summary>
        [HttpGet("")]
        public IActionResult Index()
        {
            ApplyNoStoreHeaders();
            return Json(new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["stage"] = "diagnostics",
                ["adfsAuthorizeAvailable"] = false,
                ["adfsDiagnosticStatus"] = "retired",
                ["sessionAvailable"] = HttpContext.Session.IsAvailable
            });
        }

        /// <summary>
        /// 回傳最小 Session 可用性而非 Session identifier。Session ID 是 server-side correlation/security boundary，
        /// 即使 DEBUG 與已授權操作員也不能成為 response contract。action 不寫 Session、不建立 cache 或 distributed-store
        /// connection，也沒有 timer、subscription、background task 或額外 cleanup owner。
        /// </summary>
        [HttpGet("session")]
        public IActionResult GetSessionInfo()
        {
            ApplyNoStoreHeaders();
            return Json(new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["stage"] = "session",
                ["available"] = HttpContext.Session.IsAvailable
            });
        }

        /// <summary>
        /// 回傳 process 層級、無身份資料的有界效能快照。<see cref="Process"/> 是本 action 建立之 OS handle 的唯一 owner，
        /// 必須在讀取 working set、private memory 與 thread count 後由 using 確定性釋放；結果禁止包含 process user、
        /// command line、endpoint、Session、token 或其他可跨要求識別資料，也不啟動 polling timer 或背景 profiler。
        /// </summary>
        [HttpGet("performance")]
        public IActionResult GetPerformanceInfo()
        {
            ApplyNoStoreHeaders();
            using var process = Process.GetCurrentProcess();
            return Json(new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["stage"] = "performance",
                ["workingSetMb"] = process.WorkingSet64 / 1024 / 1024,
                ["privateMemoryMb"] = process.PrivateMemorySize64 / 1024 / 1024,
                ["threadCount"] = process.Threads.Count
            });
        }

        /// <summary>
        /// 將所有診斷回應標示為 private、no-store，並補上舊代理相容的 no-cache／expires header。此固定操作在任何
        /// response body 建立前完成，不配置 cache entry、buffer、stream、timer、subscription 或背景清理；header 的生命週期
        /// 由 ASP.NET Core response owner 管理，request 結束後不留下 shared mutable state。
        /// </summary>
        private void ApplyNoStoreHeaders()
        {
            Response.Headers.CacheControl = "private, no-store";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.Expires = "0";
        }
    }
#endif
}
