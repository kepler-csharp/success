const scanForm = document.getElementById("scanForm");
const scanInput = document.getElementById("scanInput");
const resultCard = document.getElementById("resultCard");
const ticketDetails = document.getElementById("ticketDetails");
const historyList = document.getElementById("historyList");
const verdictMark = document.getElementById("verdictMark");
const apiStatus = document.getElementById("apiStatus");

const currentDate = document.getElementById("currentDate");
const currentTime = document.getElementById("currentTime");
const todayCount = document.getElementById("todayCount");
const approvedCount = document.getElementById("approvedCount");
const rejectedCount = document.getElementById("rejectedCount");

let localStats = {
    today: 0,
    approved: 0,
    rejected: 0
};

scanForm.addEventListener("submit", async function (event) {
    event.preventDefault();

    const code = scanInput.value.trim();
    if (code === "") {
        showResult({
            success: false,
            title: "Code needed",
            message: "Scan a QR or type the ticket code.",
            type: "error"
        });
        return;
    }

    await validateTicket(code);
});

document.querySelectorAll(".demo-button").forEach(function (button) {
    button.addEventListener("click", async function () {
        scanInput.value = button.dataset.code;
        await validateTicket(button.dataset.code);
    });
});

document.getElementById("clearButton").addEventListener("click", function () {
    scanInput.value = "";
    scanInput.focus();
});

async function validateTicket(code) {
    setLoading();

    try {
        const token = document.querySelector('input[name="__RequestVerificationToken"]').value;

        const response = await fetch(window.ticketUrls.validate, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "RequestVerificationToken": token
            },
            body: JSON.stringify({ scanCode: code })
        });

        const data = await response.json();
        showResult(data);
        addHistory(data, code);
        updateLocalStats(data.success);
        markApiStatus(true);
        scanInput.value = "";
        scanInput.focus();
    } catch {
        markApiStatus(false);
        showResult({
            success: false,
            title: "Connection error",
            message: "The ticket could not be checked. Check the API connection.",
            type: "error"
        });
    }
}

function setLoading() {
    resultCard.className = "panel result-panel loading";
    verdictMark.className = "verdict-mark loading";
    verdictMark.innerHTML = '<i class="fas fa-spinner fa-spin"></i>';
    document.getElementById("resultTitle").textContent = "Checking...";
    document.getElementById("resultMessage").textContent = "Checking the main API.";
    ticketDetails.classList.add("hidden");
}

function showResult(data) {
    const resultType = data.success ? "success" : (data.type || "error");
    resultCard.className = `panel result-panel ${resultType}`;
    verdictMark.className = `verdict-mark ${data.success ? "success" : "error"}`;
    verdictMark.innerHTML = data.success ? '<i class="fas fa-check"></i>' : '<i class="fas fa-xmark"></i>';
    document.getElementById("resultTitle").textContent = data.title || "Result";
    document.getElementById("resultMessage").textContent = data.message || "";

    if (!data.ticket) {
        ticketDetails.classList.add("hidden");
        return;
    }

    const ticket = data.ticket;
    const contact = [ticket.email, ticket.phoneNumber].filter(Boolean).join(" - ");
    const seat = [ticket.row, ticket.seatNumber].filter(Boolean).join("-");
    const typeStatus = [ticket.ticketType || ticket.entryMode, ticket.status].filter(Boolean).join(" / ");

    document.getElementById("clientName").textContent = ticket.clientName || "No name";
    document.getElementById("clientContact").textContent = contact || "No contact";
    document.getElementById("eventName").textContent = ticket.eventName || "--";
    document.getElementById("venueName").textContent = ticket.venueName || "--";
    document.getElementById("showtimeStart").textContent = formatDateTime(ticket.showtimeStart);
    document.getElementById("seatNumber").textContent = seat || "No seat";
    document.getElementById("ticketType").textContent = typeStatus || "--";
    document.getElementById("ticketCode").textContent = ticket.code || "--";
    document.getElementById("scanTime").textContent = formatDateTime(ticket.scanTime);

    const photo = document.getElementById("clientPhoto");
    if (ticket.photoUrl) {
        photo.src = ticket.photoUrl;
        photo.classList.add("has-photo");
    } else {
        photo.removeAttribute("src");
        photo.classList.remove("has-photo");
    }

    ticketDetails.classList.remove("hidden");
}

function addHistory(data, scannedCode) {
    const item = document.createElement("div");
    item.className = `history-item ${data.success ? "success" : "danger"}`;

    const name = data.ticket ? data.ticket.clientName : scannedCode;
    const message = data.title || "Check";
    const time = new Date().toLocaleTimeString("en-US", {
        hour: "2-digit",
        minute: "2-digit"
    });

    item.innerHTML = `
        <div>
            <strong>${escapeHtml(name)}</strong>
            <span>${escapeHtml(message)}</span>
        </div>
        <time>${time}</time>
    `;

    const emptyText = historyList.querySelector(".empty-text");
    if (emptyText) {
        emptyText.remove();
    }

    historyList.prepend(item);
}

function updateLocalStats(success) {
    localStats.today++;

    if (success) {
        localStats.approved++;
    } else {
        localStats.rejected++;
    }

    paintStats(localStats);
}

function paintStats(stats) {
    todayCount.textContent = stats.today;
    approvedCount.textContent = stats.approved;
    rejectedCount.textContent = stats.rejected;
}

function markApiStatus(isOnline) {
    apiStatus.className = `status ${isOnline ? "online" : "offline"}`;
    apiStatus.textContent = isOnline ? "API connected" : "API offline";
}

function formatDateTime(value) {
    if (!value) {
        return "--";
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
        return "--";
    }

    return date.toLocaleString("en-US", {
        day: "2-digit",
        month: "short",
        hour: "2-digit",
        minute: "2-digit"
    });
}

function escapeHtml(value) {
    return String(value || "")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#039;");
}

function updateClock() {
    const now = new Date();

    currentDate.textContent = now.toLocaleDateString("en-US", {
        weekday: "long",
        day: "2-digit",
        month: "short"
    });

    currentTime.textContent = now.toLocaleTimeString("en-US", {
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit"
    });
}

updateClock();
paintStats(localStats);
setInterval(updateClock, 1000);
