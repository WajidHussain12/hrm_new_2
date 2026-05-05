document.addEventListener("DOMContentLoaded", function () {
    if (typeof window.extrasChartData !== 'undefined' && typeof window.deductionChartData !== 'undefined') {
        var ExtrasData = window.extrasChartData;
        var DeductionsData = window.deductionChartData;

        if (ExtrasData && ExtrasData.length > 0) {
            var extrasDataPoints = ExtrasData.map(function(item) {
                return { label: item.MM, y: item.Extras };
            });

            var options = {
                animationEnabled: true,
                title: {
                    text: "Extras"
                },
                axisY: {
                    title: "Amount",
                },
                data: [{
                    yValueFormatString: "#,###",
                    type: "spline",
                    dataPoints: extrasDataPoints
                }]
            };
            
            // Check if plugin exists (since old code used jquery wrapper for CanvasJS)
            if ($.fn.CanvasJSChart) {
                $("#chartContainerExtras").CanvasJSChart(options);
            } else {
                // Vanilla CanvasJS fallback
                var chart1 = new CanvasJS.Chart("chartContainerExtras", options);
                chart1.render();
            }
        }

        if (DeductionsData && DeductionsData.length > 0) {
            var deductionDataPoints = DeductionsData.map(function(item) {
                return { label: item.MM, y: item.Deduction };
            });

            var chart = new CanvasJS.Chart("chartContainerDeduction", {
                animationEnabled: true,
                theme: "light2", // "light1", "light2", "dark1", "dark2"
                title: {
                    text: "Deductions"
                },
                axisY: {
                    title: "Amount"
                },
                data: [{
                    type: "column",
                    yValueFormatString: "#,###",
                    showInLegend: true,
                    legendMarkerColor: "grey",
                    legendText: "Month Wise",
                    dataPoints: deductionDataPoints
                }]
            });
            chart.render();
        }
    }
});
