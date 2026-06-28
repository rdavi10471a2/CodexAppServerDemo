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
