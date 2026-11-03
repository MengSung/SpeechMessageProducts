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
