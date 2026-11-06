// Profil ve topluluk: kendi profilini düzenle (resim, açıklama, şifre), bütün vulu kullanıcılarını gör.
const profile = { viewing: '', me: null, members: [], requestId: 0 };

function bannerUrl(user) {
    return user && user.bannerVersion
        ? `${state.apiBase}/users/${encodeURIComponent(user.username)}/banner?v=${user.bannerVersion}`
        : null;
}

function avatarUrl(user) {
    return user && user.avatarVersion
        ? `${state.apiBase}/users/${encodeURIComponent(user.username)}/avatar?v=${user.avatarVersion}`
        : null;
}

/** Avatar kutusunu doldurur: resim varsa resim, yoksa (ya da yüklenemezse) baş harf. */
function setAvatar(node, user) {
    const initial = (user && user.username || '?').charAt(0).toUpperCase();
    const url = avatarUrl(user);
    node.replaceChildren();
    node.textContent = url ? '' : initial;
    if (!url) return;
    const img = el('img', { src: url, alt: '' });
    img.addEventListener('error', () => { img.remove(); node.textContent = initial; });
    node.append(img);
}

function avatarEl(user, size) {
    const node = el('span', { class: 'avatar ' + size, 'aria-hidden': 'true' });
    setAvatar(node, user);
    return node;
}

async function loadMyProfile() {
    try {
        profile.me = await bridge.invoke('profile.me');
        setAvatar($('user-avatar'), profile.me);
    } catch { /* sunucuya ulaşılamazsa baş harf kalır */ }
}

async function openProfile(username) {
    // Kendi profilin önce başkalarının gördüğü gibi açılır; "Profili düzenle" ile düzenlemeye geçilir.
    // Düzenlerken yapılan değişiklik profili yeniden açtığında düzenleme modu kalır.
    if (username !== profile.viewing) profile.editing = false;
    profile.viewing = username;
    showPage('profile');
    loadMembers();

    const requestId = ++profile.requestId;
    $('profile-view').replaceChildren(el('div', { class: 'profile-card skeleton' }));
    try {
        const user = await bridge.invoke('profile.get', { username });
        if (requestId === profile.requestId) renderProfile(user);
    } catch (err) {
        if (requestId !== profile.requestId) return;
        $('profile-view').replaceChildren(el('div', { class: 'profile-card' },
            el('p', { class: 'form-error' }, 'Profil alınamadı: ' + err.message)));
    }
}

function lastSeenText(user) {
    const seen = parseDate(user.lastSeenAt);
    return seen ? 'Son görülme: ' + relativeDate(seen).toLowerCase() : 'Henüz giriş yapmadı';
}

function setProfileEditing(editing) {
    profile.editing = editing;
    openProfile(state.username);
}

function renderProfile(user) {
    const own = user.username === state.username;
    const editing = own && !!profile.editing;
    const joined = parseDate(user.createdAt);

    const head = el('div', { class: 'profile-head' },
        avatarEl(user, 'avatar-xl'),
        el('div', { class: 'profile-id' },
            el('h1', {}, user.username, user.isAdmin && adminMark()),
            el('div', { class: 'profile-meta' },
                [joined && 'Katıldı: ' + formatDate(joined), lastSeenText(user)].filter(Boolean).join(' · ')),
            roleChips(user.roles)),
        editing && el('div', { class: 'profile-head-actions' },
            el('button', { class: 'btn btn-sm', onclick: changeAvatar }, 'Resmi değiştir'),
            user.avatarVersion > 0 && el('button', { class: 'btn btn-sm', onclick: removeAvatar }, 'Kaldır'),
            el('button', { class: 'btn btn-sm btn-primary', onclick: () => setProfileEditing(false) }, 'Bitti')),
        own && !editing && el('div', { class: 'profile-head-actions' },
            el('button', { class: 'btn btn-sm', onclick: () => showPage('settings') }, 'Ayarlar'),
            el('button', { class: 'btn btn-sm btn-primary', onclick: () => setProfileEditing(true) }, 'Profili düzenle')),
        !own && el('div', { class: 'profile-head-actions' },
            ...friendButtons(user.username),
            el('button', { class: 'btn btn-sm btn-primary', onclick: () => { showPage('messages'); openMessagesPage(user.username); } }, 'Mesaj gönder')));

    const banner = el('div', { class: 'profile-banner' + (user.bannerVersion ? ' has-image' : '') },
        editing && el('div', { class: 'profile-banner-actions' },
            el('button', { class: 'btn btn-sm', onclick: changeBanner }, user.bannerVersion ? 'Bannerı değiştir' : 'Banner ekle'),
            user.bannerVersion > 0 && el('button', { class: 'btn btn-sm', onclick: removeBanner }, 'Kaldır')));
    const url = bannerUrl(user);
    if (url) {
        banner.style.background = `center / cover no-repeat url("${url}")`;
        // Yüklenemezse (silinmiş, ağ yok) düz şerit kalır
        const probe = new Image();
        probe.onerror = () => { banner.style.background = ''; banner.classList.remove('has-image'); };
        probe.src = url;
    }

    const sections = [
        banner,
        head,
        editing ? bioEditor(user) : el('section', { class: 'profile-section' },
            el('h2', {}, 'Hakkında'),
            el('p', { class: 'profile-bio' + (user.bio ? '' : ' empty') },
                user.bio || (own ? 'Henüz bir şey yazmadın. "Profili düzenle" ile kendinden bahset.' : 'Henüz bir şey yazmamış.'))),
        skinSection(user, editing),
        playtimeSection(user, own),
    ].filter(Boolean);

    if (user.packs && user.packs.length) {
        sections.push(el('section', { class: 'profile-section' },
            el('h2', {}, `Paylaştığı paketler (${user.packs.length})`),
            el('div', { class: 'profile-packs' }, user.packs.map(pack =>
                el('button', { class: 'profile-pack', onclick: () => showPage('packs') },
                    el('span', { class: 'name' }, pack.name),
                    el('span', { class: 'meta' }, `${pack.mcVersion} · ${LOADER_NAMES[pack.loader] || pack.loader} · rev ${pack.revision}`))))));
    }

    if (editing) sections.push(passwordForm());
    if (own) sections.push(el('div', { class: 'profile-foot' },
        el('button', { class: 'btn btn-sm btn-danger', onclick: logout }, 'Çıkış yap')));

    $('profile-view').replaceChildren(el('div', { class: 'profile-card' }, sections));
    markActiveMember();
}

/** Toplam oynama süresi ve en çok oynanan paketler (launcher gönderir; kullanıcı kapatabilir). */
function playtimeSection(user, own) {
    const p = user.playtime;
    if (!p || !p.totalSeconds) {
        return own && el('section', { class: 'profile-section' },
            el('h2', {}, 'Oynama'),
            el('p', { class: 'profile-bio empty' }, state.settings.showPlaytimeOnProfile === false
                ? 'Oynama süren profilinde gizli (Ayarlar).'
                : 'Biraz oynadıktan sonra toplam süren burada görünür.'));
    }
    const max = Math.max(...p.top.map(t => t.seconds), 1);
    return el('section', { class: 'profile-section' },
        el('h2', {}, 'Oynama'),
        el('div', { class: 'playtime-total' }, el('b', {}, formatPlayTime(p.totalSeconds)), ' toplam'),
        p.top.length > 0 && el('div', { class: 'playtime-top' }, p.top.map(t =>
            el('div', { class: 'playtime-row' },
                el('span', { class: 'playtime-name' }, t.name),
                el('span', { class: 'playtime-bar' }, el('i', { style: `width:${Math.max(4, Math.round(t.seconds / max * 100))}%` })),
                el('span', { class: 'playtime-time' }, formatPlayTime(t.seconds))))));
}

function bioEditor(user) {
    const text = el('textarea', { class: 'textarea', rows: 3, maxLength: 300, spellcheck: false, placeholder: 'Kendinden bahset: ne oynarsın, hangi sunucudasın…', 'aria-label': 'Hakkımda' });
    text.value = user.bio || '';
    const note = el('span', { class: 'profile-note' });
    const counter = el('span', { class: 'profile-note' }, `${text.value.length}/300`);
    const save = el('button', { class: 'btn btn-sm btn-primary', disabled: true }, 'Kaydet');

    text.addEventListener('input', () => {
        counter.textContent = `${text.value.length}/300`;
        save.disabled = text.value.trim() === (user.bio || '');
        note.textContent = '';
    });
    save.addEventListener('click', async () => {
        save.disabled = true;
        try {
            const updated = await bridge.invoke('profile.saveBio', { bio: text.value });
            user.bio = updated.bio;
            note.textContent = 'Kaydedildi ✓';
            note.classList.remove('error');
            loadMembers(true);
        } catch (err) {
            note.textContent = err.message;
            note.classList.add('error');
            save.disabled = false;
        }
    });

    return el('section', { class: 'profile-section' },
        el('h2', {}, 'Hakkımda'),
        text,
        el('div', { class: 'profile-row' }, counter, note, save));
}

function passwordForm() {
    const current = el('input', { type: 'password', autocomplete: 'current-password', 'aria-label': 'Mevcut şifre', placeholder: 'Mevcut şifre' });
    const next = el('input', { type: 'password', autocomplete: 'new-password', 'aria-label': 'Yeni şifre', placeholder: 'Yeni şifre' });
    const repeat = el('input', { type: 'password', autocomplete: 'new-password', 'aria-label': 'Yeni şifre tekrar', placeholder: 'Yeni şifre tekrar' });
    const note = el('span', { class: 'profile-note' });
    const submit = el('button', { class: 'btn btn-sm btn-primary', type: 'submit' }, 'Şifreyi değiştir');

    async function onSubmit(event) {
        event.preventDefault();
        note.classList.add('error');
        if (!current.value || !next.value) { note.textContent = 'Alanları doldur.'; return; }
        if (next.value.length < 6) { note.textContent = 'Yeni şifre en az 6 karakter olmalı.'; return; }
        if (next.value !== repeat.value) { note.textContent = 'Yeni şifreler aynı değil.'; return; }

        submit.disabled = true;
        note.textContent = '';
        try {
            await bridge.invoke('profile.changePassword', { currentPassword: current.value, newPassword: next.value });
            current.value = next.value = repeat.value = '';
            note.classList.remove('error');
            note.textContent = 'Şifren değiştirildi ✓';
        } catch (err) {
            note.textContent = err.message;
        }
        submit.disabled = false;
    }

    return el('section', { class: 'profile-section' },
        el('h2', {}, 'Şifre değiştir'),
        el('form', { class: 'password-form', onsubmit: onSubmit, autocomplete: 'off' },
            current, next, repeat,
            el('div', { class: 'profile-row' }, note, submit)));
}

const AVATAR_SIDE = 512, AVATAR_MAX_BYTES = 900 * 1024;

async function changeAvatar() {
    try {
        const picked = await bridge.invoke('profile.pickAvatar');
        if (!picked) return; // vazgeçildi
        // GIF hareketli kalsın diye olduğu gibi; diğerleri kırpılıp küçültülür
        let dataUrl = picked.dataUrl;
        if (picked.type !== 'image/gif') {
            const cropped = await cropDialog(picked.dataUrl, { aspect: 1, round: true, outWidth: AVATAR_SIDE, title: 'Profil resmini ayarla' });
            if (!cropped) return;
            dataUrl = encodeImage(cropped, AVATAR_SIDE, AVATAR_MAX_BYTES).dataUrl;
        }
        afterMyProfileChange(await bridge.invoke('profile.uploadAvatar', { dataUrl }));
    } catch (err) {
        await alertDialog({ title: 'Resim yüklenemedi', message: err.message });
    }
}

const BANNER_MAX_SIDE = 1600, BANNER_MAX_BYTES = 1400 * 1024;

async function changeBanner() {
    try {
        const picked = await bridge.invoke('profile.pickBanner');
        if (!picked) return; // vazgeçildi
        // GIF hareketli kalsın diye olduğu gibi; diğerleri profildeki 4:1 şeride göre kırpılıp küçültülür
        let dataUrl = picked.dataUrl;
        if (picked.type !== 'image/gif') {
            const cropped = await cropDialog(picked.dataUrl, { aspect: 4, outWidth: BANNER_MAX_SIDE, title: 'Bannerı ayarla' });
            if (!cropped) return;
            dataUrl = encodeImage(cropped, BANNER_MAX_SIDE, BANNER_MAX_BYTES).dataUrl;
        }
        afterMyProfileChange(await bridge.invoke('profile.uploadBanner', { dataUrl }));
    } catch (err) {
        await alertDialog({ title: 'Banner yüklenemedi', message: err.message });
    }
}

/**
 * Resmi kırpma penceresi: sürükleyerek kaydırılır, tekerlek ya da kaydırıcıyla yakınlaştırılır. Çerçeve hep resmin
 * içinde kalır. Kırpılan bölge outWidth genişliğinde bir <canvas> olarak döner; vazgeçilirse null.
 * aspect: genişlik / yükseklik. round: profil resmi gibi yuvarlak önizleme (çıktı yine kare).
 */
async function cropDialog(src, { aspect, round = false, outWidth, title }) {
    const img = await loadImage(src);
    const iw = img.naturalWidth, ih = img.naturalHeight;
    let result = null;

    await openModal(close => {
        // Çerçeve pencereye sığacak boyutta; yükseklik oranla belirlenir
        const vw = Math.min(aspect >= 2 ? 640 : 360, window.innerWidth - 80);
        const vh = Math.round(vw / aspect);
        const minScale = Math.max(vw / iw, vh / ih);
        let scale = minScale, x = (vw - iw * scale) / 2, y = (vh - ih * scale) / 2;

        const picture = el('img', { src, alt: '', draggable: false, class: 'crop-img' });
        const frame = el('div', { class: 'crop-frame' + (round ? ' round' : ''), style: `width:${vw}px;height:${vh}px` },
            picture, el('div', { class: 'crop-mask', 'aria-hidden': 'true' }));
        const zoom = el('input', { type: 'range', min: 1, max: 4, step: 0.01, value: 1, class: 'crop-zoom', 'aria-label': 'Yakınlaştır' });

        const clamp = () => {
            x = Math.min(0, Math.max(vw - iw * scale, x));
            y = Math.min(0, Math.max(vh - ih * scale, y));
        };
        const paint = () => {
            clamp();
            picture.style.width = iw * scale + 'px';
            picture.style.height = ih * scale + 'px';
            picture.style.transform = `translate(${x}px, ${y}px)`;
            zoom.value = scale / minScale;
        };
        // Verilen noktayı (çerçeveye göre) sabit tutarak yakınlaştırır
        const zoomTo = (next, cx = vw / 2, cy = vh / 2) => {
            next = Math.min(minScale * 4, Math.max(minScale, next));
            x = cx - (cx - x) * next / scale;
            y = cy - (cy - y) * next / scale;
            scale = next;
            paint();
        };

        let drag = null;
        frame.addEventListener('pointerdown', e => { drag = { px: e.clientX, py: e.clientY, x, y }; frame.setPointerCapture(e.pointerId); frame.classList.add('dragging'); });
        frame.addEventListener('pointermove', e => { if (!drag) return; x = drag.x + e.clientX - drag.px; y = drag.y + e.clientY - drag.py; paint(); });
        const stop = () => { drag = null; frame.classList.remove('dragging'); };
        frame.addEventListener('pointerup', stop);
        frame.addEventListener('pointercancel', stop);
        frame.addEventListener('wheel', e => {
            e.preventDefault();
            const r = frame.getBoundingClientRect();
            zoomTo(scale * (e.deltaY < 0 ? 1.1 : 1 / 1.1), e.clientX - r.left, e.clientY - r.top);
        }, { passive: false });
        zoom.addEventListener('input', () => zoomTo(minScale * Number(zoom.value)));
        paint();

        const save = el('button', { class: 'btn btn-primary' }, 'Kaydet');
        save.addEventListener('click', () => {
            const outHeight = Math.round(outWidth / aspect);
            const canvas = el('canvas', { width: outWidth, height: outHeight });
            canvas.getContext('2d').drawImage(img, -x / scale, -y / scale, vw / scale, vh / scale, 0, 0, outWidth, outHeight);
            result = canvas;
            close(true);
        });

        return [
            el('div', { class: 'modal-head' }, title),
            el('div', { class: 'modal-body crop-body' },
                frame,
                el('div', { class: 'crop-tools' }, el('span', { class: 'hint' }, '−'), zoom, el('span', { class: 'hint' }, '+')),
                el('p', { class: 'hint' }, 'Resmi sürükleyerek kaydır; tekerlek ya da kaydırıcıyla yakınlaştır.')),
            el('div', { class: 'modal-foot' },
                el('button', { class: 'btn', onclick: () => close(false) }, 'Vazgeç'),
                save),
        ];
    }, { wide: aspect >= 2 });
    return result;
}

async function removeBanner() {
    try { afterMyProfileChange(await bridge.invoke('profile.removeBanner')); }
    catch (err) { await alertDialog({ title: 'Banner kaldırılamadı', message: err.message }); }
}

async function removeAvatar() {
    try { afterMyProfileChange(await bridge.invoke('profile.removeAvatar')); }
    catch (err) { await alertDialog({ title: 'Resim kaldırılamadı', message: err.message }); }
}

function afterMyProfileChange(updated) {
    profile.me = { ...profile.me, ...updated };
    setAvatar($('user-avatar'), profile.me);
    openProfile(state.username);
    loadMembers(true);
}

// ── Topluluk listesi ──────────────────────────────────────────────────
/** Üye listesi (avatar sürümleri dahil) bir kez yüklenir; başka sayfalar da avatar için kullanır. */
async function ensureMembers() {
    if (!profile.members.length) {
        try { profile.members = await bridge.invoke('profile.list'); }
        catch { /* sunucuya ulaşılamazsa baş harfler kalır */ }
    }
    return profile.members;
}

async function loadMembers(force) {
    if (profile.members.length && !force) { renderMembers(); return; }
    try {
        profile.members = await bridge.invoke('profile.list');
        renderMembers();
    } catch (err) {
        $('members-list').replaceChildren(el('p', { class: 'form-error' }, err.message));
    }
}

/** Profilin yanındaki liste: arkadaşların (çevrimiçiler önce). Bütün üyeleri aramak Sosyal'de. */
function renderMembers() {
    const query = $('members-search').value.trim().toLocaleLowerCase('tr');
    // Çevrimiçi olanlar (önce oynayanlar) üstte
    const rank = u => { const p = presenceOf(u.username); return !p ? 2 : p.state === 'playing' ? 0 : 1; };
    const mine = new Set([...friends.friends, state.username]);
    const list = profile.members.filter(u => mine.has(u.username) && (!query || u.username.toLocaleLowerCase('tr').includes(query)))
        .slice().sort((a, b) => rank(a) - rank(b) || a.username.localeCompare(b.username, 'tr'));
    $('members-count').textContent = friends.friends.length ? `${friends.friends.length} arkadaş` : '';
    if (list.length <= 1 && !query) {
        $('members-list').replaceChildren(el('p', { class: 'hint side-hint' }, 'Henüz arkadaşın yok.'),
            el('button', { class: 'btn btn-sm members-find', onclick: () => showPage('friends') }, 'Sosyal\'de arkadaş bul'));
        return;
    }

    $('members-list').replaceChildren(...(list.length ? list.map(user =>
        el('button', { class: 'member', 'data-username': user.username, onclick: () => openProfile(user.username) },
            el('span', { class: 'presence-avatar' }, avatarEl(user, 'avatar-md'),
                presenceOf(user.username) && el('span', { class: 'presence-dot', 'data-state': presenceOf(user.username).state })),
            el('span', { class: 'member-text' },
                el('span', { class: 'member-name' }, user.username,
                    user.isAdmin && adminMark(),
                    user.username === state.username && el('span', { class: 'member-you' }, 'sen')),
                el('span', { class: 'member-sub' },
                    presenceEl(presenceOf(user.username)) || user.bio || lastSeenText(user)))))
        : [el('p', { class: 'hint' }, 'Kimse bulunamadı.')]));
    markActiveMember();
}

function markActiveMember() {
    for (const row of document.querySelectorAll('.member'))
        row.classList.toggle('active', row.dataset.username === profile.viewing);
}

function setupProfilePage() {
    $('members-search').addEventListener('input', renderMembers);
}
