/*
檔案整體說明

目的與定位：處理回報頁日期選取事件與資料更新接點，確保畫面提交對應目前日期；晚到回應必須沿既有載入協調器判定，不能覆寫新日期狀態。

主要功能與流程：
操作入口包含OnValueChanged、GridDateChanged、notify、getODataLocalDateFilter；呼叫參數與輸出形狀以各入口宣告為準。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
*/
    function OnValueChanged(arg) {

        //debugger;

        //alert("OnValueChanged : " + arg);
        //alert("OnValueChanged : " + arg.value);

        //alert("OnValueChanged : " + arg.attribute('text'));
        //alert("OnValueChanged : " + text: arg.attribute("text"));

        //alert( getODataLocalDateFilter(arg.value) );
    }
    function GridDateChanged() {

        //debugger;

        alert("GridDateChanged !");
    //alert("OnValueChanged : " + arg.value);

    //alert("OnValueChanged : " + arg.attribute('text'));
    //alert("OnValueChanged : " + text: arg.attribute("text"));

    //alert( getODataLocalDateFilter(arg.value) );
    }

        function notify(data) {
            var buttonText = data.component.option("text");
            DevExpress.ui.notify("The " + buttonText + " button was clicked");
        }

        function getODataLocalDateFilter(date) {

            //debugger;
            if (date == null) {
                //date = Date.now();
                date = new Date();
                //return ;
            }

            //alert("getODataLocalDateFilter !" + date);
            //alert("HELLO 我愛嘟嘟妞妞 006!" + date);

            //-- Description: For converting the date object to local time format
            //-- You can also convert this to UTC Date format
            //-- UTC Usage: getUTCMonth(), getUTCFullYear(), getUTCHours() ...

            var monthString;
            var rawMonth = (date.getMonth() + 1).toString();
            if (rawMonth.length == 1) {
        monthString = "0" + rawMonth;
    }
            else {monthString = rawMonth; }

            var dateString;
            //var rawDate = date.getUTCDate().toString();
            var rawDate = date.getDate().toString();
            if (rawDate.length == 1) {
        dateString = "0" + rawDate;
    }
            else {dateString = rawDate; }


            var DateFilter = "";
            //DateFilter += date.getFullYear() + "-";
            //DateFilter += monthString + "-";
            //DateFilter += dateString;
            //DateFilter += " T" + date.getHours() + ":";
            //DateFilter += date.getMinutes() + ":";
            //DateFilter += date.getSeconds() + ":";
            //DateFilter += date.getMilliseconds();

            //return DateFilter;

            return date.getFullYear() + "/" + rawMonth + "/" + rawDate;
        }
