const scanForm = document.getElementById("scanForm");
const scanInput = document.getElementById("scanInput");
const resultCard = document.getElementById("resultCard");
const ticketDetails = document.getElementById("ticketDetails");
const verdictMark = document.getElementById("verdictMark");
const apiStatus = document.getElementById("apiStatus");
const qrReader = document.getElementById("qrReader");
const cameraStatus = document.getElementById("cameraStatus");
const startCameraButton = document.getElementById("startCameraButton");
const stopCameraButton = document.getElementById("stopCameraButton");
const scanNextButton = document.getElementById("scanNextButton");

const currentDate = document.getElementById("currentDate");
const currentTime = document.getElementById("currentTime");

let qrScanner = null;
let isScannerRunning = false;
let isScanLocked = false;

// Mantiene el flujo manual para lectores fisicos o digitacion.
scanForm.addEventListener("submit", async function (event) {
    event.preventDefault();

    const code = extractScanCode(scanInput.value);
    if (code === "") {
        showResult({
            success: false,
            title: "Codigo requerido",
            message: "Escanea el QR o escribe el codigo del ticket.",
            type: "error"
        });
        return;
    }

    await validateTicket(code);
});

document.getElementById("clearButton").addEventListener("click", function () {
    scanInput.value = "";
    scanInput.focus();
});

if (startCameraButton && stopCameraButton && scanNextButton) {
    // La camara se inicia por click para que el navegador permita pedir permisos.
    startCameraButton.addEventListener("click", startCameraScanner);
    stopCameraButton.addEventListener("click", stopCameraScanner);
    scanNextButton.addEventListener("click", resumeCameraScanner);
}

async function validateTicket(code, options = {}) {
    setLoading();

    try {
        // Token antiforgery generado por Razor en el formulario.
        const token = document.querySelector('input[name="__RequestVerificationToken"]').value;

        // El backend valida el ticket y devuelve un resultado listo para pintar.
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
        markApiStatus(true);
        scanInput.value = "";
        if (!options.fromScanner) {
            scanInput.focus();
        }
    } catch {
        markApiStatus(false);
        showResult({
            success: false,
            title: "No se pudo validar el ticket",
            message: "Revisa la conexion e intenta de nuevo.",
            type: "error"
        });
    }
}

async function startCameraScanner() {
    if (isScannerRunning) {
        return;
    }

    // En celulares la camara solo funciona en HTTPS o localhost.
    if (!window.isSecureContext && !["localhost", "127.0.0.1"].includes(window.location.hostname)) {
        setCameraStatus("La camara requiere HTTPS", false);
        return;
    }

    if (typeof Html5Qrcode === "undefined") {
        setCameraStatus("El lector QR no esta disponible", false);
        return;
    }

    try {
        setCameraStatus("Solicitando permiso...", true);
        // Html5Qrcode abre la camara y decodifica QR en el navegador.
        const scannerOptions = typeof Html5QrcodeSupportedFormats === "undefined"
            ? {}
            : { formatsToSupport: [Html5QrcodeSupportedFormats.QR_CODE] };

        qrScanner = qrScanner || new Html5Qrcode(qrReader.id, scannerOptions);

        await qrScanner.start(
            { facingMode: "environment" },
            {
                fps: 10,
                qrbox: getQrBox,
                aspectRatio: 1.333334,
                disableFlip: false
            },
            handleQrScan
        );

        isScannerRunning = true;
        isScanLocked = false;
        startCameraButton.classList.add("hidden");
        stopCameraButton.classList.remove("hidden");
        scanNextButton.classList.add("hidden");
        setCameraStatus("Apunta a un QR", true);
    } catch {
        isScannerRunning = false;
        setCameraStatus("No se pudo iniciar la camara", false);
        startCameraButton.classList.remove("hidden");
        stopCameraButton.classList.add("hidden");
        scanNextButton.classList.add("hidden");
    }
}

async function stopCameraScanner() {
    if (!qrScanner || !isScannerRunning) {
        return;
    }

    try {
        await qrScanner.stop();
        qrScanner.clear();
    } catch {
        // El navegador puede haber detenido la camara previamente.
    }

    isScannerRunning = false;
    isScanLocked = false;
    qrReader.innerHTML = "";
    startCameraButton.classList.remove("hidden");
    stopCameraButton.classList.add("hidden");
    scanNextButton.classList.add("hidden");
    setCameraStatus("Camara apagada", true);
    scanInput.focus();
}

function resumeCameraScanner() {
    if (!qrScanner || !isScannerRunning) {
        return;
    }

    try {
        qrScanner.resume();
        isScanLocked = false;
        scanNextButton.classList.add("hidden");
        setCameraStatus("Apunta a un QR", true);
    } catch {
        setCameraStatus("Reinicia la camara", false);
        startCameraButton.classList.remove("hidden");
        scanNextButton.classList.add("hidden");
    }
}

async function handleQrScan(decodedText) {
    const code = extractScanCode(decodedText);

    if (isScanLocked || code === "") {
        return;
    }

    isScanLocked = true;
    scanInput.value = code;
    setCameraStatus("QR leido. Validando...", true);

    if (navigator.vibrate) {
        navigator.vibrate(80);
    }

    try {
        // Se pausa para evitar validar varias veces el mismo QR.
        qrScanner.pause(true);
    } catch {
        // Algunos navegadores no permiten pausar con el estado actual de la camara.
    }

    // Reutiliza la misma validacion que usa el formulario manual.
    await validateTicket(code, { fromScanner: true });

    if (isScannerRunning) {
        scanNextButton.classList.remove("hidden");
        setCameraStatus("Validado. Listo para el siguiente escaneo.", true);
    }
}

function extractScanCode(value) {
    const text = String(value || "").trim();
    if (text === "") {
        return "";
    }

    const jsonCode = extractCodeFromJson(text);
    if (jsonCode) {
        return jsonCode;
    }

    const urlCode = extractCodeFromUrl(text);
    if (urlCode) {
        return urlCode;
    }

    return text;
}

function extractCodeFromJson(text) {
    if (!text.startsWith("{") && !text.startsWith("[")) {
        return "";
    }

    try {
        return findCodeInObject(JSON.parse(text));
    } catch {
        return "";
    }
}

function findCodeInObject(value) {
    if (!value || typeof value !== "object") {
        return "";
    }

    const codeKeys = ["qrCode", "ticketCode", "code", "ticketId", "id", "token"];
    for (const key of codeKeys) {
        if (typeof value[key] === "string" || typeof value[key] === "number") {
            const code = String(value[key]).trim();
            if (code) {
                return code;
            }
        }
    }

    for (const nested of Object.values(value)) {
        const code = findCodeInObject(nested);
        if (code) {
            return code;
        }
    }

    return "";
}

function extractCodeFromUrl(text) {
    try {
        const url = new URL(text);
        const params = ["qrCode", "ticketCode", "code", "ticketId", "id", "token"];
        for (const param of params) {
            const value = url.searchParams.get(param);
            if (value) {
                return value.trim();
            }
        }

        const lastPathPart = url.pathname.split("/").filter(Boolean).pop();
        return lastPathPart ? decodeURIComponent(lastPathPart).trim() : "";
    } catch {
        return "";
    }
}

function getQrBox(viewfinderWidth, viewfinderHeight) {
    // Mantiene el cuadro de lectura proporcional en desktop y celular.
    const minEdge = Math.min(viewfinderWidth, viewfinderHeight);
    const size = Math.floor(Math.min(minEdge * 0.72, 280));
    return {
        width: size,
        height: size
    };
}

function setCameraStatus(message, isOk) {
    cameraStatus.textContent = message;
    cameraStatus.className = `camera-status ${isOk ? "online" : "offline"}`;
}

function setLoading() {
    resultCard.className = "panel result-panel loading";
    verdictMark.className = "verdict-mark loading";
    verdictMark.innerHTML = '<i class="fas fa-spinner fa-spin"></i>';
    document.getElementById("resultTitle").textContent = "Validando...";
    document.getElementById("resultMessage").textContent = "Espera un momento.";
    ticketDetails.classList.add("hidden");
}

function showResult(data) {
    // Cambia el estado visual segun la respuesta del servidor.
    const resultType = data.success ? "success" : (data.type || "error");
    resultCard.className = `panel result-panel ${resultType}`;
    verdictMark.className = `verdict-mark ${data.success ? "success" : "error"}`;
    verdictMark.innerHTML = data.success ? '<i class="fas fa-check"></i>' : '<i class="fas fa-xmark"></i>';
    document.getElementById("resultTitle").textContent = toUserText(data.title || "Resultado");
    document.getElementById("resultMessage").textContent = toUserText(data.message || "");

    if (!data.ticket) {
        ticketDetails.classList.add("hidden");
        return;
    }

    const ticket = data.ticket;
    const contact = [ticket.email, ticket.phoneNumber].filter(Boolean).join(" - ");
    const seat = [ticket.row, ticket.seatNumber].filter(Boolean).join("-");
    const typeStatus = [ticket.ticketType || ticket.entryMode, ticket.status].filter(Boolean).join(" / ");

    document.getElementById("clientName").textContent = cleanDisplayValue(ticket.clientName, "Sin nombre");
    document.getElementById("clientContact").textContent = contact || "Sin contacto";
    document.getElementById("eventName").textContent = ticket.eventName || "--";
    document.getElementById("venueName").textContent = ticket.venueName || "--";
    document.getElementById("showtimeStart").textContent = formatDateTime(ticket.showtimeStart);
    document.getElementById("seatNumber").textContent = seat || "Sin asiento";
    document.getElementById("ticketType").textContent = typeStatus || "--";
    document.getElementById("ticketCode").textContent = formatTicketCode(ticket.code);
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

function cleanDisplayValue(value, fallback) {
    const text = String(value || "").trim();
    if (!text || looksLikePayload(text)) {
        return fallback;
    }

    return text;
}

function formatTicketCode(value) {
    const code = String(value || "").trim();
    if (!code) {
        return "--";
    }

    if (!looksLikePayload(code) && code.length <= 36) {
        return code;
    }

    return `${code.slice(0, 10)}...${code.slice(-6)}`;
}

function looksLikePayload(value) {
    return value.length > 80
        || value.startsWith("{")
        || value.startsWith("[")
        || /^https?:\/\//i.test(value)
        || /^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$/.test(value);
}

function markApiStatus(isOnline) {
    apiStatus.className = `status ${isOnline ? "online" : "offline"}`;
    apiStatus.textContent = isOnline ? "Listo para validar" : "Sin conexion";
}

function toUserText(value) {
    return String(value || "")
        .replace(/\bapi\b/gi, "sistema")
        .replace(/\bjson\b/gi, "respuesta")
        .replace(/\btoken\b/gi, "sesion")
        .replace(/\bEntry allowed\b/gi, "Ingreso permitido")
        .replace(/\bEntry rejected\b/gi, "Ingreso rechazado")
        .replace(/\bValid ticket\. Access granted\./gi, "Ticket valido. Acceso permitido.")
        .replace(/\bEntry was not authorized\./gi, "El ingreso no fue autorizado.")
        .replace(/\bAlready used\b/gi, "Ya usado")
        .replace(/\bUsed\b/gi, "Usado");
}

function formatDateTime(value) {
    if (!value) {
        return "--";
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
        return "--";
    }

    return date.toLocaleString("es-CO", {
        day: "2-digit",
        month: "short",
        hour: "2-digit",
        minute: "2-digit"
    });
}

function updateClock() {
    const now = new Date();

    currentDate.textContent = now.toLocaleDateString("es-CO", {
        weekday: "long",
        day: "2-digit",
        month: "short"
    });

    currentTime.textContent = now.toLocaleTimeString("es-CO", {
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit"
    });
}

updateClock();
setInterval(updateClock, 1000);
