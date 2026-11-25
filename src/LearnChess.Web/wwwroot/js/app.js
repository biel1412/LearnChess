"use strict";

// ================== Constantes ==================
const START_FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
const GLYPHS = { k: "♚", q: "♛", r: "♜", b: "♝", n: "♞", p: "♟" };
const SYMBOLS = [
    { s: "!!", t: "Lance brilhante" },
    { s: "!", t: "Bom lance" },
    { s: "!?", t: "Lance interessante" },
    { s: "?!", t: "Lance duvidoso" },
    { s: "?", t: "Erro" },
    { s: "??", t: "Erro grave" },
];
const LEVEL_BARS = { Basico: "●○○○", Medio: "●●○○", Avancado: "●●●○", Profissional: "●●●●" };
const STATUS_PT = {
    EmAndamento: "Em andamento", XequeMate: "Xeque-mate", Afogamento: "Afogamento",
    Empate: "Empate", Desistencia: "Desistência",
};

// ================== Estado ==================
let game = null;        // GameState vindo da API
let viewPly = null;     // null = posição atual; número = revendo após o lance N (0 = início)
let flipped = false;
let selected = null;    // casa selecionada (ex.: "e2")
let busy = false;       // aguardando servidor / máquina pensando
let dragFrom = null;

const $ = (id) => document.getElementById(id);

// ================== API ==================
async function api(method, url, body) {
    const res = await fetch(url, {
        method,
        headers: body ? { "Content-Type": "application/json" } : {},
        body: body ? JSON.stringify(body) : undefined,
    });
    if (!res.ok) {
        let msg = `Erro ${res.status}`;
        try { const p = await res.json(); msg = p.detail || p.title || msg; } catch { /* sem corpo */ }
        throw new Error(msg);
    }
    return res.status === 204 ? null : res.json();
}

function toast(msg, isError = false) {
    const el = $("toast");
    el.textContent = msg;
    el.classList.toggle("error", isError);
    el.classList.remove("hidden");
    clearTimeout(toast._t);
    toast._t = setTimeout(() => el.classList.add("hidden"), 3200);
}

function remember(id) {
    try { localStorage.setItem("learnchess:lastGame", id); } catch { /* armazenamento indisponível */ }
}
function recall() {
    try { return localStorage.getItem("learnchess:lastGame"); } catch { return null; }
}

// ================== Fluxo da partida ==================
async function setGame(state, { keepView = true } = {}) {
    // Se o usuário estiver revendo lances antigos, mantém a revisão mesmo quando a máquina joga.
    const prevId = game?.id;
    game = state;
    if (!keepView || prevId !== state.id) viewPly = null;
    if (viewPly !== null && viewPly > game.moves.length) viewPly = null;
    selected = null;
    remember(game.id);
    renderAll();
}

async function runEngineIfNeeded() {
    if (!game || game.isOver || game.isPlayerTurn) return;
    busy = true;
    renderStatus();
    try {
        const state = await api("POST", `/api/games/${game.id}/engine-move`);
        busy = false;
        await setGame(state);
    } catch (e) {
        busy = false;
        renderStatus();
        toast(e.message, true);
    }
}

async function playMove(uci) {
    if (busy) return;
    busy = true;
    selected = null;
    try {
        const state = await api("POST", `/api/games/${game.id}/moves`, { uci });
        busy = false;
        await setGame(state, { keepView: false });
        await runEngineIfNeeded();
    } catch (e) {
        busy = false;
        renderAll();
        toast(e.message, true);
    }
}

async function loadGame(id) {
    const state = await api("GET", `/api/games/${id}`);
    await setGame(state, { keepView: false });
    await runEngineIfNeeded();
}

async function newGame(level, color) {
    const state = await api("POST", "/api/games", { level, playerColor: color });
    flipped = false;
    await setGame(state, { keepView: false });
    toast(`Nova partida — nível ${state.levelName}. Você joga de ${state.playerColor === "white" ? "brancas" : "pretas"}.`);
    await runEngineIfNeeded();
}

// ================== Tabuleiro ==================
function parseFen(fen) {
    const [placement, turn] = fen.split(" ");
    const squares = {};
    placement.split("/").forEach((row, i) => {
        let file = 0;
        const rank = 8 - i;
        for (const ch of row) {
            if (/\d/.test(ch)) { file += +ch; continue; }
            squares[String.fromCharCode(97 + file) + rank] = ch;
            file++;
        }
    });
    return { squares, turn };
}

function displayedPosition() {
    const moves = game?.moves ?? [];
    if (!game) return { fen: START_FEN, last: null, san: "" };
    if (viewPly === null || viewPly === moves.length) {
        return { fen: game.fen, last: game.lastMoveUci, san: moves.at(-1)?.san ?? "" };
    }
    if (viewPly === 0) return { fen: START_FEN, last: null, san: "" };
    const m = moves[viewPly - 1];
    return { fen: m.fenAfter, last: m.uci, san: m.san };
}

const isLive = () => viewPly === null || viewPly === (game?.moves.length ?? 0);
const canInteract = () => game && isLive() && game.isPlayerTurn && !game.isOver && !busy;
const legalFrom = (sq) => (game?.legalMoves ?? []).filter((m) => m.from === sq);

function bottomIsWhite() {
    const playerWhite = (game?.playerColor ?? "white") === "white";
    return playerWhite !== flipped;
}

function renderBoard() {
    const board = $("board");
    const { fen, last, san } = displayedPosition();
    const { squares, turn } = parseFen(fen);

    // Rei em xeque: na posição ao vivo vem da API; na revisão, deduz pelo "+"/"#" do lance.
    let checkSq = null;
    if (/[+#]$/.test(san)) {
        const kingChar = turn === "w" ? "K" : "k";
        checkSq = Object.keys(squares).find((s) => squares[s] === kingChar) ?? null;
    }

    const targets = selected ? legalFrom(selected) : [];
    const lastFrom = last?.slice(0, 2), lastTo = last?.slice(2, 4);
    const whiteBottom = bottomIsWhite();
    const interactive = canInteract();

    const frag = document.createDocumentFragment();
    for (let row = 0; row < 8; row++) {
        for (let col = 0; col < 8; col++) {
            const file = whiteBottom ? col : 7 - col;
            const rank = whiteBottom ? 8 - row : row + 1;
            const sq = String.fromCharCode(97 + file) + rank;
            const el = document.createElement("div");
            el.className = "sq " + ((file + rank) % 2 === 1 ? "dark" : "light");
            el.dataset.sq = sq;

            if (sq === lastFrom || sq === lastTo) el.classList.add("last");
            if (sq === selected) el.classList.add("selected");
            if (sq === checkSq) el.classList.add("check");
            const target = targets.find((m) => m.to === sq);
            if (target) {
                el.classList.add("target");
                if (squares[sq] || target.san.includes("x")) el.classList.add("capture");
            }

            const p = squares[sq];
            if (p) {
                const isWhite = p === p.toUpperCase();
                const span = document.createElement("span");
                span.className = "piece " + (isWhite ? "w" : "b");
                span.textContent = GLYPHS[p.toLowerCase()];
                const own = interactive && (isWhite ? "white" : "black") === game.playerColor;
                if (own && legalFrom(sq).length) {
                    span.draggable = true;
                    el.classList.add("clickable");
                }
                el.appendChild(span);
            }
            if (target) el.classList.add("clickable");

            if (col === 0) el.insertAdjacentHTML("beforeend", `<span class="coord rank">${rank}</span>`);
            if (row === 7) el.insertAdjacentHTML("beforeend", `<span class="coord file">${String.fromCharCode(97 + file)}</span>`);
            frag.appendChild(el);
        }
    }
    board.replaceChildren(frag);
}

function onSquareClick(sq) {
    if (!canInteract()) {
        if (game && !isLive()) toast("Você está revendo um lance antigo. Volte à posição atual para jogar.");
        return;
    }
    if (selected) {
        const options = legalFrom(selected).filter((m) => m.to === sq);
        if (options.length) {
            tryMove(selected, sq);
            return;
        }
    }
    selected = legalFrom(sq).length ? (selected === sq ? null : sq) : null;
    renderBoard();
}

function tryMove(from, to) {
    const options = (game.legalMoves ?? []).filter((m) => m.from === from && m.to === to);
    if (!options.length) return false;
    if (options.length > 1 || options[0].promotion) {
        askPromotion(options);
    } else {
        playMove(options[0].uci);
    }
    return true;
}

function askPromotion(options) {
    const box = $("promotion");
    const white = game.playerColor === "white";
    box.replaceChildren();
    for (const p of ["q", "r", "b", "n"]) {
        const opt = options.find((m) => m.promotion === p);
        if (!opt) continue;
        const btn = document.createElement("button");
        btn.innerHTML = `<span class="piece ${white ? "w" : "b"}">${GLYPHS[p]}</span>`;
        btn.title = opt.san;
        btn.onclick = () => { box.classList.add("hidden"); playMove(opt.uci); };
        box.appendChild(btn);
    }
    box.onclick = (e) => { if (e.target === box) { box.classList.add("hidden"); selected = null; renderBoard(); } };
    box.classList.remove("hidden");
}

// Eventos do tabuleiro (delegação)
$("board").addEventListener("click", (e) => {
    const sqEl = e.target.closest(".sq");
    if (sqEl) onSquareClick(sqEl.dataset.sq);
});
$("board").addEventListener("dragstart", (e) => {
    const sqEl = e.target.closest(".sq");
    if (!sqEl || !canInteract()) { e.preventDefault(); return; }
    dragFrom = sqEl.dataset.sq;
    selected = dragFrom;
    e.dataTransfer.effectAllowed = "move";
    e.dataTransfer.setData("text/plain", dragFrom);
    // Re-renderiza após o navegador capturar a imagem do arraste.
    requestAnimationFrame(() => {
        renderBoard();
        $("board").querySelector(`[data-sq="${dragFrom}"] .piece`)?.classList.add("dragging");
    });
});
$("board").addEventListener("dragover", (e) => {
    if (!dragFrom) return;
    const sqEl = e.target.closest(".sq");
    if (sqEl && legalFrom(dragFrom).some((m) => m.to === sqEl.dataset.sq)) {
        e.preventDefault();
        $("board").querySelectorAll(".drag-over").forEach((x) => x.classList.remove("drag-over"));
        sqEl.classList.add("drag-over");
    }
});
$("board").addEventListener("drop", (e) => {
    e.preventDefault();
    const sqEl = e.target.closest(".sq");
    const from = dragFrom;
    dragFrom = null;
    if (sqEl && from && tryMove(from, sqEl.dataset.sq)) return;
    selected = null;
    renderBoard();
});
$("board").addEventListener("dragend", () => {
    if (dragFrom) { dragFrom = null; renderBoard(); }
});

// ================== Lista de lances ==================
function annotationPly() {
    if (!game || !game.moves.length) return 0;
    return viewPly === null ? game.moves.length : viewPly;
}

function renderMoves() {
    const box = $("moves");
    const moves = game?.moves ?? [];
    if (!moves.length) {
        box.innerHTML = `<p class="empty">Nenhum lance ainda.</p>`;
        return;
    }
    const active = annotationPly();
    const parts = [];
    const cell = (m) => {
        if (!m) return `<div></div>`;
        const cls = ["mv", m.playedBy === "maquina" ? "machine" : "", m.ply === active ? "active" : ""].join(" ");
        const who = m.playedBy === "maquina" ? "Máquina" : "Você";
        return `<div class="${cls}" data-ply="${m.ply}" title="${who} — ${esc(m.uci)}">${esc(m.san)}` +
            (m.annotation ? `<span class="ann">${esc(m.annotation)}</span>` : "") +
            (m.comment ? `<span class="has-comment">💬</span>` : "") + `</div>`;
    };

    let i = 0;
    if (moves[0].color === "black") {
        parts.push(`<div class="num">${moves[0].moveNumber}.</div><div class="mv">…</div>${cell(moves[0])}`);
        parts.push(commentsRow(null, moves[0]));
        i = 1;
    }
    for (; i < moves.length; i += 2) {
        const w = moves[i], b = moves[i + 1];
        parts.push(`<div class="num">${w.moveNumber}.</div>${cell(w)}${cell(b)}`);
        parts.push(commentsRow(w, b));
    }
    if (game.isOver) parts.push(`<div class="result">${esc(game.result)} — ${esc(game.resultReason ?? "")}</div>`);
    box.innerHTML = parts.join("");
    box.querySelector(".mv.active")?.scrollIntoView({ block: "nearest" });
}

function commentsRow(w, b) {
    const texts = [w, b].filter((m) => m?.comment).map((m) => `${m.color === "white" ? m.moveNumber + "." : m.moveNumber + "…"}${m.san}: ${m.comment}`);
    return texts.length ? `<div></div><div class="comment-row">${texts.map(esc).join("\n")}</div>` : "";
}

$("moves").addEventListener("click", (e) => {
    const el = e.target.closest(".mv[data-ply]");
    if (!el) return;
    goTo(+el.dataset.ply);
});

function goTo(ply) {
    if (!game) return;
    const n = game.moves.length;
    ply = Math.max(0, Math.min(n, ply));
    viewPly = ply === n ? null : ply;
    selected = null;
    $("promotion").classList.add("hidden");
    renderAll();
}

$("nav-first").onclick = () => goTo(0);
$("nav-prev").onclick = () => goTo((viewPly ?? game?.moves.length ?? 0) - 1);
$("nav-next").onclick = () => goTo((viewPly ?? game?.moves.length ?? 0) + 1);
$("nav-last").onclick = () => goTo(game?.moves.length ?? 0);
$("review-back").onclick = (e) => { e.preventDefault(); goTo(game.moves.length); };
$("btn-flip").onclick = () => { flipped = !flipped; renderBoard(); renderTags(); };

document.addEventListener("keydown", (e) => {
    if (e.target.matches("textarea, input") || document.querySelector("dialog[open]")) return;
    const cur = viewPly ?? game?.moves.length ?? 0;
    if (e.key === "ArrowLeft") goTo(cur - 1);
    else if (e.key === "ArrowRight") goTo(cur + 1);
    else if (e.key === "Home") goTo(0);
    else if (e.key === "End") goTo(game?.moves.length ?? 0);
    else if (e.key === "f") $("btn-flip").click();
    else return;
    e.preventDefault();
});

// ================== Anotações ==================
function renderAnnotation() {
    const ply = annotationPly();
    const m = ply > 0 ? game.moves[ply - 1] : null;
    $("ann-target").textContent = m
        ? `${m.moveNumber}${m.color === "white" ? "." : "…"} ${m.san}${m.annotation} (${m.playedBy === "maquina" ? "máquina" : "você"})`
        : "selecione um lance na lista";

    const symBox = $("symbols");
    symBox.replaceChildren(...SYMBOLS.map(({ s, t }) => {
        const b = document.createElement("button");
        b.textContent = s;
        b.title = t;
        b.disabled = !m;
        if (m?.annotation === s) b.classList.add("on");
        b.onclick = () => saveAnnotation(ply, m.annotation === s ? "" : s, undefined);
        return b;
    }));

    const c = $("comment");
    if (document.activeElement !== c || c.dataset.ply !== String(ply)) {
        c.value = m?.comment ?? "";
        c.dataset.ply = String(ply);
    }
    c.disabled = !m;
    $("btn-save-comment").disabled = !m;
}

async function saveAnnotation(ply, annotation, comment) {
    if (!game || ply <= 0) return;
    try {
        const state = await api("PUT", `/api/games/${game.id}/moves/${ply}/annotation`, { annotation, comment });
        await setGame(state, { keepView: true });
        flash("comment-hint", "Salvo no JSON ✓");
    } catch (e) {
        toast(e.message, true);
    }
}

$("btn-save-comment").onclick = () => saveAnnotation(+$("comment").dataset.ply, undefined, $("comment").value);
$("comment").addEventListener("keydown", (e) => {
    if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) $("btn-save-comment").click();
});

$("btn-save-notes").onclick = async () => {
    try {
        const state = await api("PUT", `/api/games/${game.id}/notes`, { notes: $("notes").value });
        await setGame(state, { keepView: true });
        flash("notes-hint", "Salvo no JSON ✓");
    } catch (e) {
        toast(e.message, true);
    }
};

function flash(id, text) {
    const el = $(id);
    el.textContent = text;
    clearTimeout(el._t);
    el._t = setTimeout(() => (el.textContent = ""), 2500);
}

// ================== Status ==================
function renderStatus() {
    const dot = $("status-dot");
    dot.className = "dot";
    if (!game) {
        $("status-text").textContent = "Escolha um nível e comece uma partida.";
    } else if (game.isOver) {
        dot.classList.add("over");
        $("status-text").textContent = game.resultReason ?? "Partida encerrada.";
    } else if (busy && !game.isPlayerTurn) {
        dot.classList.add("thinking");
        $("status-text").textContent = "Máquina pensando…";
    } else if (game.isPlayerTurn) {
        dot.classList.add("you");
        $("status-text").textContent = game.inCheck ? "Sua vez — você está em xeque!" : "Sua vez";
    } else {
        dot.classList.add("thinking");
        $("status-text").textContent = "Vez da máquina";
    }

    if (game) {
        $("game-meta").textContent =
            `Nível ${game.levelName} · você joga de ${game.playerColor === "white" ? "brancas" : "pretas"} · ` +
            `${game.moves.length} meio-lances · arquivo ${game.id}.json`;
    }

    const eng = game?.engine;
    let info = "";
    if (eng) {
        if (eng.randomChoice && eng.depth <= 1 && eng.nodes === 0) {
            info = "Máquina: escolheu um lance ao acaso (nível iniciante).";
        } else {
            const ev = eng.mateIn != null
                ? `mate em ${Math.abs(eng.mateIn)} para as ${eng.mateIn > 0 ? "brancas" : "pretas"}`
                : `avaliação ${eng.evalWhiteCp >= 0 ? "+" : ""}${(eng.evalWhiteCp / 100).toFixed(2)}`;
            info = `Máquina: ${ev} · profundidade ${eng.depth} · ${eng.nodes.toLocaleString("pt-BR")} posições · ${(eng.elapsedMs / 1000).toFixed(1)} s`;
        }
    }
    $("engine-info").textContent = info;

    const hasGame = !!game;
    const hasPlayerMove = game?.moves.some((m) => m.playedBy === "jogador");
    $("btn-undo").disabled = !hasGame || busy || !hasPlayerMove;
    $("btn-resign").disabled = !hasGame || busy || game.isOver;
    for (const [id, kind] of [["btn-json", "json"], ["btn-pgn", "pgn"]]) {
        const a = $(id);
        a.classList.toggle("disabled", !hasGame);
        if (hasGame) a.href = `/api/games/${game.id}/export/${kind}`;
    }
    $("notes").disabled = !hasGame;
    $("btn-save-notes").disabled = !hasGame;
}

function renderTags() {
    if (!game) { $("tag-top").innerHTML = ""; $("tag-bottom").innerHTML = ""; return; }
    const you = `<span>👤 <strong>Você</strong></span>`;
    const cpu = `<span>🤖 <strong>Máquina</strong> — ${esc(game.levelName)}</span>`;
    const whiteBottom = bottomIsWhite();
    const bottomColor = whiteBottom ? "white" : "black";
    const youBottom = bottomColor === game.playerColor;
    const label = (c) => `<span>${c === "white" ? "♔ brancas" : "♚ pretas"}</span>`;
    $("tag-bottom").innerHTML = (youBottom ? you : cpu) + label(bottomColor);
    $("tag-top").innerHTML = (youBottom ? cpu : you) + label(whiteBottom ? "black" : "white");
}

function renderReviewBanner() {
    const reviewing = game && !isLive();
    $("review-banner").classList.toggle("hidden", !reviewing);
    if (reviewing) {
        $("review-label").textContent = viewPly === 0
            ? "posição inicial"
            : (() => { const m = game.moves[viewPly - 1]; return `${m.moveNumber}${m.color === "white" ? "." : "…"} ${m.san}`; })();
    }
}

function renderAll() {
    renderBoard();
    renderTags();
    renderMoves();
    renderAnnotation();
    renderStatus();
    renderReviewBanner();
    if (game && document.activeElement !== $("notes")) $("notes").value = game.notes ?? "";
}

function esc(s) {
    return String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}

// ================== Botões / diálogos ==================
$("btn-undo").onclick = async () => {
    try {
        const state = await api("POST", `/api/games/${game.id}/undo`);
        await setGame(state, { keepView: false });
        toast("Lance desfeito.");
    } catch (e) { toast(e.message, true); }
};

$("btn-resign").onclick = async () => {
    if (!confirm("Tem certeza de que deseja desistir desta partida?")) return;
    try {
        await setGame(await api("POST", `/api/games/${game.id}/resign`));
    } catch (e) { toast(e.message, true); }
};

$("btn-new").onclick = () => $("dlg-new").showModal();
$("form-new").addEventListener("submit", async (e) => {
    if (e.submitter?.value !== "ok") return;
    const data = new FormData($("form-new"));
    try {
        await newGame(data.get("level"), data.get("color"));
    } catch (err) { toast(err.message, true); }
});

$("btn-saved").onclick = async () => {
    $("dlg-saved").showModal();
    await renderSavedList();
};
$("btn-close-saved").onclick = () => $("dlg-saved").close();

async function renderSavedList() {
    const box = $("saved-list");
    box.innerHTML = `<p class="empty">Carregando…</p>`;
    try {
        const list = await api("GET", "/api/games");
        if (!list.length) { box.innerHTML = `<p class="empty">Nenhuma partida salva ainda.</p>`; return; }
        box.innerHTML = list.map((g) => `
            <div class="saved-item">
                <div class="title">${esc(g.levelName)} · ${g.playerColor === "white" ? "♔ brancas" : "♚ pretas"} · ${esc(STATUS_PT[g.status] ?? g.status)} ${g.result !== "*" ? "(" + esc(g.result) + ")" : ""}</div>
                <div class="sub">${new Date(g.updatedAt).toLocaleString("pt-BR")} · ${g.moveCount} meio-lances · ${esc(g.id)}.json</div>
                <div class="opening">${esc(g.opening) || "—"}</div>
                <div class="btns">
                    <button class="btn small primary" data-open="${esc(g.id)}">Abrir</button>
                    <button class="btn small danger" data-del="${esc(g.id)}" title="Excluir">🗑</button>
                </div>
            </div>`).join("");
    } catch (e) {
        box.innerHTML = `<p class="empty">${esc(e.message)}</p>`;
    }
}

$("saved-list").addEventListener("click", async (e) => {
    const open = e.target.closest("[data-open]")?.dataset.open;
    const del = e.target.closest("[data-del]")?.dataset.del;
    try {
        if (open) {
            $("dlg-saved").close();
            flipped = false;
            await loadGame(open);
        } else if (del && confirm("Excluir esta partida (o arquivo JSON será apagado)?")) {
            await api("DELETE", `/api/games/${del}`);
            if (game?.id === del) { game = null; viewPly = null; renderAll(); }
            await renderSavedList();
        }
    } catch (err) { toast(err.message, true); }
});

// ================== Início ==================
async function init() {
    const levels = await api("GET", "/api/levels");
    $("levels").innerHTML = levels.map((l, i) => `
        <label>
            <input type="radio" name="level" value="${l.level}" ${i === 1 ? "checked" : ""}>
            <span class="level"><strong>${esc(l.name)} <span class="bars">${LEVEL_BARS[l.level] ?? ""}</span></strong><span>${esc(l.description)}</span></span>
        </label>`).join("");

    renderAll();
    const last = recall();
    if (last) {
        try { await loadGame(last); return; } catch { /* partida apagada */ }
    }
    $("dlg-new").showModal();
}

init().catch((e) => toast(e.message, true));
