// Ana akış: giriş → açılış animasyonu → ana ekran. Sayfa geçişleri, paket seçimi, oyna ve konsol.
const state = {
    instances: [],          // sunucu paketleri (kind: 'server') + kullanıcı instance'ları (kind: 'custom')
    selected: null,
    ramOptionsGb: [4, 8, 12, 16],
    ramGb: 8,
    gameRunning: false,
    busy: 0,                // süren uzun işlem sayısı (kurulum, indirme)
    useSavedPassword: false,
    settings: {},
    username: '',
    page: 'home',
    details: null,          // seçili paketin istatistikleri ve yenilikleri (instances.details)
    activityText: '',       // süren işlemin son log satırı; durum çubuğunda gösterilir
};

const LOADER_NAMES = { vanilla: 'Vanilla', fabric: 'Fabric', quilt: 'Quilt', forge: 'Forge', neoforge: 'NeoForge' };

const store = {
    get(key, fallback) {
        try { const value = localStorage.getItem(key); return value === null ? fallback : value; }
        catch { return fallback; }
    },
    set(key, value) { try { localStorage.setItem(key, value); } catch { /* yok say */ } },
};

function showView(id) {
    for (const view of document.querySelectorAll('.view')) view.hidden = view.id !== id;
}

function showPage(name) {
    state.page = name;
    for (const page of document.querySelectorAll('.page')) page.hidden = page.id !== 'page-' + name;
    for (const button of document.querySelectorAll('#page-tabs button'))
        button.classList.toggle('active', button.dataset.page === name);
    $('btn-user').classList.toggle('active', name === 'profile');

    if (name === 'modrinth') openModrinthPage();
    if (name === 'settings') openSettingsPage();
    if (name === 'admin') openAdminPage();
    if (name === 'packs') openPacksPage();
    refreshActivity();
}

/** Uzun süren bir işlemi sarar: bittiğine kadar "meşgul" göstergesi açık kalır. */
async function withActivity(promiseFactory) {
    if (state.busy === 0 && !launching) state.activityText = '';
    state.busy++;
    refreshActivity();
    updatePlayButton();
    try { return await promiseFactory(); }
    finally {
        state.busy--;
        refreshActivity();
        updatePlayButton();
    }
}

function refreshActivity() {
    // Oyna sayfasında konsol zaten görünür; diğer sayfalarda son satır alt köşede gösterilir
    $('activity').hidden = !(state.busy > 0 && state.page !== 'home');
}

// ── Başlık çubuğu ─────────────────────────────────────────────────────
function setupTitlebar() {
    const titlebar = $('titlebar');
    let dragging = false, framePending = false;

    titlebar.addEventListener('pointerdown', event => {
        if (event.button !== 0 || event.target.closest('button')) return;
        dragging = true;
        titlebar.setPointerCapture(event.pointerId);
        bridge.send('window.dragStart');
    });
    titlebar.addEventListener('pointermove', () => {
        if (!dragging || framePending) return;
        framePending = true;
        requestAnimationFrame(() => { framePending = false; if (dragging) bridge.send('window.dragMove'); });
    });
    const stop = event => { dragging = false; try { titlebar.releasePointerCapture(event.pointerId); } catch { /* yok say */ } };
    titlebar.addEventListener('pointerup', stop);
    titlebar.addEventListener('pointercancel', stop);

    $('btn-min').addEventListener('click', () => bridge.send('window.minimize'));
    $('btn-close').addEventListener('click', () => bridge.send('window.close'));

    $('btn-user').addEventListener('click', () => openProfile(state.username));
}

// ── Açılır menüler (RAM seçimi) ───────────────────────────────────────
const POPOVERS = [['ram-pop', 'btn-ram']];

function setPopover(popoverId, buttonId, open) {
    if (open) closePopovers();
    $(popoverId).hidden = !open;
    $(buttonId).setAttribute('aria-expanded', String(open));
    if (open) $(popoverId).querySelector('[aria-selected="true"], button')?.focus();
}

function closePopovers() {
    for (const [popoverId, buttonId] of POPOVERS) setPopover(popoverId, buttonId, false);
}

function setupPopovers() {
    document.addEventListener('mousedown', event => {
        for (const [popoverId, buttonId] of POPOVERS)
            if (!$(popoverId).hidden && !event.target.closest('#' + popoverId + ', #' + buttonId)) setPopover(popoverId, buttonId, false);
    });
    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape') return;
        for (const [popoverId, buttonId] of POPOVERS)
            if (!$(popoverId).hidden) { setPopover(popoverId, buttonId, false); $(buttonId).focus(); }
    });
}

// ── Giriş ─────────────────────────────────────────────────────────────
const SAVED_MASK = '••••••••••';

function setLoginStatus(text, isError = false) {
    const status = $('login-status');
    status.textContent = text;
    status.classList.toggle('error', isError);
}

function setupLoginForm() {
    const username = $('login-username'), password = $('login-password');

    // Sadece ingilizce harf, sayı ve alt çizgi
    username.addEventListener('input', () => {
        username.value = username.value.replace(/[^a-zA-Z0-9_]/g, '');
        if (state.useSavedPassword) { state.useSavedPassword = false; password.value = ''; }
    });

    // Kayıtlı şifre arayüze gelmez; alan maskeyle dolu görünür, kullanıcı yazmaya başlarsa temizlenir
    password.addEventListener('focus', () => { if (state.useSavedPassword) password.select(); });
    password.addEventListener('input', () => { state.useSavedPassword = false; });

    $('login-form').addEventListener('submit', event => { event.preventDefault(); tryLogin(); });
}

/**
 * Giriş ekranını hazırlar. Açılışta (auto) "Beni hatırla" ile kayıtlı hesap varsa giriş ekranı hiç
 * gösterilmez: perde kapanır, giriş arkada yapılır, sunucuya bağlanana kadar perde kapalı kalır.
 * Çıkış yapınca auto kapalıdır; kullanıcı bilerek çıktığı için tekrar otomatik girilmez.
 */
async function showLogin({ auto = false } = {}) {
    const username = $('login-username'), password = $('login-password');

    const init = await bridge.invoke('app.init');
    state.ramOptionsGb = init.ramOptionsGb;
    applySettings(init.settings);
    $('app-version').textContent = 'v' + init.version;
    state.apiBase = init.apiBaseUrl || '';
    $('btn-user').hidden = true;
    $('page-tabs').hidden = true;
    $('tab-admin').hidden = true;
    closePopovers();

    username.value = init.savedUsername || '';
    password.value = '';
    state.useSavedPassword = false;
    $('login-remember').checked = !!init.savedUsername;
    if (init.savedUsername && init.hasSavedPassword) { password.value = SAVED_MASK; state.useSavedPassword = true; }

    const canAutoLogin = auto && !!init.savedUsername && init.hasSavedPassword;

    if (canAutoLogin && !state.settings.skipSplash) {
        await autoLoginBehindCurtain(init.savedUsername);
        return;
    }

    showView('view-login');
    (init.savedUsername ? $('login-submit') : username).focus();

    // Açılış animasyonu kapalıysa otomatik giriş giriş ekranında denenir
    if (canAutoLogin) { tryLogin(); return; }

    // Sunucu uykudaysa kullanıcı yazarken uyansın
    setLoginStatus('Sunucuya bağlanılıyor...');
    bridge.invoke('app.wake').then(result => {
        if ($('login-submit').disabled) return; // giriş denemesi sürüyor, mesajına dokunma
        setLoginStatus(result.reachable ? '' : 'Sunucuya ulaşılamadı.', !result.reachable);
    });
}

/** Kayıtlı hesapla giriş: perde kapalı ve yazı oynarken yapılır; olmazsa perde giriş ekranına açılır. */
async function autoLoginBehindCurtain(savedUsername) {
    beginSplash(savedUsername);

    // Yazı bittiği halde bağlantı sürüyorsa (sunucu uykudan uyanıyor olabilir) perdede belirtilir
    const slowTimer = setTimeout(() => setSplashStatus('Sunucuya bağlanılıyor…'), CURTAIN_OPEN_AT - 600);
    const result = await requestLogin({ username: savedUsername, password: '', useSaved: true, remember: true });
    clearTimeout(slowTimer);
    setSplashStatus('');

    if (result.success) {
        await enterLauncher(result);
        return;
    }

    // Giriş olmadı (şifre değişmiş, hesap kapatılmış ya da sunucuya ulaşılamadı): giriş ekranına açılır
    setLoginStatus(result.error || 'Giriş başarısız.', true);
    await wait(Math.max(0, CURTAIN_OPEN_AT - (performance.now() - splashStartedAt)));
    await openCurtainsTo('view-login');
    $('login-submit').focus();
}

async function requestLogin(args) {
    try { return await bridge.invoke('auth.login', args); }
    catch (err) { return { success: false, error: err.message }; }
}

async function tryLogin() {
    const username = $('login-username').value.trim();
    const password = $('login-password').value;

    if (!username || !password) { setLoginStatus('Kullanıcı adı ve şifre boş bırakılamaz.', true); return; }

    $('login-submit').disabled = true;
    setLoginStatus('Giriş yapılıyor, lütfen bekleyin...');

    const result = await requestLogin({
        username,
        password: state.useSavedPassword ? '' : password,
        useSaved: state.useSavedPassword,
        remember: $('login-remember').checked,
    });

    $('login-submit').disabled = false;
    if (!result.success) { setLoginStatus(result.error || 'Giriş başarısız.', true); return; }

    $('login-password').value = '';
    setLoginStatus('');
    await enterLauncher(result);
}

// ── Giriş sonrası ─────────────────────────────────────────────────────
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));

// Sahne açılışı zamanlaması (ms): yazı yazılıp parladıktan sonra perdeler açılır, açılış bitince sahne kalkar
const CURTAIN_OPEN_AT = 3600;
const CURTAIN_DONE_AFTER = 1500;

let splashStartedAt = null; // açılış oynuyorsa başladığı an

/** Perdeyi anında kapatır ve yazı animasyonunu baştan başlatır. */
function beginSplash(username) {
    const splash = $('view-splash');
    $('splash-greeting').textContent = `Hoş geldin, ${username}`;
    setSplashStatus('');
    splash.classList.remove('play', 'open');
    splash.hidden = false;
    $('view-login').hidden = true;
    void splash.offsetWidth; // animasyon her seferinde baştan oynasın
    splash.classList.add('play');
    splashStartedAt = performance.now();
}

function setSplashStatus(text) {
    const status = $('splash-status');
    status.textContent = text;
    status.classList.toggle('visible', !!text);
}

/** Verilen ekranı perdenin arkasında açar ve perdeleri çeker. */
async function openCurtainsTo(viewId) {
    const splash = $('view-splash');
    $(viewId).hidden = false;
    splash.classList.add('open');
    await wait(CURTAIN_DONE_AFTER);
    splash.hidden = true;
    splash.classList.remove('play', 'open');
    splashStartedAt = null;
}

async function enterLauncher(session) {
    // Perde (otomatik girişte zaten kapalı) arayüzden önce gelir; arayüz arkada hazırlanır
    const useSplash = !state.settings.skipSplash;
    if (useSplash && splashStartedAt === null) beginSplash(session.username);

    state.username = session.username;
    $('user-name').textContent = session.username;
    setAvatar($('user-avatar'), { username: session.username, avatarVersion: 0 });
    $('btn-user').hidden = false;
    loadMyProfile(); // avatarı arka planda getirir
    state.isAdmin = !!session.isAdmin;
    $('page-tabs').hidden = false;
    $('tab-admin').hidden = !session.isAdmin;
    $('packs-grid').replaceChildren(); // başka hesaptan kalan liste görünmesin

    clearConsole();
    showPage('home');
    await reloadInstances(store.get('lastPack', ''));
    if (!state.instances.some(i => !i.locked))
        appendLog({ text: 'Hesabınıza tanımlı bir paket yok. Yeni bir instance oluşturabilirsiniz.', level: 'warn' });

    if (!useSplash) {
        showView('view-main');
        startMusic();
    } else {
        // Yazı tamamlanınca (ve arayüz hazırsa) perdeler açılır; arkadan launcher görünür
        await wait(Math.max(0, CURTAIN_OPEN_AT - (performance.now() - splashStartedAt)));
        startMusic();
        await openCurtainsTo('view-main');
    }

    await offerLauncherUpdate();
}

async function offerLauncherUpdate() {
    let check;
    try { check = await bridge.invoke('launcher.check'); }
    catch { return; }
    if (check.status !== 'available') return;

    if (!check.diskOk) {
        await alertDialog({
            title: 'Yetersiz disk alanı',
            message: `Launcher güncellemesi için yeterli disk alanı yok.\nGereken: ${check.neededMb} MB, mevcut: ${check.freeMb} MB`,
        });
        return;
    }

    const size = check.sizeMb ? ` (${check.sizeMb} MB)` : '';
    const notes = check.notes && check.notes.trim() ? `\n\nYenilikler:\n${check.notes.trim()}` : '';
    const accepted = await confirmDialog({
        title: 'Launcher güncellemesi',
        message: `Launcher v${check.version} sürümüne güncellenebilir${size}.${notes}\n\nŞimdi güncellensin mi? Launcher kapanıp güncellendikten sonra yeniden açılır.`,
    });
    if (!accepted) { appendLog({ text: 'Launcher güncellemesi ertelendi.', level: 'muted' }); return; }

    try { await bridge.invoke('launcher.update'); }
    catch (err) { appendLog({ text: err.message, level: 'error' }); }
}

// ── Paketler ve instance'lar ──────────────────────────────────────────
/** Listeyi sunucudan yeniden alır; verilen kimlik (yoksa önceki seçim, o da yoksa ilk öğe) seçilir. */
async function reloadInstances(selectId) {
    state.instances = await bridge.invoke('instances.list');
    renderInstanceLists();

    const wanted = selectId || (state.selected && state.selected.id);
    const usable = state.instances.filter(i => !i.locked);
    const next = usable.find(i => i.id === wanted) || usable[0] || null;
    if (next) selectInstance(next);
    else clearSelection();
}

function loaderLabel(instance) {
    const version = instance.mcVersion;
    return instance.loader === 'vanilla' ? version : `${version} · ${LOADER_NAMES[instance.loader] || instance.loader}`;
}

function renderInstanceLists() {
    const card = instance => {
        const thumb = el('div', { class: 'thumb' + (instance.thumbnail ? '' : ' plain') });
        if (instance.thumbnail) thumb.style.backgroundImage = `url("${instance.thumbnail}")`;

        if (instance.locked) {
            return el('button', {
                class: 'pack-card locked', 'data-id': instance.id, disabled: true,
                title: 'Bu paket için yetkin yok', 'aria-label': instance.name + ' (yetki gerekli)',
            },
                thumb,
                el('div', { class: 'info' },
                    el('span', { class: 'name' }, instance.name),
                    el('span', { class: 'ver' }, 'Yetki gerekli')),
                fromMarkup('<svg class="lock" viewBox="0 0 24 24" aria-hidden="true"><rect x="5" y="11" width="14" height="10" rx="2"/><path d="M8 11V7a4 4 0 018 0v4"/></svg>'));
        }

        return el('button', { class: 'pack-card', 'data-id': instance.id, onclick: () => { selectInstance(instance); showPage('home'); } },
            thumb,
            instance.iconUrl && el('img', { class: 'card-icon', src: instance.iconUrl, alt: '' }),
            el('div', { class: 'info' },
                el('span', { class: 'name' }, instance.name),
                el('span', { class: 'ver' }, loaderLabel(instance))));
    };

    // Kilitli paketler listenin sonunda durur
    const servers = state.instances.filter(i => i.kind === 'server').sort((a, b) => a.locked - b.locked);
    const customs = state.instances.filter(i => i.kind === 'custom');

    $('pack-list').replaceChildren(...(servers.length
        ? servers.map(card)
        : [el('p', { class: 'pack-empty' }, 'Hesabınıza tanımlı paket yok.')]));

    $('instance-list').replaceChildren(...(customs.length
        ? customs.map(card)
        : [el('p', { class: 'pack-empty' }, 'Henüz instance yok. "+ Yeni" ile oluştur ya da Modrinth\'ten bir modpack kur.')]));

    markSelectedCard();
}

function markSelectedCard() {
    for (const card of document.querySelectorAll('.pack-card'))
        card.classList.toggle('selected', !!state.selected && card.dataset.id === state.selected.id);
}

function clearSelection() {
    state.selected = null;
    $('pack-logo').hidden = true;
    $('pack-title').hidden = false;
    $('pack-title').textContent = 'vulu Launcher';
    $('pack-desc').textContent = 'Başlamak için soldan bir paket seç ya da yeni bir instance oluştur.';
    $('pack-tags').replaceChildren();
    $('pack-actions').replaceChildren();
    $('stage-bg').className = 'stage-bg plain';
    $('stage-bg').style.backgroundImage = '';
    state.details = null;
    $('pack-stats').hidden = true;
    renderVersionPicker();
    updatePlayButton();
}

function selectInstance(instance) {
    state.selected = instance;
    store.set('lastPack', instance.id);
    markSelectedCard();

    const bg = $('stage-bg');
    bg.classList.toggle('plain', !instance.background);
    bg.classList.toggle('side', !!instance.background && instance.backgroundStyle === 'side');
    bg.style.backgroundImage = instance.background ? `url("${instance.background}")` : '';

    const logo = $('pack-logo'), title = $('pack-title');
    logo.hidden = !instance.logo;
    title.hidden = !!instance.logo;
    if (instance.logo) { logo.src = instance.logo; logo.alt = instance.name; }
    else title.textContent = instance.name;

    $('pack-desc').textContent = instance.description;
    $('pack-tags').replaceChildren(
        el('span', { class: 'tag', id: 'tag-mc' }, 'Minecraft ' + mcVersionOf(instance)),
        el('span', { class: 'tag' }, instance.loader === 'vanilla'
            ? 'Vanilla'
            : `${LOADER_NAMES[instance.loader]} ${instance.loaderVersion || ''}`.trim()));

    const isCustom = instance.kind === 'custom';
    $('pack-actions').replaceChildren(...[
        isCustom && el('button', { class: 'btn btn-sm', onclick: () => openNewInstanceDialog(instance) }, 'Düzenle'),
        isCustom && el('button', { class: 'btn btn-sm', onclick: () => openInstanceContent(instance) }, 'İçerik'),
        isCustom && el('button', { class: 'btn btn-sm', onclick: () => { openModrinthFor(instance); } }, 'Mod ekle'),
        isCustom && state.isAdmin && el('button', { class: 'btn btn-sm btn-accent', onclick: () => openShareDialog(instance) },
            instance.sharedPackId ? 'Paylaşımı güncelle' : 'Paylaş'),
        el('button', { class: 'btn btn-sm', onclick: () => bridge.invoke('files.open', { target: 'instance', id: instance.id }) }, 'Klasörü aç'),
        el('button', { class: 'btn btn-sm', onclick: () => openBackgroundDialog(instance) }, 'Arka plan'),
        isCustom && el('button', { class: 'btn btn-sm btn-danger', onclick: () => deleteInstance(instance) }, 'Sil'),
    ].filter(Boolean));

    // Öncelik: bu paket için yapılmış seçim → ayarlardaki varsayılan → paketin önerdiği
    const saved = parseInt(store.get('ram_' + instance.id, ''), 10);
    const fallback = state.settings.defaultRamGb > 0 ? state.settings.defaultRamGb : instance.recommendedRamGb;
    state.ramGb = state.ramOptionsGb.includes(saved) ? saved : fallback;
    renderRamOptions();

    renderVersionPicker();
    state.details = null;
    renderDetails();
    loadDetails();
    updatePlayButton();
}

// ── Vanilla sürüm seçimi ──────────────────────────────────────────────
/** Sunucunun Vanilla paketinde Minecraft sürümü seçilebilir; modlu paketlerin sürümü sabittir. */
function canPickVersion(instance) {
    return !!instance && instance.kind === 'server' && instance.loader === 'vanilla';
}

function mcVersionOf(instance) {
    if (!canPickVersion(instance)) return instance.mcVersion;
    return store.get('mcver_' + instance.id, '') || instance.mcVersion;
}

async function renderVersionPicker() {
    const instance = state.selected;
    const select = $('version-select'), label = $('dock-version');
    const pickable = canPickVersion(instance);

    select.hidden = !pickable;
    label.hidden = pickable;
    if (!pickable) { label.textContent = instance ? loaderLabel(instance) : '—'; return; }

    const current = mcVersionOf(instance);
    select.replaceChildren(el('option', { value: current }, current));
    select.value = current;

    if (!state.mcVersions) {
        try { state.mcVersions = await bridge.invoke('game.versions'); }
        catch { return; } // liste alınamazsa yalnızca mevcut sürüm seçili kalır
    }
    if (state.selected !== instance) return;

    const versions = state.mcVersions.includes(current) ? state.mcVersions : [current, ...state.mcVersions];
    select.replaceChildren(...versions.map(v => el('option', { value: v }, v)));
    select.value = current;
}

function setupVersionPicker() {
    $('version-select').addEventListener('change', event => {
        const instance = state.selected;
        if (!canPickVersion(instance)) return;
        store.set('mcver_' + instance.id, event.target.value);
        const tag = $('tag-mc');
        if (tag) tag.textContent = 'Minecraft ' + event.target.value;
    });
}

// ── Paket ayrıntıları: istatistikler ve yenilikler ────────────────────
async function loadDetails() {
    const instance = state.selected;
    if (!instance) return;
    let details;
    try { details = await bridge.invoke('instances.details', { id: instance.id }); }
    catch { details = { failed: true }; }
    if (state.selected !== instance) return; // bu arada başka paket seçildi
    state.details = details;
    renderDetails();
    refreshStatus();
}

function formatSize(mb) {
    if (!(mb > 0)) return '0 MB';
    if (mb < 1) return Math.max(1, Math.round(mb * 1024)) + ' KB';
    if (mb < 10) return mb.toFixed(1).replace('.', ',') + ' MB';
    return mb >= 1024 ? (mb / 1024).toFixed(1).replace('.', ',') + ' GB' : Math.round(mb) + ' MB';
}

function parseDate(value) {
    if (!value) return null;
    const date = new Date(value);
    return isNaN(date) ? null : date;
}

function formatDate(date) {
    return date.toLocaleDateString('tr-TR', { day: 'numeric', month: 'short', year: 'numeric' });
}

function relativeDate(date) {
    const startOfDay = d => new Date(d.getFullYear(), d.getMonth(), d.getDate());
    const days = Math.round((startOfDay(new Date()) - startOfDay(date)) / 86400000);
    if (days <= 0) return 'Bugün';
    if (days === 1) return 'Dün';
    if (days < 30) return days + ' gün önce';
    return formatDate(date);
}

function renderDetails() {
    const instance = state.selected, details = state.details;
    const loading = !details;
    const installed = details && details.installed;

    $('pack-stats').hidden = !instance;
    const setStat = (id, text, title) => {
        const node = $(id);
        node.textContent = loading ? '…' : text;
        node.classList.toggle('loading', loading);
        node.title = title || '';
    };

    if (details && details.failed) {
        setStat('stat-mods', '—'); setStat('stat-size', '—'); setStat('stat-updated', '—');
    } else {
        const updated = parseDate(details && details.updatedAt);
        setStat('stat-mods', installed ? String(details.modCount) : '—');
        setStat('stat-size', installed ? formatSize(details.sizeMb) : 'Kurulmadı');
        setStat('stat-updated', updated ? relativeDate(updated) : '—', updated ? formatDate(updated) : '');
    }
}

function renderRamOptions() {
    const recommended = state.selected ? state.selected.recommendedRamGb : null;
    $('ram-label').textContent = state.ramGb + ' GB';
    $('ram-options').replaceChildren(...state.ramOptionsGb.map(gb =>
        el('button', {
            role: 'option',
            'aria-selected': String(gb === state.ramGb),
            onclick: () => {
                state.ramGb = gb;
                if (state.selected) store.set('ram_' + state.selected.id, String(gb));
                renderRamOptions();
                setPopover('ram-pop', 'btn-ram', false);
                $('btn-ram').focus();
            },
        },
            el('span', {}, gb + ' GB'),
            gb === recommended && el('span', { class: 'note' }, 'önerilen'))));
}

// ── Oyna ──────────────────────────────────────────────────────────────
let launching = false;

function updatePlayButton() {
    const button = $('btn-play'), label = $('play-label');
    const busy = launching || state.busy > 0;
    button.classList.toggle('busy', busy);
    $('version-select').disabled = busy || state.gameRunning;

    if (launching) { button.disabled = true; label.textContent = 'Başlatılıyor…'; }
    else if (state.busy > 0) { button.disabled = true; label.textContent = 'İşlem sürüyor…'; }
    else if (state.gameRunning) { button.disabled = true; label.textContent = 'Oyun çalışıyor'; }
    else { button.disabled = !state.selected; label.textContent = 'OYNA'; }
    refreshStatus();
}

/** Log satırındaki ilerlemeyi yüzdeye çevirir: "%45", "45%" ya da "12/120". Bulunamazsa null. */
function progressOf(text) {
    let match = /%\s?(\d{1,3})|(\d{1,3})\s?%/.exec(text);
    if (match) return Math.min(100, Number(match[1] || match[2]));
    match = /(\d+)\s?\/\s?(\d+)/.exec(text);
    if (match && Number(match[2]) > 0) return Math.min(100, Math.round(Number(match[1]) * 100 / Number(match[2])));
    return null;
}

/** Alt çubuktaki durum metni ve ilerleme çubuğu. */
function refreshStatus() {
    const bar = $('status-bar').parentElement;
    let text, pct = 0, mode = '';

    if (launching || state.busy > 0) {
        text = state.activityText || (launching ? 'Başlatılıyor…' : 'İşlem sürüyor…');
        pct = progressOf(text);
        if (pct === null) mode = 'indeterminate';
    } else if (state.gameRunning) {
        text = 'Oyun çalışıyor'; pct = 100; mode = 'running';
    } else if (!state.selected) {
        text = 'Başlamak için bir paket seç';
    } else if (!state.details) {
        text = 'Paket bilgileri alınıyor…'; mode = 'indeterminate';
    } else if (state.details.installed === false) {
        text = 'İlk açılışta oyun dosyaları indirilecek';
    } else {
        text = 'Hazır'; pct = 100;
    }

    $('status-text').textContent = text;
    $('status-text').title = text;
    $('status-pct').textContent = mode === 'indeterminate' ? '' : pct + '%';
    $('status-bar').style.width = (pct || 0) + '%';
    bar.className = 'bar' + (mode ? ' ' + mode : '');
}

async function play() {
    if (!state.selected || state.gameRunning || launching) return;

    clearConsole();
    state.activityText = '';
    launching = true;
    updatePlayButton();
    try {
        const result = await bridge.invoke('game.play', {
            instanceId: state.selected.id,
            ramGb: state.ramGb,
            mcVersion: canPickVersion(state.selected) ? mcVersionOf(state.selected) : '',
        });
        state.gameRunning = result.started;
    } catch (err) {
        appendLog({ text: err.message, level: 'error' });
    }
    launching = false;
    updatePlayButton();
}

// ── Konsol ────────────────────────────────────────────────────────────
const MAX_LOG_LINES = 1500;
let lastLineWasProgress = false;

function appendLog(line) {
    const body = $('console-body');
    const stickToBottom = body.scrollHeight - body.scrollTop - body.clientHeight < 40;
    const time = line.time || new Date().toTimeString().slice(0, 8);

    // İlerleme satırları (indirme yüzdesi vb.) art arda gelirse aynı satırı günceller
    let node = line.replace && lastLineWasProgress ? body.lastElementChild : null;
    if (!node) {
        node = el('div', {});
        body.append(node);
        while (body.childElementCount > MAX_LOG_LINES) body.firstElementChild.remove();
    }
    node.className = 'log-line ' + (line.level || 'info');
    node.replaceChildren(el('span', { class: 't' }, time), line.text);
    lastLineWasProgress = !!line.replace;

    if (stickToBottom) body.scrollTop = body.scrollHeight;

    // Oyun çıktısı dışındaki son satır, diğer sayfalardaki işlem göstergesinde de görünür
    // ve ana ekrandaki durum çubuğunu sürer
    if (line.level !== 'game' && line.level !== 'gameError') {
        $('activity-text').textContent = line.text;
        if (launching || state.busy > 0) { state.activityText = line.text; refreshStatus(); }
    }
}

function clearConsole() {
    $('console-body').replaceChildren();
    lastLineWasProgress = false;
}

function setConsoleExpanded(expanded) {
    $('console').classList.toggle('expanded', expanded);
    $('console-toggle').textContent = expanded ? 'Küçült' : 'Büyüt';
}

function setupConsole() {
    bridge.on('log', lines => lines.forEach(appendLog));
    bridge.on('gameExited', () => { state.gameRunning = false; updatePlayButton(); loadDetails(); });

    $('console-clear').addEventListener('click', clearConsole);
    $('console-toggle').addEventListener('click', () => setConsoleExpanded(!$('console').classList.contains('expanded')));
}

// ── Ayarların arayüze uygulanması ─────────────────────────────────────
function applySettings(settings) {
    state.settings = settings;

    document.documentElement.style.setProperty('--accent', settings.accentColor || '#c27e3e');
    setConsoleExpanded(!!settings.consoleExpanded);

    const audio = $('music');
    $('music-volume').value = settings.musicVolume;
    audio.volume = settings.musicVolume / 100;
    if (!settings.musicEnabled && !audio.paused) audio.pause();
    refreshMusicWidget();
}

/** Ayarlarda tek bir alanı değiştirir ve kaydeder. */
async function saveSettings(patch) {
    const result = await bridge.invoke('settings.save', { ...state.settings, ...patch });
    applySettings(result.settings);
    return result.settings;
}

// ── Müzik ─────────────────────────────────────────────────────────────
function refreshMusicWidget() {
    const audio = $('music'), off = audio.paused;
    document.querySelector('.music').classList.toggle('paused', off);
    $('music-toggle').setAttribute('aria-label', off ? 'Müziği çal' : 'Müziği duraklat');
    $('music-toggle').title = off ? 'Çal' : 'Duraklat';
    $('music-mute').classList.toggle('muted', audio.muted);
    $('music-mute').setAttribute('aria-label', audio.muted ? 'Sesi aç' : 'Sesi kapat');
    $('music-mute').title = audio.muted ? 'Sesi aç' : 'Sesi kapat';
}

function refreshMusicProgress() {
    const audio = $('music'), seek = $('music-seek');
    const value = audio.duration > 0 ? Math.round(audio.currentTime / audio.duration * 1000) : 0;
    if (!seek.matches(':active')) seek.value = value; // sürüklerken zıplamasın
    seek.style.setProperty('--pct', seek.value / 10 + '%');
}

function setupMusic() {
    const audio = $('music'), slider = $('music-volume'), seek = $('music-seek');
    let saveTimer = null;

    audio.addEventListener('timeupdate', refreshMusicProgress);
    audio.addEventListener('loadedmetadata', refreshMusicProgress);
    audio.addEventListener('volumechange', refreshMusicWidget);
    seek.addEventListener('input', () => {
        if (audio.duration > 0) audio.currentTime = seek.value / 1000 * audio.duration;
        seek.style.setProperty('--pct', seek.value / 10 + '%');
    });
    $('music-restart').addEventListener('click', () => { audio.currentTime = 0; refreshMusicProgress(); });
    $('music-mute').addEventListener('click', () => { audio.muted = !audio.muted; });

    slider.addEventListener('input', () => {
        audio.volume = slider.value / 100;
        state.settings.musicVolume = Number(slider.value);
        clearTimeout(saveTimer);
        saveTimer = setTimeout(() => saveSettings({ musicVolume: Number(slider.value) }).catch(() => { }), 400);
    });
    $('music-toggle').addEventListener('click', () => {
        const enable = audio.paused;
        if (enable) audio.play().catch(() => { });
        else audio.pause();
        saveSettings({ musicEnabled: enable }).catch(() => { });
    });
    audio.addEventListener('play', refreshMusicWidget);
    audio.addEventListener('pause', refreshMusicWidget);
}

function startMusic() {
    if (!state.settings.musicEnabled) return;
    $('music').play().catch(() => { /* otomatik oynatma engellendiyse kullanıcı düğmeyle açar */ });
}

// ── Başlat ────────────────────────────────────────────────────────────
window.addEventListener('DOMContentLoaded', () => {
    // Açılıştaki el yazısı fontu giriş ekranındayken hazır olsun
    document.fonts.load('92px "Great Vibes"').catch(() => { });

    setupTitlebar();
    setupPopovers();
    setupVersionPicker();
    setupPacksPage();
    setupProfilePage();
    setupConsole();
    setupMusic();
    setupLoginForm();
    $('btn-play').addEventListener('click', play);
    $('btn-ram').addEventListener('click', () => setPopover('ram-pop', 'btn-ram', $('ram-pop').hidden));
    $('btn-new-instance').addEventListener('click', () => openNewInstanceDialog());
    for (const button of document.querySelectorAll('#page-tabs button'))
        button.addEventListener('click', () => showPage(button.dataset.page));

    // Dosya sürükleyip bırakmak sayfayı değiştirmesin
    window.addEventListener('dragover', event => event.preventDefault());
    window.addEventListener('drop', event => event.preventDefault());

    showLogin({ auto: true }).catch(err => { showView('view-login'); setLoginStatus(err.message, true); });
});
