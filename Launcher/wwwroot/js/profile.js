// Profil ve topluluk: kendi profilini düzenle (resim, açıklama, şifre), bütün vulu kullanıcılarını gör.
const profile = { viewing: '', me: null, members: [], requestId: 0 };

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
    } catch { /* sunucu uyanıyorsa baş harf kalır */ }
}

async function openProfile(username) {
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

function renderProfile(user) {
    const own = user.username === state.username;
    const joined = parseDate(user.createdAt);

    const head = el('div', { class: 'profile-head' },
        avatarEl(user, 'avatar-xl'),
        el('div', { class: 'profile-id' },
            el('h1', {}, user.username, user.isAdmin && adminMark()),
            el('div', { class: 'profile-meta' },
                [joined && 'Katıldı: ' + formatDate(joined), lastSeenText(user)].filter(Boolean).join(' · '))),
        own && el('div', { class: 'profile-head-actions' },
            el('button', { class: 'btn btn-sm', onclick: changeAvatar }, 'Resmi değiştir'),
            user.avatarVersion > 0 && el('button', { class: 'btn btn-sm', onclick: removeAvatar }, 'Kaldır')));

    const sections = [
        el('div', { class: 'profile-banner', 'aria-hidden': 'true' }),
        head,
        own ? bioEditor(user) : el('section', { class: 'profile-section' },
            el('h2', {}, 'Hakkında'),
            el('p', { class: 'profile-bio' + (user.bio ? '' : ' empty') }, user.bio || 'Henüz bir şey yazmamış.')),
    ];

    if (user.packs && user.packs.length) {
        sections.push(el('section', { class: 'profile-section' },
            el('h2', {}, `Paylaştığı paketler (${user.packs.length})`),
            el('div', { class: 'profile-packs' }, user.packs.map(pack =>
                el('button', { class: 'profile-pack', onclick: () => showPage('packs') },
                    el('span', { class: 'name' }, pack.name),
                    el('span', { class: 'meta' }, `${pack.mcVersion} · ${LOADER_NAMES[pack.loader] || pack.loader} · rev ${pack.revision}`))))));
    }

    if (own) {
        sections.push(passwordForm());
        sections.push(el('div', { class: 'profile-foot' },
            el('button', { class: 'btn btn-sm', onclick: () => showPage('settings') }, 'Ayarlar'),
            el('button', { class: 'btn btn-sm btn-danger', onclick: logout }, 'Çıkış yap')));
    }

    $('profile-view').replaceChildren(el('div', { class: 'profile-card' }, sections));
    markActiveMember();
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

async function changeAvatar() {
    try {
        const updated = await bridge.invoke('profile.pickAvatar');
        if (!updated) return; // vazgeçildi
        afterMyProfileChange(updated);
    } catch (err) {
        await alertDialog({ title: 'Resim yüklenemedi', message: err.message });
    }
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
async function loadMembers(force) {
    if (profile.members.length && !force) { renderMembers(); return; }
    try {
        profile.members = await bridge.invoke('profile.list');
        renderMembers();
    } catch (err) {
        $('members-list').replaceChildren(el('p', { class: 'form-error' }, err.message));
    }
}

function renderMembers() {
    const query = $('members-search').value.trim().toLocaleLowerCase('tr');
    const list = profile.members.filter(u => !query || u.username.toLocaleLowerCase('tr').includes(query));
    $('members-count').textContent = `${profile.members.length} kişi`;

    $('members-list').replaceChildren(...(list.length ? list.map(user =>
        el('button', { class: 'member', 'data-username': user.username, onclick: () => openProfile(user.username) },
            avatarEl(user, 'avatar-md'),
            el('span', { class: 'member-text' },
                el('span', { class: 'member-name' }, user.username,
                    user.isAdmin && adminMark(),
                    user.username === state.username && el('span', { class: 'member-you' }, 'sen')),
                el('span', { class: 'member-sub' }, user.bio || lastSeenText(user)))))
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
