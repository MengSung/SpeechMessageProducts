
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

            debugger;

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


