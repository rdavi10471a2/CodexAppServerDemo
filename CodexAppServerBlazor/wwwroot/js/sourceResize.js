export function attachSourceSplitter(layout, sidebar, detail, splitter) {
    if (!layout || !sidebar || !detail || !splitter) {
        return;
    }

    let startX = 0;
    let startSidebarWidth = 0;
    let startDetailWidth = 0;

    const minSidebar = 260;
    const minDetail = 460;

    function onPointerMove(event) {
        const delta = event.clientX - startX;
        const nextSidebar = Math.max(minSidebar, startSidebarWidth + delta);
        const nextDetail = Math.max(minDetail, startDetailWidth - delta);
        sidebar.style.width = `${nextSidebar}px`;
        detail.style.width = `${nextDetail}px`;
    }

    function onPointerUp() {
        splitter.classList.remove("dragging");
        document.body.style.cursor = "";
        document.body.style.userSelect = "";
        window.removeEventListener("pointermove", onPointerMove);
        window.removeEventListener("pointerup", onPointerUp);
    }

    splitter.addEventListener("pointerdown", event => {
        event.preventDefault();
        startX = event.clientX;
        startSidebarWidth = sidebar.getBoundingClientRect().width;
        startDetailWidth = detail.getBoundingClientRect().width;
        splitter.classList.add("dragging");
        document.body.style.cursor = "col-resize";
        document.body.style.userSelect = "none";
        window.addEventListener("pointermove", onPointerMove);
        window.addEventListener("pointerup", onPointerUp);
    });
}

export function attachMainSplitter(layout, connection, workPanel, splitter) {
    if (!layout || !connection || !workPanel || !splitter || splitter.dataset.resizeAttached === "true") {
        return;
    }

    splitter.dataset.resizeAttached = "true";

    let startX = 0;
    let startConnectionWidth = 0;
    const minConnection = 260;
    const maxConnection = 620;

    function onPointerMove(event) {
        const delta = event.clientX - startX;
        const nextConnection = Math.min(maxConnection, Math.max(minConnection, startConnectionWidth + delta));
        connection.style.flexBasis = `${nextConnection}px`;
        connection.style.width = `${nextConnection}px`;
    }

    function onPointerUp() {
        splitter.classList.remove("dragging");
        document.body.style.cursor = "";
        document.body.style.userSelect = "";
        window.removeEventListener("pointermove", onPointerMove);
        window.removeEventListener("pointerup", onPointerUp);
    }

    splitter.addEventListener("pointerdown", event => {
        event.preventDefault();
        startX = event.clientX;
        startConnectionWidth = connection.getBoundingClientRect().width;
        splitter.classList.add("dragging");
        document.body.style.cursor = "col-resize";
        document.body.style.userSelect = "none";
        window.addEventListener("pointermove", onPointerMove);
        window.addEventListener("pointerup", onPointerUp);
    });
}

export function attachAssistantSplitter(layout, transcript, composer, splitter) {
    if (!layout || !transcript || !composer || !splitter || splitter.dataset.resizeAttached === "true") {
        return;
    }

    splitter.dataset.resizeAttached = "true";

    let startY = 0;
    let startTranscriptHeight = 0;
    let startComposerHeight = 0;

    const minTranscript = 160;
    const minComposer = 120;

    function onPointerMove(event) {
        const delta = event.clientY - startY;
        const nextTranscript = Math.max(minTranscript, startTranscriptHeight + delta);
        const nextComposer = Math.max(minComposer, startComposerHeight - delta);
        transcript.style.flexBasis = `${nextTranscript}px`;
        transcript.style.height = `${nextTranscript}px`;
        composer.style.flexBasis = `${nextComposer}px`;
        composer.style.height = `${nextComposer}px`;
    }

    function onPointerUp() {
        splitter.classList.remove("dragging");
        document.body.style.cursor = "";
        document.body.style.userSelect = "";
        window.removeEventListener("pointermove", onPointerMove);
        window.removeEventListener("pointerup", onPointerUp);
    }

    splitter.addEventListener("pointerdown", event => {
        event.preventDefault();
        startY = event.clientY;
        startTranscriptHeight = transcript.getBoundingClientRect().height;
        startComposerHeight = composer.getBoundingClientRect().height;
        splitter.classList.add("dragging");
        document.body.style.cursor = "row-resize";
        document.body.style.userSelect = "none";
        window.addEventListener("pointermove", onPointerMove);
        window.addEventListener("pointerup", onPointerUp);
    });
}

export function attachStreamSplitter(layout, history, stream, splitter) {
    if (!layout || !history || !stream || !splitter || splitter.dataset.resizeAttached === "true") {
        return;
    }

    splitter.dataset.resizeAttached = "true";

    let startX = 0;
    let startHistoryWidth = 0;
    let startStreamWidth = 0;

    const minHistory = 420;
    const minStream = 260;

    function onPointerMove(event) {
        const delta = event.clientX - startX;
        const nextHistory = Math.max(minHistory, startHistoryWidth + delta);
        const nextStream = Math.max(minStream, startStreamWidth - delta);
        layout.style.gridTemplateColumns = `${nextHistory}px 12px ${nextStream}px`;
    }

    function onPointerUp() {
        splitter.classList.remove("dragging");
        document.body.style.cursor = "";
        document.body.style.userSelect = "";
        window.removeEventListener("pointermove", onPointerMove);
        window.removeEventListener("pointerup", onPointerUp);
    }

    splitter.addEventListener("pointerdown", event => {
        event.preventDefault();
        startX = event.clientX;
        startHistoryWidth = history.getBoundingClientRect().width;
        startStreamWidth = stream.getBoundingClientRect().width;
        splitter.classList.add("dragging");
        document.body.style.cursor = "col-resize";
        document.body.style.userSelect = "none";
        window.addEventListener("pointermove", onPointerMove);
        window.addEventListener("pointerup", onPointerUp);
    });
}

export function attachComposerAutoScroll(textarea) {
    if (!textarea || textarea.dataset.autoScrollAttached === "true") {
        return;
    }

    textarea.dataset.autoScrollAttached = "true";
    textarea.addEventListener("input", () => {
        textarea.scrollTop = textarea.scrollHeight;
    });
}

export function scrollComposerToBottom(textarea) {
    if (!textarea) {
        return;
    }

    window.requestAnimationFrame(() => {
        textarea.scrollTop = textarea.scrollHeight;
    });
}

export function scrollElementToBottom(element) {
    if (!element) {
        return;
    }

    window.requestAnimationFrame(() => {
        element.scrollTop = element.scrollHeight;
    });
}

export async function copyTextToClipboard(text) {
    if (!text) {
        return;
    }

    if (navigator.clipboard && window.isSecureContext) {
        await navigator.clipboard.writeText(text);
        return;
    }

    const textarea = document.createElement("textarea");
    textarea.value = text;
    textarea.setAttribute("readonly", "");
    textarea.style.position = "fixed";
    textarea.style.left = "-9999px";
    textarea.style.top = "0";
    document.body.appendChild(textarea);
    textarea.select();
    document.execCommand("copy");
    document.body.removeChild(textarea);
}

export function openHtmlDocument(html, title) {
    const popup = window.open("", "_blank");
    if (!popup) {
        return;
    }

    popup.opener = null;
    popup.document.open();
    popup.document.write(html || "");
    popup.document.title = title || popup.document.title;
    popup.document.close();
}

export function setBeforeUnloadGuard(enabled, message) {
    if (enabled) {
        window.__codingServicesBeforeUnloadMessage = message || "Refreshing will reset the current Coding Services session.";
        if (!window.__codingServicesBeforeUnloadHandler) {
            window.__codingServicesBeforeUnloadHandler = event => {
                event.preventDefault();
                event.returnValue = window.__codingServicesBeforeUnloadMessage;
                return window.__codingServicesBeforeUnloadMessage;
            };
            window.addEventListener("beforeunload", window.__codingServicesBeforeUnloadHandler);
        }

        return;
    }

    if (window.__codingServicesBeforeUnloadHandler) {
        window.removeEventListener("beforeunload", window.__codingServicesBeforeUnloadHandler);
        window.__codingServicesBeforeUnloadHandler = null;
    }

    window.__codingServicesBeforeUnloadMessage = "";
}
