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
            tab('roles', 'Roller'),
            tab('registrations', 'Kayıt istekleri', pendingRegistrations.length),
            tab('announcement', 'Duyuru'),
            tab('storage', 'Alan')),
        content);

    if (adminTab === 'registrations') renderRegistrationsTab(content);
    else if (adminTab === 'announcement') renderAnnouncementTab(content);
    else if (adminTab === 'storage') renderStorageTab(content);
    else if (adminTab === 'roles') renderRolesTab(content);
    else renderUsersTab(content);
}

// ── Kullanıcılar ──────────────────────────────────────────────────────
function renderUsersTab(body) {
    body.replaceChildren(el('p', { class: 'admin-toolbar' }, 'Yükleniyor...'));

    // Roller yüklenemezse (eski sunucu) kullanıcılar rolsüz listelenir ve düzenlemede roller değişmez
    Promise.all([bridge.invoke('admin.users'), bridge.invoke('admin.roles').catch(() => null)]).then(([users, roles]) => {
        const byId = new Map((roles || []).map(r => [r.id, r]));
        const toolbar = el('div', { class: 'admin-toolbar' },
            el('span', {}, `${users.length} kullanıcı`),
            el('button', { class: 'btn btn-sm btn-primary', onclick: () => editUser(null, body, roles) }, '+ Kullanıcı ekle'));

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
                        .filter(Boolean).join(' · ')),
                    roleChips((user.roles || []).map(id => byId.get(id)).filter(Boolean))),
                el('button', { class: 'btn btn-sm', onclick: () => editUser(user, body, roles) }, 'Düzenle'),
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

/** roles: bütün roller ([{ id, name, color }]); null ise (yüklenemedi) rol seçici gösterilmez ve roller değişmez. */
function editUser(user, body, roles) {
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
        const roleBoxes = (roles || []).map(role => ({
            role, box: el('input', { type: 'checkbox', checked: isEdit && (user.roles || []).includes(role.id) }),
        }));
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
                    ...(roles ? { roles: roleBoxes.filter(r => r.box.checked).map(r => r.role.id) } : {}),
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
                roles && el('label', { style: 'margin-top:12px' }, 'Roller'),
                roles && (roleBoxes.length
                    ? el('div', { class: 'role-picks' }, roleBoxes.map(r => el('label', { class: 'role-pick', style: `--role:${roleColor(r.role.color)}` },
                        r.box, el('i', { 'aria-hidden': 'true' }), el('span', {}, r.role.name))))
                    : el('p', { class: 'hint' }, 'Henüz rol yok. Roller sekmesinden oluşturabilirsin.')),
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

// ── Roller ────────────────────────────────────────────────────────────
// Discord'daki roller gibi: kullanıcılara verilir, paylaşılan paketlerde "Kimler erişebilir" olarak seçilir.
function renderRolesTab(body) {
    body.replaceChildren(el('p', { class: 'admin-toolbar' }, 'Yükleniyor...'));
    bridge.invoke('admin.roles').then(roles => {
        const toolbar = el('div', { class: 'admin-toolbar' },
            el('span', {}, roles.length ? `${roles.length} rol` : 'Henüz rol yok'),
            el('button', { class: 'btn btn-sm btn-primary', onclick: () => editRole(null, body) }, '+ Rol ekle'));
        const intro = el('p', { class: 'hint admin-intro' },
            'Rol verdiğin kişiler o role açılan paketleri görür. Paylaşırken "Kimler erişebilir" kısmında rol seçilmeyen paket herkese açıktır; adminler her paketi görür.');
        const rows = roles.map(role => el('div', { class: 'user-row' },
            el('div', {},
                roleChips([role]),
                el('div', { class: 'meta' }, `${role.members} kişi · ${role.packs} paket`)),
            el('button', { class: 'btn btn-sm', onclick: () => editRole(role, body) }, 'Düzenle'),
            el('button', { class: 'btn btn-sm btn-danger', onclick: () => deleteRole(role, body) }, 'Sil')));
        body.replaceChildren(toolbar, intro, ...rows);
    }).catch(err => {
        body.replaceChildren(el('p', { class: 'form-error' }, 'Hata: ' + err.message));
    });
}

const ROLE_COLORS = ['#c9a992', '#e3b04f', '#7cc47f', '#5fa8d3', '#9b7fd1', '#e2604a', '#d17fb0', '#9c704c'];

function editRole(role, body) {
    const isEdit = !!role;
    return openModal(close => {
        const name = el('input', { type: 'text', value: isEdit ? role.name : '', maxLength: 24, spellcheck: false, autofocus: true, placeholder: 'ör. AGLR' });
        let color = isEdit ? roleColor(role.color) : ROLE_COLORS[0];
        const preview = el('div', { class: 'role-preview' });
        const paint = () => preview.replaceChildren(roleChips([{ name: name.value.trim() || 'Rol', color }]));
        const custom = el('input', { type: 'color', value: color, title: 'Başka bir renk' });
        const swatches = ROLE_COLORS.map(c => el('button', {
            type: 'button', class: 'role-swatch', style: `--role:${c}`, title: c,
            onclick: () => { color = c; custom.value = c; mark(); paint(); },
        }));
        const mark = () => swatches.forEach((s, i) => s.classList.toggle('active', ROLE_COLORS[i] === color));
        custom.addEventListener('input', () => { color = custom.value; mark(); paint(); });
        name.addEventListener('input', paint);
        mark(); paint();

        const error = el('div', { class: 'form-error' });
        const save = el('button', { class: 'btn btn-primary', type: 'submit' }, isEdit ? 'Kaydet' : 'Ekle');
        async function submit(event) {
            event.preventDefault();
            save.disabled = true;
            error.textContent = '';
            try {
                await bridge.invoke('admin.saveRole', { id: isEdit ? role.id : '', name: name.value.trim(), color });
                close(true);
                renderRolesTab(body);
                return;
            } catch (err) { error.textContent = err.message; }
            save.disabled = false;
        }
        return el('form', { onsubmit: submit, style: 'display:contents' },
            el('div', { class: 'modal-head' }, isEdit ? `Rolü düzenle — ${role.name}` : 'Yeni rol'),
            el('div', { class: 'modal-body form' },
                el('label', {}, 'Rol adı'), name,
                el('label', {}, 'Renk'),
                el('div', { class: 'role-swatches' }, swatches, custom),
                preview,
                error),
            el('div', { class: 'modal-foot' },
                el('button', { class: 'btn', type: 'button', onclick: () => close(false) }, 'Vazgeç'),
                save));
    });
}

async function deleteRole(role, body) {
    const accepted = await confirmDialog({
        title: 'Rolü sil',
        message: `"${role.name}" silinsin mi? ${role.members ? `${role.members} kişiden kaldırılır. ` : ''}Bir paketin erişimini belirleyen rol silinemez.`,
        okText: 'Sil', cancelText: 'Vazgeç', danger: true,
    });
    if (!accepted) return;
    try { await bridge.invoke('admin.deleteRole', { id: role.id }); }
    catch (err) { await alertDialog({ title: 'Silinemedi', message: err.message }); }
    renderRolesTab(body);
}
