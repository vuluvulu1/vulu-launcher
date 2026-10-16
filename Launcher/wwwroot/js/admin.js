// Admin sayfası: kullanıcı yönetimi (MongoDB) ve kayıt istekleri (API; MongoDB bağlantısı gerekmez).
let adminConnected = false;
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
    if (!adminConnected) { renderConnectForm(body); return; }

    body.replaceChildren(el('p', { class: 'admin-toolbar' }, 'Yükleniyor...'));

    bridge.invoke('admin.users').then(users => {
        const toolbar = el('div', { class: 'admin-toolbar' },
            el('span', {}, `${users.length} kullanıcı`),
            el('button', { class: 'btn btn-sm btn-primary', onclick: () => editUser(null, body) }, '+ Kullanıcı ekle'));

        const rows = users.map(user => el('div', { class: 'user-row' + (user.isActive ? '' : ' inactive') },
            el('div', { class: 'uname' }, user.username,
                user.isAdmin && el('span', { class: 'badge' }, 'admin'),
                !user.isActive && el('span', { class: 'badge' }, 'pasif')),
            el('button', { class: 'btn btn-sm', onclick: () => editUser(user, body) }, 'Düzenle'),
            el('button', { class: 'btn btn-sm btn-danger', onclick: () => deleteUser(user, body) }, 'Sil')));

        body.replaceChildren(toolbar, ...rows);
    }).catch(err => {
        body.replaceChildren(el('p', { class: 'form-error' }, 'Hata: ' + err.message));
    });
}

/** MongoDB bağlantı formu; bağlanınca onConnected çağrılır (varsayılan: kullanıcı listesi). */
function renderConnectForm(body, onConnected = () => renderUsersTab(body)) {
    const username = el('input', { type: 'text', placeholder: 'MongoDB kullanıcı adı', autofocus: true, spellcheck: false });
    const password = el('input', { type: 'password', placeholder: 'Şifre' });
    const error = el('div', { class: 'form-error' });
    const submit = el('button', { class: 'btn btn-primary', type: 'submit' }, 'Bağlan');

    const form = el('form', {
        class: 'admin-connect',
        onsubmit: async event => {
            event.preventDefault();
            submit.disabled = true;
            error.textContent = 'Bağlanılıyor...';
            try {
                const result = await bridge.invoke('admin.connect', { username: username.value, password: password.value });
                if (result.success) { adminConnected = true; onConnected(); return; }
                error.textContent = result.error;
            } catch (err) {
                error.textContent = err.message;
            }
            submit.disabled = false;
        },
    },
        el('h3', {}, 'MongoDB bağlantısı'),
        el('label', {}, 'Kullanıcı adı'), username,
        el('label', {}, 'Şifre'), password,
        submit, error);

    body.replaceChildren(form);
    username.focus();
}

function editUser(user, body) {
    const isEdit = !!user;

    return openModal(close => {
        const username = el('input', { type: 'text', value: isEdit ? user.username : '', maxLength: 16, spellcheck: false, autofocus: true });
        const password = el('input', { type: 'password', placeholder: isEdit ? 'Boş bırakırsan değişmez' : '' });
        const isAdmin = el('input', { type: 'checkbox', checked: isEdit && user.isAdmin });
        const isActive = el('input', { type: 'checkbox', checked: !isEdit || user.isActive });
        const error = el('div', { class: 'form-error' });
        const save = el('button', { class: 'btn btn-primary', type: 'submit' }, isEdit ? 'Kaydet' : 'Ekle');

        async function submit(event) {
            event.preventDefault();
            save.disabled = true;
            error.textContent = '';
            try {
                const result = await bridge.invoke('admin.saveUser', {
                    id: isEdit ? user.id : '',
                    username: username.value.trim(),
                    password: password.value,
                    isAdmin: isAdmin.checked,
                    isActive: isActive.checked,
                });
                if (result.success) { close(true); renderUsersTab(body); return; }
                error.textContent = result.error;
            } catch (err) {
                error.textContent = 'Hata: ' + err.message;
            }
            save.disabled = false;
        }

        return el('form', { onsubmit: submit, style: 'display:contents' },
            el('div', { class: 'modal-head' }, isEdit ? `Düzenle — ${user.username}` : 'Yeni kullanıcı'),
            el('div', { class: 'modal-body form' },
                el('label', {}, 'Kullanıcı adı'), username,
                el('label', {}, 'Şifre'), password,
                el('div', { class: 'row', style: 'margin-top:10px' },
                    el('label', { class: 'check' }, isAdmin, el('span', {}, 'Admin')),
                    el('label', { class: 'check' }, isActive, el('span', {}, 'Aktif'))),
                error),
            el('div', { class: 'modal-foot' },
                el('button', { class: 'btn', type: 'button', onclick: () => close(false) }, 'Vazgeç'),
                save));
    });
}

async function deleteUser(user, body) {
    const accepted = await confirmDialog({
        title: 'Kullanıcı sil',
        message: `'${user.username}' silinsin mi? Bu işlem geri alınamaz.`,
        okText: 'Sil', cancelText: 'Vazgeç', danger: true,
    });
    if (!accepted) return;

    try { await bridge.invoke('admin.deleteUser', { id: user.id }); }
    catch (err) { await alertDialog({ title: 'Silinemedi', message: err.message }); }
    renderUsersTab(body);
}
