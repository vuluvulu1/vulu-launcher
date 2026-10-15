// Modrinth sayfası: mod / modpack / kaynak paketi / shader arama, filtreleme, sıralama ve kurma.
const MR_TYPES = [
    { key: 'mod', label: 'Modlar' },
    { key: 'modpack', label: 'Modpackler' },
    { key: 'resourcepack', label: 'Kaynak paketleri' },
    { key: 'shader', label: 'Shaderlar' },
];

const MR_SORTS = {
    '': 'Otomatik',
    relevance: 'İlgi düzeyi',
    downloads: 'İndirme sayısı',
    follows: 'Takipçi sayısı',
    newest: 'En yeni',
    updated: 'Son güncellenen',
};

const MR_HEADERS = {
    categories: 'Kategoriler',
    features: 'Özellikler',
    resolutions: 'Çözünürlük',
    'performance impact': 'Performans etkisi',
};

// Modrinth kategori adları İngilizce gelir; bilinenler çevrilir, kalanı olduğu gibi gösterilir
const MR_CATEGORY_NAMES = {
    adventure: 'Macera', cursed: 'Tuhaf', decoration: 'Dekorasyon', economy: 'Ekonomi', equipment: 'Ekipman', food: 'Yemek',
    'game-mechanics': 'Oyun mekanikleri', library: 'Kütüphane', magic: 'Büyü', management: 'Yönetim', minigame: 'Mini oyun',
    mobs: 'Yaratıklar', optimization: 'Optimizasyon', social: 'Sosyal', storage: 'Depolama', technology: 'Teknoloji',
    transportation: 'Ulaşım', utility: 'Araçlar', worldgen: 'Dünya üretimi',
    challenging: 'Zorlu', combat: 'Savaş', 'kitchen-sink': 'Her şeyden biraz', lightweight: 'Hafif', multiplayer: 'Çok oyunculu', quests: 'Görevler',
    modded: 'Modlu', realistic: 'Gerçekçi', simplistic: 'Sade', themed: 'Temalı', tweaks: 'İnce ayar', 'vanilla-like': 'Vanilla benzeri',
    audio: 'Ses', blocks: 'Bloklar', 'core-shaders': 'Çekirdek shader', entities: 'Varlıklar', environment: 'Ortam', fonts: 'Yazı tipi',
    gui: 'Arayüz', items: 'Eşyalar', locale: 'Dil', models: 'Modeller',
    cartoon: 'Çizgi film', fantasy: 'Fantastik', 'semi-realistic': 'Yarı gerçekçi',
    atmosphere: 'Atmosfer', bloom: 'Parlama', 'colored-lighting': 'Renkli ışık', foliage: 'Bitki örtüsü', 'path-tracing': 'Path tracing',
    pbr: 'PBR', reflections: 'Yansımalar', shadows: 'Gölgeler',
    high: 'Yüksek', medium: 'Orta', low: 'Düşük', potato: 'Çok düşük', screenshot: 'Ekran görüntüsü',
};

const modrinth = {
    type: 'mod',
    targetId: '',
    sort: '',
    mcVersion: '',           // yalnızca modpack aramasında
    loader: '',              // yalnızca modpack aramasında
    openSource: false,
    categories: {},          // tür → seçili kategori adları (Set)
    filtersOpen: false,
    filterData: {},          // tür → sunucudan gelen filtre seçenekleri
    offset: 0,
    total: 0,
    requestId: 0,
    ready: false,
};

function categoryLabel(name) {
    return MR_CATEGORY_NAMES[name] || name.charAt(0).toUpperCase() + name.slice(1);
}

function formatCount(count) {
    if (count >= 1e6) return (count / 1e6).toFixed(1).replace('.0', '') + ' Mn';
    if (count >= 1e3) return (count / 1e3).toFixed(1).replace('.0', '') + ' B';
    return String(Math.round(count));
}

function formatAgo(iso) {
    if (!iso) return '';
    const days = Math.floor((Date.now() - new Date(iso).getTime()) / 86400000);
    if (days < 1) return 'bugün';
    if (days < 30) return `${days} gün önce`;
    if (days < 365) return `${Math.floor(days / 30)} ay önce`;
    return `${Math.floor(days / 365)} yıl önce`;
}

function customInstances() {
    return state.instances.filter(i => i.kind === 'custom');
}

function selectedCategories() {
    return modrinth.categories[modrinth.type] || (modrinth.categories[modrinth.type] = new Set());
}

/** Bir instance'ın "Mod ekle" düğmesinden gelindiğinde o instance hedef seçili açılır. */
function openModrinthFor(instance) {
    modrinth.targetId = instance.id;
    if (modrinth.type === 'modpack') modrinth.type = 'mod';
    showPage('modrinth');
}

function setupModrinthPage() {
    modrinth.ready = true;

    let timer = null;
    $('mr-query').addEventListener('input', () => {
        clearTimeout(timer);
        timer = setTimeout(() => searchModrinth(false), 350);
    });
    $('mr-query').addEventListener('keydown', event => {
        if (event.key === 'Enter') { clearTimeout(timer); searchModrinth(false); }
    });

    $('mr-sort').replaceChildren(...Object.entries(MR_SORTS).map(([key, label]) => el('option', { value: key }, label)));
    $('mr-sort').addEventListener('change', () => { modrinth.sort = $('mr-sort').value; searchModrinth(false); });
    $('mr-target').addEventListener('change', () => { modrinth.targetId = $('mr-target').value; searchModrinth(false); });
    $('mr-version').addEventListener('change', () => { modrinth.mcVersion = $('mr-version').value; searchModrinth(false); });
    $('mr-loader').addEventListener('change', () => { modrinth.loader = $('mr-loader').value; searchModrinth(false); });
    $('mr-filter-toggle').addEventListener('click', () => { modrinth.filtersOpen = !modrinth.filtersOpen; renderModrinthFilters(); });
    $('mr-more').addEventListener('click', () => searchModrinth(true));
}

async function openModrinthPage() {
    if (!modrinth.ready) setupModrinthPage();

    $('mr-types').replaceChildren(...MR_TYPES.map(type => el('button', {
        class: type.key === modrinth.type ? 'active' : '',
        onclick: () => { modrinth.type = type.key; openModrinthPage(); },
    }, type.label)));

    // Modpack kendi instance'ını oluşturur; diğer içerikler var olan bir instance'a kurulur
    const customs = customInstances();
    const isPack = modrinth.type === 'modpack';
    $('mr-target-wrap').hidden = isPack || customs.length === 0;
    $('mr-version-wrap').hidden = !isPack;
    $('mr-loader-wrap').hidden = !isPack;
    $('mr-sort').value = modrinth.sort;

    if (!isPack) {
        if (!customs.some(i => i.id === modrinth.targetId))
            modrinth.targetId = state.selected && state.selected.kind === 'custom' ? state.selected.id : (customs[0] ? customs[0].id : '');
        $('mr-target').replaceChildren(...customs.map(i =>
            el('option', { value: i.id, selected: i.id === modrinth.targetId }, `${i.name} (${loaderLabel(i)})`)));
    }

    renderModrinthFilters();
    searchModrinth(false);

    // Filtre seçenekleri tür başına bir kez alınır
    if (!modrinth.filterData[modrinth.type]) {
        const type = modrinth.type;
        try {
            modrinth.filterData[type] = await bridge.invoke('modrinth.filters', { type });
            if (type === modrinth.type) renderModrinthFilters();
        } catch { /* filtreler olmadan da arama çalışır */ }
    }
}

function renderModrinthFilters() {
    const data = modrinth.filterData[modrinth.type];
    const selected = selectedCategories();
    const activeCount = selected.size + (modrinth.openSource ? 1 : 0);

    const toggle = $('mr-filter-toggle');
    toggle.textContent = activeCount > 0 ? `Filtreler (${activeCount})` : 'Filtreler';
    toggle.classList.toggle('btn-primary', activeCount > 0);

    if (data && modrinth.type === 'modpack') {
        if ($('mr-version').options.length === 0) {
            $('mr-version').replaceChildren(el('option', { value: '' }, 'Tüm sürümler'),
                ...data.minecraftVersions.map(version => el('option', { value: version }, version)));
            $('mr-loader').replaceChildren(el('option', { value: '' }, 'Tümü'),
                ...data.loaders.map(loader => el('option', { value: loader }, LOADER_NAMES[loader] || loader)));
        }
        $('mr-version').value = modrinth.mcVersion;
        $('mr-loader').value = modrinth.loader;
    }

    const panel = $('mr-filters');
    panel.hidden = !modrinth.filtersOpen;
    if (!modrinth.filtersOpen) return;

    if (!data) { panel.replaceChildren(el('p', { class: 'page-note' }, 'Filtreler yükleniyor...')); return; }

    const groups = data.categories.map(group => el('div', { class: 'mr-filter-group' },
        el('div', { class: 'mr-filter-title' }, MR_HEADERS[group.header] || group.header),
        el('div', { class: 'chips' }, ...group.names.map(name => el('button', {
            class: 'chip' + (selected.has(name) ? ' active' : ''),
            onclick: () => {
                if (selected.has(name)) selected.delete(name); else selected.add(name);
                renderModrinthFilters();
                searchModrinth(false);
            },
        }, categoryLabel(name))))));

    const openSource = el('input', { type: 'checkbox', checked: modrinth.openSource });
    openSource.addEventListener('change', () => { modrinth.openSource = openSource.checked; renderModrinthFilters(); searchModrinth(false); });

    panel.replaceChildren(...groups,
        el('div', { class: 'mr-filter-foot' },
            el('label', { class: 'check' }, openSource, el('span', {}, 'Yalnızca açık kaynak')),
            el('button', {
                class: 'link-btn', disabled: activeCount === 0,
                onclick: () => { selected.clear(); modrinth.openSource = false; renderModrinthFilters(); searchModrinth(false); },
            }, 'Filtreleri temizle')));
}

async function searchModrinth(append) {
    const results = $('mr-results'), status = $('mr-status'), more = $('mr-more');
    const isPack = modrinth.type === 'modpack';

    if (!isPack && !modrinth.targetId) {
        results.replaceChildren();
        more.hidden = true;
        status.replaceChildren(
            'İçerik kurmak için önce bir instance gerekli. ',
            el('button', { class: 'link-btn', onclick: () => openNewInstanceDialog() }, 'Yeni instance oluştur'),
            ' ya da Modpackler sekmesinden hazır bir paket kur.');
        return;
    }

    const requestId = ++modrinth.requestId;
    if (!append) { modrinth.offset = 0; status.textContent = 'Aranıyor...'; }
    more.disabled = true;

    let result;
    try {
        result = await bridge.invoke('modrinth.search', {
            query: $('mr-query').value.trim(),
            type: modrinth.type,
            instanceId: isPack ? '' : modrinth.targetId,
            mcVersion: isPack ? modrinth.mcVersion : '',
            loader: isPack ? modrinth.loader : '',
            sort: modrinth.sort,
            categories: [...selectedCategories()],
            openSource: modrinth.openSource,
            offset: modrinth.offset,
        });
    } catch (err) {
        if (requestId !== modrinth.requestId) return;
        status.textContent = 'Arama başarısız: ' + err.message;
        more.disabled = false;
        return;
    }
    if (requestId !== modrinth.requestId) return; // daha yeni bir arama başladı

    modrinth.total = result.total;
    modrinth.offset += result.hits.length;

    const rows = result.hits.map(renderModrinthHit);
    if (append) results.append(...rows);
    else { results.replaceChildren(...rows); $('page-modrinth').scrollTop = 0; }

    const target = state.instances.find(i => i.id === modrinth.targetId);
    const count = `${result.total.toLocaleString('tr-TR')} sonuç`;
    status.textContent = result.total === 0
        ? 'Sonuç bulunamadı. Filtreleri gevşetmeyi dene.'
        : !isPack && target
            ? `${count} · ${target.name} ile uyumlu olanlar (Minecraft ${target.mcVersion}${target.loader !== 'vanilla' && modrinth.type === 'mod' ? ', ' + LOADER_NAMES[target.loader] : ''})`
            : count;

    more.hidden = modrinth.offset >= modrinth.total || result.hits.length === 0;
    more.disabled = false;
}

/** Kurulumu çalıştırır; sonucu düğmenin üzerinde gösterir. versionId boşsa en yeni uyumlu sürüm kurulur. */
async function installModrinthHit(hit, button, versionId) {
    const original = button.textContent;
    button.disabled = true;
    button.textContent = 'Kuruluyor…';

    try {
        if (hit.type === 'modpack') {
            const result = await withActivity(() =>
                bridge.invoke('modrinth.installModpack', { projectId: hit.id, versionId: versionId || '' }));
            if (!result.success) throw new Error(result.error);
            await reloadInstances(result.id);
            showPage('home');
            return;
        }

        const result = await withActivity(() =>
            bridge.invoke('modrinth.install', { instanceId: modrinth.targetId, projectId: hit.id, versionId: versionId || '' }));
        if (!result.success) throw new Error(result.error);

        button.classList.remove('btn-primary');
        button.textContent = 'Kuruldu ✓';
        // Bağımlılık olarak başka şeyler de kurulduysa bildir
        if (result.installed.length > 1)
            $('mr-status').textContent = `Kuruldu: ${result.installed.join(', ')}`;
    } catch (err) {
        button.textContent = original;
        await alertDialog({ title: 'Kurulamadı', message: err.message });
    }
    button.disabled = false;
}

/** Kurulacak sürümü seçtirir. Seçilen sürümün kimliğini döner; vazgeçilirse undefined. */
function pickModrinthVersion(hit) {
    const TYPE_NAMES = { release: 'Kararlı', beta: 'Beta', alpha: 'Alfa' };

    return openModal(close => {
        const list = el('div', { class: 'admin-body' }, el('p', { class: 'admin-toolbar' }, 'Sürümler yükleniyor...'));

        bridge.invoke('modrinth.versions', { projectId: hit.id, instanceId: hit.type === 'modpack' ? '' : modrinth.targetId })
            .then(versions => {
                if (versions.length === 0) {
                    list.replaceChildren(el('p', { class: 'pack-empty' }, 'Uyumlu sürüm bulunamadı.'));
                    return;
                }
                list.replaceChildren(...versions.map(version => el('div', { class: 'user-row' },
                    el('div', {},
                        el('div', { class: 'uname' }, version.name || version.number,
                            version.type !== 'release' && el('span', { class: 'badge' }, TYPE_NAMES[version.type] || version.type)),
                        el('div', { class: 'meta' }, [
                            version.number,
                            version.gameVersions.slice(-3).join(', '),
                            version.loaders.map(l => LOADER_NAMES[l] || l).join(', '),
                            formatAgo(version.published),
                        ].filter(Boolean).join(' · '))),
                    el('span', {}),
                    el('button', { class: 'btn btn-sm btn-primary', onclick: () => close(version.id) }, 'Bu sürümü kur'))));
            })
            .catch(err => list.replaceChildren(el('p', { class: 'form-error' }, 'Sürümler alınamadı: ' + err.message)));

        return [
            el('div', { class: 'modal-head' }, `${hit.title} — sürüm seç`,
                el('button', { class: 'link-btn', onclick: () => close() }, 'Kapat')),
            list,
        ];
    }, { wide: true });
}

function renderModrinthHit(hit) {
    const isPack = hit.type === 'modpack';
    const button = el('button', { class: 'btn btn-sm ' + (hit.installed ? '' : 'btn-primary') },
        isPack ? 'Instance olarak kur' : hit.installed ? 'Güncelle' : 'Kur');
    button.addEventListener('click', () => installModrinthHit(hit, button, ''));

    const tags = [
        ...hit.categories.map(name => el('span', { class: 'chip static' }, categoryLabel(name))),
        ...(hit.type === 'mod' || isPack ? hit.loaders.map(name => el('span', { class: 'chip static loader' }, LOADER_NAMES[name] || name)) : []),
    ];

    return el('div', { class: 'mr-hit' },
        hit.iconUrl ? el('img', { class: 'mr-icon', src: hit.iconUrl, alt: '', loading: 'lazy' }) : el('div', { class: 'mr-icon plain' }),
        el('div', { class: 'mr-text' },
            el('div', { class: 'mr-title' }, hit.title, el('span', { class: 'mr-author' }, hit.author)),
            el('div', { class: 'mr-desc' }, hit.description),
            el('div', { class: 'mr-meta' },
                el('span', {}, `↓ ${formatCount(hit.downloads)}`),
                el('span', {}, `♥ ${formatCount(hit.follows)}`),
                hit.updated && el('span', {}, `Güncelleme: ${formatAgo(hit.updated)}`),
                ...tags)),
        el('div', { class: 'mr-actions' },
            button,
            el('div', {},
                el('button', {
                    class: 'link-btn',
                    onclick: async () => {
                        const versionId = await pickModrinthVersion(hit);
                        if (versionId) installModrinthHit(hit, button, versionId);
                    },
                }, 'Sürüm seç'),
                el('button', {
                    class: 'link-btn', title: 'Modrinth sayfasını tarayıcıda aç',
                    onclick: () => bridge.invoke('modrinth.openPage', { type: hit.type, slug: hit.slug }).catch(() => { }),
                }, 'Sayfa ↗'))));
}
