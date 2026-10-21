// Keşfet → Modrinth: mod / modpack / kaynak paketi / shader arama, filtreleme, sıralama ve kurma.
// Instance seçmeden de gezilebilir; "Kur"a basınca nereye kurulacağı sorulur (gerekirse yeni instance açılır).
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

const NEW_INSTANCE = '__new';

const modrinth = {
    type: 'mod',
    targetId: '',
    sort: '',
    mcVersion: '',           // instance seçilmediğinde (ya da modpack aramasında) sürüm filtresi
    loader: '',              // instance seçilmediğinde (ya da modpack aramasında) yükleyici filtresi
    openSource: false,
    categories: {},          // tür → seçili kategori adları (Set)
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
    $('mr-target').addEventListener('change', () => {
        const value = $('mr-target').value;
        if (value === NEW_INSTANCE) {
            $('mr-target').value = modrinth.targetId; // vazgeçilirse eski seçim kalır
            openNewInstanceDialog(null, { onCreated: id => { modrinth.targetId = id; openModrinthPage(); } });
            return;
        }
        modrinth.targetId = value;
        openModrinthPage();
    });
    $('mr-version').addEventListener('change', () => { modrinth.mcVersion = $('mr-version').value; searchModrinth(false); });
    $('mr-loader').addEventListener('change', () => { modrinth.loader = $('mr-loader').value; searchModrinth(false); });
    $('mr-more').addEventListener('click', () => searchModrinth(true));
}

async function openModrinthPage() {
    if (!modrinth.ready) setupModrinthPage();

    $('mr-types').replaceChildren(...MR_TYPES.map(type => el('button', {
        class: type.key === modrinth.type ? 'active' : '',
        onclick: () => { modrinth.type = type.key; openModrinthPage(); },
    }, type.label)));

    // Modpack kendi instance'ını oluşturur; diğer içerikler bir instance'a kurulur. Instance seçilmediyse
    // sonuçlar sürüm/yükleyici filtresiyle süzülür ve kurarken hedef sorulur.
    const customs = customInstances();
    const isPack = modrinth.type === 'modpack';
    if (modrinth.targetId && !customs.some(i => i.id === modrinth.targetId)) modrinth.targetId = '';
    const freeBrowse = isPack || !modrinth.targetId;
    $('mr-target-wrap').hidden = isPack;
    $('mr-version-wrap').hidden = !freeBrowse;
    $('mr-loader-wrap').hidden = !freeBrowse || !(isPack || modrinth.type === 'mod');
    $('mr-sort').value = modrinth.sort;

    if (!isPack) {
        $('mr-target').replaceChildren(
            el('option', { value: '' }, customs.length ? 'Seçilmedi (her şeyi göster)' : 'Henüz instance yok'),
            ...customs.map(i => el('option', { value: i.id }, `${i.name} (${loaderLabel(i)})`)),
            el('option', { value: NEW_INSTANCE }, '+ Yeni instance oluştur…'));
        $('mr-target').value = modrinth.targetId;
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

    if (data && (modrinth.type === 'modpack' || !modrinth.targetId)) {
        if ($('mr-version').options.length === 0 && data.minecraftVersions) {
            $('mr-version').replaceChildren(el('option', { value: '' }, 'Tüm sürümler'),
                ...data.minecraftVersions.map(version => el('option', { value: version }, version)));
            $('mr-loader').replaceChildren(el('option', { value: '' }, 'Tümü'),
                ...data.loaders.map(loader => el('option', { value: loader }, LOADER_NAMES[loader] || loader)));
        }
        $('mr-version').value = modrinth.mcVersion;
        $('mr-loader').value = modrinth.loader;
    }

    // Filtreler sağ panelde hep açık durur
    const panel = $('mr-filters');
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
            }, activeCount > 0 ? `Temizle (${activeCount})` : 'Temizle')));
}

async function searchModrinth(append) {
    const results = $('mr-results'), status = $('mr-status'), more = $('mr-more');
    const isPack = modrinth.type === 'modpack';
    const freeBrowse = isPack || !modrinth.targetId;

    const requestId = ++modrinth.requestId;
    if (!append) { modrinth.offset = 0; status.textContent = 'Aranıyor...'; }
    more.disabled = true;

    let result;
    try {
        result = await bridge.invoke('modrinth.search', {
            query: $('mr-query').value.trim(),
            type: modrinth.type,
            instanceId: isPack ? '' : modrinth.targetId,
            mcVersion: freeBrowse ? modrinth.mcVersion : '',
            loader: freeBrowse && (isPack || modrinth.type === 'mod') ? modrinth.loader : '',
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
            : !isPack && canInstallGames()
                ? `${count} · "Kur"a basınca nereye kurulacağını seçersin`
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

        // Instance seçilmediyse nereye kurulacağı sorulur; seçilen instance sonraki kurulumlar için hedef olur
        let instanceId = modrinth.targetId;
        const newTarget = !instanceId;
        if (newTarget) {
            instanceId = await chooseInstallTarget(hit);
            if (!instanceId) { button.textContent = original; button.disabled = false; return; }
        }

        const result = await withActivity(() =>
            bridge.invoke('modrinth.install', { instanceId, projectId: hit.id, versionId: versionId || '' }));
        if (!result.success) throw new Error(result.error);

        button.classList.remove('btn-primary');
        button.textContent = 'Kuruldu ✓';
        // Hedef yeni seçildiyse sonuçlar artık o instance'a göre süzülür
        if (newTarget) { modrinth.targetId = instanceId; openModrinthPage(); }
        // Bağımlılık olarak başka şeyler de kurulduysa bildir
        if (result.installed.length > 1)
            $('mr-status').textContent = `Kuruldu: ${result.installed.join(', ')}`;
    } catch (err) {
        button.textContent = original;
        await alertDialog({ title: 'Kurulamadı', message: err.message });
    }
    button.disabled = false;
}

/** Bir içeriğin bu instance'a uyup uymadığı: sürüm ve (modlarda) yükleyici. Uymuyorsa nedeni, uyuyorsa ''. */
function hitFitsInstance(hit, instance) {
    const versions = hit.gameVersions || [];
    if (versions.length && !versions.includes(instance.mcVersion)) return `Minecraft ${instance.mcVersion} desteklenmiyor`;
    if (hit.type !== 'mod') return '';
    if (instance.loader === 'vanilla') return "Vanilla'ya mod kurulamaz";
    const loaders = hit.loaders || [];
    const fits = loaders.includes(instance.loader) || (instance.loader === 'quilt' && loaders.includes('fabric'));
    return fits ? '' : `${LOADER_NAMES[instance.loader] || instance.loader} desteklenmiyor`;
}

/** Bu içerik için önerilen yeni instance: en yeni desteklenen sürüm ve uygun yükleyici. */
function suggestedInstance(hit) {
    const versions = hit.gameVersions || [];
    const mcVersion = versions[versions.length - 1] || '';
    const preferred = ['fabric', 'neoforge', 'forge', 'quilt'];
    const loader = hit.type === 'mod' ? preferred.find(l => (hit.loaders || []).includes(l)) || 'fabric'
        : hit.type === 'shader' ? 'fabric'   // shaderlar Iris ile (Fabric) çalışır
        : 'vanilla';
    return { name: hit.title.slice(0, 40), mcVersion, loader };
}

/** "Nereye kurulsun?": uyumlu instance'lar ya da bu içerik için yeni bir instance. Seçilen instance kimliğini döner. */
function chooseInstallTarget(hit) {
    return new Promise(resolve => {
        let next = null; // pencere kapandıktan sonra ne olacağı

        openModal(close => {
            const customs = customInstances();
            const rows = customs.map(instance => ({ instance, problem: hitFitsInstance(hit, instance) }))
                .sort((a, b) => (a.problem ? 1 : 0) - (b.problem ? 1 : 0));
            const preset = suggestedInstance(hit);

            const list = rows.map(({ instance, problem }) => el('div', { class: 'user-row target-row' + (problem ? ' inactive' : '') },
                el('div', {},
                    el('div', { class: 'uname' }, instance.name),
                    el('div', { class: 'meta' }, problem || loaderLabel(instance))),
                el('span', {}),
                el('button', {
                    class: 'btn btn-sm' + (problem ? '' : ' btn-primary'), disabled: !!problem,
                    onclick: () => { next = { id: instance.id }; close(); },
                }, 'Buraya kur')));

            return [
                el('div', { class: 'modal-head' }, `${hit.title} nereye kurulsun?`),
                el('div', { class: 'modal-body form target-body' },
                    el('div', { class: 'target-new-box' },
                        el('div', {},
                            el('b', {}, 'Bu içerik için yeni bir instance'),
                            el('p', { class: 'hint', style: 'margin-top:2px' }, preset.mcVersion
                                ? `Önerilen: Minecraft ${preset.mcVersion}${preset.loader !== 'vanilla' ? ' · ' + (LOADER_NAMES[preset.loader] || preset.loader) : ''}. Oluştururken değiştirebilirsin.`
                                : 'Sürümü ve yükleyiciyi oluştururken seçebilirsin.')),
                        el('button', { class: 'btn btn-primary', onclick: () => { next = { create: preset }; close(); } }, '+ Yeni instance oluştur')),
                    customs.length
                        ? [el('label', {}, 'Ya da var olan bir instance'), ...list]
                        : el('p', { class: 'hint' }, 'Henüz instance yok.')),
                el('div', { class: 'modal-foot' },
                    el('button', { class: 'btn', onclick: () => close() }, 'Vazgeç')),
            ];
        }).then(() => {
            if (!next) { resolve(undefined); return; }
            if (next.id) { resolve(next.id); return; }
            // Pencere oluşturulunca true ile kapanır, kimlik onCreated'a biraz sonra gelir; vazgeçilirse true gelmez
            openNewInstanceDialog(null, { preset: next.create, onCreated: id => resolve(id) })
                .then(created => { if (created !== true) resolve(undefined); });
        });
    });
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
    const button = canInstallGames()
        ? el('button', { class: 'btn btn-sm ' + (hit.installed ? '' : 'btn-primary') },
            isPack ? 'Instance olarak kur' : hit.installed ? 'Güncelle' : 'Kur')
        : desktopOnlyButton();
    if (canInstallGames()) button.addEventListener('click', () => installModrinthHit(hit, button, ''));

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
                canInstallGames() && el('button', {
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
