// Ayarlar sayfası. Her değişiklik anında kaydedilir.
const ACCENT_COLORS = ['#c27e3e', '#c2483e', '#4f9d69', '#3e86c2', '#8a5cc2', '#c24f8f'];

let savedTimer = null;

function flashSaved(text = 'Kaydedildi ✓', isError = false) {
    const note = $('settings-saved');
    note.textContent = text;
    note.classList.toggle('error', isError);
    clearTimeout(savedTimer);
    savedTimer = setTimeout(() => { note.textContent = ''; }, isError ? 5000 : 1600);
}

async function changeSetting(patch) {
    try { await saveSettings(patch); flashSaved(); return true; }
    catch (err) { flashSaved(err.message, true); return false; }
}

function settingRow(title, hint, control) {
    return el('div', { class: 'set-row' },
        el('div', { class: 'set-text' }, el('div', { class: 'set-title' }, title), hint && el('div', { class: 'set-hint' }, hint)),
        el('div', { class: 'set-control' }, control));
}

function segmentedControl(options, current, onPick) {
    const wrap = el('div', { class: 'segmented' });
    const render = value => wrap.replaceChildren(...options.map(([key, label]) => el('button', {
        type: 'button', class: key === value ? 'active' : '',
        onclick: async () => { render(key); if (!(await onPick(key))) render(current); else current = key; },
    }, label)));
    render(current);
    return wrap;
}

function toggleControl(checked, onChange) {
    const input = el('input', { type: 'checkbox', checked });
    input.addEventListener('change', async () => { if (!(await onChange(input.checked))) input.checked = !input.checked; });
    return el('label', { class: 'switch' }, input, el('span', {}));
}

async function openSettingsPage() {
    const body = $('settings-body');
    let info;
    try { info = await bridge.invoke('settings.get'); }
    catch (err) { body.replaceChildren(el('p', { class: 'form-error' }, 'Ayarlar alınamadı: ' + err.message)); return; }

    applySettings(info.settings);
    const s = info.settings;

    // ── Oyun ──────────────────────────────────────────────────────────
    const javaPath = el('input', { type: 'text', value: s.customJavaPath, placeholder: 'C:\\...\\bin\\java.exe', spellcheck: false });
    const javaCustom = el('div', { class: 'set-inline', hidden: s.javaMode !== 'custom' },
        javaPath,
        el('button', {
            class: 'btn btn-sm', onclick: async () => {
                const picked = await bridge.invoke('settings.pickJava');
                if (!picked.path) return;
                javaPath.value = picked.path;
                await changeSetting({ javaMode: 'custom', customJavaPath: picked.path });
            },
        }, 'Gözat'));
    javaPath.addEventListener('change', () => changeSetting({ javaMode: 'custom', customJavaPath: javaPath.value }));

    const javaInstalled = el('span', {}, info.installedJava.length
        ? 'İndirilmiş: ' + info.installedJava.map(v => 'Java ' + v).join(', ')
        : 'Henüz indirilmiş Java yok; ilk oyun açılışında indirilir.');

    const jvmArgs = el('input', { type: 'text', value: s.jvmArgs, placeholder: 'Örn. -XX:+UseZGC', spellcheck: false });
    jvmArgs.addEventListener('change', () => changeSetting({ jvmArgs: jvmArgs.value }));

    const width = el('input', { type: 'number', min: 0, max: 7680, value: s.gameWidth || '', placeholder: 'Genişlik' });
    const height = el('input', { type: 'number', min: 0, max: 4320, value: s.gameHeight || '', placeholder: 'Yükseklik' });
    const saveSize = () => changeSetting({ gameWidth: Number(width.value) || 0, gameHeight: Number(height.value) || 0 });
    width.addEventListener('change', saveSize);
    height.addEventListener('change', saveSize);

    const game = el('section', { class: 'set-section' },
        el('h2', {}, 'Oyun'),
        settingRow('Varsayılan RAM', 'Bir paket için ayrıca seçim yapmadıysan kullanılır.',
            segmentedControl([[0, 'Önerilen'], ...info.ramOptionsGb.map(gb => [gb, gb + ' GB'])], s.defaultRamGb,
                value => changeSetting({ defaultRamGb: value }))),
        settingRow('Java', 'Otomatik modda oyunun istediği sürüm (8, 17, 21...) Eclipse Temurin olarak indirilir.',
            el('div', { class: 'set-stack' },
                segmentedControl([['auto', 'Otomatik (Adoptium)'], ['custom', 'Elle seç']], s.javaMode, async value => {
                    javaCustom.hidden = value !== 'custom';
                    // Elle modda yol girilene kadar kaydetme; boş yol hata verir
                    if (value === 'custom' && !javaPath.value.trim()) return true;
                    return changeSetting({ javaMode: value, customJavaPath: javaPath.value });
                }),
                javaCustom)),
        settingRow('İndirilen Java sürümleri', javaInstalled,
            el('button', {
                class: 'btn btn-sm', disabled: info.installedJava.length === 0,
                onclick: async () => {
                    const accepted = await confirmDialog({
                        title: 'Java sürümlerini sil',
                        message: 'İndirilmiş Java sürümleri silinecek. Oyun bir sonraki açılışta gerekeni yeniden indirir.',
                        okText: 'Sil', cancelText: 'Vazgeç', danger: true,
                    });
                    if (!accepted) return;
                    try { await bridge.invoke('settings.removeJava'); flashSaved('Silindi ✓'); }
                    catch (err) { flashSaved(err.message, true); }
                    openSettingsPage();
                },
            }, 'Sil')),
        settingRow('Ek JVM argümanları', 'İleri düzey. Boş bırakılırsa varsayılanlar kullanılır.', jvmArgs),
        settingRow('Oyun penceresi', 'Boş bırakılırsa oyunun varsayılan boyutu kullanılır.',
            el('div', { class: 'set-inline narrow' }, width, el('span', {}, '×'), height)),
        settingRow('Tam ekran başlat', null, toggleControl(s.fullscreen, value => changeSetting({ fullscreen: value }))));

    // ── Launcher davranışı ────────────────────────────────────────────
    const behaviour = el('section', { class: 'set-section' },
        el('h2', {}, 'Launcher'),
        isDesktopApp() && settingRow('Oyun açılınca', '"Kapat" seçilirse oyun çıktısı konsolda görünmez ve oynama süresi sayılmaz.',
            segmentedControl([['keep', 'Açık kalsın'], ['minimize', 'Küçült'], ['close', 'Kapat']], s.onGameStart,
                value => changeSetting({ onGameStart: value }))),
        settingRow('Açılış animasyonunu atla', null, toggleControl(s.skipSplash, value => changeSetting({ skipSplash: value }))),
        settingRow('Arkadaşların ne görsün', 'Sunucular ve profil sayfasındaki arkadaş listesinde.',
            segmentedControl([['full', 'Ne oynadığım'], ['online', 'Sadece çevrimiçi'], ['hidden', 'Görünmez']], s.presenceMode || 'full',
                value => changeSetting({ presenceMode: value }))),
        settingRow('Mesaj bildirimleri', 'Launcher arkadayken ya da oyundayken yeni mesaj gelince ' + (isDesktopApp() ? 'Windows bildirimi.' : 'telefon bildirimi.'),
            toggleControl(s.dmNotifications !== false, value => changeSetting({ dmNotifications: value }))),
        settingRow('Bildirimde mesajı göster', 'Kapalıysa bildirimde yalnızca kimden geldiği yazar; mesajın kendisi görünmez.',
            toggleControl(s.dmNotificationPreview !== false, value => changeSetting({ dmNotificationPreview: value }))),
        settingRow('Beğeni ve yorum bildirimi', 'Ekran görüntün beğenilince ya da yorum gelince haber verir.',
            toggleControl(s.socialNotifications !== false, value => changeSetting({ socialNotifications: value }))),
        settingRow('Oynama süremi profilimde göster', 'Toplam oynama süren ve en çok oynadığın paketler profilinde görünür.',
            toggleControl(s.showPlaytimeOnProfile !== false, value => changeSetting({ showPlaytimeOnProfile: value }))),
        settingRow('Paket güncelleme bildirimi', 'Kurduğun bir vulu pack güncellenince haber verir.',
            toggleControl(s.packUpdateNotifications !== false, value => changeSetting({ packUpdateNotifications: value }))),
        isDesktopApp() && settingRow("Discord'da durum göster", 'Discord açıksa arkadaşların ne oynadığını görür: "vulu Launcher · AGLR SMP oynuyor".',
            toggleControl(s.discordPresence !== false, value => changeSetting({ discordPresence: value }))),
        settingRow('Konsolu büyük aç', null, toggleControl(s.consoleExpanded, value => changeSetting({ consoleExpanded: value }))));

    // ── Ses ve görünüm ────────────────────────────────────────────────
    const volume = el('input', { type: 'range', min: 0, max: 100, value: s.musicVolume });
    volume.addEventListener('input', () => { $('music').volume = volume.value / 100; $('music-volume').value = volume.value; });
    volume.addEventListener('change', () => changeSetting({ musicVolume: Number(volume.value) }));

    const swatches = el('div', { class: 'swatches' });
    const renderSwatches = current => swatches.replaceChildren(...ACCENT_COLORS.map(color => el('button', {
        type: 'button', class: 'swatch' + (color === current ? ' active' : ''), style: `background:${color}`, title: color,
        onclick: async () => { if (await changeSetting({ accentColor: color })) renderSwatches(color); },
    })));
    renderSwatches(s.accentColor);

    const theme = ['neon', 'atlas'].includes(document.documentElement.dataset.ui) ? document.documentElement.dataset.ui : 'classic';
    const look = el('section', { class: 'set-section' },
        el('h2', {}, 'Ses ve görünüm'),
        settingRow('Tema', 'Neon: siberpunk, koyu ve parlak. Atlas: kâşif defteri, açık renkli. ' + (isDesktopApp() ? 'Bu bilgisayar' : 'Bu cihaz') + ' için hatırlanır.',
            segmentedControl([['classic', 'Klasik'], ['neon', 'Neon'], ['atlas', 'Atlas']], theme, async value => {
                // Vurgu rengi satırı yalnızca klasik temada var; sayfa, tema değiştiği anda yenilenir
                await setUiTheme(value, () => openSettingsPage());
                return true;
            })),
        settingRow('Menü müziği', null, toggleControl(s.musicEnabled, async value => {
            if (!(await changeSetting({ musicEnabled: value }))) return false;
            if (value) $('music').play().catch(() => { });
            return true;
        })),
        settingRow('Ses düzeyi', null, el('div', { class: 'set-inline' }, volume)),
        theme === 'classic' && settingRow('Vurgu rengi', null, swatches));

    // ── Hesap ve dosyalar ─────────────────────────────────────────────
    const account = el('section', { class: 'set-section' },
        el('h2', {}, 'Hesap ve dosyalar'),
        settingRow('Hesap', `${state.username} olarak giriş yapıldı.`,
            el('button', { class: 'btn btn-sm', onclick: logout }, 'Çıkış yap')),
        settingRow('Kayıtlı şifre', info.hasSavedPassword ? `${info.savedUsername} için şifre ${isDesktopApp() ? 'bu bilgisayarda' : 'bu cihazda'} kayıtlı.` : 'Kayıtlı şifre yok.',
            el('button', {
                class: 'btn btn-sm', disabled: !info.hasSavedPassword,
                onclick: async () => { await bridge.invoke('auth.forgetPassword'); flashSaved('Silindi ✓'); openSettingsPage(); },
            }, 'Sil')),
        settingRow('Oyun klasörü', info.gameFolder,
            isDesktopApp() && el('div', { class: 'set-inline' },
                el('button', { class: 'btn btn-sm', onclick: () => bridge.invoke('files.open', { target: 'base' }) }, 'Aç'),
                el('button', { class: 'btn btn-sm', onclick: () => bridge.invoke('files.open', { target: 'instances' }) }, 'Instance\'lar'))),
        settingRow('Sürüm', null, el('span', { class: 'set-value' }, 'vulu Launcher v' + info.version)));

    // Oyun ayarları (Java, RAM, pencere) telefonda oyun açılana kadar gösterilmez
    body.replaceChildren(...[state.canLaunchGame && game, behaviour, look, account].filter(Boolean));
}

// Tema geçiş efektleri: ekranı kapatan katmanın süresi ve temanın değiştiği an (ms)
const THEME_FX = {
    neon: { className: 'glitch', rootClass: 'fx-glitch', swapAt: 340, duration: 760 },   // → neon: sinyal bozulması
    classic: { className: 'iris', rootClass: null, swapAt: 550, duration: 1100 },        // → klasik: altın iris
    atlas: { className: 'page', rootClass: null, swapAt: 500, duration: 1000 },          // → atlas: sayfa çevrilir
};
let themeFxRunning = false;

/**
 * Temayı değiştirir. Yalnızca CSS değiştiği için sayfa yeniden yüklenmez; değişim, ekranı kısa süre kapatan
 * bir efektin ortasında olur. onSwap tema değiştiği anda çağrılır.
 */
async function setUiTheme(theme, onSwap) {
    const root = document.documentElement;
    const apply = () => {
        root.dataset.ui = theme;
        store.set('uiTheme', theme);
        if (typeof renderDownloads === 'function') renderDownloads();
        if (onSwap) onSwap();
    };

    const fx = THEME_FX[theme];
    if (root.dataset.ui === theme || !fx || themeFxRunning) { apply(); return; }
    if (isMobile()) { await liteThemeFx(theme, apply); return; }

    themeFxRunning = true;
    const layer = el('div', { id: 'theme-fx', class: fx.className, 'aria-hidden': 'true' });
    document.body.append(layer);
    if (fx.rootClass) root.classList.add(fx.rootClass);
    try {
        await wait(fx.swapAt);
        apply();
        await wait(fx.duration - fx.swapAt);
    } finally {
        layer.remove();
        if (fx.rootClass) root.classList.remove(fx.rootClass);
        themeFxRunning = false;
    }
}

// ── Telefon için tema geçişleri ───────────────────────────────────────
// Masaüstündeki efektlerin aynısı, aynı zamanlamayla; ama yalnızca transform ve opaklıkla (GPU'da) oynar. Masaüstü
// sürümü filter, clip-path, blend ve her karede yeniden boyanan gradyan kullanır; telefonda bunlar kare kaçırır.
// Her efekt iki yarıya bölünür: ilk yarı ekranı kapatır, tema kapalı ekranın arkasında değişir ve yeni tema çizilene
// kadar beklenir, ikinci yarı açar. Böylece temanın yeniden hesaplandığı duraklama görünmez.

/** Sert geçişli kareler: [[ofset, değer], ...]; her değer bir sonraki ofsete kadar sabit kalır (CSS steps(1) gibi). */
function holdFrames(prop, points) {
    return points.flatMap(([offset, value], i) => {
        const next = points[i + 1];
        return next ? [{ offset, [prop]: value }, { offset: next[0], [prop]: value }] : [{ offset, [prop]: value }];
    });
}

/** Bir öğe: sınıfı ve isteğe bağlı satır içi stil. */
const fxPart = (cls, style = '') => el('i', { class: cls, style });

const LITE_FX = {
    // Klasik → Neon: sinyal bozulması. Renk parlamaları ve karartma, kayan şeritler, parazit; ekran sarsılır.
    neon: {
        duration: 760, swapAt: 340,
        build(layer) {
            const parts = [];
            const add = (node, frames) => { layer.append(node); parts.push([node, frames]); };
            add(fxPart('fx-fill fx-cyan'), holdFrames('opacity', [[0, 0], [.2, .08], [.36, 0], [.56, .16], [.66, 0], [1, 0]]));
            add(fxPart('fx-fill fx-pink'), holdFrames('opacity', [[0, 0], [.36, .22], [.44, 0], [1, 0]]));
            add(fxPart('fx-fill fx-dark'), holdFrames('opacity', [[0, 0], [.44, 1], [.56, 0], [1, 0]]));
            // Masaüstündeki kayan dilimler: her adımda ekranın bir bandı yana kaymış şeritlerle görünür
            const steps = [
                [0, .1, 10, 8, -24], [.1, .2, 42, 14, 32], [.2, .3, 70, 18, -14], [.3, .4, 4, 34, 20], [.4, .48, 24, 46, -44],
                [.48, .58, 0, 100, 0, .5], [.58, .68, 56, 24, 26], [.68, .78, 14, 10, -10], [.78, .88, 82, 12, 12],
            ];
            for (const [from, to, top, height, shift, alpha = 1] of steps)
                add(fxPart('fx-slice', `top:${top}%;height:${height}%;transform:translateX(${shift}px)`),
                    holdFrames('opacity', from === 0 ? [[0, alpha], [to, 0], [1, 0]] : [[0, 0], [from, alpha], [to, 0], [1, 0]]));
            add(fxPart('fx-fill fx-noise'), holdFrames('opacity', [[0, .9], [.1, .3], [.3, .9], [.44, 1], [.5, .3], [.7, 0], [1, 0]]));
            // Ekran sarsıntısı: masaüstünde filtreli; burada yalnızca kaydırma. body'nin kutusu ekranın kendisi olduğu
            // için içindeki sabit konumlu öğeler yerinden oynamaz.
            parts.push([document.body, holdFrames('transform', [
                [0, 'none'], [.08, 'translate(-8px, 0) skewX(-3deg)'], [.16, 'translate(10px, 1px)'], [.24, 'translate(-4px, -2px) skewX(6deg)'],
                [.32, 'translate(14px, 0)'], [.4, 'translate(-12px, 2px) skewX(-8deg)'], [.48, 'none'], [.56, 'translate(6px, 0)'],
                [.64, 'translate(-3px, 0) skewX(2deg)'], [.72, 'translate(2px, 0)'], [.8, 'none'], [1, 'none']])]);
            return parts;
        },
    },
    // Neon → Klasik: altın kenarlı iris ortada kapanır, klasik temaya açılır. Halka ve karartma bir kez çizilir,
    // yalnızca büyütülüp küçültülür (masaüstünde yarıçap her karede yeniden boyanır).
    classic: {
        duration: 1100, swapAt: 550,
        build(layer) {
            const diagonal = Math.hypot(innerWidth, innerHeight);
            const hole = 10;                                   // kapalıyken deliğin yarıçapı (px)
            const open = (diagonal / 2 + 60) / hole;           // açıkken ölçek: delik ekranın köşelerini aşar
            const size = Math.ceil(diagonal + 40);
            const iris = fxPart('fx-iris', `width:${size}px;height:${size}px;margin:${-size / 2}px 0 0 ${-size / 2}px;` +
                `background:radial-gradient(circle at 50% 50%, transparent ${hole}px, rgba(243, 214, 164, .95) ${hole + .3}px,` +
                ` #c27e3e ${hole + .8}px, rgba(8, 6, 4, .96) ${hole + 2}px, #000 ${hole + 9}px)`);
            layer.append(iris, fxPart('fx-fill fx-black'));
            const ease = 'cubic-bezier(.65, 0, .35, 1)';
            return [
                [iris, [{ offset: 0, transform: `scale(${open})`, easing: ease }, { offset: .45, transform: 'scale(1)' },
                    { offset: .55, transform: 'scale(1)', easing: ease }, { offset: 1, transform: `scale(${open})` }]],
                // Ortadaki küçük delik de kapanır: tema değişirken ekran tamamen karanlık
                [layer.lastChild, holdFrames('opacity', [[0, 0], [.44, 1], [.56, 0], [1, 0]])],
            ];
        },
    },
    // → Atlas: bir kâğıt sayfa sağdan sola çevrilir; sayfa ekranı kapatınca tema değişir
    atlas: {
        duration: 1000, swapAt: 500,
        build(layer) {
            const page = fxPart('fx-page'), edge = fxPart('fx-page-edge');
            layer.append(page, edge);
            const ease = 'cubic-bezier(.55, 0, .35, 1)';
            return [
                [page, [{ offset: 0, transform: 'translateX(100%)', easing: ease }, { offset: .45, transform: 'none' },
                    { offset: .55, transform: 'none', easing: ease }, { offset: 1, transform: 'translateX(-105%)' }]],
                [edge, [{ offset: 0, transform: 'translateX(100vw)', easing: ease }, { offset: .45, transform: 'translateX(-90px)' },
                    { offset: .55, transform: 'translateX(-90px)', opacity: 1, easing: ease }, { offset: 1, transform: 'translateX(-90px)', opacity: 0 }]],
            ];
        },
    },
};

/**
 * Efektin [from, to] aralığını oynatır (ms); sonunda o anki görünümde kalır. Ekran kare üretmezse (uygulama arka
 * plandaysa) animasyon zamanı ilerlemez; tema yine de değişsin diye süre dolunca beklemeden devam edilir.
 */
function playFxSlice(parts, duration, from, to) {
    const done = Promise.all(parts.map(([node, frames]) => node.animate(frames, {
        duration, delay: -from, endDelay: -(duration - to), fill: 'both', easing: 'linear',
    }).finished.catch(() => { })));
    return Promise.race([done, wait(to - from + 400)]);
}

async function liteThemeFx(theme, apply) {
    themeFxRunning = true;
    const fx = LITE_FX[theme];
    const layer = el('div', { id: 'theme-fx', class: 'lite lite-' + theme, 'aria-hidden': 'true' });
    document.body.append(layer);
    const parts = fx.build(layer);
    try {
        await playFxSlice(parts, fx.duration, 0, fx.swapAt);
        apply();
        await nextPaint();   // yeni tema, kapalı ekranın arkasında çizilsin
        await playFxSlice(parts, fx.duration, fx.swapAt, fx.duration);
    } finally {
        for (const [node] of parts) node.getAnimations().forEach(a => a.cancel());
        layer.remove();
        themeFxRunning = false;
    }
}

async function logout() {
    const accepted = await confirmDialog({ title: 'Çıkış yap', message: 'Hesaptan çıkış yapılsın mı?', okText: 'Çıkış yap', cancelText: 'Vazgeç' });
    if (!accepted) return;

    try { await bridge.invoke('auth.logout'); }
    catch (err) { await alertDialog({ title: 'Çıkış yapılamadı', message: err.message }); return; }

    $('music').pause();
    adminTab = null;
    pendingRegistrations = [];
    updateAdminBadge();
    profile.me = null;
    profile.members = [];
    state.instances = [];
    state.selected = null;
    await showLogin();
}
