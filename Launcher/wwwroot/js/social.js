// Sosyal: paylaşılan ekran görüntüleri ve bütün üyelerin listesi. Mesajlar messages.js'te.
// Paylaşırken görüntü burada (tarayıcının kodlayıcısıyla) WebP'ye çevrilip küçültülür; vulu sunucusunun alanı sınırlı.

const social = { shots: [], more: false, filter: 'all', loading: false, requestId: 0, view: 'feed', loadedOnce: false };
const SHOT_MAX_SIDE = 1920, THUMB_MAX_SIDE = 480;
const SHOT_MAX_BYTES = 1100 * 1024, THUMB_MAX_BYTES = 150 * 1024;

// ── Akış ──────────────────────────────────────────────────────────────
async function openShotsPage() {
    social.shots = [];
    social.more = false;
    renderShots();
    if (!profile.members.length) ensureMembers().then(() => { if (state.page === 'social') renderShots(); });
    await loadShots();
}

async function loadShots() {
    if (social.loading) return;
    social.loading = true;
    const requestId = ++social.requestId;
    const status = $('shots-status');
    status.classList.remove('error');
    if (!social.shots.length) {
        status.textContent = '';
        $('shots-grid').replaceChildren(...[0, 1, 2].map(() => el('div', { class: 'shot-tile skeleton shot-skel' })));
    }
    const slow = setTimeout(() => { if (!social.shots.length) status.textContent = WAKING_TEXT; }, 4000);
    try {
        const last = social.shots[social.shots.length - 1];
        const result = await bridge.invoke('social.feed', {
            before: last ? last.createdAt : '',
            author: social.filter === 'mine' ? state.username : '',
        });
        if (requestId !== social.requestId) return;
        social.shots = social.shots.concat(result.shots);
        social.more = result.more;
        status.textContent = '';
        renderShots();
        if (!social.shots.length) $('shots-grid').replaceChildren(emptyState({
            icon: '▦',
            title: social.filter === 'mine' ? 'Henüz ekran görüntüsü paylaşmadın' : 'Akış şimdilik boş',
            text: 'Oyunda F2 ile ekran görüntüsü al, sonra buradan paylaş. Bütün vulu üyeleri görür, beğenip yorum yapabilir.',
            action: el('button', { class: 'btn btn-primary btn-sm', onclick: startShareFlow }, '+ Ekran görüntüsü paylaş'),
        }));
    } catch (err) {
        if (requestId !== social.requestId) return;
        status.textContent = 'Ekran görüntüleri alınamadı: ' + err.message;
        status.classList.add('error');
    } finally {
        clearTimeout(slow);
        if (requestId === social.requestId) social.loading = false;
    }
}

function renderShots() {
    $('shots-grid').replaceChildren(...social.shots.map((shot, index) => shotTile(shot, index)));
    $('shots-more').hidden = !social.more;
}

function shotTile(shot, index) {
    const img = el('div', { class: 'shot-thumb' + (shot.thumbUrl ? '' : ' missing') });
    if (shot.thumbUrl) img.style.backgroundImage = `url("${shot.thumbUrl}")`;
    return el('article', { class: 'shot-tile', 'data-id': shot.id },
        el('button', { class: 'shot-open', title: 'Büyük gör', onclick: () => viewSharedShot(index) }, img),
        el('div', { class: 'shot-info' },
            el('button', { class: 'shot-author', onclick: () => openProfile(shot.author) },
                avatarEl({ username: shot.author, avatarVersion: memberAvatarVersion(shot.author) }, 'avatar-sm'),
                el('span', {}, shot.author)),
            el('div', { class: 'shot-counts' },
                el('button', { class: 'shot-comment-count', title: 'Yorumlar', onclick: () => viewSharedShot(index) },
                    el('span', { 'aria-hidden': 'true' }, '💬'), ' ', String(shot.commentCount || 0)),
                likeButton(shot))),
        shot.caption && el('p', { class: 'shot-text' }, shot.caption),
        el('div', { class: 'shot-meta' }, [shot.packName, relativeDate(new Date(shot.createdAt))].filter(Boolean).join(' · ')));
}

function likeButton(shot, onChange) {
    const button = el('button', { class: 'shot-like' + (shot.liked ? ' liked' : ''), title: shot.liked ? 'Beğenmekten vazgeç' : 'Beğen', 'aria-pressed': String(!!shot.liked) },
        el('span', { 'aria-hidden': 'true' }, shot.liked ? '♥' : '♡'), ' ', String(shot.likes));
    button.addEventListener('click', async () => {
        button.disabled = true;
        try {
            const result = await bridge.invoke('social.like', { id: shot.id, like: !shot.liked });
            shot.liked = result.liked;
            shot.likes = result.likes;
            button.replaceWith(likeButton(shot, onChange));
            const tile = document.querySelector(`.shot-tile[data-id="${shot.id}"] .shot-like`);
            if (tile && tile !== button) tile.replaceWith(likeButton(shot));
            if (onChange) onChange();
        } catch (err) { appendLog({ text: err.message, level: 'error' }); button.disabled = false; }
    });
    return button;
}

/** Büyük görünüm: ok tuşlarıyla akışta gezilir. */
async function viewSharedShot(index) {
    let onKey;
    await openModal(close => {
        const img = el('img', { class: 'shot-full', alt: '' });
        const info = el('div', { class: 'modal-foot shot-foot' });
        const loading = el('span', { class: 'hint shot-loading' }, 'Yükleniyor…');
        const comments = el('div', { class: 'shot-comments' });
        let token = 0;

        const show = async i => {
            index = (i + social.shots.length) % social.shots.length;
            const shot = social.shots[index];
            const mine = ++token;
            img.src = shot.thumbUrl || '';
            loading.hidden = false;
            renderInfo();
            loadComments(shot, mine);
            try {
                const { url } = await bridge.invoke('social.image', { id: shot.id });
                if (mine === token) img.src = url;
            } catch (err) { if (mine === token) loading.textContent = err.message; return; }
            if (mine === token) loading.hidden = true;
        };

        function renderInfo() {
            const shot = social.shots[index];
            const at = new Date(shot.createdAt);
            info.replaceChildren(...[
                el('div', { class: 'shot-caption shot-who' },
                    el('button', { class: 'shot-author', onclick: () => { close(); openProfile(shot.author); } },
                        avatarEl({ username: shot.author, avatarVersion: memberAvatarVersion(shot.author) }, 'avatar-sm'), el('span', {}, shot.author)),
                    el('span', {}, [shot.packName, `${formatDate(at)} ${at.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })}`].filter(Boolean).join(' · ')),
                    shot.caption && el('span', { class: 'shot-view-caption' }, shot.caption)),
                likeButton(shot),
                el('button', { class: 'btn btn-sm', title: 'Resimler\\vulu Launcher klasörüne kaydeder', onclick: () => bridge.invoke('social.save', { id: shot.id, author: shot.author }).catch(err => alertDialog({ title: 'Kaydedilemedi', message: err.message })) }, 'Kaydet'),
                shot.canDelete && el('button', { class: 'btn btn-sm btn-danger', onclick: () => close({ remove: index }) }, 'Sil'),
                el('button', { class: 'btn btn-sm', onclick: () => close(null) }, 'Kapat'),
            ].filter(Boolean));
        }

        async function loadComments(shot, mine) {
            comments.replaceChildren(el('p', { class: 'hint' }, 'Yorumlar yükleniyor…'));
            let list = [];
            try { list = await bridge.invoke('social.comments', { id: shot.id }); }
            catch (err) { if (mine === token) comments.replaceChildren(el('p', { class: 'form-error' }, err.message)); return; }
            if (mine !== token) return;
            renderComments(shot, list);
        }

        function renderComments(shot, list) {
            const input = el('input', { type: 'text', maxLength: 300, placeholder: 'Yorum yaz…', spellcheck: true });
            const send = el('button', { class: 'btn btn-sm btn-primary', type: 'submit' }, 'Gönder');
            const form = el('form', { class: 'shot-comment-form', onsubmit: async e => {
                e.preventDefault();
                const text = input.value.trim();
                if (!text) return;
                send.disabled = true;
                try {
                    const c = await bridge.invoke('social.comment', { id: shot.id, text });
                    shot.commentCount = c.commentCount;
                    list.push(c);
                    renderComments(shot, list);
                    updateTileCounts(shot);
                    comments.querySelector('input').focus();
                } catch (err) { await alertDialog({ title: 'Yorum gönderilemedi', message: err.message }); send.disabled = false; }
            } }, input, send);
            comments.replaceChildren(
                el('div', { class: 'shot-comment-list' }, list.length ? list.map(c => {
                    const at = parseDate(c.createdAt);
                    return el('div', { class: 'shot-comment' },
                        avatarEl({ username: c.author, avatarVersion: memberAvatarVersion(c.author) }, 'avatar-sm'),
                        el('div', { class: 'shot-comment-body' },
                            el('div', {}, el('button', { class: 'shot-comment-author', onclick: () => { close(); openProfile(c.author); } }, c.author),
                                el('span', { class: 'muted' }, at ? ' · ' + relativeDate(at) : '')),
                            el('p', {}, c.text)),
                        c.canDelete && el('button', { class: 'link-btn shot-comment-del', title: 'Yorumu sil', onclick: async () => {
                            try {
                                await bridge.invoke('social.deleteComment', { id: shot.id, commentId: c.id });
                                list.splice(list.indexOf(c), 1);
                                shot.commentCount = list.length;
                                renderComments(shot, list);
                                updateTileCounts(shot);
                            } catch (err) { await alertDialog({ title: 'Silinemedi', message: err.message }); }
                        } }, 'sil'));
                }) : [el('p', { class: 'hint' }, 'Henüz yorum yok. İlk yorumu sen yaz.')]),
                form);
            const listEl = comments.querySelector('.shot-comment-list');
            listEl.scrollTop = listEl.scrollHeight;
        }

        onKey = e => {
            if (e.target.closest && e.target.closest('input, textarea')) return;   // yorum yazarken ok tuşları metinde kalsın
            if (e.key === 'ArrowLeft') show(index - 1);
            else if (e.key === 'ArrowRight') show(index + 1);
        };
        document.addEventListener('keydown', onKey);
        const many = social.shots.length > 1;
        show(index);
        return [
            el('div', { class: 'shot-view shared' },
                el('button', { class: 'shot-nav prev', title: 'Önceki (←)', onclick: () => show(index - 1), disabled: !many }, '‹'),
                img, loading,
                el('button', { class: 'shot-nav next', title: 'Sonraki (→)', onclick: () => show(index + 1), disabled: !many }, '›')),
            info,
            comments,
        ];
    }, { wide: true }).then(async result => {
        document.removeEventListener('keydown', onKey);   // Esc ya da arka plan tıklamasıyla kapansa da
        if (result && result.remove != null) await deleteSharedShot(social.shots[result.remove]);
    });
}

function updateTileCounts(shot) {
    const count = document.querySelector(`.shot-tile[data-id="${shot.id}"] .shot-comment-count`);
    if (count) count.replaceChildren(el('span', { 'aria-hidden': 'true' }, '💬'), ' ', String(shot.commentCount || 0));
}

/** Bildirimden: o ekran görüntüsü açılır (akışta yoksa başa eklenir). */
async function openSharedShot(id) {
    showPage('shots');
    let shot;
    try { shot = await bridge.invoke('social.shot', { id }); }
    catch (err) { await alertDialog({ title: 'Ekran görüntüsü', message: err.message }); return; }
    // Akış yüklenirken gelen liste bunu ezmesin diye yükleme bitene kadar beklenir
    for (let i = 0; i < 40 && social.loading; i++) await wait(100);
    let index = social.shots.findIndex(s => s.id === id);
    if (index < 0) { social.shots.unshift(shot); renderShots(); index = 0; }
    viewSharedShot(index);
}

async function deleteSharedShot(shot) {
    const own = shot.author === state.username;
    if (!await confirmDialog({
        title: 'Ekran görüntüsünü kaldır',
        message: own ? 'Bu ekran görüntüsü Sosyal\'den kaldırılsın mı? Bilgisayarındaki dosyaya dokunulmaz.' : `${shot.author} kişisinin ekran görüntüsü kaldırılsın mı?`,
        okText: 'Kaldır', cancelText: 'Vazgeç', danger: true,
    })) return;
    try {
        await bridge.invoke('social.delete', { id: shot.id });
        social.shots = social.shots.filter(s => s.id !== shot.id);
        renderShots();
    } catch (err) { await alertDialog({ title: 'Kaldırılamadı', message: err.message }); }
}

// ── Paylaşma ──────────────────────────────────────────────────────────
/** "+ Paylaş": önce instance, sonra o instance'ın ekran görüntülerinden biri seçilir. */
async function startShareFlow() {
    const instances = state.instances;
    const instance = instances.length === 1 ? instances[0] : await openModal(close => [
        el('div', { class: 'modal-head' }, 'Hangi paketten?'),
        el('div', { class: 'modal-body' },
            el('div', { class: 'members-list dm-pick' }, ...instances.map(i =>
                el('button', { class: 'member', onclick: () => close(i) },
                    el('span', { class: 'dm-card-icon' }, i.iconUrl ? el('img', { src: i.iconUrl, alt: '', class: 'pick-icon' }) : '🧱'),
                    el('span', { class: 'member-text' }, el('span', { class: 'member-name' }, i.name), el('span', { class: 'member-sub' }, loaderLabel(i))))))),
        el('div', { class: 'modal-foot' }, el('button', { class: 'btn', onclick: () => close(null) }, 'Vazgeç')),
    ]);
    if (!instance) return;
    const name = await openScreenshots(instance, { pick: true });
    if (name) await shareScreenshot(instance, name);
}

/** Görüntüyü küçültür, açıklama sorar ve paylaşır. Paylaşıldıysa true. */
async function shareScreenshot(instance, name) {
    let prepared;
    try {
        const { dataUrl } = await bridge.invoke('social.localImage', { id: instance.id, name });
        prepared = await prepareShot(dataUrl);
    } catch (err) { await alertDialog({ title: 'Paylaşılamadı', message: err.message }); return false; }

    const shared = await openModal(close => {
        const caption = el('textarea', { class: 'textarea', rows: 2, maxLength: 200, placeholder: 'Açıklama (isteğe bağlı)', autofocus: true });
        const status = el('p', { class: 'form-error' });
        const submit = el('button', { class: 'btn btn-primary', onclick: async () => {
            submit.disabled = true;
            status.textContent = '';
            try {
                const shot = await bridge.invoke('social.share', { instanceId: instance.id, caption: caption.value, ...prepared });
                close(shot);
            } catch (err) { status.textContent = err.message; submit.disabled = false; }
        } }, 'Paylaş');
        return [
            el('div', { class: 'modal-head' }, 'Ekran görüntüsünü paylaş'),
            el('div', { class: 'modal-body form' },
                el('img', { class: 'share-preview', src: prepared.image, alt: '' }),
                el('p', { class: 'hint' }, `${instance.name} · ${prepared.width}×${prepared.height} · ${formatSize(prepared.bytes / 1048576)}. Bütün vulu üyeleri görebilir.`),
                caption, status),
            el('div', { class: 'modal-foot' }, el('button', { class: 'btn', onclick: () => close(null) }, 'Vazgeç'), submit),
        ];
    });
    if (!shared) return false;
    showPage('shots');
    return true;
}

function loadImage(src) {
    return new Promise((resolve, reject) => {
        const img = new Image();
        img.onload = () => resolve(img);
        img.onerror = () => reject(new Error('Görüntü açılamadı.'));
        img.src = src;
    });
}

/** Uzun kenarı en fazla `maxSide` olacak şekilde küçültüp WebP'ye çevirir; boyut sınırına sığana dek kaliteyi düşürür. */
function encodeImage(img, maxSide, maxBytes) {
    // <img> ya da <canvas> (kırpılmış resim)
    const iw = img.naturalWidth || img.width, ih = img.naturalHeight || img.height;
    let scale = Math.min(1, maxSide / Math.max(iw, ih));
    for (let attempt = 0; attempt < 8; attempt++) {
        const w = Math.max(1, Math.round(iw * scale)), h = Math.max(1, Math.round(ih * scale));
        const canvas = el('canvas', { width: w, height: h });
        canvas.getContext('2d').drawImage(img, 0, 0, w, h);
        for (const quality of [0.85, 0.75, 0.65]) {
            let dataUrl = canvas.toDataURL('image/webp', quality);
            if (!dataUrl.startsWith('data:image/webp')) dataUrl = canvas.toDataURL('image/jpeg', quality);   // WebP yoksa
            const bytes = Math.floor((dataUrl.length - dataUrl.indexOf(',') - 1) * 3 / 4);
            if (bytes <= maxBytes) return { dataUrl, width: w, height: h, bytes };
        }
        scale *= 0.8;
    }
    throw new Error('Görüntü yeterince küçültülemedi.');
}

async function prepareShot(dataUrl) {
    const img = await loadImage(dataUrl);
    const full = encodeImage(img, SHOT_MAX_SIDE, SHOT_MAX_BYTES);
    const thumb = encodeImage(img, THUMB_MAX_SIDE, THUMB_MAX_BYTES);
    return { image: full.dataUrl, thumb: thumb.dataUrl, width: full.width, height: full.height, bytes: full.bytes };
}

// ── Arkadaşlar ────────────────────────────────────────────────────────
// Arkadaşlar istekle eklenir. Gelen istekler Sosyal sekmesinde rozet olur.
const friends = { friends: [], incoming: [], outgoing: [], loaded: false, timer: null };
const FRIENDS_REFRESH_MS = 60000;

/** 'friend' | 'incoming' | 'outgoing' | null */
function friendState(username) {
    if (friends.friends.includes(username)) return 'friend';
    if (friends.incoming.includes(username)) return 'incoming';
    if (friends.outgoing.includes(username)) return 'outgoing';
    return null;
}

async function refreshFriends() {
    try {
        Object.assign(friends, await bridge.invoke('friends.list'), { loaded: true });
    } catch { /* sunucuya ulaşılamazsa eski liste kalır */ }
    setFriendBadges(friends.incoming.length);
    if (state.page === 'social') renderSocialList();
}

/** Bekleyen istek sayısı: üst sekmedeki Sosyal rozeti ve Sosyal'in içindeki Arkadaşlar alt sekmesi. */
function setFriendBadges(count) {
    for (const badge of [$('friend-badge'), ...document.querySelectorAll('.social-friend-badge')]) {
        badge.textContent = count || '';
        badge.hidden = !count;
    }
}

/** Girişten sonra istekler düzenli sorulur (yeni istek rozet ve bildirim olur). */
function startFriendRequests() {
    clearInterval(friends.timer);
    refreshFriends();
    friends.timer = setInterval(refreshFriends, FRIENDS_REFRESH_MS);
}

function stopFriendRequests() {
    clearInterval(friends.timer);
    Object.assign(friends, { friends: [], incoming: [], outgoing: [], loaded: false });
    setFriendBadges(0);
}

/** İstek gönder / kabul et / reddet / geri al / çıkar; sonra liste yenilenir. */
async function friendAction(action, username) {
    if (action === 'remove') {
        const ok = await confirmDialog({
            title: 'Arkadaşlıktan çıkar', message: `${username} arkadaşlarından çıkarılsın mı?`,
            okText: 'Çıkar', cancelText: 'Vazgeç', danger: true,
        });
        if (!ok) return;
    }
    try {
        const result = await bridge.invoke('friends.' + action, { username });
        if (action === 'request' && result && result.status === 'accepted') appendLog({ text: `${username} ile artık arkadaşsınız.`, level: 'ok' });
    } catch (err) { await alertDialog({ title: 'Olmadı', message: err.message }); }
    await refreshFriends();
    refreshPresence();
    if (state.page === 'profile' && typeof openProfile === 'function' && profile.viewing === username) openProfile(username);
}

/** Profilde ve listede gösterilen arkadaşlık düğmeleri. */
function friendButtons(username, small = true) {
    const cls = 'btn' + (small ? ' btn-sm' : '');
    switch (friendState(username)) {
        case 'friend': return [el('button', { class: cls, title: 'Arkadaşlıktan çıkar', onclick: () => friendAction('remove', username) }, '✓ Arkadaş')];
        case 'incoming': return [
            el('button', { class: cls + ' btn-primary', onclick: () => friendAction('accept', username) }, 'Kabul et'),
            el('button', { class: cls, onclick: () => friendAction('decline', username) }, 'Reddet')];
        case 'outgoing': return [el('button', { class: cls, title: 'İsteği geri al', onclick: () => friendAction('decline', username) }, 'İstek gönderildi')];
        default: return [el('button', { class: cls, onclick: () => friendAction('request', username) }, '+ Arkadaş ekle')];
    }
}

// ── Sosyal ekranı: solda kişiler, ortada akış ya da sohbet ─────────────
// social.view: 'feed' (ekran görüntüleri) | 'chat' (dm.active ile sohbet). Telefonda liste ve içerik ayrı ekranlardır
// (.main-open içerik açıkken).

/** Sohbet şu an bu kişiyle açık mı (kişi verilmezse: herhangi biriyle). */
function inChat(user) {
    return state.page === 'social' && social.view === 'chat' && !!dm.active && (!user || dm.active === user);
}

/**
 * Sosyal'e girildiğinde: okunmamış mesaj varsa o konuşma açılır; istek varsa liste zaten en üstte gösterir;
 * yoksa akış (ya da açık kalan sohbet). from: eski alt sekme adları ('messages', 'friends', 'shots') de gelebilir.
 */
async function openSocialPage(from) {
    renderSocialList();
    const ready = Promise.all([loadConversations(), refreshFriends(), refreshPresence(), ensureMembers(), serversForCards().catch(() => [])]);
    if (from === 'shots') showSocialFeed();
    else if (!inChat()) { showSocialFeed(); $('page-social').classList.toggle('main-open', false); }
    await ready;
    renderSocialList();
    const unread = dm.conversations.find(c => c.unread > 0);
    if ((from === 'social' || from === 'messages') && unread && !inChat()) openConversation(unread.username);
    if (from === 'friends') $('social-search').focus();
}

function setSocialView(view) {
    social.view = view;
    $('social-feed').hidden = view !== 'feed';
    $('dm-chat').hidden = view !== 'chat';
    $('page-social').classList.add('main-open');
    renderSocialList();
}

function showSocialFeed() {
    dm.active = null;
    dm.peer = null;
    setSocialView('feed');
    if (!social.loadedOnce) { social.loadedOnce = true; openShotsPage(); }
}

/** Telefonda içerikten (akış ya da sohbet) kişi listesine dönüş. */
function backToSocialList() {
    $('page-social').classList.remove('main-open');
}

/**
 * Soldaki liste: Akış, bekleyen istekler, kişiler (konuşmalar ve arkadaşlar tek listede: okunmamışlar, oyundakiler,
 * çevrimiçiler önce), gönderilen istekler; arama yapılınca diğer üyeler de "Ekle" düğmesiyle çıkar.
 */
function renderSocialList() {
    const list = $('social-list');
    if (!list) return;
    const query = $('social-search').value.trim().toLocaleLowerCase('tr');
    const match = name => !query || name.toLocaleLowerCase('tr').includes(query);
    const member = name => profile.members.find(m => m.username === name) || { username: name, avatarVersion: memberAvatarVersion(name) };
    const conv = name => dm.conversations.find(c => c.username === name);
    const group = (title, count) => el('div', { class: 'social-group' }, title, count > 0 && el('span', { class: 'friends-count' }, count));
    const btn = (label, onclick, primary) => el('button', {
        class: 'btn btn-xs' + (primary ? ' btn-primary' : ''),
        onclick: event => { event.stopPropagation(); onclick(); },
    }, label);
    const row = (name, { sub, actions = [], onclick, active = false, badge = 0 }) => {
        const p = presenceOf(name);
        return el('div', { class: 'social-row' + (active ? ' active' : '') + (p ? '' : ' away') },
            el('button', { class: 'member', onclick },
                el('span', { class: 'presence-avatar' }, avatarEl(member(name), 'avatar-md'), p && el('span', { class: 'presence-dot', 'data-state': p.state })),
                el('span', { class: 'member-text' },
                    el('span', { class: 'member-name' }, name, member(name).isAdmin && adminMark()),
                    el('span', { class: 'member-sub' }, sub || presenceEl(p) || (conv(name) ? relativeDate(new Date(conv(name).lastAt)) : lastSeenText(member(name)))))),
            badge > 0 && el('span', { class: 'tab-badge' }, String(badge)),
            actions.filter(Boolean).length > 0 && el('div', { class: 'social-row-actions' }, actions.filter(Boolean)));
    };
    // Arkadaş bir vulu sunucusunda oynuyorsa yanına tek tıkla gidilir (doğru paket açılır, gerekirse kurulur)
    const join = name => {
        const p = presenceOf(name);
        const server = p && p.state === 'playing' && p.server && community.servers.find(s => s.address === p.server);
        return server && canInstallGames() && btn('Katıl', () => joinServer(server), true);
    };

    const parts = [];
    if (!query) parts.push(el('button', { class: 'social-feed-link' + (social.view === 'feed' ? ' active' : ''), onclick: showSocialFeed },
        el('span', { class: 'social-feed-icon', 'aria-hidden': 'true' }, '▦'),
        el('span', { class: 'member-text' }, el('span', { class: 'member-name' }, 'Akış'), el('span', { class: 'member-sub' }, 'Paylaşılan ekran görüntüleri'))));

    const incoming = friends.incoming.filter(match);
    if (incoming.length) parts.push(group('İstekler', incoming.length), ...incoming.map(name => row(name, {
        sub: 'Arkadaş olmak istiyor', onclick: () => openProfile(name),
        actions: [btn('Kabul et', () => friendAction('accept', name), true), btn('Reddet', () => friendAction('decline', name))],
    })));

    const known = new Set([...dm.conversations.map(c => c.username), ...friends.friends]);
    known.delete(state.username);
    const rank = name => {
        const c = conv(name), p = presenceOf(name);
        return (c && c.unread ? 0 : 10) + (!p ? 2 : p.state === 'playing' ? 0 : 1);
    };
    const people = [...known].filter(match).sort((a, b) => rank(a) - rank(b)
        || new Date((conv(b) || {}).lastAt || 0) - new Date((conv(a) || {}).lastAt || 0) || a.localeCompare(b, 'tr'));
    parts.push(group('Kişiler', 0));
    parts.push(...(people.length
        ? people.map(name => row(name, {
            onclick: () => openConversation(name), active: inChat(name), badge: (conv(name) || {}).unread || 0, actions: [join(name)],
        }))
        : [el('p', { class: 'hint side-hint' }, !friends.loaded ? 'Yükleniyor…' : query ? 'Bu adla bir arkadaşın ya da konuşman yok.' : 'Henüz kimse yok. Yukarıdan birini ara ve arkadaş ekle.')]));

    const outgoing = friends.outgoing.filter(match);
    if (outgoing.length) parts.push(group('Gönderdiğin istekler', outgoing.length), ...outgoing.map(name => row(name, {
        sub: 'Yanıt bekleniyor', onclick: () => openProfile(name), actions: [btn('Geri al', () => friendAction('decline', name))],
    })));

    if (query) {
        const others = profile.members.map(m => m.username)
            .filter(name => name !== state.username && !known.has(name) && !friends.incoming.includes(name) && !friends.outgoing.includes(name) && match(name))
            .sort((a, b) => a.localeCompare(b, 'tr')).slice(0, 30);
        parts.push(group('Topluluk', others.length), ...(others.length
            ? others.map(name => row(name, { onclick: () => openConversation(name), actions: [btn('+ Ekle', () => friendAction('request', name))] }))
            : [el('p', { class: 'hint side-hint' }, 'Bu adla bir üye yok.')]));
    }
    list.replaceChildren(...parts);
}

function setupSocial() {
    for (const button of document.querySelectorAll('#shots-filter button'))
        button.addEventListener('click', () => {
            social.filter = button.dataset.filter;
            for (const b of document.querySelectorAll('#shots-filter button')) b.classList.toggle('active', b === button);
            openShotsPage();
        });
    $('shots-more').addEventListener('click', loadShots);
    $('shots-share').addEventListener('click', startShareFlow);
    $('social-search').addEventListener('input', renderSocialList);
    $('social-search').addEventListener('keydown', e => { if (e.key === 'Escape') { e.target.value = ''; renderSocialList(); } });
    for (const back of document.querySelectorAll('.social-back')) back.addEventListener('click', backToSocialList);
}
