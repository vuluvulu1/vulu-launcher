// Mesajlar: uçtan uca şifreli direkt mesajlar. Şifreleme launcher'ın C# tarafında yapılır; burada yalnızca düz metin görünür.

const dm = { conversations: [], active: null, messages: [], peer: null, unread: 0, loadingMore: false, reachedStart: false };
const DM_MAX = 2000;

function setDmUnread(count) {
    dm.unread = count;
    const badge = $('dm-badge');
    badge.textContent = count > 99 ? '99+' : String(count);
    badge.hidden = !count;
}

async function openMessagesPage(user) {
    await loadConversations();
    // Avatarlar üye listesinden gelir; liste henüz yüklenmediyse yüklenince yeniden çizilir
    if (!profile.members.length) ensureMembers().then(() => { if (state.page === 'messages') renderConversations(); });
    if (user) openConversation(user);
    else if (!dm.active && dm.conversations.length) openConversation(dm.conversations[0].username);
    else renderChat();
}

async function loadConversations() {
    try { dm.conversations = await bridge.invoke('dm.conversations'); }
    catch (err) { $('dm-list').replaceChildren(el('p', { class: 'form-error' }, err.message)); return; }
    renderConversations();
}

function renderConversations() {
    const list = dm.conversations;
    $('dm-list').replaceChildren(...(list.length ? list.map(c => {
        const p = typeof presenceOf === 'function' ? presenceOf(c.username) : null;
        const member = (profile.members || []).find(u => u.username === c.username) || { username: c.username };
        return el('button', { class: 'member dm-conv' + (dm.active === c.username ? ' active' : ''), onclick: () => openConversation(c.username) },
            el('span', { class: 'presence-avatar' }, avatarEl(member, 'avatar-md'), p && el('span', { class: 'presence-dot', 'data-state': p.state })),
            el('span', { class: 'member-text' },
                el('span', { class: 'member-name' }, c.username),
                el('span', { class: 'member-sub' }, p ? presenceEl(p) : relativeDate(new Date(c.lastAt)))),
            c.unread > 0 && el('span', { class: 'tab-badge' }, String(c.unread)));
    }) : [el('p', { class: 'hint' }, 'Henüz mesaj yok. "Yeni mesaj" ile birine yaz.')]));
}

async function openConversation(user) {
    dm.active = user;
    dm.messages = [];
    dm.reachedStart = false;
    renderConversations();
    renderChat(true);
    try {
        const result = await bridge.invoke('dm.history', { user });
        if (dm.active !== user) return;
        dm.messages = result.messages;
        dm.peer = result;
        dm.reachedStart = result.messages.length < 50;
        renderChat();
        scrollChatToEnd();
        await bridge.invoke('dm.read', { user });
        const conv = dm.conversations.find(c => c.username === user);
        if (conv && conv.unread) { setDmUnread(Math.max(0, dm.unread - conv.unread)); conv.unread = 0; renderConversations(); }
    } catch (err) {
        $('dm-chat').replaceChildren(el('p', { class: 'form-error dm-empty' }, err.message));
    }
}

async function loadOlder() {
    if (dm.loadingMore || dm.reachedStart || !dm.messages.length) return;
    dm.loadingMore = true;
    const box = $('dm-messages'), before = box.scrollHeight;
    try {
        const result = await bridge.invoke('dm.history', { user: dm.active, before: dm.messages[0].createdAt });
        dm.reachedStart = result.messages.length < 50;
        dm.messages = result.messages.concat(dm.messages);
        renderMessages();
        box.scrollTop = box.scrollHeight - before;
    } catch { }
    dm.loadingMore = false;
}

function renderChat(loading) {
    const chat = $('dm-chat');
    if (!dm.active) {
        chat.replaceChildren(el('div', { class: 'dm-empty' },
            el('p', {}, 'Bir yazışma seç ya da yeni bir mesaj başlat.'),
            el('p', { class: 'hint' }, '🔒 Mesajlar uçtan uca şifrelidir: yalnızca sen ve karşındaki kişi okuyabilir; vulu sunucusu bile okuyamaz.')));
        return;
    }
    const peer = dm.peer || {};
    const input = el('textarea', { id: 'dm-input', class: 'textarea', rows: 2, maxLength: DM_MAX, placeholder: `${dm.active} kişisine yaz…`, spellcheck: true, disabled: peer.peerReady === false });
    const counter = el('span', { class: 'dm-counter' });
    const send = el('button', { class: 'btn btn-primary', type: 'submit', disabled: peer.peerReady === false }, 'Gönder');
    input.addEventListener('input', () => { counter.textContent = input.value.length > DM_MAX - 200 ? `${input.value.length}/${DM_MAX}` : ''; });
    input.addEventListener('keydown', e => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); form.requestSubmit(); } });
    const submit = async card => {
        const text = input.value.trim();
        if (!text && !card) return;
        send.disabled = true;
        if (await sendDm(dm.active, text, card)) { input.value = ''; counter.textContent = ''; }
        send.disabled = false;
        input.focus();
    };
    const attach = el('button', { class: 'btn dm-attach', type: 'button', title: 'Paket ya da sunucu paylaş', disabled: peer.peerReady === false,
        onclick: async () => { const card = await pickCard(); if (card) submit(card); } }, '+');
    const form = el('form', { class: 'dm-compose', onsubmit: e => { e.preventDefault(); submit(null); } },
        attach, input, el('div', { class: 'dm-compose-side' }, counter, send));

    const box = el('div', { id: 'dm-messages', class: 'dm-messages' });
    box.addEventListener('scroll', () => { if (box.scrollTop < 40) loadOlder(); });
    chat.replaceChildren(el('div', { id: 'dm-head', class: 'dm-head' }), box,
        peer.peerReady === false
            ? el('p', { class: 'hint dm-notice' }, peer.peerError || `${dm.active} henüz mesajlaşmayı kullanmadı; launcher'ın yeni sürümüyle bir kez giriş yaptığında yazabilirsin.`)
            : form);
    renderChatHeader();
    if (loading) box.replaceChildren(el('p', { class: 'hint dm-empty' }, 'Yükleniyor…'));
    else renderMessages();
    queueMicrotask(() => input.focus());
}

function renderChatHeader() {
    const head = $('dm-head');
    if (!head) return;
    const peer = dm.peer || {};
    head.replaceChildren(...[
        el('div', { class: 'dm-head-main' },
            el('button', { class: 'dm-peer', onclick: () => openProfile(dm.active) }, dm.active),
            el('span', { class: 'dm-lock', title: 'Uçtan uca şifreli' }, peer.verified ? '🔒 Doğrulandı' : '🔒 Uçtan uca şifreli')),
        el('button', { class: 'btn btn-sm', onclick: () => showSafetyCode(dm.active) }, 'Güvenlik kodu'),
        peer.keyChanged && el('div', { class: 'dm-warning' },
            `${dm.active} kişisinin şifreleme anahtarı değişmiş. Yeni bir bilgisayara geçmiş ya da şifresi sıfırlanmış olabilir; ama araya biri girmiş de olabilir. Önemli bir şey yazmadan önce güvenlik kodunu birlikte karşılaştırın.`,
            el('button', { class: 'link-btn', onclick: () => showSafetyCode(dm.active) }, 'Güvenlik kodunu göster')),
    ].filter(Boolean));   // replaceChildren false'u metin olarak ekler
}

function renderMessages() {
    const box = $('dm-messages');
    if (!box) return;
    if (!dm.messages.length) { box.replaceChildren(el('p', { class: 'hint dm-empty' }, 'Henüz mesaj yok. İlk mesajı sen yaz.')); return; }
    let lastDay = '';
    const nodes = [];
    for (const m of dm.messages) {
        const at = new Date(m.createdAt);
        const day = at.toLocaleDateString('tr-TR', { day: 'numeric', month: 'long', year: 'numeric' });
        if (day !== lastDay) { nodes.push(el('div', { class: 'dm-day' }, day)); lastDay = day; }
        nodes.push(el('div', { class: 'dm-msg' + (m.mine ? ' mine' : '') + (m.problem ? ' broken' : ''), 'data-id': m.id },
            (m.text || m.problem || !m.card) && el('div', { class: 'dm-bubble' }, m.text != null ? m.text : m.problem),
            m.card && dmCard(m.card, m.mine),
            el('div', { class: 'dm-time' },
                at.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' }) + (m.mine && m.read ? ' · okundu' : ''),
                m.mine && el('button', { class: 'link-btn dm-del', title: 'Herkesten sil', onclick: () => deleteMessage(m) }, 'sil'))));
    }
    box.replaceChildren(...nodes);
}

function scrollChatToEnd() {
    const box = $('dm-messages');
    if (box) box.scrollTop = box.scrollHeight;
}

async function deleteMessage(m) {
    if (!await confirmDialog({ title: 'Mesajı sil', message: 'Bu mesaj iki taraftan da silinsin mi?', okText: 'Sil', cancelText: 'Vazgeç', danger: true })) return;
    try {
        await bridge.invoke('dm.delete', { id: m.id });
        dm.messages = dm.messages.filter(x => x.id !== m.id);
        renderMessages();
    } catch (err) { await alertDialog({ title: 'Silinemedi', message: err.message }); }
}

async function showSafetyCode(user) {
    let info;
    try { info = await bridge.invoke('dm.safety', { user }); }
    catch (err) { await alertDialog({ title: 'Güvenlik kodu', message: err.message }); return; }
    const ok = await openModal(close => [
        el('div', { class: 'modal-head' }, `${user} ile güvenlik kodu`),
        el('div', { class: 'modal-body health-body' },
            el('p', { class: 'hint' }, `Bu kodu ${user} ile karşılaştırın (yüz yüze, sesli sohbette ya da başka bir uygulamada). İki tarafta da aynıysa mesajlarınızı yalnızca siz okuyabilirsiniz.`),
            el('div', { class: 'safety-code' }, info.code),
            info.verified && el('p', { class: 'hint' }, '✓ Bu kişiyi daha önce doğruladın.')),
        el('div', { class: 'modal-foot' },
            el('button', { class: 'btn', onclick: () => close(false) }, 'Kapat'),
            !info.verified && el('button', { class: 'btn btn-primary', onclick: () => close(true) }, 'Kodlar aynı, doğrula')),
    ]);
    if (ok) {
        try { await bridge.invoke('dm.verify', { user }); if (dm.peer && dm.active === user) { dm.peer.verified = true; dm.peer.keyChanged = false; renderChatHeader(); } }
        catch (err) { await alertDialog({ title: 'Doğrulanamadı', message: err.message }); }
    }
}

async function newMessage() {
    const user = await pickUser('Yeni mesaj');
    if (user) openConversation(user);
}

/** Kullanıcı seçtirir (son yazışılanlar önce); vazgeçilirse null. */
async function pickUser(title) {
    const all = (profile.members && profile.members.length ? profile.members : await bridge.invoke('profile.list').catch(() => []))
        .filter(u => u.username !== state.username);
    const recent = dm.conversations.map(c => c.username);
    const members = [...all].sort((a, b) => (recent.indexOf(a.username) + 1 || 1e9) - (recent.indexOf(b.username) + 1 || 1e9));
    return openModal(close => {
        const search = el('input', { type: 'text', placeholder: 'Kullanıcı ara…', spellcheck: false, autofocus: true });
        const list = el('div', { class: 'members-list dm-pick' });
        const render = () => {
            const q = search.value.trim().toLocaleLowerCase('tr');
            list.replaceChildren(...members.filter(u => !q || u.username.toLocaleLowerCase('tr').includes(q)).slice(0, 50).map(u =>
                el('button', { class: 'member', onclick: () => close(u.username) }, avatarEl(u, 'avatar-md'),
                    el('span', { class: 'member-text' }, el('span', { class: 'member-name' }, u.username)))));
        };
        search.addEventListener('input', render);
        render();
        return [el('div', { class: 'modal-head' }, title), el('div', { class: 'modal-body form' }, search, list),
            el('div', { class: 'modal-foot' }, el('button', { class: 'btn', onclick: () => close(null) }, 'Vazgeç'))];
    }).then(u => u || null);
}

/** Mesaj gönderir ve açık yazışmaya ekler. Başarılıysa true. */
async function sendDm(user, text, card) {
    try {
        const result = await bridge.invoke('dm.send', { user, text: text || '', card: card || undefined });
        if (dm.active === user) {
            dm.messages.push(result.message);
            renderMessages();
            scrollChatToEnd();
            if (result.keyChanged && dm.peer) { dm.peer.keyChanged = true; renderChatHeader(); }
        }
        const conv = dm.conversations.find(c => c.username === user);
        if (conv) conv.lastAt = result.message.createdAt; else dm.conversations.unshift({ username: user, lastAt: result.message.createdAt, unread: 0 });
        dm.conversations.sort((a, b) => new Date(b.lastAt) - new Date(a.lastAt));
        if (state.page === 'messages') renderConversations();
        return true;
    } catch (err) {
        await alertDialog({ title: 'Mesaj gönderilemedi', message: err.message });
        return false;
    }
}

// ── Davet kartları: mesajla paylaşılan vulu pack ve sunucular ─────────
const packInvite = pack => ({ type: 'pack', id: pack.id, name: pack.name });
const serverInvite = server => ({ type: 'server', id: server.id, name: server.name, address: server.address });

async function packsForCards() {
    if (!state.packsCache) state.packsCache = (await bridge.invoke('packs.list')).packs;
    return state.packsCache;
}

async function serversForCards() {
    if (!community.servers.length) community.servers = await bridge.invoke('community.servers');
    return community.servers;
}

/** Sohbetteki "+" : paylaşılacak paketi ya da sunucuyu seçtirir. */
async function pickCard() {
    let packs = [], servers = [], error = null;
    try { [packs, servers] = await Promise.all([packsForCards(), serversForCards()]); }
    catch (err) { error = err.message; }
    return openModal(close => {
        const row = (icon, title, sub, card) => el('button', { class: 'member', onclick: () => close(card) },
            el('span', { class: 'dm-card-icon' }, icon),
            el('span', { class: 'member-text' }, el('span', { class: 'member-name' }, title), sub && el('span', { class: 'member-sub' }, sub)));
        return [
            el('div', { class: 'modal-head' }, 'Paylaş'),
            el('div', { class: 'modal-body' },
                error && el('p', { class: 'form-error' }, error),
                el('h3', { class: 'dm-pick-title' }, 'vulu Packs'),
                el('div', { class: 'members-list dm-pick' }, ...(packs.length
                    ? packs.map(p => row('📦', p.name, `Minecraft ${p.mcVersion} · rev ${p.revision}`, packInvite(p)))
                    : [el('p', { class: 'hint' }, 'Paylaşılmış paket yok.')])),
                el('h3', { class: 'dm-pick-title' }, 'Sunucular'),
                el('div', { class: 'members-list dm-pick' }, ...(servers.length
                    ? servers.map(s => row('🎮', s.name, s.address, serverInvite(s)))
                    : [el('p', { class: 'hint' }, 'Eklenmiş sunucu yok.')]))),
            el('div', { class: 'modal-foot' }, el('button', { class: 'btn', onclick: () => close(null) }, 'Vazgeç')),
        ];
    }).then(c => c || null);
}

/** Paket/sunucu kartındaki "Gönder": kişiyi seçtirip kartı gönderir ve yazışmayı açar. */
async function shareCardWith(card) {
    const user = await pickUser(card.type === 'pack' ? `${card.name} paketini gönder` : `${card.name} davetini gönder`);
    if (!user) return;
    if (await sendDm(user, '', card)) { showPage('messages'); openMessagesPage(user); }
}

function dmCard(card, mine) {
    const action = el('button', { class: 'btn btn-sm btn-primary' }, card.type === 'pack' ? 'Paketi aç' : 'Katıl');
    const note = el('span', { class: 'dm-card-note' });
    action.addEventListener('click', async () => {
        action.disabled = true;
        try { await (card.type === 'pack' ? openPackCard(card, note) : joinServerCard(card, note)); }
        catch (err) { note.textContent = err.message; }
        action.disabled = false;
    });
    return el('div', { class: 'dm-card' + (mine ? ' mine' : '') },
        el('span', { class: 'dm-card-icon' }, card.type === 'pack' ? '📦' : '🎮'),
        el('div', { class: 'dm-card-text' },
            el('span', { class: 'dm-card-kind' }, card.type === 'pack' ? 'vulu pack' : 'Sunucu daveti'),
            el('span', { class: 'dm-card-name' }, card.name),
            card.address && el('span', { class: 'dm-card-sub' }, card.address),
            note),
        action);
}

async function openPackCard(card, note) {
    const pack = (await packsForCards()).find(p => p.id === card.id);
    if (!pack) { note.textContent = 'Bu paket artık vulu Packs\'te yok.'; return; }
    if (!pack.installed) { await installPack(pack); state.packsCache = null; return; }
    if (pack.installed.revision < pack.revision) { await updatePack(pack); state.packsCache = null; return; }
    await openInstalledPack(pack.installed.instanceId);
}

async function joinServerCard(card, note) {
    const server = (await serversForCards()).find(s => s.id === card.id || s.address === card.address);
    if (server) { await joinServer(server); return; }
    await copyText(card.address);
    note.textContent = 'Bu sunucu vulu listesinde yok; adres kopyalandı. Oyunda Çok Oyunculu → Sunucu ekle ile bağlanabilirsin.';
}

/** Arka planda gelen yeni mesajlar (C# birkaç saniyede bir sorar). */
function onDmUpdate(update) {
    setDmUnread(update.unread);
    if (!update.messages.length) return;
    for (const m of update.messages) {
        const conv = dm.conversations.find(c => c.username === m.peer);
        if (conv) { conv.lastAt = m.createdAt; if (!(state.page === 'messages' && dm.active === m.peer)) conv.unread++; }
        else dm.conversations.unshift({ username: m.peer, lastAt: m.createdAt, unread: state.page === 'messages' && dm.active === m.peer ? 0 : 1 });
        if (state.page === 'messages' && dm.active === m.peer && !dm.messages.some(x => x.id === m.id)) {
            dm.messages.push(m);
            renderMessages();
            scrollChatToEnd();
            bridge.invoke('dm.read', { user: m.peer }).catch(() => {});
        } else if (state.page !== 'messages') {
            const preview = m.text ? m.text.slice(0, 80) : m.card ? (m.card.type === 'pack' ? `📦 ${m.card.name}` : `🎮 ${m.card.name} daveti`) : 'yeni mesaj';
            appendLog({ text: `💬 ${m.peer}: ${preview}`, level: 'accent' });
        }
    }
    dm.conversations.sort((a, b) => new Date(b.lastAt) - new Date(a.lastAt));
    if (state.page === 'messages') renderConversations();
}

function setupMessagesPage() {
    $('dm-new').addEventListener('click', newMessage);
    bridge.on('dmUpdate', onDmUpdate);
}
