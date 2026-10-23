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
    // Hazır boyutlar; elle yazılan boyut "Özel" sayılır
    const SIZE_PRESETS = [['0x0', 'Varsayılan'], ['1280x720', '1280×720'], ['1600x900', '1600×900'], ['1920x1080', '1920×1080'], ['custom', 'Özel']];
    const sizeKey = (w, h) => !w && !h ? '0x0' : SIZE_PRESETS.some(([k]) => k === `${w}x${h}`) ? `${w}x${h}` : 'custom';
    const customSize = el('div', { class: 'set-inline narrow', hidden: sizeKey(s.gameWidth, s.gameHeight) !== 'custom' }, width, el('span', {}, '×'), height);
    const saveSize = () => changeSetting({ gameWidth: Number(width.value) || 0, gameHeight: Number(height.value) || 0 });
    width.addEventListener('change', saveSize);
    height.addEventListener('change', saveSize);
    const sizePresets = segmentedControl(SIZE_PRESETS, sizeKey(s.gameWidth, s.gameHeight), value => {
        customSize.hidden = value !== 'custom';
        if (value === 'custom') { width.focus(); return true; }
        const [w, h] = value.split('x').map(Number);
        width.value = w || ''; height.value = h || '';
        return changeSetting({ gameWidth: w, gameHeight: h });
    });

    // Bilgisayarda bulunan Java'lar: otomatik modda aynı sürüm indirilmeden kullanılır; istenirse biri seçilir
    const detected = info.detectedJava || [];
    const detectedList = el('div', { class: 'set-stack java-list' }, detected.length
        ? detected.map(j => el('div', { class: 'java-row' },
            el('div', {}, el('b', {}, `Java ${j.version}`), el('span', { class: 'hint' }, ` · ${j.source}`),
                el('div', { class: 'hint java-path', title: j.path }, j.path)),
            el('button', {
                class: 'btn btn-sm', onclick: async () => {
                    javaPath.value = j.path;
                    javaCustom.hidden = false;
                    if (await changeSetting({ javaMode: 'custom', customJavaPath: j.path }) !== false) openSettingsPage();
                },
            }, s.javaMode === 'custom' && s.customJavaPath === j.path ? 'Seçili ✓' : 'Bunu kullan')))
        : [el('span', { class: 'hint' }, 'Bilgisayarda kurulu Java bulunamadı; gereken sürüm otomatik indirilir.')]);

    const game = el('section', { class: 'set-section' },
        el('h2', {}, 'Oyun'),
        settingRow('Varsayılan RAM', 'Bir paket için ayrıca seçim yapmadıysan kullanılır.',
            segmentedControl([[0, 'Önerilen'], ...info.ramOptionsGb.map(gb => [gb, gb + ' GB'])], s.defaultRamGb,
                value => changeSetting({ defaultRamGb: value }))),
        // Java'yı, pencereyi ve tam ekranı telefonda oyun motoru (Amethyst) yönetir
        isDesktopApp() && settingRow('Java', 'Otomatik modda oyunun istediği sürüm (8, 17, 21...) bilgisayarda kuruluysa o kullanılır, yoksa Eclipse Temurin olarak indirilir.',
            el('div', { class: 'set-stack' },
                segmentedControl([['auto', 'Otomatik'], ['custom', 'Elle seç']], s.javaMode, async value => {
                    javaCustom.hidden = value !== 'custom';
                    // Elle modda yol girilene kadar kaydetme; boş yol hata verir
                    if (value === 'custom' && !javaPath.value.trim()) return true;
                    return changeSetting({ javaMode: value, customJavaPath: javaPath.value });
                }),
                javaCustom)),
        isDesktopApp() && settingRow('Bilgisayarda bulunan Java', null, detectedList),
        isDesktopApp() && settingRow('İndirilen Java sürümleri', javaInstalled,
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
        isDesktopApp() && settingRow('Oyun penceresi', 'Oyun açıldığında pencerenin boyutu. Varsayılan: oyunun kendi boyutu.',
            el('div', { class: 'set-stack' }, sizePresets, customSize)),
        isDesktopApp() && settingRow('Tam ekran başlat', null, toggleControl(s.fullscreen, value => changeSetting({ fullscreen: value }))));

    // ── Launcher davranışı ────────────────────────────────────────────
    const behaviour = el('section', { class: 'set-section' },
        el('h2', {}, 'Launcher'),
        isDesktopApp() && settingRow('Oyun açılınca', '"Kapat" seçilirse oyun çıktısı konsolda görünmez ve oynama süresi sayılmaz.',
            segmentedControl([['keep', 'Açık kalsın'], ['minimize', 'Küçült'], ['close', 'Kapat']], s.onGameStart,
                value => changeSetting({ onGameStart: value }))),
        settingRow('Açılış animasyonunu atla', null, toggleControl(s.skipSplash, value => changeSetting({ skipSplash: value }))),
        isDesktopApp() && settingRow('Launcher boyutu', 'Otomatikte büyük ekranlarda pencere ve yazılar ekranla birlikte büyür.',
            segmentedControl([[0, 'Otomatik'], [100, '%100'], [125, '%125'], [150, '%150'], [175, '%175']], s.windowScale || 0,
                value => changeSetting({ windowScale: value }))),
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
        settingRow('Sürüm', null, el('span', { class: 'set-value' }, 'vulu Launcher v' + info.version)),
        settingRow('Açık kaynak lisansları', 'Kullanılan bileşenler ve lisans metinleri.',
            el('button', { class: 'btn btn-sm', onclick: openLicenses }, 'Göster')));

    // Oyun ayarları (Java, RAM, pencere) telefonda oyun açılana kadar gösterilmez
    body.replaceChildren(...[state.canLaunchGame && game, behaviour, look, account].filter(Boolean));
}

// Üçüncü taraf bileşenlerin listesi ve lisans metinleri (wwwroot/licenses, fonts). Windows'ta sayfa diskten (file://)
// açıldığı için fetch çalışmaz; metinler iframe'de gösterilir.
const LICENSE_FILES = [
    ['licenses/notices.txt', 'Bileşenler'],
    ['licenses/LGPL-3.0.txt', 'GNU LGPL 3.0'], ['licenses/GPL-3.0.txt', 'GNU GPL 3.0'],
    ['licenses/LGPL-2.1.txt', 'GNU LGPL 2.1'], ['licenses/GPL-2.0.txt', 'GNU GPL 2.0'],
    ['licenses/Apache-2.0.txt', 'Apache 2.0'], ['licenses/MIT.txt', 'MIT'], ['licenses/BSD-2-Clause.txt', 'BSD 2-Clause'],
    ['licenses/LWJGL.txt', 'LWJGL (BSD 3-Clause)'], ['licenses/ANGLE.txt', 'ANGLE'], ['licenses/SDL-zlib.txt', 'SDL (zlib)'],
    ['fonts/OFL-IBMPlex.txt', 'IBM Plex (OFL)'], ['fonts/OFL-GreatVibes.txt', 'Great Vibes (OFL)'],
    ['fonts/OFL-Silkscreen.txt', 'Silkscreen (OFL)'],
];

function openLicenses() {
    const frame = el('iframe', { class: 'license-frame', src: LICENSE_FILES[0][0], title: 'Lisans metni' });
    const picker = el('select', { onchange: event => { frame.src = event.target.value; } },
        ...LICENSE_FILES.map(([file, name]) => el('option', { value: file }, name)));
    return openModal(close => [
        el('div', { class: 'modal-head' }, 'Açık kaynak lisansları'),
        el('div', { class: 'modal-body license-body' }, picker, frame),
        el('div', { class: 'modal-foot' }, el('button', { class: 'btn btn-primary', onclick: () => close() }, 'Kapat')),
    ], { wide: true });
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
    // Klasik → Neon: sinyal bozulması. Renk parlamaları ve karartma, ekranda kayan şerit bantları ve parazit.
    // Katman sayısı az tutulur (her tam ekran katman telefon GPU'sunda ayrı bir doku demek): bantlar 9 ayrı öğe yerine
    // dört boyda dört öğedir ve her adımda yeni yerine taşınır. Masaüstündeki sayfa sarsıntısı telefonda yok: bütün
    // sayfayı ayrı bir katmana alıp baştan çizdirirdi.
    neon: {
        duration: 760, swapAt: 340,
        build(layer) {
            const parts = [];
            const add = (node, ...frameSets) => { layer.append(node); for (const frames of frameSets) parts.push([node, frames]); };
            add(fxPart('fx-fill fx-cyan'), holdFrames('opacity', [[0, 0], [.2, .08], [.36, 0], [.56, .16], [.66, 0], [1, 0]]));
            add(fxPart('fx-fill fx-pink'), holdFrames('opacity', [[0, 0], [.36, .22], [.44, 0], [1, 0]]));
            add(fxPart('fx-fill fx-dark'), holdFrames('opacity', [[0, 0], [.44, 1], [.56, 0], [1, 0]]));

            // Masaüstündeki dilimler: [başlangıç, bitiş, üst %, kayma px]; her bant kendi boyundaki öğeyle gösterilir
            const h = innerHeight / 100;
            const bands = [
                { height: 12, steps: [[0, .1, 10, -24], [.1, .2, 42, 32], [.68, .78, 14, -10], [.78, .88, 82, 12]] },
                { height: 24, steps: [[.2, .3, 70, -14], [.58, .68, 56, 26]] },
                { height: 46, steps: [[.3, .4, 4, 20], [.4, .48, 24, -44]] },
                { height: 100, steps: [[.48, .58, 0, 0]], alpha: .5 },
            ];
            for (const { height, steps, alpha = 1 } of bands) {
                const at = ([, , top, shift]) => `translate(${shift}px, ${top * h}px)`;
                const opacity = [], transform = [];
                if (steps[0][0] > 0) { opacity.push([0, 0]); transform.push([0, at(steps[0])]); }
                steps.forEach((step, i) => {
                    const [from, to] = step;
                    opacity.push([from, alpha]);
                    transform.push([from, at(step)]);
                    const next = steps[i + 1];
                    if (!next || next[0] > to) opacity.push([to, 0]);   // arada boşluk varsa bant kaybolur
                });
                opacity.push([1, 0]);
                transform.push([1, at(steps[steps.length - 1])]);
                add(fxPart('fx-slice', `height:${height}%`), holdFrames('opacity', opacity), holdFrames('transform', transform));
            }
            add(fxPart('fx-fill fx-noise'), holdFrames('opacity', [[0, .9], [.1, .3], [.3, .9], [.44, 1], [.5, .3], [.7, 0], [1, 0]]));
            return parts;
        },
    },
    // Neon → Klasik: altın kenarlı iris ortada kapanır, klasik temaya açılır. Halka tam boyda bir kez çizilir ve yalnızca
    // küçültülür (büyütmek dev bir doku çizdirirdi); kenarlardan kararan hare ve en sonda tam karanlık irisin dışını karartır.
    classic: {
        duration: 1100, swapAt: 550,
        build(layer) {
            const size = Math.ceil(Math.hypot(innerWidth, innerHeight)) + 24;
            const ring = fxPart('fx-ring', `width:${size}px;height:${size}px;margin:${-size / 2}px 0 0 ${-size / 2}px`);
            const vignette = fxPart('fx-fill fx-vignette'), black = fxPart('fx-fill fx-black');
            layer.append(vignette, black, ring);
            const ease = 'cubic-bezier(.65, 0, .35, 1)';
            return [
                [ring, [{ offset: 0, transform: 'scale(1)', opacity: 1, easing: ease }, { offset: .45, transform: 'scale(.02)', opacity: 1 },
                    { offset: .46, transform: 'scale(.02)', opacity: 0 }, { offset: .54, transform: 'scale(.02)', opacity: 0 },
                    { offset: .55, transform: 'scale(.02)', opacity: 1, easing: ease }, { offset: 1, transform: 'scale(1)', opacity: 1 }]],
                [vignette, [{ offset: 0, opacity: 0, easing: 'ease-in' }, { offset: .3, opacity: 1 },
                    { offset: .7, opacity: 1, easing: 'ease-out' }, { offset: 1, opacity: 0 }]],
                [black, [{ offset: 0, opacity: 0 }, { offset: .2, opacity: 0, easing: 'ease-in' }, { offset: .45, opacity: 1 },
                    { offset: .55, opacity: 1, easing: 'ease-out' }, { offset: .8, opacity: 0 }, { offset: 1, opacity: 0 }]],
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
                [edge, [{ offset: 0, transform: `translateX(${innerWidth}px)`, easing: ease }, { offset: .45, transform: 'translateX(-90px)' },
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

/**
 * Yeni tema kapalı ekranın arkasında çizilirken beklenir: üst üste birkaç kare hızlı gelince (çizim bitti) devam
 * edilir. Telefonda tema değişimi yarım saniyeyi bulabilir; açılış onu beklemezse animasyon yarım çizilmiş sayfanın
 * üstünde takılır. En fazla maxMs beklenir.
 */
function settled(maxMs = 900) {
    return Promise.race([wait(maxMs), new Promise(resolve => {
        const start = performance.now();
        let last = start, calm = 0;
        requestAnimationFrame(function tick(now) {
            calm = now - last < 34 ? calm + 1 : 0;
            last = now;
            if (calm >= 3 || now - start > maxMs) resolve();
            else requestAnimationFrame(tick);
        });
    })]);
}

/** Geçiş boyunca kareleri sayar (bir sonraki iyileştirme için konsola yazılır). */
function frameMeter() {
    const meter = { frames: 0, worst: 0, slow: 0, running: true };
    let last = performance.now();
    requestAnimationFrame(function tick(now) {
        if (!meter.running) return;
        const gap = now - last;
        last = now;
        meter.frames++;
        meter.worst = Math.max(meter.worst, gap);
        if (gap > 34) meter.slow++;
        requestAnimationFrame(tick);
    });
    return meter;
}

async function liteThemeFx(theme, apply) {
    themeFxRunning = true;
    const fx = LITE_FX[theme];
    const layer = el('div', { id: 'theme-fx', class: 'lite lite-' + theme, 'aria-hidden': 'true' });
    document.body.append(layer);
    const parts = fx.build(layer);
    const cover = frameMeter();
    let reveal = null, swapMs = 0;
    try {
        await playFxSlice(parts, fx.duration, 0, fx.swapAt);
        cover.running = false;
        const swapStart = performance.now();
        apply();
        await settled();
        swapMs = performance.now() - swapStart;
        reveal = frameMeter();
        await playFxSlice(parts, fx.duration, fx.swapAt, fx.duration);
    } finally {
        cover.running = false;
        if (reveal) reveal.running = false;
        for (const [node] of parts) node.getAnimations().forEach(a => a.cancel());
        layer.remove();
        themeFxRunning = false;
    }
    if (typeof appendLog === 'function') {
        const stat = m => m ? `${m.frames} kare, ${m.slow} yavaş, en uzun ${Math.round(m.worst)} ms` : '-';
        appendLog({ text: `Tema geçişi (${theme}): kapanış ${stat(cover)} · tema ${Math.round(swapMs)} ms · açılış ${stat(reveal)}`, level: 'muted' });
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
