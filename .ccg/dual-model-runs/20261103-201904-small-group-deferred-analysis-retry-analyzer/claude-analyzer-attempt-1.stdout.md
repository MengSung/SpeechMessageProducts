##分析：runLoad 新增可選 jQuery Deferred 分支（最小修正評估）

### 現狀判斷
症狀「`...load(...).done is not a function`」成立的唯一方式是：`runLoad` 目前一律回傳原生 `Promise`，而原生 `Promise` 沒有 `.done/.fail`。只要呼叫端（或共用 adapter 的既有程式路徑）仍以 jQuery Deferred 介面消費回傳值，就必定炸在這裡——這與「兩個 Grid 共用同一 adapter」且兩次出現同一錯誤的描述一致。提出的最小修正方向（可選 Deferred factory、Grid 端注入 `$.Deferred`）**對症**，但下列幾點是「最小修正」實作時最容易漏掉、且會造成二次隱性錯誤的地方。

---

### Critical

1. **resolve/reject 的呼叫順序必須在清理 state 之後，不能在之前**
   目前敘述：「當前 success resolve(value)...再清除 activeRequest」。這個順序搭配原生 `Promise` 是安全的，因為 `.then` callback 一定排進 microtask，`resolve()` 之後緊接的清理程式碼必定先執行完才輪到 Grid 的 `.then`。
   但換成 `$.Deferred`／jQuery 2.2.4 之後**語意完全不同**：`deferred.resolve()`／`reject()` 會**同步**、在同一個呼叫堆疊內直接執行所有已註冊的 `.done/.fail` callback。若 Grid 的 `.done` callback 在同一個 tick 內（例如分頁、重新整理）同步觸發下一次對同一個 owner 的 `runLoad`，那次重入會看到尚未被清除的舊 `activeRequest`／世代狀態，造成誤判（錯誤地判定衝突、或放任兩個請求並存）。
   **要求**：清除 `activeRequest`／owner pending 狀態必須搬到呼叫 `deferred.resolveWith/rejectWith` **之前**，兩個分支（native/jQuery）都要用同一順序，不能只為了相容舊行為而保留「先 resolve 再清理」。

2. **成功/失敗 callback 丟例外時，清理邏輯會被跳過（同步語意的直接後果）**
   承上，若清理碼寫在 `resolve()`／`reject()` 呼叫之後，一旦 Grid 的 `.done`／`.fail` 內丟例外，例外會同步往上傳，直接跳過 runLoad 自己的清理行。必須用 `try { ... } finally { 清理 activeRequest/owner 狀態 }` 包住觸發 Deferred 的那一步，而不是依賴程式碼執行順序的假設。這點原生 Promise 分支因為 `.then` callback 本就非同步隔離，不會暴露這個問題；但這不代表 jQuery 分支可以照搬同一段清理碼位置。

3. **多參數／metadata 轉送，必須在「連到 transport 的 done/fail」那一刻就分支，不能先經過現有的 `Promise.resolve(transport).then(...)` 再轉成 Deferred**
   目前內部實作是 `Promise.resolve(transport).then(value => resolve(value))`。即使外層改成回傳 `$.Deferred().promise()`，只要內部仍先把 `transport`（真正的 jQuery 多參數 promise）丟進 `Promise.resolve(...)` 做介接，第二參數（`extra`：`totalCount/summary/groupCount`）在那一步就已經依 Promise/A+ 規則被丟棄——因為 native Promise 解析 thenable 時只呼叫單參數的 `resolvePromise`。
   換句話說：「新增 Deferred 分支」若只是在**最外層**包一層轉接（`nativePromise.then(data => deferred.resolve(data))`），`.done/.fail` 存在了、`TypeError` 不會再出現，但 totalCount/分組等 metadata 仍然遺失，變成更難察覺的靜默錯誤。
   **要求**：jQuery 分支必須直接對 `originalLoad(loadOptions)` 回傳的原始 transport promise 掛 `.done(function(){ deferred.resolveWith(this, arguments); }).fail(function(){ deferred.rejectWith(this, arguments); })`，世代檢查要包在這一層，不能經過任何先截斷成單參數的中介轉換。

4. **對外只能回傳 `deferred.promise()`，不能回傳 Deferred 本體**
   否則 Grid（或任何拿到回傳值的程式）可以外部呼叫 `.resolve()/.reject()`，破壞「只有 coordinator 能決定結算」這個不變量，世代防護形同虛設。這點不涉及放寬安全守門本身，而是封裝正確性，必須在程式碼審查時明確檢查。

---

### Warning

5. **`StaleGenerationError` 的拒絕要確保 `.fail()` 端不會被當成「使用者可見錯誤」**
   原生 Promise 路徑用 `.catch()` 吞掉過期世代的拒絕；換成 jQuery `.fail()` 的 Grid 端，要確認同一顆 `StaleGenerationError` 被 reject 時，Grid（或任何監聽 `.fail` 的程式）同樣會靜默忽略，而不是跳出錯誤 toast。這是行為對等性，不是功能新增，但很容易在只關注「`.done` 能不能叫到」時被忽略。

6. **jQuery 2.2.4（< 3.0）的 `.then()` 不是 Promises/A+ 語意**
   2.2.4 的 `.then()` 不會自動把 callback 內丟出的例外轉成 rejection（這是 3.0 才改的行為）。如果任何地方仍混用 `.then()` 鏈接 jQuery Deferred 做進一步處理，例外會直接同步往外拋，而不是被下一個 `.catch` 接住。目前的最小修正設計以 `try/finally` 處理（見 Critical #2）已經是對的方向，只是要明確：不能假設「掛在 jQuery promise 後面的 `.then` 會像原生 Promise 一樣安全吃掉例外」。

7. **double-settle 不會報錯，測試必須用呼叫次數斷言，不能只驗證最終值**
   jQuery Deferred 對已經 resolve/reject 過的物件再呼叫一次 resolve/reject 是靜默 no-op，不會丟例外。若清理邏輯或世代檢查有漏洞導致同一個 Deferred 被結算兩次，測試若只檢查「`.done` 收到的最終資料正確」會完全測不出來。測試應額外斷言 `.done`/`.fail` 各自被呼叫的**次數**（單次載入、單次結算）。

8. **owner WeakMap 重新掛載時機**
   重複 mount 會 dispose 舊 owner、abort transport、清 pending refresh——這個流程對 jQuery 分支一樣要走過，且舊 owner 對應的 Deferred 若仍有外部持有的參照（例如 Grid 內部快取了 promise），世代檢查必須保證它最終走到 reject（或永遠 pending 但被忽略），絕對不能在 dispose 之後才 resolve 並觸發已銷毀 Grid 的 `.done` 回調去碰已經不存在的 DOM/狀態。這點敘述中已有世代防護覆蓋，但 Deferred 分支的「同步觸發」特性（Critical #1）會放大這類競態的可觀察性，值得在 dispose 相關測試裡額外補一組「dispose 後 transport 才完成」的案例。

9. **Node 測試環境要確認用的是真的 jQuery 2.2.4 Deferred，不是替代實作**
   jQuery 在 Node 下需要一個 `window`（通常透過 jsdom 或 `require('jquery')(window)` 模式）才能正確載入；如果測試用的是簡化 shim 或版本不符的 jQuery，前面提到的「同步 callback 觸發」「`.then()` 非 Promise/A+」等版本特性可能測不出來，等於沒測到真正會出事的那部分行為。需要在測試設定裡明確確認載入的是 repo 實際依賴的 jQuery 2.2.4 原始碼。

---

### Info

10. **圖表空白另案處理**
    圖表使用獨立的 DataSource，與這個 Grid 共用 adapter 的 `.done is not a function` 是不同程式路徑；在沒有圖表端程式/契約的情況下，不應假設同一個 runLoad/Deferred 修正能解決圖表空白，需要圖表自己的 DataSource 設定與錄製的錯誤資訊才能判斷。

11. **日期切換維持原生 Promise 是合理的保守選擇**
    日期切換現有程式用 `.then().catch()`，沒有依賴 `.done/.fail` 或多參數 metadata，維持原生 Promise 作為 default 分支不會引入回歸風險，是這個最小修正裡风险最低的部分。

12. **沒有被要求但要注意：不要因為加了 Deferred 分支就順手放寬世代檢查的位置或拿掉 StaleGenerationError**——本次評估未發現提案有此意圖，僅在此重申以免後續實作走偏（遵循既有要求，不另外提出放寬建議）。

---

### 結論
方向正確、範圍夠小，但「可選 Deferred factory」四個字背後藏著至少三個容易被忽略的實作細節：**(a)** resolve/reject 前先清理狀態（同步語意重入問題）、**(b)** 用 try/finally 保護清理碼不被 callback 例外跳過、**(c)** 多參數轉送必須在貼著 transport 的 `.done/.fail` 那一層做，不能先經過既有的 `Promise.resolve(transport).then()` 介接。這三點任何一個沒處理，修正後仍會「`.done` 能呼叫了，但行為仍錯」或「偶發重入/漏清理」。建議把 Critical #1–4 都寫成獨立測試案例（而不是只測「`.done` 存在且收到正確最終值」），才算覆蓋到題述要求的「多參數、錯誤、亂序、dispose、獨立 owner」。

---
SESSION_ID: aac82133-766b-42ae-9457-a65cce374239
