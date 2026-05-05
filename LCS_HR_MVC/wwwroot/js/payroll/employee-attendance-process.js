document.addEventListener("DOMContentLoaded", () => {
    const form = document.getElementById("attendanceProcessForm");
    const city = document.getElementById("ddlCity");

    if (!form || !city) {
        return;
    }

    form.addEventListener("submit", (event) => {
        const submitter = event.submitter;
        if (!submitter || submitter.value !== "ProcessAttendance") {
            return;
        }

        if (!city.value || city.value === "00") {
            event.preventDefault();
            city.style.border = "1px solid red";
            city.focus();
        }
    });
});
