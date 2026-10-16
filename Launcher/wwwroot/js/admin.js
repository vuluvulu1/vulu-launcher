// Admin sayfası: kullanıcılar ve kayıt istekleri. Hepsi API üzerinden, giriş yapan admin hesabıyla yapılır.
// Bekleyen istek varsa sayfa o sekmeyle açılır
let adminTab = null;

/** Admin sekmesi her açıldığında yeniden çizilir. */
async function openAdminPage() {
    await refreshRegistrations();
    if (!adminTab) adminTab = pendingRegistrations.length ? 'registrations' : 'users';
    renderAdminTabs();
}

function renderAdminTabs() {
    const content = el('div', { class: 'admin-content' });
    const tab = (key, label, count) => el('button', {
        class: key === adminTab ? 'active' : '',
        onclick: async () => {
            adminTab = key;
            if (key === 'registrations') await refreshRegistrations();
            renderAdminTabs();
        },
    }, label, count > 0 && el('span', { class: 'tab-count' }, count));

    $('admin-body').replaceChildren(
        el('div', { class: 'tabs admin-tabs' },
            tab('users', 'Kullanıcılar'),
            tab('registrations', 'Kayıt istekleri', pendingRegistrations.length)),
        content);

    if (adminTab === 'registrations') renderRegistrationsTab(content);
    else renderUsersTab(content);
}

// ── Kullanıcılar ──────────────────────────────────────────────────────
function renderUsersTab(body) {
    body.replaceChildren(el('p', { class: 'admin-toolbar' }, 'Yükleniyor...'));

    bridge.invoke('admin.users').then(users => {
        const toolbar = el('div', { class: 'admin-toolbar' },
            el('span', {}, `${users.length} kullanıcı`),
            el('button', { class: 'btn btn-sm btn-primary', onclick: () => editUser(null, body) }, '+ Kullanıcı ekle'));

        const rows = users.map(user => {
            const seen = parseDate(user.lastSeenAt);
            const isSelf = user.username === state.username;
            return el('div', { class: 'user-row' + (user.isActive ? '' : ' inactive') },
                el('div', {},
                    el('div', { class: 'uname' }, user.username,
                        user.isAdmin && adminMark(),
                        !user.isActive && el('span', { class: 'badge' }, 'pasif'),
                        isSelf && el('span', { class: 'member-you' }, 'sen')),
                    el('div', { class: 'meta' }, [user.email, seen ? 'Son görülme: ' + timeAgo(seen) : 'Henüz giriş yapmadı']
                        .filter(Boolean).join(' · '))),
                el('button', { class: 'btn btn-sm', onclick: () => editUser(user, body) }, 'Düzenle'),
                el('button', {
                    class: 'btn btn-sm btn-danger', disabled: isSelf, title: isSelf ? 'Kendi hesabını silemezsin' : '',
                    onclick: () => deleteUser(user, body),
                }, 'Sil'));
        });

        body.replaceChildren(toolbar, ...rows);
    }).catch(err => {
        body.replaceChildren(el('p', { class: 'form-error' }, 'Hata: ' + err.message));
    });
}

function editUser(user, body) {
    const isEdit = !!user;
    const isSelf = isEdit && user.username === state.username;

    return openModal(close => {
        // Kullanıcı adı sonradan değişmez: oyundaki kimlik (eşyalar, ilerleme) ada bağlıdır
        const username = el('input', { type: 'text', value: isEdit ? user.username : '', maxLength: 16, spellcheck: false, autofocus: !isEdit, disabled: isEdit });
        username.addEventListener('input', () => { username.value = username.value.replace(/[^a-zA-Z0-9_]/g, ''); });
        const email = el('input', { type: 'text', value: isEdit ? user.email : '', maxLength: 254, spellcheck: false, disabled: isEdit, placeholder: isEdit ? '' : 'İsteğe bağlı' });
        const password = el('input', { type: 'password', placeholder: isEdit ? 'Boş bırakırsan değişmez' : 'En az 6 karakter', autofocus: isEdit });
        // Kendi adminliğini kaldırma ve kendini kapatma sunucuda da engelli
        const isAdmin = el('input', { type: 'checkbox', checked: isEdit && user.isAdmin, disabled: isSelf });
        const isActive = el('input', { type: 'checkbox', checked: !isEdit || user.isActive, disabled: isSelf });
        const error = el('div', { class: 'form-error' });
        const save = el('button', { class: 'btn btn-primary', type: 'submit' }, isEdit ? 'Kaydet' : 'Ekle');

        async function submit(event) {
            event.preventDefault();
            save.disabled = true;
            error.textContent = '';
            try {
                await bridge.invoke('admin.saveUser', {
                    id: isEdit ? user.id : '',
                    username: username.value.trim(),
                    email: email.value.trim(),
                    password: password.value,
                    isAdmin: isAdmin.checked,
                    isActive: isActive.checked,
                });
                close(true);
                renderUsersTab(body);
                return;
            } catch (err) {
                error.textContent = err.message;
            }
            save.disabled = false;
        }

        return el('form', { onsubmit: submit, style: 'display:contents' },
            el('div', { class: 'modal-head' }, isEdit ? `Düzenle — ${user.username}` : 'Yeni kullanıcı'),
            el('div', { class: 'modal-body form' },
                el('label', {}, 'Kullanıcı adı'), username,
                isEdit && el('p', { class: 'hint', style: 'margin-top:2px' }, 'Kullanıcı adı değiştirilemez; oyundaki eşyalar ve ilerleme ada bağlıdır.'),
                el('label', {}, 'E-posta'), email,
                el('label', {}, isEdit ? 'Yeni şifre' : 'Şifre'), password,
                isEdit && el('p', { class: 'hint', style: 'margin-top:2px' }, 'Şifre değişirse kullanıcının açık oturumları kapanır.'),
                el('div', { class: 'row', style: 'margin-top:10px' },
                    el('label', { class: 'check' }, isAdmin, el('span', {}, 'Admin')),
                    el('label', { class: 'check' }, isActive, el('span', {}, 'Aktif'))),
                isSelf && el('p', { class: 'hint', style: 'margin-top:4px' }, 'Kendi adminliğini kaldıramaz ya da kendini kapatamazsın.'),
                error),
            el('div', { class: 'modal-foot' },
                el('button', { class: 'btn', type: 'button', onclick: () => close(false) }, 'Vazgeç'),
                save));
    });
}

async function deleteUser(user, body) {
    const accepted = await confirmDialog({
        title: 'Kullanıcı sil',
        message: `'${user.username}' silinsin mi? Hesap ve profil resmi silinir; paylaştığı paketler yerinde kalır. Bu işlem geri alınamaz.`,
        okText: 'Sil', cancelText: 'Vazgeç', danger: true,
    });
    if (!accepted) return;

    try { await bridge.invoke('admin.deleteUser', { id: user.id, username: user.username }); }
    catch (err) { await alertDialog({ title: 'Silinemedi', message: err.message }); }
    renderUsersTab(body);
}
