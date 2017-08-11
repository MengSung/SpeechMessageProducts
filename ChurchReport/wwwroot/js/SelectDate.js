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

            debugger;

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
