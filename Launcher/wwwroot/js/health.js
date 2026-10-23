// Oyunun sağlığı: açılmadan önce eksik bağımlılık penceresi, çöktükten sonra sade bir çökme özeti.

/**
 * Eksik bağımlılıklar: hangi modun neyi istediği ve Modrinth karşılığı.
 * Dönen değer: 'install' (eksikleri kur ve oyna) | 'ignore' (yine de oyna) | undefined (vazgeç)
 */
/** CurseForge sayfaları cf.openPage ile, diğerleri (Modrinth) tarayıcıda açılır. */
function openContentLink(url) {
    if (!url) return;
    const command = /^https:\/\/(www\.)?curseforge\.com\//.test(url) ? 'cf.openPage' : 'links.open';
    bridge.invoke(command, { url }).catch(() => { });
}

function missingDepsDialog(missing, instance) {
    const installable = missing.filter(m => m.projectId);
    const canInstall = instance.kind === 'custom' && installable.length > 0;
    return openModal(close => [
        el('div', { class: 'modal-head' }, 'Eksik modlar var'),
        el('div', { class: 'modal-body health-body' },
            el('p', { class: 'hint' }, missing.length === 1
                ? 'Bir mod, kurulu olmayan başka bir modu istiyor. Oyun bu hâliyle büyük ihtimalle "Error loading mods" hatasıyla açılmaz.'
                : `${missing.length} mod eksik. Oyun bu hâliyle büyük ihtimalle "Error loading mods" hatasıyla açılmaz.`),
            el('div', { class: 'health-list' }, missing.map(m => el('div', { class: 'health-item' },
                m.iconUrl ? el('img', { class: 'health-icon', src: m.iconUrl, alt: '' }) : el('span', { class: 'health-icon' }),
                el('div', { class: 'health-text' },
                    el('b', {}, m.title || m.id, m.source && el('span', { class: 'badge' }, m.source === 'curseforge' ? 'CurseForge' : 'Modrinth')),
                    el('span', {}, `İsteyen: ${m.requiredBy.join(', ')}`),
                    !m.projectId && el('span', { class: 'health-warn' }, 'CurseForge\'ta da Modrinth\'te de bulunamadı; elle kurman gerekiyor.')),
                // Bulunduysa projenin sayfası, bulunamadıysa CurseForge araması
                el('button', {
                    class: 'link-btn', title: m.pageUrl || m.searchUrl,
                    onclick: () => openContentLink(m.pageUrl || m.searchUrl),
                }, m.pageUrl ? 'Sayfa ↗' : 'Ara ↗'))))),
        el('div', { class: 'modal-foot' },
            el('button', { class: 'btn', onclick: () => close(undefined) }, 'Vazgeç'),
            el('button', { class: 'btn', onclick: () => close('ignore') }, 'Yine de oyna'),
            canInstall && el('button', { class: 'btn btn-primary', onclick: () => close('install') },
                installable.length === missing.length ? 'Eksikleri kur ve oyna' : `Bulunanları kur (${installable.length}) ve oyna`)),
    ], { wide: false });
}

/**
 * Oyun açılmadan önce bulunan dosya sorunları: boş/bozuk jar'lar, aynı dosyanın kopyaları ve yerine konmamış elle
 * indirilecekler. 'retry' (düzeltildi, yeniden dene), 'ignore' (yine de oyna) ya da undefined döner.
 */
function fileProblemsDialog(files, instance) {
    const bad = [...files.broken.map(b => ({ file: b.file, text: b.reason })),
        ...files.duplicates.map(d => ({ file: d.file, text: d.reason === 'version'
            ? `${d.copyOf} ile aynı mod; aynı modun iki sürümü birlikte yüklenemez.`
            : `${d.copyOf} dosyasının kopyası; aynı mod iki kez yüklenir.` }))];
    const canFix = instance.kind === 'custom';
    return openModal(close => {
        const sections = [];
        if (bad.length) {
            const move = canFix && el('button', { class: 'btn btn-sm' }, 'Yedeğe taşı');
            if (move) move.onclick = async () => {
                move.disabled = true;
                try {
                    await bridge.invoke('mods.backupFiles', { instanceId: instance.id, files: bad.map(b => b.file) });
                    move.textContent = '✓ mods-yedek klasörüne taşındı';
                } catch (err) { move.disabled = false; move.textContent = err.message; }
            };
            sections.push(el('div', { class: 'health-section' },
                el('div', { class: 'health-section-head' }, el('b', {}, 'Sorunlu dosyalar'), move),
                el('div', { class: 'health-list' }, bad.map(b => el('div', { class: 'health-item' },
                    el('span', { class: 'crash-mark', 'aria-hidden': 'true' }, '!'),
                    el('div', { class: 'health-text' }, el('b', {}, b.file.replace(/^mods\//, '')), el('span', {}, b.text)))))));
        }
        if (files.manual.length) {
            const open = el('button', { class: 'btn btn-sm' }, 'Elle indir…');
            open.onclick = async () => { if (await showManualDownloads(files.manual, canFix ? instance.id : null)) open.textContent = '✓ Hepsi yerinde'; };
            sections.push(el('div', { class: 'health-section' },
                el('div', { class: 'health-section-head' }, el('b', {}, 'Elle indirilmesi gerekenler'), open),
                el('div', { class: 'health-list' }, files.manual.map(m => el('div', { class: 'health-item' },
                    m.iconUrl ? el('img', { class: 'health-icon', src: m.iconUrl, alt: '' }) : el('span', { class: 'health-icon' }),
                    el('div', { class: 'health-text' }, el('b', {}, m.title), el('span', {}, m.fileName)))))));
        }
        return [
            el('div', { class: 'modal-head' }, 'Mod dosyalarında sorun var'),
            el('div', { class: 'modal-body health-body' },
                el('p', { class: 'hint' }, 'Oyun bu dosyalarla büyük ihtimalle açılmaz. Sorunlu dosyalar silinmez, instance klasöründeki mods-yedek klasörüne taşınır.'),
                ...sections),
            el('div', { class: 'modal-foot' },
                el('button', { class: 'btn', onclick: () => close(undefined) }, 'Vazgeç'),
                el('button', { class: 'btn', onclick: () => close('ignore') }, 'Yine de oyna'),
                el('button', { class: 'btn btn-primary', onclick: () => close('retry') }, 'Tekrar dene')),
        ];
    }, { wide: true });
}

/**
 * "Dosyaları doğrula": içerikleri, mod dosyalarını ve oyun dosyalarını denetler, bozuk ya da eksik olanları onarır,
 * sonra ne yapıldığını gösterir.
 */
async function verifyInstance(instance) {
    if (state.busy > 0 || state.gameRunning || launching) { appendLog({ text: 'Şu an başka bir işlem sürüyor.', level: 'warn' }); return; }
    let result;
    try { result = await withActivity(() => bridge.invoke('instances.verify', { id: instance.id })); }
    catch (err) { await alertDialog({ title: 'Doğrulanamadı', message: err.message }); return; }
    loadDetails();

    const fixed = result.repaired.length + result.moved.length;
    const clean = !fixed && !result.gameError && !result.missing.length && !result.manual.length && !result.notes.length;
    const line = (mark, text, detail) => el('div', { class: 'health-item' },
        el('span', { class: 'verify-mark ' + mark, 'aria-hidden': 'true' }, mark === 'ok' ? '✓' : '!'),
        el('div', { class: 'health-text' }, el('b', {}, text), detail && el('span', {}, detail)));
    const items = [
        line('ok', result.checkedCount ? `${result.checkedCount} içerik denetlendi` : 'İçerik listesi denetlendi',
            result.repaired.length ? `Yeniden indirilenler: ${result.repaired.join(', ')}` : 'Hepsi sağlam.'),
        result.moved.length > 0 && line('ok', `${result.moved.length} sorunlu dosya mods-yedek klasörüne taşındı`,
            result.moved.map(f => f.replace(/^mods\//, '')).join(', ')),
        line(result.gameError ? 'warn' : 'ok', result.gameError ? 'Oyun dosyaları denetlenemedi' : 'Oyun dosyaları denetlendi', result.gameError
            || 'Minecraft, mod yükleyici ve kütüphaneler; eksik ya da bozuk olan yeniden indirildi.'),
        ...result.notes.map(n => line('warn', 'Onarılamadı', n)),
        result.missing.length > 0 && line('warn', 'Eksik bağımlılıklar', `${result.missing.join(', ')} — oyunu açarken kurmanı önerecek.`),
        result.manual.length > 0 && line('warn', `${result.manual.length} dosya elle indirilmeli`, result.manual.map(m => m.title).join(', ')),
    ].filter(Boolean);

    const choice = await openModal(close => [
        el('div', { class: 'modal-head' }, clean ? `${instance.name}: sorun yok` : `${instance.name} doğrulandı`),
        el('div', { class: 'modal-body health-body' },
            el('p', { class: 'hint' }, clean ? 'Dosyalarda bir sorun bulunmadı.'
                : fixed ? 'Bulunan sorunlar onarıldı. Aşağıda kalanlar varsa onlara bak.' : 'Bazı şeyler kendiliğinden onarılamadı:'),
            el('div', { class: 'health-list' }, items)),
        el('div', { class: 'modal-foot' },
            result.manual.length > 0 && el('button', { class: 'btn', onclick: () => close('manual') }, 'Elle indir…'),
            el('button', { class: 'btn btn-primary', onclick: () => close() }, 'Tamam')),
    ].filter(Boolean));
    if (choice === 'manual') { await showManualDownloads(result.manual, instance.kind === 'custom' ? instance.id : null); loadDetails(); }
}

// ── Çökme özeti ───────────────────────────────────────────────────────
const CRASH_ACTIONS = {
    install: a => bridge.invoke('mods.installMissing', { instanceId: a.instanceId, projectIds: a.projectIds }),
    update: a => bridge.invoke('mods.update', { instanceId: a.instanceId, projectId: a.projectId, versionId: a.versionId }),
    disable: a => bridge.invoke('instances.setContentEnabled', { id: a.instanceId, file: a.file, enabled: false }),
};

/** Oyun çöktüğünde: nedenler, öneriler ve tek tıklık çözümler. */
function showCrashDialog(crash) {
    let changed = false;
    openModal(close => {
        const replay = el('button', { class: 'btn btn-primary', hidden: true, onclick: () => { close(); selectAndPlay(crash.instanceId); } }, 'Tekrar oyna');

        const items = crash.findings.map(f => el('div', { class: 'health-item crash-item' },
            el('span', { class: 'crash-mark', 'aria-hidden': 'true' }, '!'),
            el('div', { class: 'health-text' },
                el('b', {}, f.title),
                el('span', {}, f.detail),
                el('span', { class: 'crash-fix' }, 'Öneri: ' + f.fix),
                f.actions.length > 0 && el('div', { class: 'crash-actions' }, f.actions.map(action => {
                    const button = el('button', { class: 'btn btn-sm' + (action.type === 'disable' ? '' : ' btn-accent') }, action.label);
                    button.addEventListener('click', async () => {
                        button.disabled = true;
                        try {
                            await withActivity(() => CRASH_ACTIONS[action.type]({ ...action, instanceId: crash.instanceId }));
                            button.textContent = action.label + ' ✓';
                            changed = true;
                            replay.hidden = false;
                            loadDetails();
                        } catch (err) {
                            button.disabled = false;
                            appendLog({ text: err.message, level: 'error' });
                        }
                    });
                    return button;
                })))));

        return [
            el('div', { class: 'modal-head' }, `${crash.name} çöktü`),
            el('div', { class: 'modal-body health-body' },
                el('p', { class: 'hint' }, crash.findings.length === 1 && crash.findings[0].kind === 'unknown'
                    ? 'Oyun beklenmedik şekilde kapandı.'
                    : 'Oyunun kapanma nedeni büyük ihtimalle şu:'),
                el('div', { class: 'health-list' }, items)),
            el('div', { class: 'modal-foot' },
                crash.report && el('button', { class: 'btn', onclick: () => bridge.invoke('crash.openReport', { instanceId: crash.instanceId, file: crash.report }).catch(err => appendLog({ text: err.message, level: 'error' })) }, 'Çökme raporunu aç'),
                isDesktopApp() && el('button', { class: 'btn', onclick: () => bridge.invoke('files.open', { target: 'instance', id: crash.instanceId }) }, 'Klasörü aç'),
                el('span', { class: 'spacer' }),
                el('button', { class: 'btn', onclick: () => close() }, 'Kapat'),
                replay),
        ];
    }, { wide: true }).then(() => { if (changed) reloadInstances(state.selected && state.selected.id); });
}

/** Çökme penceresindeki "Tekrar oyna": ilgili instance seçilip oyun açılır. */
function selectAndPlay(instanceId) {
    const instance = state.instances.find(i => i.id === instanceId);
    if (!instance) return;
    if (!state.selected || state.selected.id !== instanceId) selectInstance(instance);
    showPage('home');
    const last = state.lastLaunch;
    play({ join: last && last.instanceId === instanceId ? last.join : '' });
}

// ── Dünyalar ve yedekler ──────────────────────────────────────────────
function openWorldsDialog(instance) {
    return openModal(close => {
        const body = el('div', { class: 'admin-body' });

        async function act(fn, okText) {
            try { await withActivity(fn); if (okText) appendLog({ text: okText, level: 'ok' }); }
            catch (err) { await alertDialog({ title: 'Olmadı', message: err.message }); }
            render();
        }

        async function render() {
            body.replaceChildren(el('p', { class: 'admin-toolbar' }, 'Yükleniyor...'));
            let data;
            try { data = await bridge.invoke('worlds.list', { id: instance.id }); }
            catch (err) { body.replaceChildren(el('p', { class: 'form-error' }, err.message)); return; }

            const toolbar = el('div', { class: 'admin-toolbar' },
                el('span', {}, data.worlds.length ? `${data.worlds.length} dünya` : ''),
                el('button', { class: 'btn btn-sm', onclick: () => bridge.invoke('worlds.openBackups', { id: instance.id }) }, 'Yedek klasörünü aç'));
            const rows = data.worlds.map(w => el('div', { class: 'world' },
                el('div', { class: 'world-head' },
                    w.icon ? el('img', { class: 'world-icon', src: w.icon, alt: '' }) : el('span', { class: 'world-icon plain' }),
                    el('div', { class: 'world-text' },
                        el('b', {}, w.name),
                        el('span', {}, `Son oynanma: ${relativeDate(new Date(w.lastPlayed)).toLowerCase()} · ${w.sizeMb} MB`)),
                    el('button', { class: 'btn btn-sm btn-primary', disabled: data.running, title: data.running ? 'Önce oyunu kapat' : null,
                        onclick: () => act(() => bridge.invoke('worlds.backup', { id: instance.id, world: w.name }), `${w.name} yedeklendi.`) }, 'Yedekle')),
                w.backups.length > 0 && el('div', { class: 'world-backups' }, w.backups.map(b => el('div', { class: 'world-backup' },
                    el('span', {}, `${relativeDate(new Date(b.createdAt))} · ${b.sizeMb} MB` + (b.auto ? ' · otomatik' : b.name.includes('-geri-yukleme-oncesi-') ? ' · geri yükleme öncesi' : '')),
                    el('button', { class: 'link-btn', disabled: data.running, onclick: async () => {
                        if (!await confirmDialog({ title: 'Geri yükle', message: `'${w.name}' bu yedekteki hâline döndürülsün mü? Şimdiki hâli önce ayrıca yedeklenir.`, okText: 'Geri yükle', cancelText: 'Vazgeç' })) return;
                        act(() => bridge.invoke('worlds.restore', { id: instance.id, world: w.name, backup: b.name }), `${w.name} geri yüklendi.`);
                    } }, 'Geri yükle'),
                    el('button', { class: 'link-btn danger', onclick: async () => {
                        if (!await confirmDialog({ title: 'Yedeği sil', message: 'Bu yedek kalıcı olarak silinsin mi?', okText: 'Sil', cancelText: 'Vazgeç', danger: true })) return;
                        act(() => bridge.invoke('worlds.deleteBackup', { id: instance.id, world: w.name, backup: b.name }));
                    } }, 'Sil'))))));

            body.replaceChildren(...[toolbar,
                data.running && el('p', { class: 'hint' }, 'Oyun açıkken yedekleme ve geri yükleme yapılamaz.'),
                ...(rows.length ? rows : [el('p', { class: 'pack-empty' }, 'Bu instance\'ta henüz dünya yok.')]),
                el('p', { class: 'hint' }, 'Paket güncellenmeden önce dünyalar otomatik yedeklenir; her dünyanın son 5 otomatik yedeği tutulur.')].filter(Boolean));
        }

        queueMicrotask(render);
        return [
            el('div', { class: 'modal-head' }, `${instance.name} — dünyalar`, el('button', { class: 'link-btn', onclick: () => close() }, 'Kapat')),
            body,
        ];
    }, { wide: true });
}

async function duplicateInstance(instance) {
    const name = await promptDialog({ title: 'Instance\'ı çoğalt', label: 'Kopyanın adı', value: `${instance.name} (kopya)`, okText: 'Çoğalt' });
    if (!name) return;
    try {
        const result = await withActivity(() => bridge.invoke('instances.duplicate', { id: instance.id, name }));
        await reloadInstances(result.id);
    } catch (err) { await alertDialog({ title: 'Çoğaltılamadı', message: err.message }); }
}

async function exportMrpack(instance) {
    try {
        const result = await withActivity(() => bridge.invoke('instances.exportMrpack', { id: instance.id }));
        appendLog({ text: `Kaydedildi: ${result.path}`, level: 'ok' });
    } catch (err) { await alertDialog({ title: 'Dışa aktarılamadı', message: err.message }); }
}
