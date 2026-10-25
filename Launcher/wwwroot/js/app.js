// Ana akış: giriş → açılış animasyonu → ana ekran. Sayfa geçişleri, paket seçimi, oyna ve konsol.
const state = {
    instances: [],          // hazır paketler (kind: 'server', yalnızca Vanilla) + kullanıcı instance'ları (kind: 'custom')
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
    packUpdates: {},        // güncellemesi olan kurulu vulu pack'ler: instanceId → { packId, name, from, to }
};

const LOADER_NAMES = { vanilla: 'Vanilla', fabric: 'Fabric', quilt: 'Quilt', forge: 'Forge', neoforge: 'NeoForge' };

const store = {
    get(key, fallback) {
        try { const value = localStorage.getItem(key); return value === null ? fallback : value; }
        catch { return fallback; }
    },
    set(key, value) { try { localStorage.setItem(key, value); } catch { /* yok say */ } },
};

/** Telefon yerleşimi (css/mobile.css ile aynı sınır): alt sekme çubuğu, tek sütun, liste → ayrıntı gezinmesi. */
const MOBILE_QUERY = matchMedia('(max-width: 900px)');
const isMobile = () => MOBILE_QUERY.matches;

function showView(id) {
    // Açılış ekranını yalnızca beginSplash/hideSplash yönetir; sönerken burada kesilmesin
    for (const view of document.querySelectorAll('.view'))
        if (view.id !== 'view-splash') view.hidden = view.id !== id;
}

// Keşfet sekmesi iki sayfayı (vulu Packs, Modrinth) kapsar; son açılan hatırlanır
const DISCOVER_PAGES = ['packs', 'modrinth'];
// Sosyal sekmesi: ekran görüntüleri, mesajlar, arkadaşlar
const SOCIAL_PAGES = ['shots', 'messages', 'friends'];

function showPage(name) {
    // Keşfet: vulu Packs, CurseForge ya da Modrinth (son açılan hatırlanır). CurseForge ve Modrinth aynı sayfayı kullanır.
    if (name === 'discover') name = store.get('discoverSource', 'packs');
    if (name === 'curseforge' || name === 'modrinth') { setContentSource(name); store.set('discoverSource', name); name = 'modrinth'; }
    else if (name === 'packs') store.set('discoverSource', 'packs');
    if (name === 'social') { name = store.get('socialSource', 'shots'); if (!SOCIAL_PAGES.includes(name)) name = 'shots'; }
    if (SOCIAL_PAGES.includes(name)) store.set('socialSource', name);

    state.page = name;
    for (const page of document.querySelectorAll('.page')) page.hidden = page.id !== 'page-' + name;
    for (const button of document.querySelectorAll('#page-tabs button'))
        button.classList.toggle('active', button.dataset.page === name || (button.dataset.page === 'discover' && DISCOVER_PAGES.includes(name))
            || (button.dataset.page === 'social' && SOCIAL_PAGES.includes(name)));
    $('btn-user').classList.toggle('active', name === 'profile');

    if (name === 'modrinth') openModrinthPage();
    if (name === 'settings') openSettingsPage();
    if (name === 'admin') openAdminPage();
    if (name === 'packs') openPacksPage();
    if (name === 'servers') openServersPage();
    if (name === 'messages') openMessagesPage();
    if (name === 'shots') openShotsPage();
    if (name === 'friends') openFriendsPage();
    if (name === 'profile') refreshPresence();
    schedulePresence();
    refreshActivity();
    reportPresence();
}

/** Discord durumu için bulunulan sayfa ve Oyna'da seçili instance. */
function reportPresence() {
    const selected = state.page === 'home' && state.selected;
    bridge.invoke('discord.page', {
        page: state.page === 'modrinth' && modrinth.source === 'curseforge' ? 'curseforge' : state.page || 'home',
        instance: selected ? selected.name : '',
        mcVersion: selected ? selected.mcVersion : '',
        loader: selected ? selected.loader : '',
    }).catch(() => {});
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
    // Oyna sayfasında konsol zaten görünür; diğer sayfalarda son satır alt köşede gösterilir.
    // İndirme paneli açıksa o ayrıntılı halini gösterdiği için bu satır gizlenir.
    $('activity').hidden = !(state.busy > 0 && state.page !== 'home') || !!downloads.job;
    renderDownloads();
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
    $('btn-close').addEventListener('click', () => {
        // Bildirim alanına küçülürse pencere yeniden kurulduğunda aynı sayfaya dönülür, müzik çalıyorsa sürer
        store.set('resumePage', state.page);
        store.set('resumeMusic', radioPlaying());
        bridge.send('window.close');
    });

    // Kurulum izni verilip uygulamaya dönülünce bekleyen güncelleme yeniden denenir (APK yeniden inmez)
    bridge.on('appResumed', () => {
        if (!androidUpdateRetry) return;
        const check = androidUpdateRetry;
        androidUpdateRetry = null;
        installAndroidUpdate({ ...check, metered: false });
    });

    // Android geri tuşu: açık pencere ya da menü varsa onu kapatır (Esc gibi); açık sohbetten listeye, diğer
    // sayfalardan Oyna'ya döner; Oyna'daysa uygulama arka plana gider
    bridge.on('backPressed', () => {
        const open = $('modal-root').lastElementChild
            || POPOVERS.some(([popoverId]) => !$(popoverId).hidden)
            || ($('pack-menu') && !$('pack-menu').hidden);
        if (open) document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        else if (isMobile() && state.page === 'messages' && dm.active) closeConversation();
        else if (!$('view-main').hidden && state.page !== 'home') showPage('home');
        else bridge.send('window.minimize');
    });

    $('btn-user').addEventListener('click', () => openProfile(state.username));
    $('btn-feedback').addEventListener('click', () => openFeedback('launcher', state.isAdmin));
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
    state.canLaunchGame = init.canLaunchGame !== false;   // Android'de oyun henüz açılmıyor
    $('btn-user').hidden = true;
    $('btn-feedback').hidden = true;
    stopFriendRequests();
    $('page-tabs').hidden = true;
    $('tab-admin').hidden = true;
    showLoginCard('login');
    closePopovers();

    // Pencere bildirim alanından geri açıldı: oturum C# tarafında açık, giriş ekranı ve perde atlanır
    if (init.session) {
        state.gameRunning = !!init.gameRunning;
        hideSplash();
        await enterLauncher(init.session, { resume: true });
        return;
    }

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

    hideSplash();
    showView('view-login');
    (init.savedUsername ? $('login-submit') : username).focus();

    // Açılış animasyonu kapalıysa otomatik giriş giriş ekranında denenir
    if (canAutoLogin) { tryLogin(); return; }

    // Kullanıcı yazarken sunucuya ulaşılıp ulaşılamadığı denetlenir
    setLoginStatus('Sunucuya bağlanılıyor...');
    bridge.invoke('app.wake').then(result => {
        if ($('login-submit').disabled) return; // giriş denemesi sürüyor, mesajına dokunma
        setLoginStatus(result.reachable ? '' : 'Sunucuya ulaşılamadı.', !result.reachable);
    });
}

/** Kayıtlı hesapla giriş: perde kapalı ve yazı oynarken yapılır; olmazsa perde giriş ekranına açılır. */
async function autoLoginBehindCurtain(savedUsername) {
    beginSplash(savedUsername);

    // Yazı bittiği halde bağlantı sürüyorsa (bağlantı yavaş olabilir) perdede belirtilir
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

/** İstek birkaç saniyeyi geçerse kullanıcı beklediğini bilsin (yavaş mobil bağlantı vb.). */
const WAKING_TEXT = 'Sunucuya bağlanılıyor… Bağlantı yavaşsa biraz sürebilir.';

async function requestLogin(args) {
    const slow = setTimeout(() => {
        if (!$('view-login').hidden) setLoginStatus(WAKING_TEXT);
        else $('splash-status').textContent = WAKING_TEXT;
    }, 4000);
    try { return await bridge.invoke('auth.login', args); }
    catch (err) { return { success: false, error: err.message }; }
    finally { clearTimeout(slow); if ($('splash-status').textContent === WAKING_TEXT) $('splash-status').textContent = ''; }
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
const CURTAIN_OPEN_AT = 2400;
const CURTAIN_DONE_AFTER = 1100;

let splashStartedAt = null; // açılış oynuyorsa başladığı an

/** Perdeyi anında kapatır ve yazı animasyonunu baştan başlatır. */
function beginSplash(username) {
    const splash = $('view-splash');
    clearTimeout(splashHideTimer);
    splash.classList.remove('closing');
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

let splashHideTimer = 0;
const SPLASH_FADE_MS = 450; // #view-splash.closing geçişiyle aynı

/** Açılış ekranını kaldırır: görünüyorsa yavaşça söner, arkasındaki ekran (launcher ya da giriş) belirir. */
function hideSplash() {
    const splash = $('view-splash');
    clearTimeout(splashHideTimer);
    const finish = () => {
        splash.hidden = true;
        splash.classList.remove('play', 'open', 'closing');
        splashStartedAt = null;
    };
    if (splash.hidden) { finish(); return; }
    splash.classList.add('closing');
    splashHideTimer = setTimeout(finish, SPLASH_FADE_MS);
}

/** İki kare bekler: yeni görünen ekran perde kapalıyken çizilsin (pencere arka plandaysa takılmaz). */
const nextPaint = () => Promise.race([
    new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))),
    wait(150),
]);

/**
 * Verilen ekranı perdenin arkasında açar, çizilmesini bekler ve perdeleri çeker. Sahne, perdeler
 * gerçekten açıldıktan sonra kalkar; tarayıcı meşgul olup geçiş geç başlasa bile yarıda kesilmez.
 */
async function openCurtainsTo(viewId) {
    const splash = $('view-splash');
    const curtain = splash.querySelector('.curtain-left');
    $(viewId).hidden = false;
    await nextPaint();

    const opened = new Promise(resolve => {
        const timer = setTimeout(resolve, CURTAIN_DONE_AFTER + 1500); // geçiş olayı gelmezse
        curtain.addEventListener('transitionend', function onEnd(event) {
            if (event.target !== curtain) return;
            curtain.removeEventListener('transitionend', onEnd);
            clearTimeout(timer);
            resolve();
        });
    });
    splash.classList.add('open');
    await opened;
    hideSplash();
}

async function enterLauncher(session, { resume = false } = {}) {
    // Perde (otomatik girişte zaten kapalı) arayüzden önce gelir; arayüz arkada hazırlanır
    const useSplash = !resume && !state.settings.skipSplash;
    if (useSplash && splashStartedAt === null) beginSplash(session.username);

    state.username = session.username;
    $('user-name').textContent = session.username;
    setAvatar($('user-avatar'), { username: session.username, avatarVersion: 0 });
    $('btn-user').hidden = false;
    loadMyProfile(); // avatarı arka planda getirir
    state.isAdmin = !!session.isAdmin;
    $('page-tabs').hidden = false;
    $('tab-admin').hidden = !session.isAdmin;
    $('btn-feedback').hidden = false;
    refreshFeedbackBadge();   // adminlere açık launcher bildirimleri
    startFriendRequests();    // bekleyen arkadaşlık istekleri Sosyal sekmesinde rozet olur
    refreshRegistrations(); // bekleyen kayıt istekleri Admin sekmesinde rozet olarak görünür
    $('packs-grid').replaceChildren(); // başka hesaptan kalan liste görünmesin

    clearConsole();
    showPage('home');
    state.packUpdates = {};
    await reloadInstances(store.get('lastPack', ''));
    refreshPackUpdates();
    refreshAnnouncement();

    if (resume) {
        const page = store.get('resumePage', 'home');
        if (page && page !== 'home' && document.getElementById('page-' + page)) showPage(page);
        showView('view-main');
        if (store.get('resumeMusic', false)) startMusic();
        updatePlayButton();
    } else if (!useSplash) {
        showView('view-main');
        startMusic();
    } else {
        // Ana ekran perdenin arkasında şimdiden çizilir; yazı tamamlanınca perdeler açılır
        $('view-main').hidden = false;
        await wait(Math.max(0, CURTAIN_OPEN_AT - (performance.now() - splashStartedAt)));
        startMusic();
        await openCurtainsTo('view-main');
    }

    await runStartupAction();   // masaüstü kısayolundan açıldıysa
    await onNotificationClicked();   // kapalıyken ya da giriş ekranındayken bildirime dokunulduysa
    await offerLauncherUpdate();
}

async function offerLauncherUpdate() {
    let check;
    try { check = await bridge.invoke('launcher.check'); }
    catch { return; }
    if (check.status !== 'available') return;
    if (check.external) { await installAndroidUpdate(check); return; }

    if (!check.diskOk) {
        await alertDialog({
            title: 'Yetersiz disk alanı',
            message: `Launcher güncellemesi için yeterli disk alanı yok.\nGereken: ${check.neededMb} MB, mevcut: ${check.freeMb} MB`,
        });
        return;
    }

    const size = check.sizeMb ? ` (${check.sizeMb} MB)` : '';
    const notes = check.notes && check.notes.trim() ? `\n\nYenilikler:\n${check.notes.trim()}` : '';
    // Android: yeni APK tarayıcıda iner, kurulumu Android yapar
    const how = check.external
        ? 'İndirilsin mi? Dosya inince açıp "Güncelle"ye dokun.'
        : 'Şimdi güncellensin mi? Launcher kapanıp güncellendikten sonra yeniden açılır.';
    const accepted = await confirmDialog({
        title: 'Launcher güncellemesi',
        message: `Launcher v${check.version} sürümüne güncellenebilir${size}.${notes}\n\n${how}`,
    });
    if (!accepted) { appendLog({ text: 'Launcher güncellemesi ertelendi.', level: 'muted' }); return; }

    try { await bridge.invoke('launcher.update'); }
    catch (err) { appendLog({ text: err.message, level: 'error' }); }
}

// ── Android: kendi kendini güncelleme ─────────────────────────────────
let androidUpdateRetry = null;   // kurulum izni verilip uygulamaya dönülünce yeniden denenecek güncelleme

/**
 * Yeni APK Wi-Fi'de sormadan, mobil veride sorarak indirilir ve Android'in yükleyicisine verilir; kurulunca launcher
 * kapanıp yeni sürüm açılır. İlk seferde Android "bilinmeyen uygulamaları yükle" iznini ve kurulum onayını ister.
 */
async function installAndroidUpdate(check) {
    if (check.metered) {
        const size = check.sizeMb ? ` (${check.sizeMb} MB)` : '';
        const notes = check.notes && check.notes.trim() ? `

Yenilikler:
${check.notes.trim()}` : '';
        const accepted = await confirmDialog({
            title: 'Launcher güncellemesi',
            message: `Launcher v${check.version} çıktı${size}. Mobil veridesin; şimdi indirilsin mi? Wi-Fi'deyken sormadan indirilir.${notes}`,
            okText: 'İndir', cancelText: 'Sonra',
        });
        if (!accepted) { appendLog({ text: 'Launcher güncellemesi ertelendi.', level: 'muted' }); return; }
    }

    let result;
    try { result = await withActivity(() => bridge.invoke('launcher.update')); }
    catch (err) { appendLog({ text: 'Güncelleme indirilemedi: ' + err.message, level: 'error' }); return; }
    if (!result || !result.needsPermission) return;

    const accepted = await confirmDialog({
        title: 'Güncelleme için izin gerekli',
        message: `Launcher v${check.version} indirildi. Android, uygulamanın kendini güncelleyebilmesi için bir kez izin istiyor.

Açılan ayarda "Bu kaynaktan izin ver"i açıp geri dön; kurulum kendiliğinden başlar.`,
        okText: 'Ayarları aç', cancelText: 'Sonra',
    });
    if (!accepted) { appendLog({ text: 'Launcher güncellemesi ertelendi.', level: 'muted' }); return; }
    androidUpdateRetry = check;
    bridge.invoke('launcher.allowInstalls').catch(() => { });
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
        // Arka planı olmayan modpack'te kartın resmi paketin simgesi olur (sağdaki küçük simgeye o zaman gerek kalmaz)
        const picture = instance.thumbnail || instance.iconUrl;
        const thumb = el('div', { class: 'thumb' + (picture ? '' : ' plain') });
        if (picture) thumb.style.backgroundImage = `url("${picture}")`;

        return el('button', { class: 'pack-card', 'data-id': instance.id, onclick: () => { selectInstance(instance); showPage('home'); } },
            thumb,
            instance.iconUrl && instance.thumbnail && el('img', { class: 'card-icon', src: instance.iconUrl, alt: '' }),
            state.packUpdates && state.packUpdates[instance.id] && el('span', { class: 'card-update', title: 'Paketin yeni sürümü var' }),
            el('div', { class: 'info' },
                el('span', { class: 'name' }, instance.name),
                el('span', { class: 'ver' }, loaderLabel(instance))));
    };

    // Vanilla en üstte tek başına durur; altında kullanıcının instance'ları
    const servers = state.instances.filter(i => i.kind === 'server');
    const customs = state.instances.filter(i => i.kind === 'custom');

    $('pack-list').replaceChildren(...servers.map(card));

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
    $('stage-glow').hidden = true;
    state.details = null;
    $('pack-stats').hidden = true;
    renderVersionPicker();
    updatePlayButton();
}

function selectInstance(instance) {
    state.selected = instance;
    reportPresence();
    store.set('lastPack', instance.id);
    markSelectedCard();

    const bg = $('stage-bg');
    // Arka planı yoksa (ör. ekran görüntüsü olmayan modpack) paketin simgesi bulanık bir zemin olur
    const glowImage = !instance.background && (instance.iconUrl || instance.logo);
    bg.classList.toggle('plain', !instance.background && !glowImage);
    bg.classList.toggle('glow', !!glowImage);
    bg.classList.toggle('side', !!instance.background && instance.backgroundStyle === 'side');
    bg.style.backgroundImage = instance.background ? `url("${instance.background}")` : '';
    const glow = $('stage-glow');
    glow.hidden = !glowImage;
    if (glowImage) glow.style.setProperty('--art', `url("${glowImage}")`);

    const logo = $('pack-logo'), title = $('pack-title');
    logo.hidden = !instance.logo;
    title.hidden = !!instance.logo;
    if (instance.logo) { logo.src = instance.logo; logo.alt = instance.name; }
    else title.textContent = instance.name;

    // Varsayılan "Kişisel instance" açıklaması bilgi taşımaz; yalnızca gerçek açıklamalar gösterilir
    const desc = instance.description && instance.description !== 'Kişisel instance' ? instance.description : '';
    $('pack-desc').textContent = desc;
    $('pack-desc').hidden = !desc;
    $('pack-tags').replaceChildren(
        el('span', { class: 'tag', id: 'tag-mc' }, 'Minecraft ' + mcVersionOf(instance)),
        el('span', { class: 'tag' }, instance.loader === 'vanilla'
            ? 'Vanilla'
            : `${LOADER_NAMES[instance.loader]} ${instance.loaderVersion || ''}`.trim()));

    renderPackActions(instance);

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

/**
 * Seçili paketin işlemleri: sık kullanılanlar düğme olarak görünür, diğerleri "⋯" menüsünde toplanır.
 */
function renderPackActions(instance) {
    const isCustom = instance.kind === 'custom';
    const item = (label, onclick, cls = '') => el('button', {
        type: 'button', role: 'menuitem', class: 'more-item ' + cls,
        onclick: () => { closePackMenu(); onclick(); },
    }, label);

    const menuItems = [
        isCustom && item('Düzenle', () => openNewInstanceDialog(instance)),
        item('Görünüm', () => openAppearanceDialog(instance)),
        item('Dünyalar ve yedekler', () => openWorldsDialog(instance)),
        item('Ekran görüntüleri', () => openScreenshots(instance)),
        canInstallGames() && item('Dosyaları doğrula', () => verifyInstance(instance)),
        isDesktopApp() && item('Klasörü aç', () => bridge.invoke('files.open', { target: 'instance', id: instance.id })),
        isDesktopApp() && item('Masaüstüne kısayol', () => createShortcut('play', instance.id, instance.name, instance.id)),
        isCustom && item('Çoğalt', () => duplicateInstance(instance)),
        isCustom && instance.loader !== 'vanilla' && item('.mrpack olarak dışa aktar', () => exportMrpack(instance)),
        isCustom && state.isAdmin && item(instance.sharedPackId ? 'Paylaşımı güncelle' : "vulu Packs'te paylaş", () => openShareDialog(instance), 'accent'),
        isCustom && el('div', { class: 'more-sep', role: 'separator' }),
        isCustom && item('Sil', () => deleteInstance(instance), 'danger'),
    ].filter(Boolean);

    const menu = el('div', { id: 'pack-menu', class: 'more-menu', role: 'menu', hidden: true }, menuItems);
    const toggle = el('button', {
        id: 'btn-pack-more', class: 'btn btn-sm btn-more', title: 'Diğer işlemler', 'aria-haspopup': 'menu', 'aria-expanded': 'false',
        onclick: () => (menu.hidden ? openPackMenu() : closePackMenu()),
    }, '⋯');

    const update = state.packUpdates && state.packUpdates[instance.id];
    $('pack-actions').replaceChildren(...[
        update && canInstallGames() && el('button', { class: 'btn btn-sm btn-primary', title: `rev ${update.from} → rev ${update.to}`, onclick: () => updateInstancePack(instance) }, 'Paketi güncelle'),
        isCustom && el('button', { class: 'btn btn-sm', onclick: () => openInstanceContent(instance) }, 'İçerik'),
        isCustom && canInstallGames() && el('button', { class: 'btn btn-sm', onclick: () => openModrinthFor(instance) }, 'Mod ekle'),
        el('div', { class: 'more-wrap' }, toggle, menu),
    ].filter(Boolean));
}

/**
 * Açılır menüyü düğmenin yanına, pencereye sığacak şekilde yerleştirir: üstte yer varsa yukarı, yoksa aşağı açılır;
 * iki tarafa da sığmazsa geniş olan tarafa açılıp kaydırılır. Menü sabit konumlu olduğu için sahnenin kenarında kesilmez.
 */
function positionMenu(menu, anchor) {
    const margin = 8, gap = 6;
    menu.style.maxHeight = '';
    const a = anchor.getBoundingClientRect();
    const height = menu.scrollHeight + 2, width = menu.offsetWidth;
    const above = a.top - gap - margin, below = window.innerHeight - a.bottom - gap - margin;
    const up = height <= above || (height > below && above > below);
    const room = up ? above : below;
    if (height > room) menu.style.maxHeight = room + 'px';
    const shown = Math.min(height, room);
    menu.style.top = (up ? a.top - gap - shown : a.bottom + gap) + 'px';
    menu.style.left = Math.max(margin, Math.min(a.left, window.innerWidth - width - margin)) + 'px';
}

function openPackMenu() {
    const menu = $('pack-menu');
    if (!menu) return;
    menu.hidden = false;
    positionMenu(menu, $('btn-pack-more'));
    $('btn-pack-more').setAttribute('aria-expanded', 'true');
    menu.querySelector('button')?.focus();
}

function closePackMenu() {
    const menu = $('pack-menu');
    if (!menu || menu.hidden) return;
    menu.hidden = true;
    $('btn-pack-more').setAttribute('aria-expanded', 'false');
}

document.addEventListener('mousedown', event => { if (!event.target.closest('.more-wrap')) closePackMenu(); });
window.addEventListener('resize', closePackMenu);
// Sayfa kayarsa menü düğmeden kopmasın diye kapanır (menünün kendi içinde kaydırması hariç)
document.addEventListener('scroll', event => { if (!event.target.closest?.('.more-menu')) closePackMenu(); }, true);
document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && $('pack-menu') && !$('pack-menu').hidden) { closePackMenu(); $('btn-pack-more').focus(); }
});

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

    const play = details && details.playTime;
    const lastPlayed = play && parseDate(play.lastPlayed);
    setStat('stat-play', play ? formatPlayTime(play.seconds) : '—',
        lastPlayed ? `Son oynama: ${relativeDate(lastPlayed)} · ${play.sessions} kez açıldı` : 'Henüz oynanmadı');

    if (details && details.failed) {
        setStat('stat-mods', '—'); setStat('stat-size', '—'); setStat('stat-updated', '—');
    } else {
        const updated = parseDate(details && details.updatedAt);
        setStat('stat-mods', installed ? String(details.modCount) : '—');
        setStat('stat-size', installed ? formatSize(details.sizeMb) : 'Kurulmadı');
        setStat('stat-updated', updated ? relativeDate(updated) : '—', updated ? formatDate(updated) : '');
    }

    // CurseForge'un elle indirilmesini istediği, henüz yerine konmamış dosyalar
    const manual = (details && details.manual) || [];
    const reminder = $('pack-manual');
    reminder.hidden = manual.length === 0;
    reminder.textContent = manual.length === 1 ? `${manual[0].title} elle indirilmeli` : `${manual.length} dosya elle indirilmeli`;
    reminder.onclick = async () => { await showManualDownloads(manual, instance.id); loadDetails(); };
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
    else if (!state.canLaunchGame) { button.disabled = true; label.textContent = 'Telefonda oyun yakında'; }
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
    } else if (!state.canLaunchGame) {
        text = 'Oyun şimdilik yalnızca bilgisayarda açılıyor';
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

async function play({ ignoreMissing = false, ignoreFiles = false, join = '' } = {}) {
    if (!state.selected || state.gameRunning || launching) return;

    const instance = state.selected;
    // Çökme penceresindeki "Tekrar oyna" aynı sunucuya yeniden bağlanabilsin
    state.lastLaunch = { instanceId: instance.id, join };
    if (!ignoreMissing && !ignoreFiles) clearConsole();
    state.activityText = '';
    launching = true;
    updatePlayButton();
    let missing = null, files = null;
    try {
        const result = await bridge.invoke('game.play', {
            instanceId: instance.id,
            ramGb: state.ramGb,
            mcVersion: canPickVersion(instance) ? mcVersionOf(instance) : '',
            ignoreMissing,
            ignoreFiles,
            join,
        });
        state.gameRunning = result.started;
        missing = result.missing || null;
        files = result.files || null;
    } catch (err) {
        appendLog({ text: err.message, level: 'error' });
    }
    launching = false;
    updatePlayButton();

    // Boş/bozuk ya da kopya jar'lar, yerine konmamış elle indirilecekler
    if (files) {
        const choice = await fileProblemsDialog(files, instance);
        loadDetails();
        if (choice === 'retry') return play({ ignoreMissing, join });
        if (choice === 'ignore') return play({ ignoreMissing, ignoreFiles: true, join });
        return;
    }

    // Eksik bağımlılık: kullanıcıya sor, kurulursa yeniden dene
    if (missing && missing.length) {
        const choice = await missingDepsDialog(missing, instance);
        if (choice === 'install') {
            let result;
            try {
                // Hepsi tek seferde; kurulanların da eksiği çıkarsa launcher onları ayrıca sormadan kurar
                result = await withActivity(() => bridge.invoke('mods.installMissing', {
                    instanceId: instance.id, items: missing.filter(m => m.projectId).map(m => ({ source: m.source || 'modrinth', projectId: m.projectId })),
                }));
                loadDetails();
            } catch (err) { appendLog({ text: err.message, level: 'error' }); return; }
            if (result.manual && result.manual.length) await showManualDownloads(result.manual, instance.id);
            return play({ ignoreMissing: (result.remaining || []).length > 0, ignoreFiles, join });
        }
        if (choice === 'ignore') return play({ ignoreMissing: true, ignoreFiles, join });
    }
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
    bridge.on('logosChanged', () => reloadInstances(state.selected && state.selected.id));
    bridge.on('toast', showToast);
    bridge.on('gameCrashed', crash => showCrashDialog(crash));

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
    if (!settings.musicEnabled && radioPlaying()) stopRadio();
    refreshMusicWidget();
}

/** Ayarlarda tek bir alanı değiştirir ve kaydeder. */
async function saveSettings(patch) {
    const result = await bridge.invoke('settings.save', { ...state.settings, ...patch });
    applySettings(result.settings);
    return result.settings;
}

// ── Bildirim kartı ────────────────────────────────────────────────────
/**
 * Launcher öndeyken gelen bildirimler sağ altta Steam'deki gibi küçük kart olur (arkadayken aynı kart masaüstünde
 * C# tarafından çizilir). Açık olan konuşmanın mesajı kart olmaz. En fazla üç kart üst üste durur, her biri 6 sn sonra
 * gider; tıklanınca ilgili sayfa açılır.
 */
function showToast(n) {
    if (!n || !n.title) return;
    if (n.tag && n.tag.startsWith('dm:') && state.page === 'messages' && typeof dm !== 'undefined' && dm.active === n.tag.slice(3)) return;
    let stack = $('toasts');
    if (!stack) stack = document.body.appendChild(el('div', { id: 'toasts', class: 'toasts', 'aria-live': 'polite' }));

    const person = n.tag && /^(dm|friend|profile):/.test(n.tag) ? n.tag.slice(n.tag.indexOf(':') + 1) : null;
    const known = person && (typeof profile !== 'undefined' && profile.members || []).find(m => m.username === person);
    let timer = 0;
    const dismiss = () => {
        clearTimeout(timer);
        card.classList.add('leaving');
        setTimeout(() => card.remove(), 250);
    };
    const card = el('button', {
        type: 'button', class: 'toast' + (n.tag && n.tag.startsWith('profile:') ? ' playing' : ''),
        onclick: () => { dismiss(); if (n.tag) routeNotification(n.tag); },
    },
        person ? avatarEl(known || { username: person }, 'avatar-md') : el('img', { class: 'toast-logo', src: 'assets/logo.png', alt: '' }),
        el('div', { class: 'toast-text' },
            el('b', {}, n.title),
            n.body && el('div', { class: 'toast-body' }, n.body)));
    stack.append(card);
    while (stack.children.length > 3) stack.firstElementChild.remove();
    timer = setTimeout(dismiss, 6000);
}

// ── Radyo ─────────────────────────────────────────────────────────────
/**
 * Soldaki çalar bir internet radyosu: istasyonlar türe göre radio-browser.info dizininden gelir (C# tarafı alır),
 * ses doğrudan istasyonun yayınından çalınır. Durdurunca yayın bağlantısı da kapatılır (boşuna veri inmesin).
 * Çalmayan istasyon atlanır. Seçilen tür ve istasyon hatırlanır.
 */
const radio = { genres: [], stations: [], genre: store.get('radioGenre', 'lofi'), index: 0, failures: 0, loading: null };

function radioStation() { return radio.stations[radio.index] || null; }
function radioGenreLabel() { return (radio.genres.find(g => g.id === radio.genre) || {}).label || ''; }
function radioPlaying() { const audio = $('music'); return !!audio.getAttribute('src') && !audio.paused; }

async function loadRadioGenres() {
    if (!radio.genres.length) radio.genres = await bridge.invoke('radio.genres');
    if (!radio.genres.some(g => g.id === radio.genre)) radio.genre = radio.genres[0].id;
}

async function loadRadioStations(genre) {
    radio.genre = genre;
    store.set('radioGenre', genre);
    radio.stations = await bridge.invoke('radio.stations', { genre });
    const saved = store.get('radioStation', '');
    radio.index = Math.max(0, radio.stations.findIndex(st => st.id === saved));
}

/** İstasyon listesi yoksa getirir (aynı anda birden çok istek olmasın). */
function ensureRadio() {
    if (radio.stations.length) return Promise.resolve();
    if (!radio.loading) radio.loading = loadRadioGenres().then(() => loadRadioStations(radio.genre)).finally(() => { radio.loading = null; });
    return radio.loading;
}

function playStation(index) {
    if (!radio.stations.length) return;
    radio.index = (index + radio.stations.length) % radio.stations.length;
    const station = radioStation();
    store.set('radioStation', station.id);
    const audio = $('music');
    audio.src = station.url;
    audio.play().catch(() => { /* çalmazsa "error" olayı sıradakine geçer */ });
    refreshMusicWidget();
}

function stopRadio() {
    const audio = $('music');
    audio.pause();
    audio.removeAttribute('src');
    audio.load();   // yayın bağlantısını kapatır
    refreshMusicWidget();
}

async function startRadio() {
    try {
        await ensureRadio();
        playStation(radio.index);
    } catch (err) {
        $('music-title').textContent = 'Radyo';
        $('music-artist').textContent = 'İstasyonlar alınamadı';
    }
}

function refreshMusicWidget() {
    const audio = $('music'), playing = radioPlaying();
    document.querySelector('.music').classList.toggle('paused', !playing);
    $('music-toggle').setAttribute('aria-label', playing ? 'Radyoyu durdur' : 'Radyoyu çal');
    $('music-toggle').title = playing ? 'Durdur' : 'Çal';
    $('music-mute').classList.toggle('muted', audio.muted);
    $('music-mute').setAttribute('aria-label', audio.muted ? 'Sesi aç' : 'Sesi kapat');
    $('music-mute').title = audio.muted ? 'Sesi aç' : 'Sesi kapat';
    $('music-live').style.setProperty('--pct', playing ? '100%' : '0%');
    const station = radioStation();
    $('music-title').textContent = station ? station.name : 'Radyo';
    $('music-artist').textContent = station
        ? [radioGenreLabel(), station.country].filter(Boolean).join(' · ')
        : 'Çalmak için ▶';
}

/** İstasyon menüsü: üstte türler, altta o türün istasyonları. */
async function openRadioMenu() {
    let menu = $('radio-menu');
    if (menu && !menu.hidden) { menu.hidden = true; return; }
    if (!menu) {
        menu = document.body.appendChild(el('div', { id: 'radio-menu', class: 'more-menu radio-menu', role: 'menu', hidden: true }));
        document.addEventListener('mousedown', event => {
            if (!menu.hidden && !event.target.closest('#radio-menu, #music-station')) menu.hidden = true;
        });
        document.addEventListener('keydown', event => { if (event.key === 'Escape') menu.hidden = true; });
    }
    const render = () => {
        menu.replaceChildren(
            el('div', { class: 'radio-genres' }, radio.genres.map(g => el('button', {
                type: 'button', class: 'chip' + (g.id === radio.genre ? ' active' : ''),
                onclick: async () => {
                    menu.replaceChildren(el('div', { class: 'radio-note' }, 'Yükleniyor…'));
                    try { await loadRadioStations(g.id); radio.index = 0; playStation(0); saveSettings({ musicEnabled: true }).catch(() => { }); }
                    catch { /* liste alınamazsa eskisi kalır */ }
                    render();
                },
            }, g.label))),
            radio.stations.length
                ? el('div', { class: 'radio-list' }, radio.stations.map((st, i) => el('button', {
                    type: 'button', role: 'menuitem', class: 'more-item' + (i === radio.index ? ' accent' : ''),
                    title: st.country || '',
                    onclick: () => { menu.hidden = true; playStation(i); saveSettings({ musicEnabled: true }).catch(() => { }); },
                }, st.name)))
                : el('div', { class: 'radio-note' }, 'Bu türde istasyon bulunamadı.'));
        positionMenu(menu, $('music-station'));
    };
    menu.replaceChildren(el('div', { class: 'radio-note' }, 'Yükleniyor…'));
    menu.hidden = false;
    positionMenu(menu, $('music-station'));
    try { await ensureRadio(); } catch { menu.replaceChildren(el('div', { class: 'radio-note' }, 'İstasyonlar alınamadı.')); return; }
    render();
}

function setupMusic() {
    const audio = $('music'), slider = $('music-volume');
    let saveTimer = null;

    audio.addEventListener('volumechange', refreshMusicWidget);
    audio.addEventListener('play', refreshMusicWidget);
    audio.addEventListener('pause', refreshMusicWidget);
    audio.addEventListener('playing', () => { radio.failures = 0; refreshMusicWidget(); });
    // Yayın açılmazsa (istasyon kapalı, biçim desteklenmiyor) sıradakine geçilir; art arda dört başarısızlıkta durulur
    audio.addEventListener('error', () => {
        if (!audio.getAttribute('src')) return;
        if (++radio.failures >= 4) { stopRadio(); $('music-artist').textContent = 'İstasyon çalınamadı'; radio.failures = 0; return; }
        playStation(radio.index + 1);
    });

    $('music-prev').addEventListener('click', async () => { await ensureRadio().catch(() => { }); playStation(radio.index - 1); });
    $('music-next').addEventListener('click', async () => { await ensureRadio().catch(() => { }); playStation(radio.index + 1); });
    $('music-station').addEventListener('click', openRadioMenu);
    $('music-mute').addEventListener('click', () => { audio.muted = !audio.muted; });

    slider.addEventListener('input', () => {
        audio.volume = slider.value / 100;
        state.settings.musicVolume = Number(slider.value);
        clearTimeout(saveTimer);
        saveTimer = setTimeout(() => saveSettings({ musicVolume: Number(slider.value) }).catch(() => { }), 400);
    });
    $('music-toggle').addEventListener('click', () => {
        const enable = !radioPlaying();
        if (enable) startRadio(); else stopRadio();
        saveSettings({ musicEnabled: enable }).catch(() => { });
    });
}

function startMusic() {
    if (!state.settings.musicEnabled) return;
    startRadio();
}

// ── Başlat ────────────────────────────────────────────────────────────
window.addEventListener('DOMContentLoaded', () => {
    // Açılıştaki el yazısı fontu giriş ekranındayken hazır olsun
    document.fonts.load('92px "Great Vibes"').catch(() => { });

    setupTitlebar();
    setupPopovers();
    setupVersionPicker();
    setupPacksPage();
    setupServersPage();
    setupMessagesPage();
    setupLibrary();
    setupSocial();
    $('mr-filter-toggle').addEventListener('click', () => {
        const open = $('mr-filter-toggle').closest('.mr-side').classList.toggle('open');
        $('mr-filter-toggle').setAttribute('aria-expanded', String(open));
    });
    for (const button of document.querySelectorAll('.discover-switch button'))
        button.addEventListener('click', () => showPage(button.dataset.source));
    setupRegisterForm();
    setupProfilePage();
    setupConsole();
    setupDownloads();
    setupMusic();
    setupLoginForm();
    $('btn-play').addEventListener('click', () => play());
    $('btn-ram').addEventListener('click', () => setPopover('ram-pop', 'btn-ram', $('ram-pop').hidden));
    $('btn-new-instance').addEventListener('click', () => openNewInstanceDialog());
    for (const button of document.querySelectorAll('#page-tabs button'))
        button.addEventListener('click', () => showPage(button.dataset.page));

    // Sağ tık menüsü açılmasın (pencere ayarı yanında, sayfa düzeyinde de)
    window.addEventListener('contextmenu', event => event.preventDefault());

    // Dosya sürükleyip bırakmak sayfayı değiştirmesin
    window.addEventListener('dragover', event => event.preventDefault());
    window.addEventListener('drop', event => event.preventDefault());

    // Hiç tema seçilmediyse önce tema sorulur, sonra normal açılış (otomatik giriş ya da giriş ekranı)
    (needsThemePick() ? pickThemeFirstRun() : Promise.resolve())
        .then(() => showLogin({ auto: true }))
        .catch(err => { hideSplash(); showView('view-login'); setLoginStatus(err.message, true); });
});
