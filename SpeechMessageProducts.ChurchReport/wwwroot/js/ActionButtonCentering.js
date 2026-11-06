/*
檔案整體說明

目的與定位：集中修正DevExtreme動作按鈕內容的置中顯示，讓圖示與文字在各種元件狀態保持一致；只處理DOM呈現，不更改提交或授權決策。

主要功能與流程：
操作入口包含getButtonHeight；呼叫參數與輸出形狀以各入口宣告為準。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
*/
(function (window, $) {
    "use strict";

    function getButtonHeight($button) {
        if (!$button || !$button.length) {
            return 48;
        }

        var h = Math.round($button.outerHeight());
        if (!h || h < 36) {
            h = 48;
        }
        return h;
    }

    window.centerActionButtonText = function (buttonId) {
        var $button = $("#" + buttonId);
        if (!$button.length) {
            return;
        }

        var buttonHeight = getButtonHeight($button);
        var $content = $button.find(".dx-button-content");

        // 只設定 content 的 flex 置中，不碰 dx-button-text（由 CSS 控制）
        $content.css({
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            height: buttonHeight + "px",
            minHeight: buttonHeight + "px",
            boxSizing: "border-box",
            paddingTop: "0",
            paddingBottom: "0"
        });
    };

    window.scheduleCenterActionButtons = function (ids) {
        var targets = Array.isArray(ids) && ids.length ? ids : ["save-button2", "save-button3"];
        targets.forEach(function (id) {
            window.centerActionButtonText(id);
        });
    };

    window.onActionButtonInitialized = function (e) {
        var id = $(e.element).attr("id");
        if (!id) {
            return;
        }

        window.centerActionButtonText(id);

        if (window.requestAnimationFrame) {
            window.requestAnimationFrame(function () {
                window.centerActionButtonText(id);
            });
        }

        setTimeout(function () {
            window.centerActionButtonText(id);
        }, 50);

        setTimeout(function () {
            window.centerActionButtonText(id);
        }, 220);
    };
}(window, jQuery));
