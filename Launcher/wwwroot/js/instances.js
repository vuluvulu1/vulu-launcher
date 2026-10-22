// Kullanıcı instance'ları: oluşturma, düzenleme, içerik listesi, silme.

/** Yeni instance oluşturur; var olan bir instance verilirse onu düzenler. */
/**
 * Yeni instance oluşturur ya da (existing verilirse) düzenler.
 * preset: { name, mcVersion, loader } ile alanlar önceden doldurulur (ör. Keşfet'te bir mod için).
 * onCreated(id) verilirse oluşturunca ana ekrana geçilmez, bu fonksiyon çağrılır.
 */
function openNewInstanceDialog(existing, { preset = null, onCreated = null } = {}) {
    const isEdit = !!existing;

    return openModal(close => {
        const name = el('input', { type: 'text', maxLength: 40, value: isEdit ? existing.name : (preset && preset.name) || '', placeholder: 'Örn. Fabric 1.21', autofocus: true, spellcheck: false });
        const mcVersion = el('select', { disabled: true }, el('option', {}, 'Yükleniyor...'));
        const loaderVersion = el('select', { disabled: true });
        const loaderRow = el('div', { class: 'segmented' });
        const loaderVersionWrap = el('div', {}, el('label', {}, 'Yükleyici sürümü'), loaderVersion);
        const error = el('div', { class: 'form-error' });
        const create = el('button', { class: 'btn btn-primary', type: 'submit', disabled: true }, isEdit ? 'Kaydet' : 'Oluştur');

        let loader = isEdit ? existing.loader : (preset && preset.loader) || 'vanilla';
        let requestId = 0;

        function renderLoaders(loaders) {
            loaderRow.replaceChildren(...loaders.map(key => el('button', {
                type: 'button',
                class: key === loader ? 'active' : '',
                onclick: () => { loader = key; renderLoaders(loaders); loadLoaderVersions(); },
            }, LOADER_NAMES[key] || key)));
        }

        // Seçilen Minecraft sürümü + yükleyici için sürümleri getirir
        async function loadLoaderVersions() {
            const current = ++requestId;
            error.textContent = '';
            loaderVersionWrap.hidden = loader === 'vanilla';
            if (loader === 'vanilla') { create.disabled = mcVersion.disabled; return; }

            create.disabled = true;
            loaderVersion.disabled = true;
            loaderVersion.replaceChildren(el('option', {}, 'Yükleniyor...'));
            try {
                const versions = await bridge.invoke('instances.loaderVersions', { loader, mcVersion: mcVersion.value });
                if (current !== requestId) return; // bu arada seçim değişti
                if (versions.length === 0) {
                    loaderVersion.replaceChildren(el('option', {}, 'Bu Minecraft sürümü için yok'));
                    return;
                }
                loaderVersion.replaceChildren(...versions.map((version, index) =>
                    el('option', { value: version }, index === 0 ? `${version} (önerilen)` : version)));

                // Düzenlerken, sürüm ve yükleyici değişmediyse şu an kurulu olan seçili gelir
                if (isEdit && loader === existing.loader && mcVersion.value === existing.mcVersion && existing.loaderVersion) {
                    if (!versions.includes(existing.loaderVersion))
                        loaderVersion.prepend(el('option', { value: existing.loaderVersion }, existing.loaderVersion));
                    loaderVersion.value = existing.loaderVersion;
                }
                loaderVersion.disabled = false;
                create.disabled = false;
            } catch (err) {
                if (current !== requestId) return;
                loaderVersion.replaceChildren(el('option', {}, 'Alınamadı'));
                error.textContent = 'Sürümler alınamadı: ' + err.message;
            }
        }

        mcVersion.addEventListener('change', loadLoaderVersions);

        bridge.invoke('instances.options').then(options => {
            mcVersion.replaceChildren(...options.minecraftVersions.map(version => el('option', { value: version }, version)));
            const wanted = isEdit ? existing.mcVersion : preset && preset.mcVersion;
            if (wanted) {
                if (!options.minecraftVersions.includes(wanted))
                    mcVersion.prepend(el('option', { value: wanted }, wanted));
                mcVersion.value = wanted;
            }
            mcVersion.disabled = false;
            renderLoaders(options.loaders);
            loadLoaderVersions();
        }).catch(err => { error.textContent = 'Sürüm listesi alınamadı: ' + err.message; });

        async function submit(event) {
            event.preventDefault();
            create.disabled = true;
            error.textContent = '';
            try {
                const result = await bridge.invoke(isEdit ? 'instances.update' : 'instances.create', {
                    id: isEdit ? existing.id : '',
                    name: name.value.trim() || `${LOADER_NAMES[loader]} ${mcVersion.value}`,
                    mcVersion: mcVersion.value,
                    loader,
                    loaderVersion: loader === 'vanilla' ? '' : loaderVersion.value,
                });
                close(true);
                await reloadInstances(result.id);
                if (onCreated) onCreated(result.id);
                else showPage('home');
            } catch (err) {
                error.textContent = err.message;
                create.disabled = false;
            }
        }

        loaderVersionWrap.hidden = true;

        return el('form', { onsubmit: submit, style: 'display:contents' },
            el('div', { class: 'modal-head' }, isEdit ? `${existing.name} — düzenle` : 'Yeni instance'),
            el('div', { class: 'modal-body form' },
                el('label', {}, 'Ad'), name,
                el('label', {}, 'Minecraft sürümü'), mcVersion,
                el('label', {}, 'Mod yükleyici'), loaderRow,
                loaderVersionWrap,
                el('p', { class: 'hint' }, isEdit
                    ? 'Dünyalar, modlar ve ayarlar yerinde kalır. Sürümü ya da yükleyiciyi değiştirirsen kurulu modlar uyumsuz kalabilir.'
                    : 'Oyun dosyaları, mod yükleyici ve gereken Java ilk açılışta indirilir.'),
                error),
            el('div', { class: 'modal-foot' },
                el('button', { class: 'btn', type: 'button', onclick: () => close(false) }, 'Vazgeç'),
                create));
    });
}

const CONTENT_TYPE_NAMES = { mod: 'Mod', resourcepack: 'Kaynak paketi', shader: 'Shader' };

function openInstanceContent(instance) {
    return openModal(close => {
        const body = el('div', { class: 'admin-body' });
        let updates = null;          // denetlendiyse: dosya → güncelleme
        let checking = false;

        async function checkUpdates() {
            checking = true; render();
            try {
                const result = await bridge.invoke('instances.checkUpdates', { id: instance.id });
                updates = Object.fromEntries(result.updates.map(u => [u.file, u]));
            } catch (err) { appendLog({ text: 'Güncellemeler denetlenemedi: ' + err.message, level: 'error' }); updates = null; }
            checking = false; render();
        }

        async function update(list) {
            for (const u of list) {
                try {
                    const r = await withActivity(() => bridge.invoke('mods.update', { instanceId: instance.id, projectId: u.projectId, versionId: u.versionId, source: u.source || '' }));
                    if (r.manual && r.manual.length) await showManualDownloads(r.manual, instance.id);
                }
                catch (err) { appendLog({ text: err.message, level: 'error' }); }
            }
            updates = null;
            render();
            loadDetails();
        }

        async function render() {
            body.replaceChildren(el('p', { class: 'admin-toolbar' }, 'Yükleniyor...'));
            let content;
            try { content = await bridge.invoke('instances.content', { id: instance.id }); }
            catch (err) { body.replaceChildren(el('p', { class: 'form-error' }, 'Hata: ' + err.message)); return; }

            const stable = updates ? Object.values(updates).filter(u => u.stable) : [];
            const toolbar = el('div', { class: 'admin-toolbar' },
                el('span', {}, `${content.items.length} dosya · Minecraft ${content.mcVersion} · ${LOADER_NAMES[content.loader] || content.loader}`),
                // Telefonda mod indirilmez (oyun açılmıyor): içerik yalnızca görülür, açılıp kapatılabilir
                canInstallGames() && el('span', { class: 'toolbar-actions' },
                    updates && stable.length > 0
                        ? el('button', { class: 'btn btn-sm btn-accent', onclick: () => update(stable) }, `Tümünü güncelle (${stable.length})`)
                        : el('button', { class: 'btn btn-sm', disabled: checking, onclick: checkUpdates },
                            checking ? 'Denetleniyor…' : updates ? 'Güncel ✓' : 'Güncellemeleri denetle'),
                    el('button', { class: 'btn btn-sm btn-primary', onclick: () => { close(); openModrinthFor(instance); } }, '+ İçerik ekle')));

            const rows = content.items.map(item => {
                const upd = updates && updates[item.file];
                const toggle = el('input', { type: 'checkbox', checked: item.enabled !== false, 'aria-label': item.enabled !== false ? 'Kapat' : 'Aç' });
                toggle.addEventListener('change', async () => {
                    toggle.disabled = true;
                    try { await bridge.invoke('instances.setContentEnabled', { id: instance.id, file: item.file, enabled: toggle.checked }); updates = null; }
                    catch (err) { await alertDialog({ title: 'Değiştirilemedi', message: err.message }); }
                    render();
                    loadDetails();
                });
                return el('div', { class: 'user-row content-row' + (item.enabled === false ? ' disabled' : '') },
                    el('div', { class: 'content-main' },
                        item.iconUrl ? el('img', { class: 'content-icon', src: item.iconUrl, alt: '' }) : el('div', { class: 'content-icon plain' }),
                        el('div', {},
                            el('div', { class: 'uname' }, item.title, item.enabled === false && el('span', { class: 'off-tag' }, 'kapalı')),
                            el('div', { class: 'meta' },
                                [CONTENT_TYPE_NAMES[item.type], item.version, `${item.sizeMb} MB`].filter(Boolean).join(' · ')))),
                    upd
                        ? el('button', { class: 'btn btn-sm btn-accent', title: `${upd.from} → ${upd.to}`, onclick: () => update([upd]) },
                            upd.stable ? `Güncelle (${upd.to})` : `Güncelle (${upd.to} · ${upd.type})`)
                        : el('span', {}),
                    el('label', { class: 'switch', title: item.enabled !== false ? 'Açık · kapatmak için tıkla' : 'Kapalı · açmak için tıkla' }, toggle, el('span', {})),
                    el('button', {
                        class: 'btn btn-sm btn-danger',
                        onclick: async () => {
                            const accepted = await confirmDialog({
                                title: 'Dosyayı kaldır', message: `'${item.title}' bu instance'tan kaldırılsın mı?`,
                                okText: 'Kaldır', cancelText: 'Vazgeç', danger: true,
                            });
                            if (!accepted) return;
                            try { await bridge.invoke('instances.removeContent', { id: instance.id, file: item.file }); }
                            catch (err) { await alertDialog({ title: 'Kaldırılamadı', message: err.message }); }
                            render();
                        },
                    }, 'Kaldır'));
            });

            body.replaceChildren(toolbar, ...(rows.length
                ? rows
                : [el('p', { class: 'pack-empty' }, 'Bu instance\'ta henüz mod, kaynak paketi ya da shader yok.')]));
        }

        queueMicrotask(render);

        return [
            el('div', { class: 'modal-head' }, `${instance.name} — içerik`,
                el('button', { class: 'link-btn', onclick: () => close() }, 'Kapat')),
            body,
        ];
    }, { wide: true });
}

async function deleteInstance(instance) {
    const accepted = await confirmDialog({
        title: 'Instance\'ı sil',
        message: `'${instance.name}' ve içindeki her şey (dünyalar, modlar, ayarlar) silinecek.\nBu işlem geri alınamaz.`,
        okText: 'Sil', cancelText: 'Vazgeç', danger: true,
    });
    if (!accepted) return;

    try {
        await bridge.invoke('instances.delete', { id: instance.id });
        await reloadInstances();
    } catch (err) {
        await alertDialog({ title: 'Silinemedi', message: err.message });
    }
}

/**
 * Görünüm: ana ekrandaki arka plan resmi ve ismin yerine geçen logo. Değişiklik hemen uygulanır;
 * pencere açık kalır ki ikisi birlikte ayarlanabilsin.
 */
function openAppearanceDialog(instance) {
    let current = instance;

    return openModal(close => {
        const error = el('div', { class: 'form-error' });
        const slots = el('div', { class: 'look-slots' });

        async function run(command) {
            error.textContent = '';
            try {
                const result = await bridge.invoke(command, { id: current.id });
                if (!result.changed) return; // dosya seçmeden kapattı
                await reloadInstances(current.id);
                current = state.instances.find(i => i.id === current.id) || current;
                render();
            } catch (err) {
                error.textContent = err.message;
            }
        }

        function slot({ title, hint, preview, hasCustom, pick, clear, resetHint }) {
            return el('section', { class: 'look-slot' },
                preview,
                el('div', { class: 'look-info' },
                    el('b', {}, title),
                    el('p', { class: 'hint' }, hint),
                    hasCustom && resetHint && el('p', { class: 'hint' }, resetHint),
                    el('div', { class: 'look-actions' },
                        el('button', { class: 'btn btn-sm btn-primary', onclick: () => run(pick) }, hasCustom ? 'Değiştir' : 'Resim seç'),
                        hasCustom && el('button', { class: 'btn btn-sm btn-danger', onclick: () => run(clear) }, 'Kaldır'))));
        }

        function render() {
            const bg = el('div', { class: 'look-preview bg' + (current.background ? '' : ' empty') });
            if (current.background) bg.style.backgroundImage = `url("${current.background}")`;
            else bg.append(el('span', {}, 'Arka plan yok'));

            // Logo, ana ekrandaki gibi karanlık zeminde gösterilir; yoksa ismin yazıyla hali
            const logo = el('div', { class: 'look-preview logo' },
                current.logo ? el('img', { src: current.logo, alt: '' }) : el('span', { class: 'look-name' }, current.name));

            slots.replaceChildren(
                slot({
                    title: 'Logo', preview: logo, hasCustom: current.hasCustomLogo,
                    hint: 'Ana ekranda paket adının yerine görünür. Saydam arka planlı PNG en iyisi; en çok 4 MB.',
                    resetHint: current.kind === 'server' ? 'Kaldırırsan paketin kendi logosuna dönülür.' : 'Kaldırırsan ad yazıyla gösterilir.',
                    pick: 'instances.pickLogo', clear: 'instances.clearLogo',
                }),
                slot({
                    title: 'Arka plan', preview: bg, hasCustom: current.hasCustomBackground,
                    hint: 'Ana ekranda arkada görünür. PNG, JPG, WEBP ya da GIF; geniş (16:9) resimler en iyisi.',
                    resetHint: current.kind === 'server' ? 'Kaldırırsan paketin kendi arka planına dönülür.' : null,
                    pick: 'instances.pickBackground', clear: 'instances.clearBackground',
                }));
        }

        render();
        return [
            el('div', { class: 'modal-head' }, `${instance.name} — görünüm`),
            el('div', { class: 'modal-body look-body' },
                slots,
                current.sharedPackId && el('p', { class: 'hint look-note' },
                    'Paylaştığın paketlerde logo da pakete eklenir; "Paylaşımı güncelle" ile kuranlara da gider.'),
                error),
            el('div', { class: 'modal-foot' },
                el('button', { class: 'btn btn-primary', onclick: () => close(true) }, 'Tamam')),
        ];
    });
}
