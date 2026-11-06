/*
檔案整體說明

目的與定位：提供舊頁面共用AJAX互動與結果處理入口，協調資料送出、載入提示及畫面更新；前端參數仍由伺服器驗證，不能把回呼當成跨頁資料owner。

主要功能與流程：
操作入口包含OnValueChanged、Save、getODataLocalDateFilter；呼叫參數與輸出形狀以各入口宣告為準。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
*/

    function OnValueChanged(arg) {
        //alert("日期更動了!");

        $.ajax({
            url: '@Url.Action("UpdateDate", "SmallGroupReport")',
            data: { SelectedDate: getODataLocalDateFilter(arg.value) },
            type: 'POST', //POST if you want to save, GET if you want to fetch data from server
            success: function (obj) {
                // here comes your response after calling the server
                //alert('Suceeded');
            },
            error: function (obj) {
                //alert('Something happened');
            }
        });
    }

        function Save(arg) {
            //alert("上傳了!");

            //debugger;

            //alert('上傳了! 001');
            var dataGrid = $('#gridContainer');
            ////var dataGrid = $('#data-grid-demo');
            //alert('上傳了! 002 = ' + dataGrid);
            //var dataGridInstance = $('#gridContainer').dxDataGrid('instance');
            //alert('上傳了! 003 = ' + dataGridInstance);

            //$("#data-grid-demo").dxDataGrid("instance").refresh();

            $.ajax({
             url: '@Url.Action("Save", "SmallGroupReport")',
                //url: '@Url.Action("Put", "SmallGroupReport")',
                data: {key: "500" },
                //type: 'PUT', //POST if you want to save, GET if you want to fetch data from server
                type: 'GET', //POST if you want to save, GET if you want to fetch data from server
                success: function (obj) {


        //$('#gridContainer').dxDataGrid({
        //    dataSource: Model,
        //});

        alert('Suceeded 001');
    //$("#gridContainer").dxDataGrid("instance").refresh();
    //$("#gridContainer").dxDataGrid("instance").refresh();
    alert('Suceeded 002');
                    //var dataGrid = $('#gridContainer').dxDataGrid('instance');
                    var dataGridInstance = $('#gridContainer').instance;

                    //$("#gridContainer").dxDataGrid("instance").refresh();
                    //var dataGrid = $('#gridContainer').dataGrid;
                    //alert('Suceeded 003');
                    dataGridInstance.refresh();
                    alert('Suceeded 004');
                    //dataGrid.repaint();
                    //alert('Suceeded 005');
                    // here comes your response after calling the server
                    //
                },
                error: function (obj) {
        //alert('Something happened');
    }
    });
        }

        //function vectorMap_tooltip_customizeTooltip(arg) {
        //    return { text: arg.attribute("text") };
        //}

        function getODataLocalDateFilter(date) {
            //-- Description: For converting the date object to local time format
            //-- You can also convert this to UTC Date format
            //-- UTC Usage: getUTCMonth(), getUTCFullYear(), getUTCHours() ...

            //debugger;

            alert("天下一統");

            var monthString;
            var rawMonth = (date.getMonth() + 1).toString();
            if (rawMonth.length == 1) {
                monthString = "0" + rawMonth;
            }
            else { monthString = rawMonth; }

            var dateString;
            //var rawDate = date.getUTCDate().toString();
            var rawDate = date.getDate().toString();
            if (rawDate.length == 1) {
                dateString = "0" + rawDate;
            }
            else { dateString = rawDate; }


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
