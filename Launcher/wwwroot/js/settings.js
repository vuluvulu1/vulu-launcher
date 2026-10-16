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
        settingRow('Oyun açılınca', '"Kapat" seçilirse oyun çıktısı konsolda görünmez.',
            segmentedControl([['keep', 'Açık kalsın'], ['minimize', 'Küçült'], ['close', 'Kapat']], s.onGameStart,
                value => changeSetting({ onGameStart: value }))),
        settingRow('Açılış animasyonunu atla', null, toggleControl(s.skipSplash, value => changeSetting({ skipSplash: value }))),
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
        settingRow('Tema', 'Neon: siberpunk, koyu ve parlak. Atlas: kâşif defteri, açık renkli. Bu bilgisayar için hatırlanır.',
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
        settingRow('Kayıtlı şifre', info.hasSavedPassword ? `${info.savedUsername} için şifre bu bilgisayarda kayıtlı.` : 'Kayıtlı şifre yok.',
            el('button', {
                class: 'btn btn-sm', disabled: !info.hasSavedPassword,
                onclick: async () => { await bridge.invoke('auth.forgetPassword'); flashSaved('Silindi ✓'); openSettingsPage(); },
            }, 'Sil')),
        settingRow('Oyun klasörü', info.gameFolder,
            el('div', { class: 'set-inline' },
                el('button', { class: 'btn btn-sm', onclick: () => bridge.invoke('files.open', { target: 'base' }) }, 'Aç'),
                el('button', { class: 'btn btn-sm', onclick: () => bridge.invoke('files.open', { target: 'instances' }) }, 'Instance\'lar'))),
        settingRow('Sürüm', null, el('span', { class: 'set-value' }, 'vulu Launcher v' + info.version)));

    body.replaceChildren(game, behaviour, look, account);
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

async function logout() {
    const accepted = await confirmDialog({ title: 'Çıkış yap', message: 'Hesaptan çıkış yapılsın mı?', okText: 'Çıkış yap', cancelText: 'Vazgeç' });
    if (!accepted) return;

    try { await bridge.invoke('auth.logout'); }
    catch (err) { await alertDialog({ title: 'Çıkış yapılamadı', message: err.message }); return; }

    $('music').pause();
    adminConnected = false;
    profile.me = null;
    profile.members = [];
    state.instances = [];
    state.selected = null;
    await showLogin();
}
