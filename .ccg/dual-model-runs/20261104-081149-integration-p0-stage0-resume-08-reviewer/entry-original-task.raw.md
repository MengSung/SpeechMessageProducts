# CCG reviewer Task: integration-p0-stage0-resume-08

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P0.Worktree

## Request
# 階段0 第8批：小組回報修正與前端守門

你是唯讀外部審查者。本輪為「單模型（使用者指定 ClaudeOnly）」，不是雙模型或配額降級。只審查，不改檔、不提交、不執行產品／工具／測試，不建立任何網路或 D365／LINE 連線。
審查重點：跨使用者／跨教會隔離、資源釋放、機密外洩、例外責任（設計10.5／10.9）、net48/net10行為一致、守門假綠燈。所有真實連線驗收仍由使用者負責。
機密欄位已遮罩；遮罩值不可用來推斷實際設定正確性。不得讀取 appsettings*、User Secrets、環境變數、Logs、金鑰／授權檔，或以 git show 取未遮罩版本。只以所附安全差異審查；如需上下文，可只查明確的非機密程式來源。不要開啟 .ccg/dual-model-runs 的其他歷史輸出或 research 原始紀錄。
輸出只列可驗證的 Critical／High／Medium／Low，包含檔案、行號、觸發條件、具體影響及最小修法；測試不足須說明可重現的缺口，不把純猜測當發現。沒有問題請明寫。不得聲稱實跑測試。
已核定例外：嚴格 .Tests 測試專案僅 CA1707 放寬；未實作 API 的 CA1716 可改語意近名；Core 不通知，Plugin 僅追蹤，最終告警在 net10 重試用盡／階段5；正式金鑰只程序環境且不讀內容；本階段不存在業務 Rejected。小組頁面由使用者確認恢復，圖表未逐項驗證，之前審查兩次為逾時。

範圍：73b6e6cf409daa7559fd3c11d482e3efc542c0d7..ae29cb40aa5c395f83c169068d7ca40e7094c509
安全差異 SHA256：647e0086a791a2ff4f30a357969ecde2890db4f436b1cba7767c195de89b228d
同批其他分份須一併審查後才算完成，本份不可代表全批。
專項：真正 jQuery Deferred 的 always/then 順序、單次完整發布與去重鍵、同 scope 併發／重入、scope/date/user 變更、失敗重試與清理；固定欄位可初始化、共享全域依賴載入次序；移除不存在 JSZip 的影響僅記錄既有風險，不要求變更 DevExtreme 版本。

```diff
diff --git a/ChurchReport.MemberInfo.Tests/JavaScript/weekly-report-load-adapter.test.js b/ChurchReport.MemberInfo.Tests/JavaScript/weekly-report-load-adapter.test.js
new file mode 100644
index 00000000..de5e47d1
--- /dev/null
+++ b/ChurchReport.MemberInfo.Tests/JavaScript/weekly-report-load-adapter.test.js
@@ -0,0 +1,289 @@
+// 直接執行產品 Razor 的 Grid adapter 與 repo 既有 jQuery Deferred，保護 DevExtreme
+// store.load 的 done/fail、多參數分頁資料與載入世代契約。測試不建立 DOM、HTTP 或 CRM 連線；
+// 每個 owner、transport 與合成資料只在單一案例存活，結束時確定 dispose 並完成所有 transport。
+'use strict';
+
+const test = require('node:test');
+const assert = require('node:assert/strict');
+const fs = require('node:fs');
+const path = require('node:path');
+const vm = require('node:vm');
+const coordinatorApi = require(
+    '../../SpeechMessageProducts.ChurchReport/wwwroot/js/collection-load-coordinator.js');
+const productRoot = path.resolve(__dirname, '../../SpeechMessageProducts.ChurchReport');
+
+/**
+ * 載入實際 jQuery 2.2.4 的 Callbacks/Deferred 區塊；只有無 DOM 的通用物件操作使用替身。
+ * 不重寫 Deferred，避免假實作支援原生 Promise 而掩蓋真實 .done 契約。
+ */
+function loadRepositoryDeferred() {
+    const source = fs.readFileSync(path.join(productRoot, 'wwwroot/lib/jquery/dist/jquery.js'), 'utf8');
+    const start = source.indexOf('var rnotwhite =');
+    const end = source.indexOf('// The deferred used on DOM ready', start);
+    assert.ok(start >= 0 && end > start, '必須載入 repo 真實 Deferred，不能靜默換成替身');
+    const jquery = {
+        each(values, callback) {
+            for (const key of Object.keys(values)) {
+                if (callback.call(values[key], key, values[key]) === false) break;
+            }
+            return values;
+        },
+        extend(...values) {
+            return values.length === 1 ? Object.assign(this, values[0]) : Object.assign(...values);
+        },
+        isFunction: value => typeof value === 'function',
+        type: value => typeof value,
+        inArray: (value, values, startIndex) => values == null ? -1 : values.indexOf(value, startIndex)
+    };
+    vm.runInNewContext(source.slice(start, end), { jQuery: jquery, slice: Array.prototype.slice });
+    return jquery;
+}
+
+/**
+ * 抽取並執行實際 Razor adapter，不複製實作。刻意只提供一個已有 store 的元件；
+ * 所有 transport 都由案例控制，adapter 若另建 AJAX 會因不存在此 API 而失敗。
+ */
+function createGrid(t, transports) {
+    const jquery = loadRepositoryDeferred();
+    const source = fs.readFileSync(path.join(productRoot, 'Views/Home/_GeneralGroupGrids.cshtml'), 'utf8');
+    const script = source.match(/<script>([\s\S]*?)<\/script>/);
+    assert.ok(script, '必須執行實際 Grid adapter');
+    const sandbox = { CollectionLoadCoordinator: coordinatorApi, $: jquery, jQuery: jquery, console };
+    sandbox.window = sandbox;
+    vm.runInNewContext(script[1], sandbox);
+    const owner = {};
+    const pending = transports.map(() => {
+        const deferred = jquery.Deferred();
+        let abortCount = 0;
+        const promise = deferred.promise();
+        promise.abort = () => { abortCount += 1; };
+        return { deferred, promise, get abortCount() { return abortCount; } };
+    });
+    let calls = 0;
+    const store = {
+        load(options) {
+            assert.equal(options, transports[calls], '保留原始 loadOptions，不自行組第二條取數管線');
+            return pending[calls++].promise;
+        }
+    };
+    const component = {
+        element: () => [owner],
+        getDataSource: () => ({ store: () => store }),
+        dispose() {}
+    };
+    sandbox.initializeWeeklyReportGridPublicationGuard({ component });
+    const coordinator = component.__weeklyReportLoadCoordinator;
+    t.after(() => {
+        sandbox.disposeWeeklyReportGridPublicationGuard({ component });
+        for (const transport of pending) transport.deferred.resolve([]);
+        assert.equal(coordinator.getDiagnostics().activeRequestCount, 0);
+        assert.equal(coordinator.getDiagnostics().pendingRefreshCount, 0);
+    });
+    return { store, pending, component, coordinator, sandbox, get calls() { return calls; } };
+}
+
+/**
+ * 觀察失敗以便紅燈案例也不產生未處理 rejection；不變更回傳物件或其契約。
+ */
+function observe(result) {
+    Promise.resolve(result).catch(() => {});
+    return result;
+}
+
+/**
+ * 這是 Console .done TypeError 的最小回歸：DevExtreme 應取得原本的 Deferred 表面，
+ * 並在 data 之外收到 totalCount/summary/groupCount，不能被原生 Promise 壓成第一個值。
+ */
+test('實際週報 adapter 保留 done/fail 與分頁完成參數', async t => {
+    const options = { skip: 0, take: 20, requireTotalCount: true };
+    const grid = createGrid(t, [options]);
+    const rows = [{ PresentRecordId: 'record-a', FullName: '合成同名' },
+        { PresentRecordId: 'record-b', FullName: '合成同名' }];
+    const extra = { totalCount: 42, summary: [7], groupCount: 2 };
+    const result = observe(grid.store.load(options));
+    assert.equal(typeof result.done, 'function', 'DevExtreme 直接呼叫 store.load(...).done(...)');
+    assert.equal(typeof result.fail, 'function');
+    assert.equal(typeof result.always, 'function');
+    assert.equal(result.resolve, undefined, '消費者不可自行完成結果以繞過世代防護');
+    const context = { operation: 'synthetic-load' };
+    const completion = new Promise((resolve, reject) => {
+        result.done(function (data, metadata) { resolve({ data, metadata, context: this }); }).fail(reject);
+    });
+    grid.pending[0].deferred.resolveWith(context, [rows, extra]);
+    const actual = await completion;
+    assert.equal(actual.data, rows);
+    assert.equal(actual.data.length, 2, '相同顯示姓名但不同資料庫 ID 都必須保留');
+    assert.equal(actual.metadata, extra);
+    assert.equal(actual.context, context);
+    assert.equal(grid.calls, 1);
+    assert.equal(grid.coordinator.getDiagnostics().activeRequestCount, 0);
+});
+
+/**
+ * 當前失敗不能被轉成成功空集合，或只保留錯誤第一參數。以合成錯誤驗證原始物件、
+ * 狀態及 callback context；任何真實機密或遠端訊息都不進入測試。
+ */
+test('實際週報 adapter 原樣傳遞當前失敗與多個失敗參數', async t => {
+    const options = {};
+    const grid = createGrid(t, [options]);
+    const result = observe(grid.store.load(options));
+    assert.equal(typeof result.fail, 'function');
+    const error = new Error('synthetic failure');
+    const context = { operation: 'synthetic-error' };
+    const completion = new Promise(resolve => {
+        result.fail(function (...args) { resolve({ args, context: this }); });
+    });
+    grid.pending[0].deferred.rejectWith(context, [error, 'timeout', 503]);
+    const actual = await completion;
+    assert.deepEqual(actual.args, [error, 'timeout', 503]);
+    assert.equal(actual.context, context);
+    assert.equal(grid.coordinator.getDiagnostics().activeRequestCount, 0);
+});
+
+/**
+ * 保護晚到成功與錯誤皆不能發布舊小組／日期資料。abort 不強制結束 transport，證明
+ * 真正的邊界仍是 generation；最新世代的 metadata 必須與最新資料配對。
+ */
+for (const oldOutcome of ['resolve', 'reject']) {
+    test(`Deferred 相容修正仍拒絕舊世代 ${oldOutcome}`, async t => {
+        const firstOptions = { scope: 'scope-a' };
+        const secondOptions = { scope: 'scope-b' };
+        const grid = createGrid(t, [firstOptions, secondOptions]);
+        const first = observe(grid.store.load(firstOptions));
+        const second = observe(grid.store.load(secondOptions));
+        assert.equal(typeof first.fail, 'function');
+        const stale = new Promise(resolve => first.fail(resolve));
+        const published = new Promise((resolve, reject) => {
+            second.done((data, metadata) => resolve({ data, metadata })).fail(reject);
+        });
+        grid.pending[1].deferred.resolve(['current'], { totalCount: 1 });
+        assert.deepEqual(await published, { data: ['current'], metadata: { totalCount: 1 } });
+        grid.pending[0].deferred[oldOutcome](['old'], { totalCount: 99 });
+        assert.equal((await stale).isStaleGeneration, true);
+        assert.equal(grid.pending[0].abortCount, 1);
+        assert.equal(grid.coordinator.getDiagnostics().activeRequestCount, 0);
+    });
+}
+
+/**
+ * 兩個 Grid owner 可同時載入，不得因一個 owner 取消而污染另一個。dispose 之後晚到資料
+ * 必須拒絕發布；成功與釋放後 transport/pending intent 都回到零。
+ */
+test('兩個 Grid owner 的取消與資料完全隔離', async t => {
+    const firstOptions = {};
+    const secondOptions = {};
+    const first = createGrid(t, [firstOptions]);
+    const second = createGrid(t, [secondOptions]);
+    const firstLoad = observe(first.store.load(firstOptions));
+    const secondLoad = observe(second.store.load(secondOptions));
+    assert.equal(typeof firstLoad.fail, 'function');
+    const rejected = new Promise(resolve => firstLoad.fail(resolve));
+    const published = new Promise((resolve, reject) => secondLoad.done(resolve).fail(reject));
+    first.sandbox.disposeWeeklyReportGridPublicationGuard({ component: first.component });
+    second.pending[0].deferred.resolve(['owner-b']);
+    first.pending[0].deferred.resolve(['owner-a']);
+    assert.deepEqual(await published, ['owner-b']);
+    assert.equal((await rejected).isStaleGeneration, true);
+    assert.equal(first.pending[0].abortCount, 1);
+    assert.equal(second.pending[0].abortCount, 0);
+});
+
+/**
+ * jQuery 2.x 會同步呼叫 done。消費者同步發起下一次 refresh 時，前一個 transport
+ * 必須已釋放，不能誤 abort 已完成請求或清掉新世代的 activeRequest。
+ */
+test('done 同步重入載入時已歸還舊 transport 且保留新世代', async t => {
+    const firstOptions = { skip: 0 };
+    const secondOptions = { skip: 20 };
+    const grid = createGrid(t, [firstOptions, secondOptions]);
+    const first = observe(grid.store.load(firstOptions));
+    let second;
+    let firstPublished = 0;
+    first.done(() => {
+        firstPublished += 1;
+        second = observe(grid.store.load(secondOptions));
+    });
+    grid.pending[0].deferred.resolve(['first'], { totalCount: 42 });
+    assert.equal(grid.pending[0].abortCount, 0);
+    assert.equal(grid.coordinator.getDiagnostics().activeRequestCount, 1);
+    const published = new Promise((resolve, reject) => second.done(resolve).fail(reject));
+    grid.pending[1].deferred.resolve(['second']);
+    assert.deepEqual(await published, ['second']);
+    assert.equal(firstPublished, 1);
+    assert.equal(grid.coordinator.getDiagnostics().activeRequestCount, 0);
+});
+
+/**
+ * 同步 loadFactory 例外也必須以相同 Deferred 失敗契約交給 Grid，不得再退回 native Promise。
+ */
+test('同步載入例外仍符合 Deferred 失敗契約', async () => {
+    const jquery = loadRepositoryDeferred();
+    const coordinator = coordinatorApi.createCollectionLoadCoordinator();
+    try {
+        const error = new Error('synthetic synchronous failure');
+        const result = observe(coordinator.runLoad(() => { throw error; }, jquery.Deferred));
+        assert.equal(typeof result.fail, 'function');
+        const actual = await new Promise(resolve => result.fail(resolve));
+        assert.equal(actual, error);
+        assert.equal(coordinator.getDiagnostics().activeRequestCount, 0);
+    } finally {
+        coordinator.dispose();
+    }
+});
+
+/**
+ * jQuery 2.x 的 done/fail 消費者可同步拋錯；即使 UI callback 本身失敗，
+ * coordinator 也必須先清空傳輸參考，且不得吞掉消費者原始例外。
+ */
+for (const outcome of ['resolve', 'reject']) {
+    test(`Deferred ${outcome} 消費者拋錯仍已釋放 active transport`, () => {
+        const jquery = loadRepositoryDeferred();
+        const coordinator = coordinatorApi.createCollectionLoadCoordinator();
+        const transport = jquery.Deferred();
+        try {
+            const result = observe(coordinator.runLoad(() => transport.promise(), jquery.Deferred));
+            assert.equal(typeof result.done, 'function');
+            const error = new Error('synthetic consumer failure');
+            result[outcome === 'resolve' ? 'done' : 'fail'](() => { throw error; });
+            assert.throws(() => transport[outcome]([]), actual => actual === error);
+            assert.equal(coordinator.getDiagnostics().activeRequestCount, 0);
+        } finally {
+            coordinator.dispose();
+        }
+    });
+}
+
+/**
+ * 模擬多次進入、取消與離開頁面；每次 owner 都要拒絕離開後才到的資料，
+ * 並歸零 transport/refresh 計數，不把前一頁回應帶入下一頁。
+ */
+test('重複 200 次 Deferred 載入與 dispose 均回到零資源基準', async () => {
+    const jquery = loadRepositoryDeferred();
+    for (let index = 0; index < 200; index += 1) {
+        const coordinator = coordinatorApi.createCollectionLoadCoordinator();
+        const transport = jquery.Deferred();
+        const result = observe(coordinator.runLoad(() => transport.promise(), jquery.Deferred));
+        assert.equal(typeof result.fail, 'function');
+        const rejected = new Promise(resolve => result.fail(resolve));
+        coordinator.requestRefresh(() => assert.fail('dispose 後不可 refresh'));
+        coordinator.dispose();
+        transport.resolve(['synthetic-late']);
+        assert.equal((await rejected).isStaleGeneration, true);
+        assert.deepEqual(coordinator.getDiagnostics(), {
+            disposed: true, activeRequestCount: 0, pendingRefreshCount: 0
+        });
+    }
+});
+
+/**
+ * 截圖已確認主版面靜態資源 404/MIME 拒絕。依實際引用解析至 wwwroot，
+ * 保護部署隨附資源的可用性；不發 HTTP、不下載套件，也不讀任何會友資料。
+ */
+test('小組回報主版面的本機腳本與樣式均有部署來源', () => {
+    const layout = fs.readFileSync(path.join(productRoot, 'Views/Shared/_Layout.cshtml'), 'utf8');
+    const missing = [];
+    for (const match of layout.matchAll(/(?:src|href)="~\/([^"?]+\.(?:js|css))(?:\?[^"]*)?"/g)) {
+        if (!fs.existsSync(path.join(productRoot, 'wwwroot', match[1]))) missing.push(match[1]);
+    }
+    assert.deepEqual(missing, [], '不能引用 repo/部署輸出都不存在的靜態檔案');
+});

diff --git a/SpeechMessageProducts.ChurchReport/Views/Home/IntegrateView.cshtml b/SpeechMessageProducts.ChurchReport/Views/Home/IntegrateView.cshtml
index 93083b10..29c5a7dc 100644
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
index 463ac7ac..ca52d63c 100644
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
index af6f9d14..d9ad8547 100644
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

diff --git a/.github/workflows/solution-ci.yml b/.github/workflows/solution-ci.yml
new file mode 100644
index 000000000..8a3789f69
--- /dev/null
+++ b/.github/workflows/solution-ci.yml
@@ -0,0 +1,218 @@
+name: Solution CI
+
+on:
+  push:
+  pull_request:
+  workflow_dispatch:
+
+permissions:
+  contents: read
+
+jobs:
+  solution:
+    runs-on: windows-latest
+    timeout-minutes: 45
+    defaults:
+      run:
+        shell: powershell
+    env:
+      DOTNET_CLI_UI_LANGUAGE: en-US
+      VSLANG: '1033'
+      BASE_COMMIT: 73b6e6cf409daa7559fd3c11d482e3efc542c0d7
+      SixLaborsLicenseFile: ''
+    steps:
+      - name: Checkout full history
+        uses: actions/checkout@v4
+        with:
+          fetch-depth: 0
+      - name: Select SDK from global.json
+        uses: actions/setup-dotnet@v4
+        with:
+          global-json-file: global.json
+      - name: Select Node for progress page tests
+        uses: actions/setup-node@v4
+        with:
+          node-version: '22'
+      - name: Prepare Six Labors license
+        env:
+          SIXLABORS_LICENSE: ${{ secrets.SIXLABORS_LICENSE }}
+        run: |
+          $ErrorActionPreference = 'Stop'
+          # 使用者核定的一般金鑰只經步驟環境傳入，不插入腳本文字，也不輸出內容。
+          if ([string]::IsNullOrWhiteSpace($env:SIXLABORS_LICENSE)) {
+            $env:SIXLABORS_LICENSE = $null
+            throw 'SIXLABORS_LICENSE is required for Solution CI.'
+          }
+          $created = $false
+          $licensePath = $null
+          try {
+            if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP) -or [string]::IsNullOrWhiteSpace($env:GITHUB_ENV)) {
+              throw 'Runner environment is unavailable.'
+            }
+            $licensePath = Join-Path $env:RUNNER_TEMP ('speechmessage-sixlabors-' + [Guid]::NewGuid().ToString('N') + '.lic')
+            # CreateNew 不覆寫既有檔。單一步驟擁有 stream，寫入故障與成功均先關閉才進行清理。
+            $stream = [IO.File]::Open($licensePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
+            $created = $true
+            try {
+              $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false), 1024, $true)
+              try { $writer.Write($env:SIXLABORS_LICENSE); $writer.Flush() }
+              finally { $writer.Dispose() }
+            } finally { $stream.Dispose() }
+            # 後續建置只取得 runner 路徑；不把內容寫到 GITHUB_ENV、摘要或 artifact。
+            [IO.File]::AppendAllText($env:GITHUB_ENV, "SixLaborsLicenseFile=$licensePath`n", [Text.UTF8Encoding]::new($false))
+          } catch {
+            if ($created -and [IO.File]::Exists($licensePath)) { Remove-Item -LiteralPath $licensePath -Force }
+            throw 'Six Labors license preparation failed.'
+          } finally { $env:SIXLABORS_LICENSE = $null }
+      - name: Restore
+        run: |
+          dotnet restore SpeechMessageProducts.sln
+          if ($LASTEXITCODE -ne 0) { throw 'Solution restore failed.' }
+          foreach ($project in @('tools/I18nCheck.Tests', 'tools/PluginPackaging.Tests')) {
+            dotnet restore $project
+            if ($LASTEXITCODE -ne 0) { throw 'Tool restore failed.' }
+          }
+      - name: Build without signing key
+        run: |
+          # 一般 Release 建置不讀金鑰、不打包；只有獨立正式打包步驟可以取得 secret。
+          dotnet build SpeechMessageProducts.sln -c Release --no-restore -warnaserror:NU1605
+          if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
+          foreach ($project in @('tools/I18nCheck.Tests', 'tools/PluginPackaging.Tests')) {
+            dotnet build $project -c Release --no-restore -warnaserror:NU1605
+            if ($LASTEXITCODE -ne 0) { throw 'Tool build failed.' }
+          }
+      - name: Release solution tests and coverage
+        run: |
+          dotnet test SpeechMessageProducts.sln -c Release --no-build --collect:"XPlat Code Coverage" --results-directory artifacts/ci/solution-tests
+          if ($LASTEXITCODE -ne 0) { throw 'Release solution tests failed.' }
+      - name: Debug tracer contracts
+        run: |
+          dotnet test ToolUtility.Dataverse.Tests -c Debug --filter "FullyQualifiedName~FileToolUtilityTracerTests"
+          if ($LASTEXITCODE -ne 0) { throw 'Debug tracer tests failed.' }
+      - name: Coverage for each new module and target
+        run: |
+          # 各目標分開收集，禁止 net10 報告或其他測試的相依模組掩蓋 net48 的缺口。
+          $cases = @(
+            @{ Project='SpeechMessage.Localization.Tests'; Framework='net48'; Module='SpeechMessage.Localization' },
+            @{ Project='SpeechMessage.Localization.Tests'; Framework='net10.0'; Module='SpeechMessage.Localization' },
+            @{ Project='LineMessagingProcessor.Tests'; Framework='net48'; Module='LineMessagingProcessor.Core' },
+            @{ Project='LineMessagingProcessor.Tests'; Framework='net10.0'; Module='LineMessagingProcessor.Core' },
+            @{ Project='SpeechMessage.Dynamics.Plugins.Tests'; Framework='net48'; Module='SpeechMessage.Dynamics.Plugins' },
+            @{ Project='SpeechMessage.Dynamics.Deployment.Tests'; Framework='net10.0'; Module='SpeechMessage.Dynamics.Deployment' },
+            @{ Project='tools/I18nCheck.Tests'; Framework='net10.0'; Module='I18nCheck' },
+            @{ Project='tools/PluginPackaging.Tests'; Framework='net10.0'; Module='PluginPackaging' }
+          )
+          foreach ($case in $cases) {
+            $results = Join-Path 'artifacts/ci/coverage' ($case.Module + '/' + $case.Framework)
+            if (Test-Path -LiteralPath $results) { throw 'Coverage output must be fresh for every run.' }
+            $include = 'DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Include=[' + $case.Module + ']*'
+            dotnet test $case.Project -c Release -f $case.Framework --no-build --collect:"XPlat Code Coverage" --results-directory $results -- $include
+            if ($LASTEXITCODE -ne 0) { throw 'Module coverage tests failed.' }
+            $reports = @(Get-ChildItem -LiteralPath $results -Filter coverage.cobertura.xml -File -Recurse)
+            if ($reports.Count -ne 1) { throw 'Expected exactly one fresh report for this module and target.' }
+            $coverage = & powershell -NoProfile -File tools/RepoQuality/Test-Coverage.ps1 -ReportPath $reports[0].FullName -Module $case.Module
+            if ($LASTEXITCODE -ne 0) { throw 'Module coverage is below the required threshold or invalid.' }
+            $coverage | Write-Output
+            ($case.Framework + ' ' + ($coverage -join ' ')) | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
+          }
+      - name: Repository quality gates
+        run: |
+          powershell -NoProfile -File tools/RepoQuality/Test-TextEncoding.ps1 -Scope Changed -Base $env:BASE_COMMIT
+          if ($LASTEXITCODE -ne 0) { throw 'Text encoding failed.' }
+          powershell -NoProfile -File tools/RepoQuality/Find-DuplicateCode.ps1 -Baseline tools/RepoQuality/duplicate-baseline.json
+          if ($LASTEXITCODE -ne 0) { throw 'Duplicate source ratchet failed.' }
+          powershell -NoProfile -File tools/RepoQuality/Test-PackageVersions.ps1
+          if ($LASTEXITCODE -ne 0) { throw 'Package version guard failed.' }
+          powershell -NoProfile -File tools/RepoQuality/Test-AnalyzerProbe.ps1
+          if ($LASTEXITCODE -ne 0) { throw 'Analyzer probes failed.' }
+          dotnet run --project tools/I18nCheck -- verify
+          if ($LASTEXITCODE -ne 0) { throw 'I18n verification failed.' }
+      - name: Repository quality tests
+        run: |
+          Import-Module Pester -RequiredVersion 3.4.0 -ErrorAction Stop
+          $result = Invoke-Pester -Script @('tools/RepoQuality/Tests/Test-TextEncoding.Tests.ps1', 'tools/RepoQuality/Tests/Find-DuplicateCode.Tests.ps1') -PassThru
+          if ($result.TotalCount -lt 35 -or $result.FailedCount -ne 0) { throw 'Repository quality tests failed or were not discovered.' }
+          powershell -NoProfile -File tools/RepoQuality/Tests/Test-P002Settings.ps1
+          if ($LASTEXITCODE -ne 0) { throw 'SDK and ignore settings failed.' }
+          powershell -NoProfile -File tools/RepoQuality/Tests/Test-Coverage.Tests.ps1
+          if ($LASTEXITCODE -ne 0) { throw 'Coverage gate tests failed.' }
+          powershell -NoProfile -File tools/RepoQuality/Tests/Test-SixLaborsCiLicense.Tests.ps1
+          if ($LASTEXITCODE -ne 0) { throw 'Six Labors CI license contracts failed.' }
+          node --test tools/RepoQuality/Tests/Test-ProgressPage.cjs
+          if ($LASTEXITCODE -ne 0) { throw 'Progress page tests failed.' }
+          # 直接驗證實際 Razor adapter 與既有 jQuery Deferred，避免只跑伺服器測試卻漏掉表格初始化錯誤。
+          node --test ChurchReport.MemberInfo.Tests/JavaScript/collection-load-coordinator.test.js ChurchReport.MemberInfo.Tests/JavaScript/weekly-report-load-adapter.test.js
+          if ($LASTEXITCODE -ne 0) { throw 'Weekly report JavaScript contracts failed.' }
+          powershell -NoProfile -File docs/scripts/Tests/Test-CcgReviewSummary.ps1
+          if ($LASTEXITCODE -ne 0) { throw 'CCG summary contracts failed.' }
+      - name: Offline plugin packaging tests
+        run: |
+          # 此處只建立與銷毀一次性測試金鑰，從不需要 GitHub 正式 secret。
+          powershell -NoProfile -File tools/PluginPackaging/Tests/Test-PluginAssembly.Tests.ps1
+          if ($LASTEXITCODE -ne 0) { throw 'Plugin metadata samples failed.' }
+          powershell -NoProfile -File tools/PluginPackaging/Tests/Test-PluginBuild.Tests.ps1 -UseEphemeralTestKey
+          if ($LASTEXITCODE -ne 0) { throw 'Plugin build samples failed.' }
+      - name: Prepare optional signing key
+        id: signing
+        env:
+          PLUGIN_SNK_BASE64: ${{ secrets.PLUGIN_SNK_BASE64 }}
+        run: |
+          # 無 secret 時只標示跳過，不能影響一般建置；本計畫不要求建立 GitHub secret。
+          if ([string]::IsNullOrWhiteSpace($env:PLUGIN_SNK_BASE64)) {
+            'Plugin packaging: skipped (PLUGIN_SNK_BASE64 is not configured).' | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
+            'enabled=false' | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
+          } else {
+            # 僅在 runner 暫存目錄還原；內容不輸出、不上傳。清理步驟以固定路徑獨立執行。
+            $keyPath = Join-Path $env:RUNNER_TEMP 'speechmessage-plugin-signing.snk'
+            if (Test-Path -LiteralPath $keyPath) { throw 'Signing key destination is unexpectedly occupied.' }
+            $bytes = $null
+            try {
+              $bytes = [Convert]::FromBase64String($env:PLUGIN_SNK_BASE64)
+              [IO.File]::WriteAllBytes($keyPath, $bytes)
+            } finally {
+              if ($null -ne $bytes) { [Array]::Clear($bytes, 0, $bytes.Length) }
+              $env:PLUGIN_SNK_BASE64 = $null
+            }
+            'enabled=true' | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
+          }
+      - name: Explicit signed plugin packaging
+        if: steps.signing.outputs.enabled == 'true'
+        run: |
+          $env:SPEECHMESSAGE_PLUGIN_KEYFILE = Join-Path $env:RUNNER_TEMP 'speechmessage-plugin-signing.snk'
+          try {
+            dotnet build SpeechMessage.Dynamics.Plugins -c Release -p:SpeechMessagePackagePlugin=true
+            if ($LASTEXITCODE -ne 0) { throw 'Explicit plugin packaging failed.' }
+            powershell -NoProfile -File tools/PluginPackaging/Test-PluginAssembly.ps1
+            if ($LASTEXITCODE -ne 0) { throw 'Signed plugin assembly validation failed.' }
+            'Plugin packaging: signed assembly validation passed.' | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
+          } finally { $env:SPEECHMESSAGE_PLUGIN_KEYFILE = $null }
+      - name: Delete optional signing key
+        if: always()
+        run: |
+          # 單一固定暫存檔，禁止遞迴刪除，也不接收外部指定路徑。
+          $keyPath = Join-Path $env:RUNNER_TEMP 'speechmessage-plugin-signing.snk'
+          if (Test-Path -LiteralPath $keyPath) { Remove-Item -LiteralPath $keyPath -Force }
+      - name: Delete Six Labors license
+        if: always()
+        run: |
+          $ErrorActionPreference = 'Stop'
+          try {
+            if (-not [string]::IsNullOrWhiteSpace($env:SixLaborsLicenseFile)) {
+              # 只清除本步驟發布於 runner 根目錄的特定格式檔名；不得依任意路徑刪檔。
+              $runnerRoot = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\', '/')
+              $licensePath = [IO.Path]::GetFullPath($env:SixLaborsLicenseFile)
+              if (-not [string]::Equals([IO.Path]::GetDirectoryName($licensePath), $runnerRoot, [StringComparison]::OrdinalIgnoreCase) -or
+                  [IO.Path]::GetFileName($licensePath) -cnotmatch '^speechmessage-sixlabors-[0-9a-f]{32}\.lic$') {
+                throw 'Refusing to delete an unowned license path.'
+              }
+              if ([IO.File]::Exists($licensePath)) { Remove-Item -LiteralPath $licensePath -Force }
+            }
+          } finally { $env:SixLaborsLicenseFile = $null }
+      - name: Upload coverage reports
+        if: always()
+        uses: actions/upload-artifact@v4
+        with:
+          name: coverage-reports
+          path: artifacts/ci/**/coverage.cobertura.xml
+          if-no-files-found: warn
+          retention-days: 14

# Unchanged regression-test context requested by user
// ============================================================================
// 檔案路徑：ChurchReport.MemberInfo.Tests/JavaScript/collection-load-coordinator.test.js
// 測試責任：以 Node 內建 test runner 決定性模擬慢網路回應亂序、重複 refresh 與 view dispose，
//           驗證每個 Grid owner 只有最新 generation 能完成發布，且所有 timer／transport 參考可清除。
// 資源生命週期：每個案例都主動 dispose coordinator，測試不建立 watcher、伺服器、DOM、Session、
//               無界 queue 或常駐程序；受控 Promise 只在案例區域存活。
// 編碼要求：本檔案必須以 UTF-8 without BOM、CRLF、final CRLF 儲存。
// ============================================================================
'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { createCollectionLoadCoordinator, mount, disposeOwner } = require(
    '../../SpeechMessageProducts.ChurchReport/wwwroot/js/collection-load-coordinator.js');

/**
 * 建立可由測試指定完成順序的 transport；abort 只記錄呼叫而不強迫 Promise 中止，
 * 用來證明 generation 才是正確性邊界，而不是假設瀏覽器一定能取消已送出的回應。
 */
function createControlledTransport() {
    let resolve;
    let reject;
    let abortCount = 0;
    const promise = new Promise((resolvePromise, rejectPromise) => {
        resolve = resolvePromise;
        reject = rejectPromise;
    });

    promise.abort = function abort() {
        abortCount += 1;
    };

    return {
        promise,
        resolve,
        reject,
        get abortCount() { return abortCount; }
    };
}

/**
 * 驗證第二世代先完成後，第一世代即使稍後成功也只能得到 stale rejection，
 * 不得把舊資料 resolve 給目前 Grid。
 */
test('晚到的舊成功回應不能覆蓋新世代', async () => {
    const coordinator = createCollectionLoadCoordinator();
    const first = createControlledTransport();
    const second = createControlledTransport();

    const firstLoad = coordinator.runLoad(() => first.promise);
    const secondLoad = coordinator.runLoad(() => second.promise);
    second.resolve(['new-generation']);
    assert.deepEqual(await secondLoad, ['new-generation']);

    first.resolve(['stale-generation']);
    await assert.rejects(firstLoad, error => error && error.isStaleGeneration === true);
    assert.equal(first.abortCount, 1);
    coordinator.dispose();
});

/**
 * 驗證舊世代的錯誤也不能改寫新世代 error UI；舊錯誤一律正規化為 stale cancellation，
 * 最新世代成功值仍完整交付。
 */
test('晚到的舊錯誤不能覆蓋新世代成功狀態', async () => {
    const coordinator = createCollectionLoadCoordinator();
    const first = createControlledTransport();
    const second = createControlledTransport();

    const firstLoad = coordinator.runLoad(() => first.promise);
    const secondLoad = coordinator.runLoad(() => second.promise);
    second.resolve(['current']);
    assert.deepEqual(await secondLoad, ['current']);

    first.reject(new Error('old network failure'));
    await assert.rejects(firstLoad, error => error && error.isStaleGeneration === true);
    coordinator.dispose();
});

/**
 * 驗證 active load 期間任意多次 refresh 只保留一個 pending intent；目前世代完成後
 * refresh action 只執行一次，不形成無界 timer、Promise 或 XHR queue。
 */
test('active load 期間重複 refresh 只合併成一次', async () => {
    const coordinator = createCollectionLoadCoordinator();
    const transport = createControlledTransport();
    let refreshCount = 0;
    const load = coordinator.runLoad(() => transport.promise);

    for (let index = 0; index < 100; index += 1) {
        coordinator.requestRefresh(() => { refreshCount += 1; });
    }

    assert.equal(coordinator.getDiagnostics().pendingRefreshCount, 1);
    transport.resolve([]);
    await load;
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(refreshCount, 1);
    assert.equal(coordinator.getDiagnostics().pendingRefreshCount, 0);
    coordinator.dispose();
});

/**
 * 驗證 dispose 先使 generation 失效、再 abort transport 並清空 pending callback；
 * 之後的晚到回應不得發布，診斷計數必須回到零且不可再次 refresh。
 */
test('dispose 後會清除 transport 與 pending refresh 並拒絕晚到回應', async () => {
    const coordinator = createCollectionLoadCoordinator();
    const transport = createControlledTransport();
    let refreshCount = 0;
    const load = coordinator.runLoad(() => transport.promise);
    coordinator.requestRefresh(() => { refreshCount += 1; });

    coordinator.dispose();
    transport.resolve(['late']);

    await assert.rejects(load, error => error && error.isStaleGeneration === true);
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(transport.abortCount, 1);
    assert.equal(refreshCount, 0);
    assert.deepEqual(coordinator.getDiagnostics(), {
        disposed: true,
        activeRequestCount: 0,
        pendingRefreshCount: 0
    });
});

/**
 * 驗證同一 DOM owner 被 partial view 重複 mount 時，舊 coordinator 與舊元件只釋放一次，
 * WeakMap 最終只指向最新 owner；測試結束明確 disposeOwner，避免測試物件留在 registry。
 */
test('同一 owner 重複 mount 會先完整釋放舊世代', () => {
    const owner = {};
    let firstDisposeCount = 0;
    let secondDisposeCount = 0;
    const first = mount(owner, () => { firstDisposeCount += 1; });
    const second = mount(owner, () => { secondDisposeCount += 1; });

    assert.equal(first.getDiagnostics().disposed, true);
    assert.equal(second.getDiagnostics().disposed, false);
    assert.equal(firstDisposeCount, 1);
    assert.equal(secondDisposeCount, 0);

    disposeOwner(owner);
    assert.equal(second.getDiagnostics().disposed, true);
    assert.equal(secondDisposeCount, 1);
});
```


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.