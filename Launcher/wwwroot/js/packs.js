// vulu Packs: adminin paylaştığı paketleri listeler, kurar ve günceller. Admin buradan siler,
// Oyna sayfasındaki "Paylaş" ile de kendi instance'ını yayınlar.
let packsLoading = false;

async function openPacksPage() {
    if (packsLoading) return;
    packsLoading = true;
    const grid = $('packs-grid'), status = $('packs-status');
    status.classList.remove('error');
    status.textContent = 'Yükleniyor…';
    if (!grid.childElementCount) grid.replaceChildren(...[0, 1, 2].map(() => el('div', { class: 'pack-tile skeleton' })));

    const slow = setTimeout(() => { if (status.textContent === 'Yükleniyor…') status.textContent = WAKING_TEXT; }, 4000);
    try {
        const result = await bridge.invoke('packs.list');
        renderPacks(result);
    } catch (err) {
        grid.replaceChildren();
        status.textContent = 'Paketler alınamadı: ' + err.message;
        status.classList.add('error');
    } finally {
        clearTimeout(slow);
        packsLoading = false;
    }
}

function renderPacks(result) {
    state.packsCache = result.packs;
    const grid = $('packs-grid'), status = $('packs-status');
    $('packs-request').hidden = !result.feedbackEnabled;
    $('packs-request-count').textContent = result.generalFeedback || '';
    $('packs-request-count').hidden = !result.generalFeedback;
    status.textContent = result.packs.length ? `${result.packs.length} paket` : '';
    if (!result.packs.length) {
        grid.replaceChildren(el('div', { class: 'packs-empty' },
            el('h2', {}, 'Henüz paylaşılan paket yok'),
            el('p', {}, result.isAdmin
                ? 'Oyna sekmesinde kendi instance\'ını seç ve "Paylaş"a bas.'
                : 'Admin bir paket paylaştığında burada görünecek.')));
        return;
    }

    grid.replaceChildren(...result.packs.map(pack => packTile(pack, result)));
}

function packTile(pack, result) {
    const cover = el('div', { class: 'pack-cover' + (pack.coverUrl ? '' : ' plain') });
    if (pack.coverUrl) cover.style.backgroundImage = `url("${pack.coverUrl}")`;
    else cover.append(el('span', {}, pack.name));

    const installed = pack.installed;
    const outdated = installed && installed.revision < pack.revision;
    const updated = parseDate(pack.updatedAt);

    let primary;
    if (!installed) primary = el('button', { class: 'btn btn-primary btn-sm', onclick: () => installPack(pack) }, 'Kur');
    else if (outdated) primary = el('button', { class: 'btn btn-primary btn-sm', onclick: () => updatePack(pack) }, 'Güncelle');
    else primary = el('button', { class: 'btn btn-sm', onclick: () => openInstalledPack(installed.instanceId) }, 'Kurulu · Aç');

    return el('article', { class: 'pack-tile' },
        cover,
        outdated && el('span', { class: 'pack-badge' }, 'Güncelleme var'),
        el('div', { class: 'pack-body' },
            el('h2', { class: 'pack-name' }, pack.name),
            el('div', { class: 'pack-meta' },
                [pack.author && ('Paylaşan: ' + pack.author), updated && relativeDate(updated), 'rev ' + pack.revision]
                    .filter(Boolean).join(' · ')),
            pack.description && el('p', { class: 'pack-desc-text' }, pack.description),
            el('div', { class: 'pack-tags' },
                el('span', { class: 'tag' }, 'Minecraft ' + pack.mcVersion),
                el('span', { class: 'tag' }, pack.loader === 'vanilla' ? 'Vanilla'
                    : `${LOADER_NAMES[pack.loader] || pack.loader} ${pack.loaderVersion || ''}`.trim()),
                pack.modCount > 0 && el('span', { class: 'tag' }, pack.modCount + ' mod'),
                ...contentTags(pack),
                pack.recommendedRamGb > 0 && el('span', { class: 'tag', title: 'Önerilen RAM' }, pack.recommendedRamGb + ' GB RAM'),
                el('span', { class: 'tag' }, formatSize(pack.sizeMb))),
            el('div', { class: 'pack-foot' },
                primary,
                result.feedbackEnabled && el('button', {
                    class: 'btn btn-sm btn-feedback', title: 'Geri bildirim ve istekler',
                    onclick: () => openFeedback(pack, result.isAdmin),
                },
                    fromMarkup('<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M21 12a8 8 0 01-11.6 7.1L4 20l1-4.6A8 8 0 1121 12z"/></svg>'),
                    'Geri bildirim',
                    pack.openFeedback > 0 && el('span', { class: 'count' }, pack.openFeedback)),
                el('button', { class: 'btn btn-sm', title: 'Bir arkadaşına mesajla gönder', onclick: () => shareCardWith(packInvite(pack)) }, 'Gönder'),
                result.isAdmin && el('button', { class: 'btn btn-sm btn-danger', onclick: () => deletePack(pack) }, 'Sil'))));
}

async function installPack(pack) {
    if (state.busy > 0 || state.gameRunning) { appendLog({ text: 'Şu an başka bir işlem sürüyor.', level: 'warn' }); return; }
    try {
        const result = await withActivity(() => bridge.invoke('packs.install', { id: pack.id }));
        await reloadInstances(result.id);
        if (state.page === 'packs') openPacksPage();
    } catch (err) {
        await alertDialog({ title: 'Kurulamadı', message: err.message });
    }
}

async function updatePack(pack) {
    const accepted = await confirmDialog({
        title: `${pack.name} güncellensin mi?`,
        message: `Paketin yeni sürümü (rev ${pack.revision}) kurulacak.\n\nPaketteki config dosyaları seninkilerin üzerine yazılır. `
            + 'Kendi eklediğin modlar ve dünyaların yerinde kalır.',
        okText: 'Güncelle', cancelText: 'Vazgeç',
    });
    if (!accepted) return;
    try {
        const result = await withActivity(() => bridge.invoke('packs.update', { id: pack.id }));
        await reloadInstances(result.id);
        if (state.page === 'packs') openPacksPage();
        refreshPackUpdates();
    } catch (err) {
        await alertDialog({ title: 'Güncellenemedi', message: err.message });
    }
}

async function openInstalledPack(instanceId) {
    await reloadInstances(instanceId);
    showPage('home');
}

async function deletePack(pack) {
    const accepted = await confirmDialog({
        title: 'Paketi kaldır',
        message: `'${pack.name}' vulu Packs'ten kaldırılsın mı?\nKuranların instance'ları silinmez ama artık güncelleme alamazlar.`,
        okText: 'Kaldır', cancelText: 'Vazgeç', danger: true,
    });
    if (!accepted) return;
    try { await bridge.invoke('packs.delete', { id: pack.id }); }
    catch (err) { await alertDialog({ title: 'Kaldırılamadı', message: err.message }); }
    openPacksPage();
}

/** Paketin mod dışındaki içerikleri: kaynak paketleri, shaderlar, KubeJS ve config. */
function contentTags(pack) {
    const has = folder => (pack.includes || []).some(p => p.toLowerCase() === folder);
    const tags = [];
    if (pack.resourcePackCount > 0) tags.push(pack.resourcePackCount + ' kaynak paketi');
    else if (has('resourcepacks')) tags.push('Kaynak paketleri');
    if (pack.shaderCount > 0) tags.push(pack.shaderCount + ' shader');
    else if (has('shaderpacks')) tags.push('Shader');
    if (has('kubejs')) tags.push('KubeJS');
    if (has('config') || has('defaultconfigs')) tags.push('Config');
    return tags.map(t => el('span', { class: 'tag' }, t));
}

// ── Paylaş (admin) ────────────────────────────────────────────────────
// Paylaş penceresinde klasör adlarının yanında görünen açıklamalar
const SHARE_LABELS = {
    'config': 'Mod ayarları', 'defaultconfigs': 'Varsayılan mod ayarları', 'kubejs': 'KubeJS betikleri',
    'mods': 'Modlar', 'resourcepacks': 'Kaynak paketleri', 'shaderpacks': 'Shaderlar', 'datapacks': 'Veri paketleri',
    'global_packs': 'Genel veri ve kaynak paketleri', 'openloader': 'OpenLoader paketleri', 'fancymenu_data': 'FancyMenu ekranları',
    'optionsshaders.txt': 'Seçili shader (OptiFine)', 'options.txt': 'Oyun ayarları', 'servers.dat': 'Sunucu listesi',
    'schematics': 'Şemalar', 'journeymap': 'JourneyMap ayarları', 'xaero': 'Xaero harita ayarları',
};
/** Yalnızca admin işaretli hesaplarda görünür; yetkiyi sunucu da ayrıca denetler. */
async function openShareDialog(instance) {
    let info;
    try { info = await bridge.invoke('packs.shareInfo', { instanceId: instance.id }); }
    catch (err) { await alertDialog({ title: 'Paylaşılamıyor', message: err.message }); return; }

    const published = await openModal(close => {
        let coverPath = '';
        const name = el('input', { type: 'text', value: info.name, maxLength: 40, spellcheck: false, autofocus: true });
        const description = el('textarea', {
            class: 'textarea', rows: 3, maxLength: 600, spellcheck: false,
            placeholder: 'Paket hakkında kısa bir açıklama: ne içeriyor, nasıl oynanıyor…',
        });
        description.value = info.description;

        const coverBox = el('button', { type: 'button', class: 'share-cover', title: 'Kapak resmi seç' },
            el('span', {}, info.existing && info.existing.hasCover ? 'Mevcut kapak kalacak · değiştirmek için tıkla' : 'Kapak resmi seç'));
        coverBox.addEventListener('click', async () => {
            try {
                const picked = await bridge.invoke('packs.pickCover');
                if (!picked.path) return;
                coverPath = picked.path;
                coverBox.style.backgroundImage = `url("${picked.url}")`;
                coverBox.classList.add('has-image');
            } catch (err) { error.textContent = err.message; }
        });

        const checks = info.entries.map(entry => {
            const box = el('input', { type: 'checkbox', checked: entry.checkedByDefault, value: entry.path });
            return {
                box,
                row: el('label', { class: 'share-entry' },
                    box,
                    el('span', { class: 'share-name' },
                        SHARE_LABELS[entry.path.toLowerCase()] && el('b', {}, SHARE_LABELS[entry.path.toLowerCase()]),
                        el('span', { class: 'share-path' }, entry.path + (entry.isDir ? '/' : ''))),
                    entry.note && el('span', { class: 'share-note' }, entry.note),
                    el('span', { class: 'share-size' }, `${entry.files} dosya · ${formatSize(entry.sizeMb)}`)),
            };
        });

        // Önerilen RAM: kuranların instance'ı bu değerle açılır (Ayarlar'da varsayılan RAM seçilmediyse)
        const ramOptions = info.ramOptionsGb || state.ramOptionsGb;
        let ramGb = ramOptions.includes(info.recommendedRamGb) ? info.recommendedRamGb : ramOptions[1] || ramOptions[0];
        const ramButtons = ramOptions.map(gb => el('button', {
            type: 'button', class: gb === ramGb ? 'active' : '',
            onclick: () => { ramGb = gb; ramButtons.forEach(b => b.classList.toggle('active', b.dataset.gb === String(gb))); },
            'data-gb': gb,
        }, gb + ' GB'));

        const error = el('div', { class: 'form-error' });
        const submit = el('button', { class: 'btn btn-primary', type: 'submit' },
            info.existing ? `Güncelle (rev ${info.existing.revision + 1})` : 'Yayınla');

        async function publish(event) {
            event.preventDefault();
            error.textContent = '';
            submit.disabled = true;
            try {
                const args = {
                    instanceId: instance.id,
                    name: name.value,
                    description: description.value,
                    includes: checks.filter(c => c.box.checked).map(c => c.box.value),
                    ramGb,
                    coverPath,
                    update: !!info.existing,
                };
                await withActivity(() => bridge.invoke('packs.publish', args));
                close(true);
            } catch (err) {
                error.textContent = err.message;
                submit.disabled = false;
            }
        }

        return el('form', { onsubmit: publish, style: 'display:contents' },
            el('div', { class: 'modal-head' }, info.existing ? `${info.name} paketini güncelle` : 'vulu Packs\'te paylaş'),
            el('div', { class: 'modal-body share-body' },
                el('div', { class: 'share-left' },
                    coverBox,
                    el('label', {}, 'Paket adı'), name,
                    el('label', {}, 'Açıklama'), description,
                    el('label', {}, 'Önerilen RAM'),
                    el('div', { class: 'segmented share-ram' }, ramButtons),
                    el('p', { class: 'hint' }, 'Paketi kuranların oyunu bu RAM ile açılır; istedikleri zaman değiştirebilirler.')),
                el('div', { class: 'share-right' },
                    el('div', { class: 'share-title' }, 'Pakete dahil edilecekler'),
                    el('p', { class: 'hint' }, 'Modrinth\'te bulunan modlar, kaynak paketleri ve shaderlar dosya yerine bağlantı olarak eklenir; '
                        + 'kuranlar Modrinth\'ten indirir. Açık kaynak paketleri ve seçili shader kuranlarda da açık gelir. '
                        + 'Görev kitabı ve özel tarifler genelde config/ ve kubejs/ içindedir.'),
                    el('div', { class: 'share-list' }, checks.length
                        ? checks.map(c => c.row)
                        : el('p', { class: 'hint' }, 'Paylaşılacak dosya bulunamadı.')),
                    error)),
            el('div', { class: 'modal-foot' },
                el('button', { class: 'btn', type: 'button', onclick: () => close(false) }, 'Vazgeç'),
                submit));
    }, { wide: true });

    if (published) {
        await reloadInstances(instance.id);
        appendLog({ text: `${instance.name} paylaşıldı. vulu Packs sekmesinde görünüyor.`, level: 'ok' });
    }
}

function setupPacksPage() {
    $('packs-refresh').addEventListener('click', openPacksPage);
    $('packs-request').addEventListener('click', () => openFeedback(null, state.isAdmin));
}
