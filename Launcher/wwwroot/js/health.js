// Oyunun sağlığı: açılmadan önce eksik bağımlılık penceresi, çöktükten sonra sade bir çökme özeti.

/**
 * Eksik bağımlılıklar: hangi modun neyi istediği ve Modrinth karşılığı.
 * Dönen değer: 'install' (eksikleri kur ve oyna) | 'ignore' (yine de oyna) | undefined (vazgeç)
 */
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
                    el('b', {}, m.title || m.id),
                    el('span', {}, `İsteyen: ${m.requiredBy.join(', ')}`),
                    !m.projectId && el('span', { class: 'health-warn' }, 'Modrinth\'te bulunamadı; elle kurman gerekiyor.')))))),
        el('div', { class: 'modal-foot' },
            el('button', { class: 'btn', onclick: () => close(undefined) }, 'Vazgeç'),
            el('button', { class: 'btn', onclick: () => close('ignore') }, 'Yine de oyna'),
            canInstall && el('button', { class: 'btn btn-primary', onclick: () => close('install') },
                installable.length === missing.length ? 'Eksikleri kur ve oyna' : `Bulunanları kur (${installable.length}) ve oyna`)),
    ], { wide: false });
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
                el('button', { class: 'btn', onclick: () => bridge.invoke('files.open', { target: 'instance', id: crash.instanceId }) }, 'Klasörü aç'),
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
    play();
}
