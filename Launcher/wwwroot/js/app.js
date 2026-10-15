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
    for (const button of document.querySelectorAll('.rail-nav button'))
        button.classList.toggle('active', button.dataset.page === name);

    if (name === 'modrinth') openModrinthPage();
    if (name === 'settings') openSettingsPage();
    refreshActivity();
}

/** Uzun süren bir işlemi sarar: bittiğine kadar "meşgul" göstergesi açık kalır. */
async function withActivity(promiseFactory) {
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
    $('btn-admin').addEventListener('click', () => openAdminPanel());
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

/** Giriş ekranını (ilk açılışta ve çıkış yapınca) hazırlar. */
async function showLogin() {
    const username = $('login-username'), password = $('login-password');

    const init = await bridge.invoke('app.init');
    state.ramOptionsGb = init.ramOptionsGb;
    applySettings(init.settings);
    $('app-version').textContent = 'v' + init.version;

    username.value = init.savedUsername || '';
    password.value = '';
    state.useSavedPassword = false;
    $('login-remember').checked = !!init.savedUsername;
    if (init.savedUsername && init.hasSavedPassword) { password.value = SAVED_MASK; state.useSavedPassword = true; }

    showView('view-login');
    (init.savedUsername ? $('login-submit') : username).focus();

    // Sunucu uykudaysa kullanıcı yazarken uyansın
    setLoginStatus('Sunucuya bağlanılıyor...');
    bridge.invoke('app.wake').then(result => {
        if ($('login-submit').disabled) return; // giriş denemesi sürüyor, mesajına dokunma
        setLoginStatus(result.reachable ? '' : 'Sunucuya ulaşılamadı.', !result.reachable);
    });
}

async function tryLogin() {
    const username = $('login-username').value.trim();
    const password = $('login-password').value;

    if (!username || !password) { setLoginStatus('Kullanıcı adı ve şifre boş bırakılamaz.', true); return; }

    $('login-submit').disabled = true;
    setLoginStatus('Giriş yapılıyor, lütfen bekleyin...');

    let result;
    try {
        result = await bridge.invoke('auth.login', {
            username,
            password: state.useSavedPassword ? '' : password,
            useSaved: state.useSavedPassword,
            remember: $('login-remember').checked,
        });
    } catch (err) {
        result = { success: false, error: err.message };
    }

    $('login-submit').disabled = false;
    if (!result.success) { setLoginStatus(result.error || 'Giriş başarısız.', true); return; }

    $('login-password').value = '';
    setLoginStatus('');
    await enterLauncher(result);
}

// ── Giriş sonrası ─────────────────────────────────────────────────────
async function enterLauncher(session) {
    state.username = session.username;
    $('welcome-name').textContent = session.username;
    $('btn-admin').hidden = !session.isAdmin;

    clearConsole();
    showPage('home');
    await reloadInstances(store.get('lastPack', ''));
    if (state.instances.length === 0)
        appendLog({ text: 'Hesabınıza tanımlı bir paket yok. Yeni bir instance oluşturabilirsiniz.', level: 'warn' });

    // Açılış animasyonu oynarken paket güncellemeleri kontrol edilir
    const splash = $('view-splash');
    const skipSplash = !!state.settings.skipSplash;
    if (!skipSplash) {
        splash.classList.remove('fade');
        $('splash-img').src = 'assets/splash.gif?' + Date.now(); // animasyon baştan oynasın
        showView('view-splash');
    }

    const [updates] = await Promise.all([
        bridge.invoke('updates.check').catch(() => []),
        new Promise(resolve => setTimeout(resolve, skipSplash ? 0 : 2600)),
    ]);

    startMusic();
    if (skipSplash) showView('view-main');
    else {
        $('view-login').hidden = true;
        $('view-main').hidden = false;
        splash.classList.add('fade');
        setTimeout(() => { splash.hidden = true; }, 650);
    }

    await offerInstanceUpdates(updates);
    await offerLauncherUpdate();
}

async function offerInstanceUpdates(updates) {
    for (const update of updates) {
        const lines = [`${update.instance} için güncelleme mevcut:`];
        if (update.modsVersion) lines.push(`  •  Mods paketi → ${update.modsVersion}`);
        if (update.fileCount > 0) lines.push(`  •  ${update.fileCount} config/kubejs dosyası (${update.sizeMb} MB)`);
        if (update.deleteCount > 0) lines.push(`  •  ${update.deleteCount} eski dosya silinecek`);
        lines.push('', 'Şimdi güncellensin mi?');

        const accepted = await confirmDialog({ title: 'Güncelleme mevcut', message: lines.join('\n') });
        if (!accepted) { appendLog({ text: `${update.instance} güncellemesi ertelendi.`, level: 'muted' }); continue; }

        try { await withActivity(() => bridge.invoke('updates.apply', { instance: update.instance })); }
        catch (err) { appendLog({ text: err.message, level: 'error' }); }
    }
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
    const accepted = await confirmDialog({
        title: 'Launcher güncellemesi',
        message: `Launcher ${check.version} sürümüne güncellenebilir${size}.\nŞimdi güncellensin mi?`,
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
    const next = state.instances.find(i => i.id === wanted) || state.instances[0] || null;
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

        return el('button', { class: 'pack-card', 'data-id': instance.id, onclick: () => { selectInstance(instance); showPage('home'); } },
            thumb,
            instance.iconUrl && el('img', { class: 'card-icon', src: instance.iconUrl, alt: '' }),
            el('div', { class: 'info' },
                el('span', { class: 'name' }, instance.name),
                el('span', { class: 'ver' }, loaderLabel(instance))));
    };

    const servers = state.instances.filter(i => i.kind === 'server');
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
        el('span', { class: 'tag' }, 'Minecraft ' + instance.mcVersion),
        el('span', { class: 'tag' }, instance.loader === 'vanilla'
            ? 'Vanilla'
            : `${LOADER_NAMES[instance.loader]} ${instance.loaderVersion || ''}`.trim()));

    const isCustom = instance.kind === 'custom';
    $('pack-actions').replaceChildren(
        isCustom && el('button', { class: 'btn btn-sm', onclick: () => openNewInstanceDialog(instance) }, 'Düzenle'),
        isCustom && el('button', { class: 'btn btn-sm', onclick: () => openInstanceContent(instance) }, 'İçerik'),
        isCustom && el('button', { class: 'btn btn-sm', onclick: () => { openModrinthFor(instance); } }, 'Mod ekle'),
        el('button', { class: 'btn btn-sm', onclick: () => bridge.invoke('files.open', { target: 'instance', id: instance.id }) }, 'Klasörü aç'),
        el('button', { class: 'btn btn-sm', onclick: () => openBackgroundDialog(instance) }, 'Arka plan'),
        isCustom && el('button', { class: 'btn btn-sm btn-danger', onclick: () => deleteInstance(instance) }, 'Sil'));

    // Öncelik: bu paket için yapılmış seçim → ayarlardaki varsayılan → paketin önerdiği
    const saved = parseInt(store.get('ram_' + instance.id, ''), 10);
    const fallback = state.settings.defaultRamGb > 0 ? state.settings.defaultRamGb : instance.recommendedRamGb;
    state.ramGb = state.ramOptionsGb.includes(saved) ? saved : fallback;
    renderRamOptions();
    updatePlayButton();
}

function renderRamOptions() {
    const recommended = state.selected ? state.selected.recommendedRamGb : null;
    $('ram-options').replaceChildren(...state.ramOptionsGb.map(gb =>
        el('button', {
            class: (gb === state.ramGb ? 'active ' : '') + (gb === recommended ? 'rec' : ''),
            onclick: () => {
                state.ramGb = gb;
                if (state.selected) store.set('ram_' + state.selected.id, String(gb));
                renderRamOptions();
            },
        }, gb + ' GB')));
}

// ── Oyna ──────────────────────────────────────────────────────────────
let launching = false;

function updatePlayButton() {
    const button = $('btn-play');
    const busy = launching || state.busy > 0;
    button.classList.toggle('busy', busy);

    if (launching) { button.disabled = true; button.textContent = 'Başlatılıyor…'; }
    else if (state.busy > 0) { button.disabled = true; button.textContent = 'İşlem sürüyor…'; }
    else if (state.gameRunning) { button.disabled = true; button.textContent = 'Oyun çalışıyor'; }
    else { button.disabled = !state.selected; button.textContent = 'OYNA'; }
}

async function play() {
    if (!state.selected || state.gameRunning || launching) return;

    clearConsole();
    launching = true;
    updatePlayButton();
    try {
        const result = await bridge.invoke('game.play', { instanceId: state.selected.id, ramGb: state.ramGb });
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
    if (line.level !== 'game' && line.level !== 'gameError') $('activity-text').textContent = line.text;
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
    bridge.on('gameExited', () => { state.gameRunning = false; updatePlayButton(); });

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
    const off = $('music').paused;
    $('music-toggle').classList.toggle('off', off);
    document.querySelector('.music').classList.toggle('paused', off);
}

function setupMusic() {
    const audio = $('music'), slider = $('music-volume');
    let saveTimer = null;

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
    setupTitlebar();
    setupConsole();
    setupMusic();
    setupLoginForm();
    $('btn-play').addEventListener('click', play);
    $('btn-new-instance').addEventListener('click', () => openNewInstanceDialog());
    for (const button of document.querySelectorAll('.rail-nav button'))
        button.addEventListener('click', () => showPage(button.dataset.page));

    // Dosya sürükleyip bırakmak sayfayı değiştirmesin
    window.addEventListener('dragover', event => event.preventDefault());
    window.addEventListener('drop', event => event.preventDefault());

    showLogin().catch(err => setLoginStatus(err.message, true));
});
