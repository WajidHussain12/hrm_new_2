document.addEventListener("DOMContentLoaded", () => {
    const form = document.getElementById("commissionProcessForm");
    const zone = document.getElementById("ddlZone");
    const city = document.getElementById("ddlCity");

    if (!form || !zone || !city) {
        return;
    }

    const citiesUrl = form.dataset.citiesUrl;
    const currentCity = city.value;

    const populateCities = async (selectedCity = "") => {
        city.innerHTML = '<option value="00">Please Select</option>';
        if (!zone.value || zone.value === "00" || !citiesUrl) {
            return;
        }

        const response = await fetch(`${citiesUrl}?zoneId=${encodeURIComponent(zone.value)}`, {
            headers: { "X-Requested-With": "XMLHttpRequest" }
        });

        if (!response.ok) {
            return;
        }

        const rows = await response.json();
        rows.forEach((item) => {
            const option = document.createElement("option");
            option.value = item.value;
            option.textContent = item.text;
            if (item.value === selectedCity) {
                option.selected = true;
            }
            city.appendChild(option);
        });
    };

    zone.addEventListener("change", () => populateCities());

    if (zone.value && zone.value !== "00" && currentCity && currentCity !== "00") {
        populateCities(currentCity);
    }

    form.addEventListener("submit", (event) => {
        const submitter = event.submitter;
        if (!submitter || submitter.value !== "ProcessCommission") {
            return;
        }

        const billing = document.getElementById("chkBillingStatus");
        const attendance = document.getElementById("chkAttendanceStatus");
        const allCommission = document.getElementById("chkAllCommissionTypes");

        if (!billing?.checked || !attendance?.checked || !allCommission?.checked) {
            event.preventDefault();
            alert("Please acknowledge all checkbox confirmations before processing commission.");
            return;
        }

        if (!city.value || city.value === "00") {
            event.preventDefault();
            alert("Please Select City!");
            return;
        }

        if (!window.confirm("Are you sure you want to execute the Commission Process?")) {
            event.preventDefault();
        }
    });
});
