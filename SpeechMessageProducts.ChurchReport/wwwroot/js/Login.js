/*
檔案整體說明

目的與定位：處理登入表單的前端送出、載入狀態、提示及導覽，配合伺服器固定登入回應欄位；輸入驗證只改善互動，不取代認證。

主要功能與流程：
操作入口包含getLoadPanelInstance、Login、Register、onBegin、onSuccess、onComplete、ShowToast；呼叫參數與輸出形狀以各入口宣告為準。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
*/
// ===========================================
// Login 頁面 JavaScript 邏輯
// ===========================================

// 基礎診斷：確認 JavaScript 執行環境
console.log('=== JavaScript 開始執行 ===');
console.log('jQuery 版本:', typeof $ !== 'undefined' ? $.fn.jquery : '未載入');
console.log('DevExtreme 版本:', typeof DevExpress !== 'undefined' ? 'loaded' : '未載入');

// 載入面板實例取得函數
function getLoadPanelInstance() { return $("#loadPanel").dxLoadPanel("instance"); }

// 登入按鈕點擊事件：顯示載入面板
function Login(arg) { getLoadPanelInstance().show(); }

// 註冊按鈕點擊事件：跳轉到註冊頁面
function Register(arg) { window.location.href = "/Home/Register"; }

// Gallery 初始化：使用輪詢機制等待實例創建
$(function() {
    console.log('[Gallery] 開始初始化流程');
    console.log('[Gallery] #gallery 元素數量:', $('#gallery').length);

    var attemptCount = 0;      // 嘗試次數計數器
    var maxAttempts = 20;      // 最大嘗試次數
    var intervalMs = 200;      // 每次間隔 200ms

    // 輪詢檢查 Gallery 實例是否已創建
    var pollGallery = setInterval(function() {
        attemptCount++;
        console.log('[Gallery] 嘗試第 ' + attemptCount + ' 次獲取實例...');

        try {
            var gallery = $("#gallery").dxGallery("instance");

            if (gallery) {
                // 成功獲取實例，停止輪詢
                clearInterval(pollGallery);
                console.log('[Gallery] ? 成功獲取實例！');
                console.log('[Gallery] 當前選項:', gallery.option());

                // 強制設定 Gallery 選項
                gallery.option({
                    slideshowDelay: 6000,     // 每張圖停留 6 秒
                    animationDuration: 800,    // 過場動畫 800ms
                    animationEnabled: true,    // 啟用動畫
                    swipeEnabled: true,        // 啟用手勢滑動
                    loop: true,                // 循環播放
                    stretchImages: false       // 不拉伸圖片
                });

                console.log('[Gallery] 設定完成:', gallery.option());

                // 檢查圖片數量並決定是否啟動自動播放
                var dataSource = gallery.option("dataSource");
                console.log('[Gallery] 圖片數量:', dataSource ? dataSource.length : 0);

                if (dataSource && dataSource.length > 1) {
                    console.log('[Gallery] 啟動自動播放');
                } else {
                    console.warn('[Gallery] 只有一張圖或無圖片');
                }
            } else if (attemptCount >= maxAttempts) {
                // 超過最大嘗試次數，停止輪詢並記錄錯誤
                clearInterval(pollGallery);
                console.error('[Gallery] ? 無法獲取實例');
                console.log('[Gallery] #gallery 存在:', $('#gallery').length > 0);
            }
        } catch (e) {
            // 捕獲異常並記錄
            console.error('[Gallery] 錯誤:', e);
            if (attemptCount >= maxAttempts) {
                clearInterval(pollGallery);
            }
        }
    }, intervalMs);
});

// ===========================================
// AJAX 事件處理
// ===========================================

// AJAX 請求開始事件
function onBegin() { }

// AJAX 請求成功事件：處理登入結果
function onSuccess(data) {
    getLoadPanelInstance().hide();  // 隱藏載入面板

    // 根據回傳的顯示類型跳轉到對應頁面
    if (data.DisplayViewType == "MultiGroupView") {
        ShowToast(data.message, "success", 2000);
        window.location.href = "/SmallGroup/MultiGroupView/" + data.ActiveListId;
    } else if (data.DisplayViewType == "IntegrateView") {
        ShowToast(data.message, "success", 2000);
        window.location.href = "/SmallGroup/IntegrateView/" + data.ActiveListId;
    } else if (data.DisplayViewType == "HappyGroupView") {
        ShowToast(data.message, "success", 2000);
        window.location.href = "/Home/HappyGroup";
    } else {
        // 登入失敗，顯示錯誤訊息
        ShowToast(data.message, "error", 4000);
    }
}

// AJAX 請求完成事件
function onComplete(data) { }

// ===========================================
// Toast 通知函數
// ===========================================

// 顯示 Toast 通知
function ShowToast(LocalToastMessage, Type, DisplayTime) {
    var ToastInstance = $("#Toast").dxToast("instance");
    ToastInstance.option({
        message: LocalToastMessage,     // 訊息內容
        type: Type,                     // 訊息類型 (success/error 等)
        displayTime: DisplayTime,       // 顯示時間 (毫秒)
        closeOnClick: true,             // 允許點擊關閉
        position: {                     // 位置設定
            at: 'center center',
            of: '.login-card',
            offset: '0 20'
        }
    });
    ToastInstance.show();  // 顯示 Toast
}
