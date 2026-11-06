// Kayıt: giriş ekranındaki "Kayıt ol" formu. İstek sunucuda bekler; sunucu e-posta doğrulaması kullanıyorsa kişi
// önce postadaki bağlantıyı açar, ardından bir admin onaylayınca hesap açılır.
// Adminler bekleyen istekleri Admin sayfasının "Kayıt istekleri" sekmesinde görür; sayısı Admin sekmesinde rozet olur.

/** Giriş kartı, kayıt formu ve "gönderildi" kartından birini gösterir. */
function showLoginCard(which) {
    $('login-form').hidden = which !== 'login';
    $('register-form').hidden = which !== 'register';
    $('register-done').hidden = which !== 'done';
    const focus = { login: 'login-username', register: 'register-username', done: 'register-back' }[which];
    if (focus) $(focus).focus();
}

/** "Gönderildi" kartının metni: e-posta doğrulaması varsa önce postayı açmasını söyler. */
function showRegisterDone(name, mail, verifyEmail) {
    const text = $('register-done-text');
    const b = value => el('b', {}, value);
    text.dataset.name = name;   // "Giriş ekranına dön" kullanıcı adını buradan doldurur
    if (verifyEmail) {
        text.replaceChildren(b(mail), ' adresine bir doğrulama bağlantısı gönderdik. Kaydını tamamlamak için bağlantıyı aç; ',
            'birkaç dakika içinde gelmezse spam klasörüne de bak.');
    } else {
        text.replaceChildren(b(name), ' için isteğin bize ulaştı.');
    }
}

function setRegisterStatus(text, isError = false) {
    const status = $('register-status');
    status.textContent = text;
    status.classList.toggle('error', isError);
}

function setupRegisterForm() {
    const username = $('register-username'), email = $('register-email');
    const password = $('register-password'), password2 = $('register-password2');

    // Oyun içi ad kuralları: İngilizce harf, rakam ve alt çizgi
    username.addEventListener('input', () => { username.value = username.value.replace(/[^a-zA-Z0-9_]/g, ''); });

    $('show-register').addEventListener('click', () => { setRegisterStatus(''); showLoginCard('register'); });
    $('show-login').addEventListener('click', () => showLoginCard('login'));
    $('register-back').addEventListener('click', () => {
        $('login-username').value = $('register-done-text').dataset.name || '';
        $('login-password').value = '';
        state.useSavedPassword = false;
        setLoginStatus('');
        showLoginCard('login');
        $('login-password').focus();
    });

    $('register-form').addEventListener('submit', async event => {
        event.preventDefault();
        const name = username.value.trim(), mail = email.value.trim();

        if (name.length < 3) { setRegisterStatus('Kullanıcı adı en az 3 karakter olmalı.', true); username.focus(); return; }
        if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(mail)) { setRegisterStatus('Geçerli bir e-posta adresi gir.', true); email.focus(); return; }
        if (password.value.length < 6) { setRegisterStatus('Şifre en az 6 karakter olmalı.', true); password.focus(); return; }
        if (password.value !== password2.value) { setRegisterStatus('Şifreler aynı değil.', true); password2.focus(); return; }

        $('register-submit').disabled = true;
        setRegisterStatus('Gönderiliyor…');
        try {
            const result = await bridge.invoke('auth.register', { username: name, email: mail, password: password.value });
            showRegisterDone(name, mail, !!result?.verifyEmail);
            for (const input of [username, email, password, password2]) input.value = '';
            setRegisterStatus('');
            showLoginCard('done');
        } catch (err) {
            setRegisterStatus(err.message, true);
        } finally {
            $('register-submit').disabled = false;
        }
    });
}

// ── Admin: bekleyen istekler ──────────────────────────────────────────
let pendingRegistrations = [];

/** Bekleyen istekleri alır ve Admin sekmesindeki rozeti günceller. Admin değilse bir şey yapmaz. */
async function refreshRegistrations() {
    if (!state.isAdmin) { pendingRegistrations = []; updateAdminBadge(); return pendingRegistrations; }
    try { pendingRegistrations = (await bridge.invoke('admin.registrations')) || []; }
    catch { /* rozet bir sonraki denemede güncellenir */ }
    updateAdminBadge();
    return pendingRegistrations;
}

function updateAdminBadge() {
    const badge = $('admin-badge');
    badge.textContent = pendingRegistrations.length || '';
    badge.hidden = !pendingRegistrations.length;
}

/** Listeyi pendingRegistrations'tan çizer; çağıran önce refreshRegistrations() ile yeniler. */
function renderRegistrationsTab(body) {
    const items = pendingRegistrations;
    if (!items.length) {
        body.replaceChildren(el('div', { class: 'reg-empty' },
            el('b', {}, 'Bekleyen kayıt isteği yok'),
            el('span', {}, 'Biri giriş ekranından kayıt olduğunda burada görünür.')));
        return;
    }

    const rows = items.map(item => {
        const created = parseDate(item.createdAt);
        // Alanı olmayan (eski sunucu) istekler doğrulanmış sayılır
        const verified = item.emailVerified !== false;
        const row = el('div', { class: 'user-row reg-row' },
            el('div', {},
                el('div', { class: 'uname' }, item.username),
                el('div', { class: 'meta' }, [item.email, !verified && 'e-posta doğrulanmadı', created && 'İstek: ' + timeAgo(created)].filter(Boolean).join(' · '))),
            el('button', {
                class: 'btn btn-sm btn-primary', disabled: !verified,
                title: verified ? '' : 'Bu kişi e-posta adresini doğrulayınca kabul edebilirsin.',
                onclick: () => decide(item, true, row),
            }, 'Kabul et'),
            el('button', { class: 'btn btn-sm btn-danger', onclick: () => decide(item, false, row) }, 'Reddet'));
        return row;
    });
    body.replaceChildren(el('div', { class: 'admin-toolbar' }, el('span', {}, `${items.length} bekleyen istek`)), ...rows);

    async function decide(item, approve, row) {
        if (!approve) {
            const accepted = await confirmDialog({
                title: 'Kayıt isteğini reddet',
                message: `${item.username} (${item.email}) isteği silinsin mi? Bu kişi isterse tekrar kayıt olabilir.`,
                okText: 'Reddet', cancelText: 'Vazgeç', danger: true,
            });
            if (!accepted) return;
        }
        for (const button of row.querySelectorAll('button')) button.disabled = true;
        try {
            await bridge.invoke(approve ? 'admin.approveRegistration' : 'admin.rejectRegistration', { id: item.id, username: item.username });
        } catch (err) {
            await alertDialog({ title: approve ? 'Onaylanamadı' : 'Reddedilemedi', message: err.message });
        }
        await refreshRegistrations();
        renderAdminTabs(); // sayılar ve liste yenilensin
    }
}
