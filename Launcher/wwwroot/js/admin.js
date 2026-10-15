// Admin sayfası: kullanıcı yönetimi (MongoDB) ve instance manifest yayınlama.
let adminConnected = false;
let adminTab = 'users';

/** Admin sekmesi her açıldığında seçili bölümü yeniden çizer. */
function openAdminPage() {
    showAdminTab(adminTab);
}

function showAdminTab(tab) {
    adminTab = tab;
    for (const button of document.querySelectorAll('#admin-tabs button'))
        button.classList.toggle('active', button.dataset.tab === tab);

    const body = $('admin-body');
    if (tab === 'users') renderUsersTab(body);
    else renderInstancesTab(body);
}

function setupAdminPage() {
    for (const button of document.querySelectorAll('#admin-tabs button'))
        button.addEventListener('click', () => showAdminTab(button.dataset.tab));
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
            el('div', {},
                el('div', { class: 'uname' }, user.username,
                    user.isAdmin && el('span', { class: 'badge' }, 'admin'),
                    !user.isActive && el('span', { class: 'badge' }, 'pasif')),
                el('div', { class: 'meta' }, 'Paketler: ' + (user.allowedPacks.join(', ') || '—'))),
            el('button', { class: 'btn btn-sm', onclick: () => editUser(user, body) }, 'Düzenle'),
            el('button', { class: 'btn btn-sm btn-danger', onclick: () => deleteUser(user, body) }, 'Sil')));

        body.replaceChildren(toolbar, ...rows);
    }).catch(err => {
        body.replaceChildren(el('p', { class: 'form-error' }, 'Hata: ' + err.message));
    });
}

function renderConnectForm(body) {
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
                if (result.success) { adminConnected = true; renderUsersTab(body); return; }
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
        const packs = el('input', { type: 'text', value: isEdit ? user.allowedPacks.join(', ') : 'Vanilla', placeholder: 'Vanilla, AGLR, R', spellcheck: false });
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
                    allowedPacks: packs.value.split(',').map(p => p.trim()).filter(Boolean),
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
                el('label', {}, 'Paketler (virgülle ayır)'), packs,
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

// ── Instance güncelleme ───────────────────────────────────────────────
function renderInstancesTab(body) {
    body.replaceChildren(el('p', { class: 'admin-toolbar' }, 'Yükleniyor...'));

    bridge.invoke('admin.instances').then(info => {
        const logBox = el('div', { class: 'admin-log' });
        const log = text => { logBox.textContent = text + '\n' + logBox.textContent; };

        const cards = info.instances.map(instance => {
            const modsVersion = el('input', { type: 'text', placeholder: 'Mods versiyon (örn: v1.0)', spellcheck: false });
            const modsUrl = el('input', { type: 'url', placeholder: 'Mods ZIP adresi (https://...)', spellcheck: false });
            const changelog = el('textarea', {
                class: 'changelog-input', rows: 2, maxLength: 600, spellcheck: false,
                placeholder: 'Yenilik notu (isteğe bağlı): eklenen/güncellenen modlar, düzeltmeler...',
                'aria-label': instance + ' yenilik notu',
            });
            const button = el('button', { class: 'btn btn-sm btn-primary' }, 'Tara ve yayınla');

            button.addEventListener('click', async () => {
                button.disabled = true;
                log(`${instance} taranıyor ve gönderiliyor...`);
                try {
                    const result = await bridge.invoke('admin.scanPush', {
                        instance, modsVersion: modsVersion.value, modsUrl: modsUrl.value, changelog: changelog.value,
                    });
                    log(result.success
                        ? `✓ ${instance}: manifest güncellendi (${result.fileCount} dosya, ${result.deletedCount} silindi)`
                        : `✗ ${instance}: sunucu manifest'i kabul etmedi.`);
                    if (result.success) { changelog.value = ''; loadDetails(); }
                } catch (err) {
                    log(`✗ ${instance}: ${err.message}`);
                }
                button.disabled = false;
            });

            return el('div', { class: 'inst-card' },
                el('div', { class: 'title' }, instance),
                el('div', { class: 'folders' }, 'Taranacak: ' + info.trackedFolders.join(', ')),
                modsVersion, modsUrl, button, changelog);
        });

        body.replaceChildren(
            el('p', { class: 'admin-toolbar' }, 'Instance klasörünü tarar, manifest oluşturur ve sunucuya gönderir.'),
            ...(cards.length ? cards : [el('p', { class: 'pack-empty' }, 'Bu bilgisayarda kurulu instance klasörü bulunamadı.')]),
            logBox);
    }).catch(err => {
        body.replaceChildren(el('p', { class: 'form-error' }, 'Hata: ' + err.message));
    });
}
