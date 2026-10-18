// Topluluk: arkadaşların çevrimiçi durumu ve vulu sunucuları.

const community = { presence: [], servers: [], status: {}, timer: null, loading: false };
const PRESENCE_REFRESH_MS = 30000;

/** Çevrimiçi durumlar: kullanıcı adı → durum. */
function presenceOf(username) {
    return community.presence.find(p => p.username === username) || null;
}

/** Oyundaysa "AGLR SMP · sunucuda · 1 sa 12 dk" parçaları; değilse yalnızca "Çevrimiçi". */
function presenceParts(p) {
    if (!p) return null;
    if (p.state !== 'playing') return { game: null, rest: 'Çevrimiçi' };
    const game = p.instance || 'Minecraft';
    const server = p.server && community.servers.find(s => s.address === p.server);
    const where = server && server.name !== game ? server.name : p.server ? 'sunucuda' : '';
    const since = parseDate(p.since);
    const minutes = since ? Math.floor((Date.now() - since) / 60000) : 0;
    const played = minutes < 1 ? '' : minutes < 60 ? `${minutes} dk` : `${Math.floor(minutes / 60)} sa ${minutes % 60} dk`;
    return { game, rest: [where, played].filter(Boolean).join(' · ') };
}

/** Düz metin hâli (ipuçları için). */
function presenceText(p) {
    const parts = presenceParts(p);
    return parts ? [parts.game, parts.rest].filter(Boolean).join(' · ') : null;
}

/** Durum satırı: oyunun adı yeşil, gerisi sade. */
function presenceEl(p) {
    const parts = presenceParts(p);
    if (!parts) return null;
    return el('span', { class: 'presence-line', title: presenceText(p) },
        parts.game && el('span', { class: 'presence-game' }, parts.game),
        parts.game && parts.rest && ' · ',
        parts.rest);
}

async function refreshPresence() {
    try { community.presence = await bridge.invoke('community.presence'); }
    catch { /* sunucuya ulaşılamazsa eski liste kalır */ }
    if (state.page === 'profile' && typeof renderMembers === 'function' && profile.members) renderMembers();
    if (state.page === 'servers') renderFriends();
    if (state.page === 'servers') renderServers();
    if (state.page === 'friends') renderFriendsPage();
}

/** Sunucular ve profil sayfası açıkken durumlar düzenli yenilenir. */
function schedulePresence() {
    clearInterval(community.timer);
    if (state.page === 'servers' || state.page === 'profile' || state.page === 'friends')
        community.timer = setInterval(() => { refreshPresence(); if (state.page === 'servers') pingAll(); }, PRESENCE_REFRESH_MS);
}

// ── Sunucular sayfası ─────────────────────────────────────────────────
async function openServersPage() {
    if (community.loading) return;
    community.loading = true;
    $('servers-add').hidden = !state.isAdmin;
    $('servers-status').textContent = 'Yükleniyor…';
    try {
        const [servers] = await Promise.all([
            bridge.invoke('community.servers'),
            refreshPresence(),
            // Etiketlerde paket adları için (Keşfet hiç açılmadıysa)
            state.packsCache ? null : bridge.invoke('packs.list').then(r => { state.packsCache = r.packs; }).catch(() => {}),
            ensureMembers(),   // arkadaş listesindeki avatarlar için
        ]);
        community.servers = servers;
        $('servers-status').textContent = servers.length ? '' : (state.isAdmin
            ? 'Henüz sunucu eklenmemiş. "+ Sunucu ekle" ile ilk sunucuyu ekle.'
            : 'Henüz sunucu eklenmemiş.');
        renderServers();
        renderFriends();
        pingAll();
    } catch (err) {
        $('servers-status').textContent = 'Sunucular alınamadı: ' + err.message;
    } finally {
        community.loading = false;
    }
}

async function pingAll() {
    await Promise.all(community.servers.map(async s => {
        try { community.status[s.id] = await bridge.invoke('community.ping', { address: s.address }); }
        catch { community.status[s.id] = { online: false }; }
        renderServerStatus(s);
    }));
}

function renderServers() {
    $('servers-grid').replaceChildren(...community.servers.map(serverCard));
}

function serverCard(s) {
    const here = community.presence.filter(p => p.state === 'playing' && p.server === s.address);
    const pack = s.packId ? (state.packsCache || []).find(p => p.id === s.packId) : null;
    const join = el('button', { class: 'btn btn-sm btn-primary', onclick: () => joinServer(s) },
        s.instanceId ? 'Bağlan' : 'Paketi kur ve bağlan');
    return el('article', { class: 'server-card', 'data-id': s.id },
        el('div', { class: 'server-head' },
            el('div', {},
                el('h2', { class: 'server-name' }, s.name),
                el('button', { class: 'server-address', title: 'Adresi kopyala', onclick: () => copyText(s.address) }, s.address)),
            el('span', { class: 'server-dot', 'data-state': 'checking', title: 'Denetleniyor' })),
        s.description && el('p', { class: 'server-desc' }, s.description),
        el('div', { class: 'server-status' }, 'Durum denetleniyor…'),
        el('div', { class: 'pack-tags' },
            el('span', { class: 'tag' }, s.packId ? (pack ? pack.name : 'vulu paketi') : `Vanilla${s.mcVersion ? ' ' + s.mcVersion : ''}`),
            here.length > 0 && el('span', { class: 'tag tag-friends', title: here.map(p => p.username).join(', ') }, `${here.length} arkadaş burada`)),
        el('div', { class: 'server-foot' },
            join,
            el('button', { class: 'btn btn-sm', title: 'Bir arkadaşını mesajla davet et', onclick: () => shareCardWith(serverInvite(s)) }, 'Davet et'),
            state.isAdmin && el('button', { class: 'btn btn-sm', onclick: () => openServerDialog(s) }, 'Düzenle'),
            state.isAdmin && el('button', { class: 'btn btn-sm btn-danger', onclick: () => deleteServer(s) }, 'Sil')));
}

function renderServerStatus(s) {
    const card = document.querySelector(`.server-card[data-id="${s.id}"]`);
    if (!card) return;
    const st = community.status[s.id];
    const dot = card.querySelector('.server-dot'), line = card.querySelector('.server-status');
    if (!st) return;
    dot.dataset.state = st.online ? 'online' : 'offline';
    dot.title = st.online ? 'Açık' : 'Kapalı ya da ulaşılamıyor';
    line.replaceChildren(st.online
        ? el('span', { title: (st.sample || []).join(', ') || null },
            [`${st.players}/${st.maxPlayers} oyuncu`, st.version, `${st.ping} ms`].filter(Boolean).join(' · '))
        : el('span', { class: 'muted' }, 'Kapalı ya da ulaşılamıyor'));
    if (st.online && st.motd) line.append(el('span', { class: 'server-motd' }, st.motd));
}

/** Bağlan: doğru instance seçilir (gerekirse paket kurulur) ve oyun doğrudan sunucuya girer. */
async function joinServer(s) {
    let instanceId = s.instanceId;
    if (!instanceId && s.packId) {
        try {
            const result = await withActivity(() => bridge.invoke('packs.install', { id: s.packId }));
            instanceId = result.id;
            await reloadInstances(instanceId);
        } catch (err) { await alertDialog({ title: 'Paket kurulamadı', message: err.message }); return; }
    }
    const instance = state.instances.find(i => i.id === instanceId);
    if (!instance) { await alertDialog({ title: 'Bağlanılamıyor', message: 'Bu sunucunun instance\'ı bulunamadı.' }); return; }
    if (canPickVersion(instance) && s.mcVersion) store.set('mcver_' + instance.id, s.mcVersion);
    selectInstance(instance);
    showPage('home');
    play({ join: s.address });
}

async function deleteServer(s) {
    if (!await confirmDialog({ title: 'Sunucuyu sil', message: `'${s.name}' listeden kaldırılsın mı? Sunucunun kendisine dokunulmaz.`, okText: 'Sil', cancelText: 'Vazgeç', danger: true })) return;
    try { await bridge.invoke('servers.delete', { id: s.id }); openServersPage(); }
    catch (err) { await alertDialog({ title: 'Silinemedi', message: err.message }); }
}

/** Admin: sunucu ekle / düzenle. Paket seçilirse oyuncular o paketin instance'ıyla bağlanır; yoksa Vanilla. */
async function openServerDialog(server) {
    let packs = [];
    try { packs = (await bridge.invoke('packs.list')).packs; state.packsCache = packs; } catch { }
    const saved = await openModal(close => {
        const name = el('input', { type: 'text', maxLength: 40, value: server ? server.name : '', placeholder: 'AGLR SMP', autofocus: true });
        const address = el('input', { type: 'text', maxLength: 120, value: server ? server.address : '', placeholder: 'oyun.alanadi.com ya da 1.2.3.4:25565', spellcheck: false });
        const description = el('input', { type: 'text', maxLength: 200, value: server ? server.description : '', placeholder: 'Kısa bir açıklama (isteğe bağlı)' });
        const pack = el('select', {},
            el('option', { value: '' }, 'Vanilla (paket yok)'),
            ...packs.map(p => el('option', { value: p.id, selected: server && server.packId === p.id }, `${p.name} · ${p.mcVersion}`)));
        const mcVersion = el('input', { type: 'text', maxLength: 40, value: server ? server.mcVersion : '', placeholder: 'ör. 1.21.1 (boşsa seçili sürüm)' });
        const mcRow = el('div', { class: 'form-sub' }, el('label', {}, 'Minecraft sürümü (Vanilla)'), mcVersion);
        const sync = () => { mcRow.hidden = !!pack.value; };
        pack.addEventListener('change', sync); sync();
        const error = el('div', { class: 'form-error' });
        async function submit(event) {
            event.preventDefault();
            error.textContent = '';
            try {
                await bridge.invoke('servers.save', { id: server ? server.id : '', name: name.value, address: address.value, description: description.value, packId: pack.value, mcVersion: mcVersion.value });
                close(true);
            } catch (err) { error.textContent = err.message; }
        }
        return el('form', { onsubmit: submit, style: 'display:contents' },
            el('div', { class: 'modal-head' }, server ? 'Sunucuyu düzenle' : 'Sunucu ekle'),
            el('div', { class: 'modal-body form' },
                el('label', {}, 'Ad'), name,
                el('label', {}, 'Adres'), address,
                el('label', {}, 'Açıklama'), description,
                el('label', {}, 'Paket'), pack,
                mcRow,
                el('p', { class: 'hint' }, 'Oyuncular "Bağlan"a basınca seçilen paketin instance\'ı açılır ve oyun doğrudan bu adrese bağlanır. Paket kurulu değilse önce kurulur.'),
                error),
            el('div', { class: 'modal-foot' },
                el('button', { class: 'btn', type: 'button', onclick: () => close(false) }, 'Vazgeç'),
                el('button', { class: 'btn btn-primary', type: 'submit' }, 'Kaydet')));
    });
    if (saved) openServersPage();
}

// ── Arkadaşlar ────────────────────────────────────────────────────────
/** Sunucular sayfasının yanındaki liste: çevrimiçi olanlar, oynadıklarıyla. */
function renderFriends() {
    const others = community.presence.filter(p => p.username !== state.username);
    $('friends-count').textContent = others.length ? `${others.length} çevrimiçi` : '';
    $('friends-list').replaceChildren(...(others.length
        ? others
            .sort((a, b) => (a.state === 'playing' ? 0 : 1) - (b.state === 'playing' ? 0 : 1) || a.username.localeCompare(b.username, 'tr'))
            .map(p => el('button', { class: 'member', onclick: () => openProfile(p.username) },
                el('span', { class: 'presence-avatar' }, avatarEl({ username: p.username, avatarVersion: memberAvatarVersion(p.username) }, 'avatar-md'),
                    el('span', { class: 'presence-dot', 'data-state': p.state })),
                el('span', { class: 'member-text' },
                    el('span', { class: 'member-name' }, p.username),
                    el('span', { class: 'member-sub' }, presenceEl(p)))))
        : [el('p', { class: 'hint' }, 'Şu an çevrimiçi kimse yok.')]));
}

function memberAvatarVersion(username) {
    const m = (profile.members || []).find(u => u.username === username);
    return m ? m.avatarVersion : 0;
}

async function copyText(text) {
    try { await navigator.clipboard.writeText(text); appendLog({ text: `Adres kopyalandı: ${text}`, level: 'muted' }); }
    catch { appendLog({ text: text, level: 'info' }); }
}

function setupServersPage() {
    $('servers-add').addEventListener('click', () => openServerDialog(null));
    $('servers-refresh').addEventListener('click', openServersPage);
}
