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
