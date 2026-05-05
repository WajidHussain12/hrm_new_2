document.addEventListener("DOMContentLoaded", () => {
    const form = document.getElementById("salaryProcessForm");
    const zone = document.getElementById("ddlZone");
    const city = document.getElementById("ddlCity");
    const division = document.getElementById("ddlBU");
    const departmentsPanel = document.getElementById("pnlDepartments");
    const checkAll = document.getElementById("chkHeaderAll");

    if (!form || !zone || !city || !division || !departmentsPanel || !checkAll) {
        return;
    }

    const citiesUrl = form.dataset.citiesUrl;
    const subDepartmentsUrl = form.dataset.subdepartmentsUrl;
    const currentCity = city.value;
    const selectedDepartments = new Set(
        (departmentsPanel.dataset.selectedSubdepartments || "")
            .split(",")
            .map((value) => value.trim())
            .filter((value) => value.length > 0)
    );

    const syncHeaderCheckbox = () => {
        const boxes = departmentsPanel.querySelectorAll(".chkdept");
        const checked = departmentsPanel.querySelectorAll(".chkdept:checked");
        checkAll.checked = boxes.length > 0 && boxes.length === checked.length;
    };

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

    const populateSubDepartments = async () => {
        departmentsPanel.innerHTML = "";
        checkAll.checked = false;

        if (!division.value || division.value === "0" || !subDepartmentsUrl) {
            return;
        }

        const response = await fetch(`${subDepartmentsUrl}?buId=${encodeURIComponent(division.value)}`, {
            headers: { "X-Requested-With": "XMLHttpRequest" }
        });

        if (!response.ok) {
            return;
        }

        const rows = await response.json();
        rows.forEach((item) => {
            const wrapper = document.createElement("label");
            const checkbox = document.createElement("input");
            checkbox.type = "checkbox";
            checkbox.name = "SelectedSubDepartments";
            checkbox.value = item.value;
            checkbox.className = "chkdept";
            checkbox.checked = selectedDepartments.has(item.value);
            wrapper.appendChild(checkbox);
            wrapper.append(` ${item.text}`);
            departmentsPanel.appendChild(wrapper);
            departmentsPanel.appendChild(document.createElement("br"));
        });

        syncHeaderCheckbox();
    };

    zone.addEventListener("change", () => populateCities());
    division.addEventListener("change", populateSubDepartments);

    checkAll.addEventListener("change", () => {
        departmentsPanel.querySelectorAll(".chkdept").forEach((checkbox) => {
            checkbox.checked = checkAll.checked;
        });
    });

    departmentsPanel.addEventListener("change", (event) => {
        const target = event.target;
        if (target instanceof HTMLInputElement && target.classList.contains("chkdept")) {
            syncHeaderCheckbox();
        }
    });

    if (zone.value && zone.value !== "00" && currentCity && currentCity !== "00") {
        populateCities(currentCity);
    }

    if (division.value && division.value !== "0") {
        populateSubDepartments();
    }

    form.addEventListener("submit", (event) => {
        const submitter = event.submitter;
        if (!submitter || submitter.value !== "ProcessSalaries") {
            return;
        }

        const billing = document.getElementById("chkBillingStatus");
        const attendance = document.getElementById("chkAttendanceStatus");
        const commission = document.getElementById("chkCommissionStatus");
        const oneTime = document.getElementById("chkOneTimeActivity");

        if (!billing?.checked || !attendance?.checked || !commission?.checked || !oneTime?.checked) {
            event.preventDefault();
            alert("Please acknowledge all checkbox confirmations before processing salaries.");
            return;
        }

        if (!city.value || city.value === "00") {
            event.preventDefault();
            alert("Please Select City!");
            return;
        }

        if (!division.value || division.value === "0") {
            event.preventDefault();
            alert("Please Select Division!");
            return;
        }

        if (departmentsPanel.querySelectorAll(".chkdept:checked").length === 0) {
            event.preventDefault();
            alert("Please select at least one sub department.");
            return;
        }

        if (!window.confirm("Are you sure you want to execute the Salary Process? This action cannot be easily undone.")) {
            event.preventDefault();
        }
    });
});
