/*
 * 檔案整體說明
 * 目的與定位：本檔為共通層 F6 的正式 LIFF 純路由契約測試，直接執行
 * GoodShepherd2.Web 的產品 JavaScript，保護 SDK 初始化先後與固定本機白名單。
 * 主要功能與流程：每案建立獨立 VM、SDK Proxy、頁面參數探針及受控 Promise，
 * 驗證 init 成功前零參數讀寫，初始化拒絕、未知鍵、原型鍵與危險目標零路由。
 * 內建 whoami 與 F12 enroll 擴充必須精確派送；純路由不得自行改寫 location。
 * 邊界與資源：所有值皆為合成資料，VM 不提供網路、計時器或真正瀏覽器，
 * 不代表 LINE SDK、手機或部署站台驗收。每案 owner 的 after hook 釋放懸置
 * 初始化並排空 bootstrap，測試與 hook 均有有界逾時，不保留身分或共用可變資料。
 * 維護與驗證：執行 node --test SpeechMessageProducts.GoodShepherd2.Web.Tests/js/liff-router.test.cjs；
 * 缺產品檔必須紅燈，不 skip 或內嵌替身。決定性斷言包含次序、讀取次數、
 * 派送路徑及零直接導頁。檔案使用 UTF-8 無 BOM、CRLF 與末尾 CRLF。
 */
'use strict';

const assert = require('node:assert/strict');
const { existsSync, readFileSync } = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const WHOAMI_PAGE = '/liff/whoami.html';
const ENROLL_PAGE = '/liff/enroll.html';
const TEST_OPTIONS = { timeout: 2000 };

/**
 * 從正式測試目錄向上定位同工作樹的方案，只組合預定產品檔路徑。
 * 不載入其他工作樹、不搜尋替代實作；缺少方案或產品檔都應保留可診斷紅燈。
 */
function findRouterPath() {
    for (let directory = __dirname; ; directory = path.dirname(directory)) {
        if (existsSync(path.join(directory, 'SpeechMessageProducts.sln'))) {
            return path.join(directory, 'SpeechMessageProducts.GoodShepherd2.Web',
                'wwwroot', 'liff', 'liff-router.js');
        }
        assert.notEqual(directory, path.dirname(directory),
            'F6 路由測試：無法在祖先目錄找到本工作樹方案。');
    }
}

/**
 * 每案只讀真實產品來源，在獨立 VM 取出 CommonJS／UMD 的 CommonJS 匯出。
 * 缺檔時直接失敗，禁止以內嵌 bootstrapRouter 或成功替身掩蓋尚未實作的事實。
 */
function loadBootstrapRouter(browser) {
    const filename = findRouterPath();
    assert.ok(existsSync(filename),
        `F6 路由測試，產品尚未實作：缺少 ${filename}；這不是已通過的測試。`);
    const source = readFileSync(filename, 'utf8');
    const commonJsModule = { exports: {} };
    browser.module = commonJsModule;
    browser.exports = commonJsModule.exports;
    browser.URL = URL;
    browser.URLSearchParams = URLSearchParams;
    vm.runInNewContext(source, browser, { filename, timeout: 1000 });
    assert.equal(typeof commonJsModule.exports.bootstrapRouter, 'function',
        '預定純 CommonJS／UMD API 必須匯出 bootstrapRouter(liff, options)。');
    return commonJsModule.exports.bootstrapRouter;
}

/**
 * 建立單案的可控 SDK 與頁面探針。resolveInit／rejectInit 是唯一的初始化開關；
 * 只有 SDK 回傳的 Promise 成功續行後才開放參數讀寫，不能把呼叫 init 當成完成。
 * 探針只記錄合成值且不拋錯，讓測試仍能抓出吞掉提前存取錯誤的實作。
 */
function createHarness(state, routes = { whoami: WHOAMI_PAGE }) {
    let resolveInit;
    let rejectInit;
    let initialized = false;
    const initialization = new Promise((resolve, reject) => {
        resolveInit = resolve;
        rejectInit = reject;
    });
    const events = [];
    const earlyAccesses = [];
    const navigations = [];
    const routeCalls = [];
    const initCalls = [];
    let stateReads = 0;

    function observeParameterAccess(operation) {
        if (!initialized) earlyAccesses.push(operation);
    }

    const search = `?${new URLSearchParams({
        'liff.state': state,
        'liff.context': 'synthetic-context'
    })}`;
    const location = {};
    const locationValues = {
        search,
        hash: '#synthetic-liff-fragment',
        href: `https://router.invalid/liff/${search}#synthetic-liff-fragment`,
        origin: 'https://router.invalid',
        pathname: '/liff/'
    };
    for (const [name, value] of Object.entries(locationValues)) {
        Object.defineProperty(location, name, {
            get() {
                observeParameterAccess(`location.${name}:get`);
                return value;
            },
            set(candidate) {
                observeParameterAccess(`location.${name}:set`);
                navigations.push({ operation: `location.${name}:set`, candidate });
            }
        });
    }
    for (const name of ['assign', 'replace', 'reload']) {
        location[name] = (candidate) => {
            observeParameterAccess(`location.${name}`);
            navigations.push({ operation: `location.${name}`, candidate });
        };
    }

    const browser = {
        open(candidate) {
            observeParameterAccess('window.open');
            navigations.push({ operation: 'window.open', candidate });
        },
        history: {
            pushState(_data, _title, candidate) {
                observeParameterAccess('history.pushState');
                navigations.push({ operation: 'history.pushState', candidate });
            },
            replaceState(_data, _title, candidate) {
                observeParameterAccess('history.replaceState');
                navigations.push({ operation: 'history.replaceState', candidate });
            }
        }
    };
    Object.defineProperty(browser, 'location', {
        get() {
            return location;
        },
        set(candidate) {
            observeParameterAccess('window.location:set');
            navigations.push({ operation: 'window.location:set', candidate });
        }
    });
    browser.window = browser;
    browser.self = browser;

    const liff = new Proxy({
        init(options) {
            initCalls.push(options.liffId);
            events.push('init:called');
            return initialization.then(() => {
                initialized = true;
                events.push('init:fulfilled');
            }, (error) => {
                events.push('init:rejected');
                throw error;
            });
        },
        get state() {
            observeParameterAccess('liff.state:getter');
            return state;
        },
        set state(_candidate) {
            observeParameterAccess('liff.state:setter');
        }
    }, {
        get(target, name, receiver) {
            if (name !== 'init') observeParameterAccess(`liff.${String(name)}:get`);
            return Reflect.get(target, name, receiver);
        },
        set(target, name, value, receiver) {
            observeParameterAccess(`liff.${String(name)}:set`);
            return Reflect.set(target, name, value, receiver);
        }
    });

    return {
        browser, liff, events, earlyAccesses, navigations, routeCalls, initCalls,
        resolveInit, rejectInit,
        get stateReads() {
            return stateReads;
        },
        options: {
            liffId: 'synthetic-liff-id',
            routes,
            readState() {
                stateReads += 1;
                events.push('readState');
                observeParameterAccess('readState');
                return new URLSearchParams(location.search).get('liff.state');
            },
            onRoute(candidate) {
                events.push('onRoute');
                observeParameterAccess('onRoute');
                routeCalls.push(candidate);
            }
        }
    };
}

/**
 * 啟動真實 bootstrap 並立即接住拒絕，避免故障注入產生未處理 rejection。
 * 單案 owner 的 after hook 會釋放 init、排空 bootstrap；不要求失敗的錯誤格式。
 */
function startRouter(t, harness) {
    const bootstrapRouter = loadBootstrapRouter(harness.browser);
    let completion;
    t.after(async () => {
        harness.resolveInit();
        if (completion) await completion;
    }, { timeout: 1000 });
    const returned = bootstrapRouter(harness.liff, harness.options);
    completion = Promise.resolve(returned).then(
        () => ({ status: 'fulfilled' }),
        (error) => ({ status: 'rejected', error })
    );
    assert.equal(typeof returned?.then, 'function',
        'bootstrapRouter 必須回傳可等待的 Promise，才能界定初始化及路由生命週期。');
    return completion;
}

/**
 * 開放 init 後，正向案例必須成功結束。負向案例另行觀察零路由，
 * 避免把 Promise 的拒絕策略或錯誤類別設計成額外產品 API。
 */
async function completeSuccessfully(harness, completion) {
    harness.resolveInit();
    const outcome = await completion;
    assert.equal(outcome.status, 'fulfilled', outcome.error?.stack);
}

/**
 * 每個案例都核對零提前存取與零直接導頁，確保未知 state 不能透過 location、
 * history 或 window.open 繞過 onRoute；所有紀錄只屬於該案例。
 */
function assertNoUnsafeEffects(harness) {
    assert.deepEqual(harness.earlyAccesses, [],
        'init 成功前不得讀寫 liff.*、頁面參數或派送路由。');
    assert.deepEqual(harness.navigations, [],
        '純路由 API 必須只委派 onRoute，不可直接改寫瀏覽器位置。');
}

// 保護契約：init 懸置時即使排空微工作也不得預讀或路由；完成後順序必須明確。
test('router_doesNotTouchLiffParamsBeforeInit', TEST_OPTIONS, async (t) => {
    const harness = createHarness('whoami');
    const completion = startRouter(t, harness);
    let settled = false;
    completion.then(() => { settled = true; });
    await Promise.resolve();
    await Promise.resolve();

    assert.deepEqual(harness.initCalls, ['synthetic-liff-id']);
    assert.deepEqual(harness.events, ['init:called']);
    assert.equal(harness.stateReads, 0);
    assert.equal(settled, false, 'bootstrap 必須等待仍懸置的 init Promise。');
    assert.deepEqual(harness.routeCalls, []);
    assertNoUnsafeEffects(harness);

    await completeSuccessfully(harness, completion);

    assert.deepEqual(harness.events,
        ['init:called', 'init:fulfilled', 'readState', 'onRoute']);
    assert.equal(harness.stateReads, 1);
    assert.deepEqual(harness.routeCalls, [WHOAMI_PAGE]);
    assertNoUnsafeEffects(harness);
});

// 故障注入：有效 whoami 仍須在 SDK 初始化拒絕時保持零 state 讀取與零導頁。
test('router_initRejectedDoesNotReadStateOrRoute', TEST_OPTIONS, async (t) => {
    const harness = createHarness('whoami');
    const completion = startRouter(t, harness);
    harness.rejectInit(new Error('synthetic init rejection'));
    await completion;

    assert.deepEqual(harness.initCalls, ['synthetic-liff-id']);
    assert.deepEqual(harness.events, ['init:called', 'init:rejected']);
    assert.equal(harness.stateReads, 0);
    assert.deepEqual(harness.routeCalls, []);
    assertNoUnsafeEffects(harness);
});

// 保護內建路由：呼叫端沒有重複登錄 whoami，仍應派送到固定本機頁面。
test('router_hasBuiltInWhoami', TEST_OPTIONS, async (t) => {
    const harness = createHarness('whoami', {});
    const completion = startRouter(t, harness);
    await completeSuccessfully(harness, completion);

    assert.equal(harness.stateReads, 1);
    assert.deepEqual(harness.routeCalls, [WHOAMI_PAGE]);
    assertNoUnsafeEffects(harness);
});

// F12 擴充契約：以白名單加入 enroll 即可使用，不要求修改純路由 API或任意 URL 轉址。
test('router_canExtendWhitelistWithF12Enroll', TEST_OPTIONS, async (t) => {
    const harness = createHarness('enroll', { enroll: ENROLL_PAGE });
    const completion = startRouter(t, harness);
    await completeSuccessfully(harness, completion);

    assert.equal(harness.stateReads, 1);
    assert.deepEqual(harness.routeCalls, [ENROLL_PAGE]);
    assertNoUnsafeEffects(harness);
});

const unknownStates = [
    'missing', 'enroll', 'WHOAMI', '/whoami', ' whoami', 'whoami ',
    'whoami?next=https://attacker.invalid/', 'whoami#enroll',
    'whoami/../enroll', 'https://attacker.invalid/', '//attacker.invalid/',
    'javascript:alert(1)', '%2F%2Fattacker.invalid/', '\\\\attacker.invalid\\',
    '__proto__', 'constructor', 'toString'
];

for (const state of unknownStates) {
    // 故障注入：相似鍵、原型鍵與外部網址都不能被補正、前綴匹配或當成導航目標。
    test(`router_unknownStateFailsClosed ${JSON.stringify(state)}`,
        TEST_OPTIONS, async (t) => {
            const harness = createHarness(state);
            const completion = startRouter(t, harness);
            harness.resolveInit();
            await completion;

            assert.deepEqual(harness.initCalls, ['synthetic-liff-id']);
            assert.equal(harness.stateReads, 1);
            assert.deepEqual(harness.routeCalls, []);
            assertNoUnsafeEffects(harness);
        });
}

// 故障注入：有可用的本機目標也不能把原型繼承屬性當成白名單自有路由。
test('router_inheritedRouteIsNotWhitelisted', TEST_OPTIONS, async (t) => {
    const routes = Object.assign(Object.create({ inherited: WHOAMI_PAGE }),
        { whoami: WHOAMI_PAGE });
    const harness = createHarness('inherited', routes);
    const completion = startRouter(t, harness);
    harness.resolveInit();
    await completion;

    assert.equal(harness.stateReads, 1);
    assert.deepEqual(harness.routeCalls, []);
    assertNoUnsafeEffects(harness);
});

const unsafeTargets = [
    'https://attacker.invalid/',
    '//attacker.invalid/',
    'javascript:alert(1)',
    'data:text/html,synthetic',
    '/\\attacker.invalid/path'
];

for (const target of unsafeTargets) {
    // 設定故障注入：鍵雖精確吻合，危險目標仍不得派送；允許在初始化前拒絕設定。
    test(`router_whitelistCannotIntroduceOpenRedirect ${JSON.stringify(target)}`,
        TEST_OPTIONS, async (t) => {
            const harness = createHarness('enroll', { enroll: target });
            const completion = startRouter(t, harness);
            harness.resolveInit();
            await completion;

            assert.deepEqual(harness.routeCalls, []);
            assertNoUnsafeEffects(harness);
        });
}


for (const target of ['/liff/../enroll.html', '/liff/./enroll.html', '/liff/%2e%2e/enroll.html',
    '/liff//enroll.html', '/liff/enroll.html?next=whoami', '/liff/enroll.html#whoami',
    '/liff/enroll.html/..', '/liff/enroll.html%00', '/liff/enroll\\page.html']) {
    // 不可先交 URL 正規化再接受，避免本機白名單中的設定錯誤演變成跳脫路由。
    test(`router_rejectsNonCanonicalLocalTarget ${JSON.stringify(target)}`, TEST_OPTIONS, async t => {
        const h = createHarness('enroll', { enroll: target }); const completion = startRouter(t, h);
        h.resolveInit(); await completion; assert.deepEqual(h.routeCalls, []); assertNoUnsafeEffects(h);
    });
}

for (const state of [null, undefined]) {
    test(`router_missingStateDefaultsWhoami ${state}`, TEST_OPTIONS, async t => {
        const h = createHarness('ignored'); h.options.readState = () => state;
        const completion = startRouter(t, h); await completeSuccessfully(h, completion);
        assert.deepEqual(h.routeCalls, [WHOAMI_PAGE]); assertNoUnsafeEffects(h);
    });
}

test('router_fixedWhoamiCannotBeOverridden', TEST_OPTIONS, async t => {
    const h = createHarness('whoami', { whoami: ENROLL_PAGE }); const completion = startRouter(t, h);
    h.resolveInit(); await completion; assert.deepEqual(h.routeCalls, []); assertNoUnsafeEffects(h);
});

for (const state of ['constructor', '__proto__', 'prototype']) {
    test(`router_ownPrototypeKeyStillRejected ${state}`, TEST_OPTIONS, async t => {
        const routes = Object.create(null); routes[state] = ENROLL_PAGE;
        const h = createHarness(state, routes); const completion = startRouter(t, h);
        h.resolveInit(); await completion; assert.deepEqual(h.routeCalls, []); assertNoUnsafeEffects(h);
    });
}

test('router_stateReaderFaultNeverRoutes', TEST_OPTIONS, async t => {
    const h = createHarness('whoami'); h.options.readState = () => { throw new Error('synthetic read'); };
    const completion = startRouter(t, h); h.resolveInit(); const outcome = await completion;
    assert.equal(outcome.status, 'rejected'); assert.deepEqual(h.routeCalls, []); assertNoUnsafeEffects(h);
});

test('router_callbackRejectionRemainsObservable', TEST_OPTIONS, async t => {
    const h = createHarness('whoami'); h.options.onRoute = async () => { throw new Error('synthetic route'); };
    const completion = startRouter(t, h); h.resolveInit(); const outcome = await completion;
    assert.equal(outcome.status, 'rejected'); assertNoUnsafeEffects(h);
});


for (const target of ['/liff/enroll.html\n', '/liff/enroll.html\r\n']) {
    // 尾端控制字元必須被精確白名單拒絕，不能交由 URL 或未來的多行比對隱式裁切。
    test(`router_trailingNewlineTargetRejected ${JSON.stringify(target)}`, TEST_OPTIONS, async t => {
        const h = createHarness('enroll', { enroll: target }); const completion = startRouter(t, h);
        h.resolveInit(); await completion; assert.deepEqual(h.routeCalls, []); assertNoUnsafeEffects(h);
    });
}

test('router_trailingNewlineKeyRejectedEvenWhenOwnRoute', TEST_OPTIONS, async t => {
    const h = createHarness('enroll\n', { ['enroll\n']: ENROLL_PAGE }); const completion = startRouter(t, h);
    h.resolveInit(); await completion; assert.deepEqual(h.routeCalls, []); assertNoUnsafeEffects(h);
});
