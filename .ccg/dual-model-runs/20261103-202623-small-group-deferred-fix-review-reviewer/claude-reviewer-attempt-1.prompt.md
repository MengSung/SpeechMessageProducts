ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: small-group-deferred-fix-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P0.Worktree

## Request
# 小組回報空白：修正審查

僅審查以下提供的程式與差異；不要使用工具、讀取 repo/config/日誌/User Secrets 或修改檔案；不得連線網站、D365 或 LINE。輸出繁體中文，按 Critical/High/Warning/Info 分級，若沒有問題直接說明。

已重現：實際 Razor Grid adapter 回傳原生 Promise，DevExtreme 22.1.6 呼叫 .done 失敗。專案 jQuery 2.2.4。現已用可選 Deferred factory 保留 done/fail/多個完成參數與 context，default 仍為原生 Promise 給日期切換。清理 active transport 在呼叫外部同步 callback 之前，不吞 callback 例外；不變更 generation、owner WeakMap、abort/dispose 機制。原始 store.load 仍為唯一 HTTP 管線。

紅燈：新增 10 個案例全部失敗（缺失 done/fail 及 14 個無部署來源的資源）。綠燈：原有 5 個加新增案例全過，另加同步重入。200 次 dispose/drain 計數歸零。

主版面移除 14 個實際回傳404的舊範本資源引用；有效 DevExtreme CSS 原已在上方載入，未切換 Globalize/Intl 或升降版本。IntegrateView 為 coordinator script 加內容版本以避免 immutable 快取混版。圖表資料來源獨立，不宣稱此次修正已證明其資料存在或視覺恢復。

重點：跨使用者/owner 隔離、單次資料生命週期、原始錯誤保留、同步重入、callback 拋錯清理、多參數 metadata、是否有假綠燈、改動是否可能帶來新問題。無第三方套件新增；測試從 repo 現有 jQuery 抽取真正的 Callbacks/Deferred 區段，泛用物件操作有最小替身。不涉及伺服器資料權限與 API 寫入。

## 精確產品與 CI 差異
diff --git a/.github/workflows/solution-ci.yml b/.github/workflows/solution-ci.yml
index db69a285f..95172747d 100644
--- a/.github/workflows/solution-ci.yml
+++ b/.github/workflows/solution-ci.yml
@@ -106,6 +106,9 @@ jobs:
           if ($LASTEXITCODE -ne 0) { throw 'Coverage gate tests failed.' }
           node --test tools/RepoQuality/Tests/Test-ProgressPage.cjs
           if ($LASTEXITCODE -ne 0) { throw 'Progress page tests failed.' }
+          # 直接驗證實際 Razor adapter 與既有 jQuery Deferred，避免只跑伺服器測試卻漏掉表格初始化錯誤。
+          node --test ChurchReport.MemberInfo.Tests/JavaScript/collection-load-coordinator.test.js ChurchReport.MemberInfo.Tests/JavaScript/weekly-report-load-adapter.test.js
+          if ($LASTEXITCODE -ne 0) { throw 'Weekly report JavaScript contracts failed.' }
           powershell -NoProfile -File docs/scripts/Tests/Test-CcgReviewSummary.ps1
           if ($LASTEXITCODE -ne 0) { throw 'CCG summary contracts failed.' }
       - name: Offline plugin packaging tests
diff --git a/SpeechMessageProducts.ChurchReport/Views/Home/IntegrateView.cshtml b/SpeechMessageProducts.ChurchReport/Views/Home/IntegrateView.cshtml
index 93083b104..29c5a7dcf 100644
--- a/SpeechMessageProducts.ChurchReport/Views/Home/IntegrateView.cshtml
+++ b/SpeechMessageProducts.ChurchReport/Views/Home/IntegrateView.cshtml
@@ -20,8 +20,9 @@
     var chartTextColor = Configuration[$"Theme:Presets:{chartThemeName}:Text"] ?? "#1d2a44";
 }
 
-@* 必須在 DateBox 與 Grid 事件可能觸發前載入；模組不保存 rows、Session 或 credential。 *@
-<script src="~/js/collection-load-coordinator.js"></script>
+@* 必須在 DateBox 與 Grid 事件可能觸發前載入；模組不保存 rows、Session 或 credential。
+   靜態檔案使用長期快取，必須附內容版本，避免新版 Razor adapter 搭配舊版協調器。 *@
+<script src="~/js/collection-load-coordinator.js" asp-append-version="true"></script>
 <script>
     // 此 coordinator 只屬於目前完整頁面。日期切換完成會導向新頁，pagehide 會先 dispose；
     // 不放入 sessionStorage/localStorage，也不在跨頁 static collection 保存使用者或回應資料。
diff --git a/SpeechMessageProducts.ChurchReport/Views/Home/_GeneralGroupGrids.cshtml b/SpeechMessageProducts.ChurchReport/Views/Home/_GeneralGroupGrids.cshtml
index 463ac7ac8..ca52d63cb 100644
--- a/SpeechMessageProducts.ChurchReport/Views/Home/_GeneralGroupGrids.cshtml
+++ b/SpeechMessageProducts.ChurchReport/Views/Home/_GeneralGroupGrids.cshtml
@@ -26,6 +26,9 @@
      * 此 adapter 僅包裝 DevExtreme 已存在的 WebApi store.load，不發出第二條 AJAX；原始 store
      * 仍負責 paging、sorting、CRUD 與認證 cookie。Coordinator 只保存 transport 與純量 generation，
      * 不保存 rows、Session 或 credential，因此 view dispose 後不會延長使用者資料生命週期。
+     * Grid 必須保留 jQuery Deferred 的 done/fail 與多參數完成契約；原生 Promise 會令現行
+     * DevExtreme 在初始化時中斷，也會遺失 data 以外的分頁總筆數。此處注入既有 factory，
+     * 仍由相同 coordinator 驗證世代，不得為相容性而直接放行未驗證的原 transport。
      */
     function initializeWeeklyReportGridPublicationGuard(e) {
         var component = e && e.component;
@@ -60,7 +63,7 @@
             store.load = function loadWithGenerationGuard(loadOptions) {
                 return coordinator.runLoad(function invokeExistingWebApiStore() {
                     return originalLoad(loadOptions);
-                });
+                }, $.Deferred);
             };
             component.__weeklyReportLoadCoordinator = coordinator;
             component.__weeklyReportLoadOwner = owner;
diff --git a/SpeechMessageProducts.ChurchReport/Views/Shared/_Layout.cshtml b/SpeechMessageProducts.ChurchReport/Views/Shared/_Layout.cshtml
index b66e40596..0316b255b 100644
--- a/SpeechMessageProducts.ChurchReport/Views/Shared/_Layout.cshtml
+++ b/SpeechMessageProducts.ChurchReport/Views/Shared/_Layout.cshtml
@@ -257,20 +257,9 @@
     <script src="~/lib/devextreme-aspnet-data/js/dx.aspnet.data.js"></script>
     <script src="~/js/devextreme/aspnet/dx.aspnet.mvc.js"></script>
     <meta name="robots" content="noindex">
-    <link rel="stylesheet" href="~/lib/devextreme/css/dx.common.css" />
-    <link rel="stylesheet" href="~/lib/devextreme/css/dx.spa.css" />
-    <link rel="stylesheet" href="~/css/demo-template.css" />
-    <script src="~/lib/devextreme/js/cldr.min.js"></script>
-    <script src="~/lib/devextreme/js/cldr/event.min.js"></script>
-    <script src="~/lib/devextreme/js/cldr/supplemental.min.js"></script>
-    <script src="~/lib/devextreme/js/cldr/unresolved.min.js"></script>
-    <script src="~/lib/devextreme/js/globalize.min.js"></script>
-    <script src="~/lib/devextreme/js/globalize/number.min.js"></script>
-    <script src="~/lib/devextreme/js/globalize/currency.min.js"></script>
-    <script src="~/lib/devextreme/js/globalize/date.min.js"></script>
-    <script src="~/lib/devextreme/js/globalize/message.min.js"></script>
-    <script src="~/lib/jszip/dist/jszip.js"></script>
-    <script src="~/lib/devextreme/js/vectormap-data/usa.js"></script>
+    @* DevExtreme 樣式已由上方 wwwroot/css/devextreme 載入。舊範本重複引用的 lib 路徑、
+       CLDR/Globalize、JSZip 與 usa.js 均無部署檔案，實際回傳 404 並遭 MIME 檢查拒絕。
+       移除這些從未成功載入的引用，保留既有版本與實際使用的資源，不切換本地化引擎。 *@
 
     <script>
         $(document).ready(function() {
diff --git a/SpeechMessageProducts.ChurchReport/wwwroot/js/collection-load-coordinator.js b/SpeechMessageProducts.ChurchReport/wwwroot/js/collection-load-coordinator.js
index af6f9d14f..d9ad85473 100644
--- a/SpeechMessageProducts.ChurchReport/wwwroot/js/collection-load-coordinator.js
+++ b/SpeechMessageProducts.ChurchReport/wwwroot/js/collection-load-coordinator.js
@@ -103,19 +103,46 @@
         return {
             /**
              * 包裝既有 DevExtreme store.load 回傳的 thenable；本方法不建立第二條 HTTP 管線。
-             * 每次呼叫先使前一世代失效並嘗試 abort，只有最新世代的 success/error 可完成外層 Promise。
+             * 每次呼叫先使前一世代失效並嘗試 abort，只有最新世代的 success/error 可完成結果。
+             * Grid 可注入既有 jQuery Deferred factory，保留 done/fail 與 data、totalCount 等多個
+             * 完成參數；未注入時仍回傳原生 Promise，供日期切換使用。不得用 Promise.resolve
+             * 同化 Deferred，否則第二個 metadata 參數會遺失。所有參考只存活於本次載入。
+             * @param {Function} loadFactory 既有 transport 的建立函式。
+             * @param {Function=} deferredFactory Grid 使用的 Deferred factory，不存入 coordinator。
              */
-            runLoad: function runLoad(loadFactory) {
+            runLoad: function runLoad(loadFactory, deferredFactory) {
+                var deferred = typeof deferredFactory === 'function' ? deferredFactory() : null;
+                var resolveNative;
+                var rejectNative;
+                var result = deferred ? deferred.promise() : new Promise(function captureCompletion(resolve, reject) {
+                    resolveNative = resolve;
+                    rejectNative = reject;
+                });
+
+                /**
+                 * 同步驗證失敗也使用呼叫端選定的回傳型別，不把 Deferred 呼叫意外轉成原生 Promise。
+                 */
+                function rejectBeforeLoad(error) {
+                    if (deferred) {
+                        deferred.reject(error);
+                    } else {
+                        rejectNative(error);
+                    }
+                    return result;
+                }
+
                 if (disposed) {
-                    return Promise.reject(createStaleGenerationError());
+                    return rejectBeforeLoad(createStaleGenerationError());
                 }
                 if (typeof loadFactory !== 'function') {
-                    return Promise.reject(new TypeError('loadFactory 必須是函式。'));
+                    return rejectBeforeLoad(new TypeError('loadFactory 必須是函式。'));
                 }
 
                 generation += 1;
                 if (activeRequest) {
-                    abortTransport(activeRequest.transport);
+                    var previousTransport = activeRequest.transport;
+                    activeRequest = null;
+                    abortTransport(previousTransport);
                 }
 
                 var requestGeneration = generation;
@@ -123,7 +150,7 @@
                 try {
                     transport = loadFactory();
                 } catch (error) {
-                    return Promise.reject(error);
+                    return rejectBeforeLoad(error);
                 }
 
                 activeRequest = {
@@ -131,25 +158,41 @@
                     transport: transport
                 };
 
-                return new Promise(function settleOnlyCurrent(resolve, reject) {
-                    Promise.resolve(transport).then(
-                        function resolveCurrent(value) {
-                            if (disposed || requestGeneration !== generation) {
-                                reject(createStaleGenerationError());
-                            } else {
-                                resolve(value);
-                            }
-                            completeIfCurrent(requestGeneration);
-                        },
-                        function rejectCurrent(error) {
-                            if (disposed || requestGeneration !== generation) {
-                                reject(createStaleGenerationError());
-                            } else {
-                                reject(error);
-                            }
-                            completeIfCurrent(requestGeneration);
-                        });
-                });
+                /**
+                 * 在呼叫消費者前先歸還 active transport；jQuery 2.x callback 可同步擲出例外，
+                 * 因此不能把清理排在 resolve/reject 後面。context 與 arguments 不寫入共用欄位。
+                 */
+                function settleCurrent(succeeded, context, args) {
+                    var stale = disposed || requestGeneration !== generation;
+                    completeIfCurrent(requestGeneration);
+                    if (stale) {
+                        rejectBeforeLoad(createStaleGenerationError());
+                    } else if (deferred) {
+                        deferred[succeeded ? 'resolveWith' : 'rejectWith'](context, args);
+                    } else if (succeeded) {
+                        resolveNative(args[0]);
+                    } else {
+                        rejectNative(args[0]);
+                    }
+                }
+
+                function resolveCurrent() {
+                    settleCurrent(true, this, arguments);
+                }
+
+                function rejectCurrent() {
+                    settleCurrent(false, this, arguments);
+                }
+
+                if (deferred && transport && typeof transport.done === 'function' &&
+                    typeof transport.fail === 'function') {
+                    // 直接觀察原 Deferred 才能保留多參數；回傳的是新的受世代保護結果，不能直接
+                    // 返回原 transport，否則晚到資料會繞過 generation 驗證。
+                    transport.done(resolveCurrent).fail(rejectCurrent);
+                } else {
+                    Promise.resolve(transport).then(resolveCurrent, rejectCurrent);
+                }
+                return result;
             },
 
             /**

## coordinator 完整程式
// ============================================================================
// 檔案路徑：SpeechMessageProducts.ChurchReport/wwwroot/js/collection-load-coordinator.js
// 檔案責任：協調單一 Grid／表格元件的非同步載入世代，阻止慢網路、代理重送、重複 refresh
//           或回應亂序讓舊 callback 覆蓋最新畫面；同時將 refresh 意圖限制為最多一個。
// 身份邊界：本檔案不判斷資料列是否重複，也不保存姓名、PresentRecordId、Session、token、
//           credential 或 response data；資料列唯一性仍由伺服器端資料庫 ID guard 負責。
// 資源生命週期：每個 coordinator 只有一個 owner、一個 active transport 與一個 pending callback。
//               duplicate mount 先 dispose 舊 owner；dispose 使 generation 失效、abort transport、
//               清除 callback 與元件 closure。WeakMap 不會強引用已移除的 DOM owner。
// 編碼要求：本檔案必須以 UTF-8 without BOM、CRLF、final CRLF 儲存。
// ============================================================================
(function exposeCollectionLoadCoordinator(root, factory) {
    'use strict';

    var api = factory();
    if (typeof module === 'object' && module.exports) {
        module.exports = api;
    } else {
        root.CollectionLoadCoordinator = api;
    }
}(typeof globalThis !== 'undefined' ? globalThis : this, function createApi() {
    'use strict';

    // WeakMap 的 key 是 DOM owner 或測試 owner；它只提供「同一元件最多一個 coordinator」語意，
    // 不保存使用者資料。owner 不再被頁面參考時，瀏覽器可一併回收 registry entry。
    var ownerRegistry = typeof WeakMap === 'function' ? new WeakMap() : null;

    /**
     * 建立代表過期世代的內部錯誤。錯誤不含 response、URL、Session 或個資，避免診斷物件
     * 延長保存大型回應；adapter 可用 isStaleGeneration 判斷這不是目前世代的服務錯誤。
     */
    function createStaleGenerationError() {
        var error = new Error('資料載入世代已過期，拒絕發布舊回應。');
        error.name = 'StaleGenerationError';
        error.isStaleGeneration = true;
        error.canceled = true;
        return error;
    }

    /**
     * 安全呼叫 transport.abort。取消只用於縮短網路資源生命週期；即使 abort 不存在、失敗或
     * 回應仍晚到，runLoad 的 generation 檢查仍會拒絕舊 callback，因此不把取消當正確性邊界。
     */
    function abortTransport(transport) {
        if (!transport || typeof transport.abort !== 'function') {
            return;
        }

        try {
            transport.abort();
        } catch (error) {
            // 某些第三方 transport 在完成後 abort 會擲例外。資源已不可再控制時只清除本地參考；
            // generation 已先失效，所以忽略 abort 例外不會讓舊資料取得發布權。
        }
    }

    /**
     * 建立只屬於一個 UI component instance 的載入協調器。
     * @param {Function=} disposeOwnedComponent 釋放此 coordinator 所擁有之 Grid 的函式；不得捕獲 Session 資料。
     * @returns {Object} 具有 runLoad、requestRefresh、dispose 與有界診斷計數的 owner。
     */
    function createCollectionLoadCoordinator(disposeOwnedComponent) {
        var generation = 0;
        var activeRequest = null;
        var pendingRefresh = null;
        var refreshScheduled = false;
        var disposed = false;
        var ownedComponentDisposer = typeof disposeOwnedComponent === 'function'
            ? disposeOwnedComponent
            : null;

        /**
         * 在目前 microtask 執行唯一 pending refresh。callback 執行前先從 owner 清除參考，
         * 即使 refresh 再同步要求 refresh，也只會建立下一個有界意圖，不會遞迴累積 queue。
         */
        function flushPendingRefresh() {
            refreshScheduled = false;
            if (disposed || activeRequest || !pendingRefresh) {
                return;
            }

            var refresh = pendingRefresh;
            pendingRefresh = null;
            refresh();
        }

        /**
         * 當目前世代完成時釋放 transport 參考，並排程最多一次 refresh。
         * 舊世代完成不得清掉新世代 transport，也不得觸發屬於新世代的 pending callback。
         */
        function completeIfCurrent(requestGeneration) {
            if (!activeRequest || activeRequest.generation !== requestGeneration) {
                return;
            }

            activeRequest = null;
            if (!disposed && pendingRefresh && !refreshScheduled) {
                refreshScheduled = true;
                Promise.resolve().then(flushPendingRefresh);
            }
        }

        return {
            /**
             * 包裝既有 DevExtreme store.load 回傳的 thenable；本方法不建立第二條 HTTP 管線。
             * 每次呼叫先使前一世代失效並嘗試 abort，只有最新世代的 success/error 可完成結果。
             * Grid 可注入既有 jQuery Deferred factory，保留 done/fail 與 data、totalCount 等多個
             * 完成參數；未注入時仍回傳原生 Promise，供日期切換使用。不得用 Promise.resolve
             * 同化 Deferred，否則第二個 metadata 參數會遺失。所有參考只存活於本次載入。
             * @param {Function} loadFactory 既有 transport 的建立函式。
             * @param {Function=} deferredFactory Grid 使用的 Deferred factory，不存入 coordinator。
             */
            runLoad: function runLoad(loadFactory, deferredFactory) {
                var deferred = typeof deferredFactory === 'function' ? deferredFactory() : null;
                var resolveNative;
                var rejectNative;
                var result = deferred ? deferred.promise() : new Promise(function captureCompletion(resolve, reject) {
                    resolveNative = resolve;
                    rejectNative = reject;
                });

                /**
                 * 同步驗證失敗也使用呼叫端選定的回傳型別，不把 Deferred 呼叫意外轉成原生 Promise。
                 */
                function rejectBeforeLoad(error) {
                    if (deferred) {
                        deferred.reject(error);
                    } else {
                        rejectNative(error);
                    }
                    return result;
                }

                if (disposed) {
                    return rejectBeforeLoad(createStaleGenerationError());
                }
                if (typeof loadFactory !== 'function') {
                    return rejectBeforeLoad(new TypeError('loadFactory 必須是函式。'));
                }

                generation += 1;
                if (activeRequest) {
                    var previousTransport = activeRequest.transport;
                    activeRequest = null;
                    abortTransport(previousTransport);
                }

                var requestGeneration = generation;
                var transport;
                try {
                    transport = loadFactory();
                } catch (error) {
                    return rejectBeforeLoad(error);
                }

                activeRequest = {
                    generation: requestGeneration,
                    transport: transport
                };

                /**
                 * 在呼叫消費者前先歸還 active transport；jQuery 2.x callback 可同步擲出例外，
                 * 因此不能把清理排在 resolve/reject 後面。context 與 arguments 不寫入共用欄位。
                 */
                function settleCurrent(succeeded, context, args) {
                    var stale = disposed || requestGeneration !== generation;
                    completeIfCurrent(requestGeneration);
                    if (stale) {
                        rejectBeforeLoad(createStaleGenerationError());
                    } else if (deferred) {
                        deferred[succeeded ? 'resolveWith' : 'rejectWith'](context, args);
                    } else if (succeeded) {
                        resolveNative(args[0]);
                    } else {
                        rejectNative(args[0]);
                    }
                }

                function resolveCurrent() {
                    settleCurrent(true, this, arguments);
                }

                function rejectCurrent() {
                    settleCurrent(false, this, arguments);
                }

                if (deferred && transport && typeof transport.done === 'function' &&
                    typeof transport.fail === 'function') {
                    // 直接觀察原 Deferred 才能保留多參數；回傳的是新的受世代保護結果，不能直接
                    // 返回原 transport，否則晚到資料會繞過 generation 驗證。
                    transport.done(resolveCurrent).fail(rejectCurrent);
                } else {
                    Promise.resolve(transport).then(resolveCurrent, rejectCurrent);
                }
                return result;
            },

            /**
             * 要求元件 refresh。active load 期間反覆呼叫只覆蓋同一個 callback 欄位；沒有 active
             * load 時也只排一個 microtask，避免慢網路或多個 UI callback 建立無界 timer queue。
             */
            requestRefresh: function requestRefresh(refreshAction) {
                if (disposed || typeof refreshAction !== 'function') {
                    return;
                }

                pendingRefresh = refreshAction;
                if (!activeRequest && !refreshScheduled) {
                    refreshScheduled = true;
                    Promise.resolve().then(flushPendingRefresh);
                }
            },

            /**
             * 釋放此 owner。先標記 disposed 與遞增 generation，使任何同步／晚到 callback 失效；
             * 再 abort transport、清除 pending callback，最後釋放元件。重複 dispose 是安全 no-op。
             */
            dispose: function dispose() {
                if (disposed) {
                    return;
                }

                disposed = true;
                generation += 1;
                if (activeRequest) {
                    abortTransport(activeRequest.transport);
                    activeRequest = null;
                }
                pendingRefresh = null;
                refreshScheduled = false;

                var disposer = ownedComponentDisposer;
                ownedComponentDisposer = null;
                if (disposer) {
                    disposer();
                }
            },

            /**
             * 回傳純量診斷值供測試／監控確認 drain；不回傳 transport、callback 或 response 參考。
             */
            getDiagnostics: function getDiagnostics() {
                return {
                    disposed: disposed,
                    activeRequestCount: activeRequest ? 1 : 0,
                    pendingRefreshCount: pendingRefresh ? 1 : 0
                };
            }
        };
    }

    /**
     * 為同一 DOM owner 掛載唯一 coordinator；若已存在舊世代，先確定 dispose 再發布新 owner。
     */
    function mount(owner, disposeOwnedComponent) {
        if (!owner || (typeof owner !== 'object' && typeof owner !== 'function')) {
            throw new TypeError('CollectionLoadCoordinator owner 必須是物件。');
        }
        if (!ownerRegistry) {
            throw new Error('目前瀏覽器不支援 WeakMap，拒絕建立可能無界保留 DOM 的 owner registry。');
        }

        var previous = ownerRegistry.get(owner);
        if (previous) {
            previous.dispose();
        }

        var coordinator = createCollectionLoadCoordinator(disposeOwnedComponent);
        ownerRegistry.set(owner, coordinator);
        return coordinator;
    }

    /**
     * 釋放 owner 目前登記的 coordinator，並立即刪除 WeakMap entry，避免保留 transport closure。
     */
    function disposeOwner(owner) {
        if (!ownerRegistry || !owner) {
            return;
        }

        var coordinator = ownerRegistry.get(owner);
        if (coordinator) {
            ownerRegistry.delete(owner);
            coordinator.dispose();
        }
    }

    return {
        createCollectionLoadCoordinator: createCollectionLoadCoordinator,
        mount: mount,
        disposeOwner: disposeOwner
    };
}));

## 新增回歸測試完整程式
// 直接執行產品 Razor 的 Grid adapter 與 repo 既有 jQuery Deferred，保護 DevExtreme
// store.load 的 done/fail、多參數分頁資料與載入世代契約。測試不建立 DOM、HTTP 或 CRM 連線；
// 每個 owner、transport 與合成資料只在單一案例存活，結束時確定 dispose 並完成所有 transport。
'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const coordinatorApi = require(
    '../../SpeechMessageProducts.ChurchReport/wwwroot/js/collection-load-coordinator.js');
const productRoot = path.resolve(__dirname, '../../SpeechMessageProducts.ChurchReport');

/**
 * 載入實際 jQuery 2.2.4 的 Callbacks/Deferred 區塊；只有無 DOM 的通用物件操作使用替身。
 * 不重寫 Deferred，避免假實作支援原生 Promise 而掩蓋真實 .done 契約。
 */
function loadRepositoryDeferred() {
    const source = fs.readFileSync(path.join(productRoot, 'wwwroot/lib/jquery/dist/jquery.js'), 'utf8');
    const start = source.indexOf('var rnotwhite =');
    const end = source.indexOf('// The deferred used on DOM ready', start);
    assert.ok(start >= 0 && end > start, '必須載入 repo 真實 Deferred，不能靜默換成替身');
    const jquery = {
        each(values, callback) {
            for (const key of Object.keys(values)) {
                if (callback.call(values[key], key, values[key]) === false) break;
            }
            return values;
        },
        extend(...values) {
            return values.length === 1 ? Object.assign(this, values[0]) : Object.assign(...values);
        },
        isFunction: value => typeof value === 'function',
        type: value => typeof value,
        inArray: (value, values, startIndex) => values == null ? -1 : values.indexOf(value, startIndex)
    };
    vm.runInNewContext(source.slice(start, end), { jQuery: jquery, slice: Array.prototype.slice });
    return jquery;
}

/**
 * 抽取並執行實際 Razor adapter，不複製實作。刻意只提供一個已有 store 的元件；
 * 所有 transport 都由案例控制，adapter 若另建 AJAX 會因不存在此 API 而失敗。
 */
function createGrid(t, transports) {
    const jquery = loadRepositoryDeferred();
    const source = fs.readFileSync(path.join(productRoot, 'Views/Home/_GeneralGroupGrids.cshtml'), 'utf8');
    const script = source.match(/<script>([\s\S]*?)<\/script>/);
    assert.ok(script, '必須執行實際 Grid adapter');
    const sandbox = { CollectionLoadCoordinator: coordinatorApi, $: jquery, jQuery: jquery, console };
    sandbox.window = sandbox;
    vm.runInNewContext(script[1], sandbox);
    const owner = {};
    const pending = transports.map(() => {
        const deferred = jquery.Deferred();
        let abortCount = 0;
        const promise = deferred.promise();
        promise.abort = () => { abortCount += 1; };
        return { deferred, promise, get abortCount() { return abortCount; } };
    });
    let calls = 0;
    const store = {
        load(options) {
            assert.equal(options, transports[calls], '保留原始 loadOptions，不自行組第二條取數管線');
            return pending[calls++].promise;
        }
    };
    const component = {
        element: () => [owner],
        getDataSource: () => ({ store: () => store }),
        dispose() {}
    };
    sandbox.initializeWeeklyReportGridPublicationGuard({ component });
    const coordinator = component.__weeklyReportLoadCoordinator;
    t.after(() => {
        sandbox.disposeWeeklyReportGridPublicationGuard({ component });
        for (const transport of pending) transport.deferred.resolve([]);
        assert.equal(coordinator.getDiagnostics().activeRequestCount, 0);
        assert.equal(coordinator.getDiagnostics().pendingRefreshCount, 0);
    });
    return { store, pending, component, coordinator, sandbox, get calls() { return calls; } };
}

/**
 * 觀察失敗以便紅燈案例也不產生未處理 rejection；不變更回傳物件或其契約。
 */
function observe(result) {
    Promise.resolve(result).catch(() => {});
    return result;
}

/**
 * 這是 Console .done TypeError 的最小回歸：DevExtreme 應取得原本的 Deferred 表面，
 * 並在 data 之外收到 totalCount/summary/groupCount，不能被原生 Promise 壓成第一個值。
 */
test('實際週報 adapter 保留 done/fail 與分頁完成參數', async t => {
    const options = { skip: 0, take: 20, requireTotalCount: true };
    const grid = createGrid(t, [options]);
    const rows = [{ PresentRecordId: 'record-a', FullName: '合成同名' },
        { PresentRecordId: 'record-b', FullName: '合成同名' }];
    const extra = { totalCount: 42, summary: [7], groupCount: 2 };
    const result = observe(grid.store.load(options));
    assert.equal(typeof result.done, 'function', 'DevExtreme 直接呼叫 store.load(...).done(...)');
    assert.equal(typeof result.fail, 'function');
    assert.equal(typeof result.always, 'function');
    assert.equal(result.resolve, undefined, '消費者不可自行完成結果以繞過世代防護');
    const context = { operation: 'synthetic-load' };
    const completion = new Promise((resolve, reject) => {
        result.done(function (data, metadata) { resolve({ data, metadata, context: this }); }).fail(reject);
    });
    grid.pending[0].deferred.resolveWith(context, [rows, extra]);
    const actual = await completion;
    assert.equal(actual.data, rows);
    assert.equal(actual.data.length, 2, '相同顯示姓名但不同資料庫 ID 都必須保留');
    assert.equal(actual.metadata, extra);
    assert.equal(actual.context, context);
    assert.equal(grid.calls, 1);
    assert.equal(grid.coordinator.getDiagnostics().activeRequestCount, 0);
});

/**
 * 當前失敗不能被轉成成功空集合，或只保留錯誤第一參數。以合成錯誤驗證原始物件、
 * 狀態及 callback context；任何真實機密或遠端訊息都不進入測試。
 */
test('實際週報 adapter 原樣傳遞當前失敗與多個失敗參數', async t => {
    const options = {};
    const grid = createGrid(t, [options]);
    const result = observe(grid.store.load(options));
    assert.equal(typeof result.fail, 'function');
    const error = new Error('synthetic failure');
    const context = { operation: 'synthetic-error' };
    const completion = new Promise(resolve => {
        result.fail(function (...args) { resolve({ args, context: this }); });
    });
    grid.pending[0].deferred.rejectWith(context, [error, 'timeout', 503]);
    const actual = await completion;
    assert.deepEqual(actual.args, [error, 'timeout', 503]);
    assert.equal(actual.context, context);
    assert.equal(grid.coordinator.getDiagnostics().activeRequestCount, 0);
});

/**
 * 保護晚到成功與錯誤皆不能發布舊小組／日期資料。abort 不強制結束 transport，證明
 * 真正的邊界仍是 generation；最新世代的 metadata 必須與最新資料配對。
 */
for (const oldOutcome of ['resolve', 'reject']) {
    test(`Deferred 相容修正仍拒絕舊世代 ${oldOutcome}`, async t => {
        const firstOptions = { scope: 'scope-a' };
        const secondOptions = { scope: 'scope-b' };
        const grid = createGrid(t, [firstOptions, secondOptions]);
        const first = observe(grid.store.load(firstOptions));
        const second = observe(grid.store.load(secondOptions));
        assert.equal(typeof first.fail, 'function');
        const stale = new Promise(resolve => first.fail(resolve));
        const published = new Promise((resolve, reject) => {
            second.done((data, metadata) => resolve({ data, metadata })).fail(reject);
        });
        grid.pending[1].deferred.resolve(['current'], { totalCount: 1 });
        assert.deepEqual(await published, { data: ['current'], metadata: { totalCount: 1 } });
        grid.pending[0].deferred[oldOutcome](['old'], { totalCount: 99 });
        assert.equal((await stale).isStaleGeneration, true);
        assert.equal(grid.pending[0].abortCount, 1);
        assert.equal(grid.coordinator.getDiagnostics().activeRequestCount, 0);
    });
}

/**
 * 兩個 Grid owner 可同時載入，不得因一個 owner 取消而污染另一個。dispose 之後晚到資料
 * 必須拒絕發布；成功與釋放後 transport/pending intent 都回到零。
 */
test('兩個 Grid owner 的取消與資料完全隔離', async t => {
    const firstOptions = {};
    const secondOptions = {};
    const first = createGrid(t, [firstOptions]);
    const second = createGrid(t, [secondOptions]);
    const firstLoad = observe(first.store.load(firstOptions));
    const secondLoad = observe(second.store.load(secondOptions));
    assert.equal(typeof firstLoad.fail, 'function');
    const rejected = new Promise(resolve => firstLoad.fail(resolve));
    const published = new Promise((resolve, reject) => secondLoad.done(resolve).fail(reject));
    first.sandbox.disposeWeeklyReportGridPublicationGuard({ component: first.component });
    second.pending[0].deferred.resolve(['owner-b']);
    first.pending[0].deferred.resolve(['owner-a']);
    assert.deepEqual(await published, ['owner-b']);
    assert.equal((await rejected).isStaleGeneration, true);
    assert.equal(first.pending[0].abortCount, 1);
    assert.equal(second.pending[0].abortCount, 0);
});

/**
 * jQuery 2.x 會同步呼叫 done。消費者同步發起下一次 refresh 時，前一個 transport
 * 必須已釋放，不能誤 abort 已完成請求或清掉新世代的 activeRequest。
 */
test('done 同步重入載入時已歸還舊 transport 且保留新世代', async t => {
    const firstOptions = { skip: 0 };
    const secondOptions = { skip: 20 };
    const grid = createGrid(t, [firstOptions, secondOptions]);
    const first = observe(grid.store.load(firstOptions));
    let second;
    let firstPublished = 0;
    first.done(() => {
        firstPublished += 1;
        second = observe(grid.store.load(secondOptions));
    });
    grid.pending[0].deferred.resolve(['first'], { totalCount: 42 });
    assert.equal(grid.pending[0].abortCount, 0);
    assert.equal(grid.coordinator.getDiagnostics().activeRequestCount, 1);
    const published = new Promise((resolve, reject) => second.done(resolve).fail(reject));
    grid.pending[1].deferred.resolve(['second']);
    assert.deepEqual(await published, ['second']);
    assert.equal(firstPublished, 1);
    assert.equal(grid.coordinator.getDiagnostics().activeRequestCount, 0);
});

/**
 * 同步 loadFactory 例外也必須以相同 Deferred 失敗契約交給 Grid，不得再退回 native Promise。
 */
test('同步載入例外仍符合 Deferred 失敗契約', async () => {
    const jquery = loadRepositoryDeferred();
    const coordinator = coordinatorApi.createCollectionLoadCoordinator();
    try {
        const error = new Error('synthetic synchronous failure');
        const result = observe(coordinator.runLoad(() => { throw error; }, jquery.Deferred));
        assert.equal(typeof result.fail, 'function');
        const actual = await new Promise(resolve => result.fail(resolve));
        assert.equal(actual, error);
        assert.equal(coordinator.getDiagnostics().activeRequestCount, 0);
    } finally {
        coordinator.dispose();
    }
});

/**
 * jQuery 2.x 的 done/fail 消費者可同步拋錯；即使 UI callback 本身失敗，
 * coordinator 也必須先清空傳輸參考，且不得吞掉消費者原始例外。
 */
for (const outcome of ['resolve', 'reject']) {
    test(`Deferred ${outcome} 消費者拋錯仍已釋放 active transport`, () => {
        const jquery = loadRepositoryDeferred();
        const coordinator = coordinatorApi.createCollectionLoadCoordinator();
        const transport = jquery.Deferred();
        try {
            const result = observe(coordinator.runLoad(() => transport.promise(), jquery.Deferred));
            assert.equal(typeof result.done, 'function');
            const error = new Error('synthetic consumer failure');
            result[outcome === 'resolve' ? 'done' : 'fail'](() => { throw error; });
            assert.throws(() => transport[outcome]([]), actual => actual === error);
            assert.equal(coordinator.getDiagnostics().activeRequestCount, 0);
        } finally {
            coordinator.dispose();
        }
    });
}

/**
 * 模擬多次進入、取消與離開頁面；每次 owner 都要拒絕離開後才到的資料，
 * 並歸零 transport/refresh 計數，不把前一頁回應帶入下一頁。
 */
test('重複 200 次 Deferred 載入與 dispose 均回到零資源基準', async () => {
    const jquery = loadRepositoryDeferred();
    for (let index = 0; index < 200; index += 1) {
        const coordinator = coordinatorApi.createCollectionLoadCoordinator();
        const transport = jquery.Deferred();
        const result = observe(coordinator.runLoad(() => transport.promise(), jquery.Deferred));
        assert.equal(typeof result.fail, 'function');
        const rejected = new Promise(resolve => result.fail(resolve));
        coordinator.requestRefresh(() => assert.fail('dispose 後不可 refresh'));
        coordinator.dispose();
        transport.resolve(['synthetic-late']);
        assert.equal((await rejected).isStaleGeneration, true);
        assert.deepEqual(coordinator.getDiagnostics(), {
            disposed: true, activeRequestCount: 0, pendingRefreshCount: 0
        });
    }
});

/**
 * 截圖已確認主版面靜態資源 404/MIME 拒絕。依實際引用解析至 wwwroot，
 * 保護部署隨附資源的可用性；不發 HTTP、不下載套件，也不讀任何會友資料。
 */
test('小組回報主版面的本機腳本與樣式均有部署來源', () => {
    const layout = fs.readFileSync(path.join(productRoot, 'Views/Shared/_Layout.cshtml'), 'utf8');
    const missing = [];
    for (const match of layout.matchAll(/(?:src|href)="~\/([^"?]+\.(?:js|css))(?:\?[^"]*)?"/g)) {
        if (!fs.existsSync(path.join(productRoot, 'wwwroot', match[1]))) missing.push(match[1]);
    }
    assert.deepEqual(missing, [], '不能引用 repo/部署輸出都不存在的靜態檔案');
});


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.