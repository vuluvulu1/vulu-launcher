// Kullanıcı instance'ları: oluşturma, düzenleme, içerik listesi, silme.

/** Yeni instance oluşturur; var olan bir instance verilirse onu düzenler. */
function openNewInstanceDialog(existing) {
    const isEdit = !!existing;

    return openModal(close => {
        const name = el('input', { type: 'text', maxLength: 40, value: isEdit ? existing.name : '', placeholder: 'Örn. Fabric 1.21', autofocus: true, spellcheck: false });
        const mcVersion = el('select', { disabled: true }, el('option', {}, 'Yükleniyor...'));
        const loaderVersion = el('select', { disabled: true });
        const loaderRow = el('div', { class: 'segmented' });
        const loaderVersionWrap = el('div', {}, el('label', {}, 'Yükleyici sürümü'), loaderVersion);
        const error = el('div', { class: 'form-error' });
        const create = el('button', { class: 'btn btn-primary', type: 'submit', disabled: true }, isEdit ? 'Kaydet' : 'Oluştur');

        let loader = isEdit ? existing.loader : 'vanilla';
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
            if (isEdit) {
                if (!options.minecraftVersions.includes(existing.mcVersion))
                    mcVersion.prepend(el('option', { value: existing.mcVersion }, existing.mcVersion));
                mcVersion.value = existing.mcVersion;
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
                showPage('home');
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

        async function render() {
            body.replaceChildren(el('p', { class: 'admin-toolbar' }, 'Yükleniyor...'));
            let content;
            try { content = await bridge.invoke('instances.content', { id: instance.id }); }
            catch (err) { body.replaceChildren(el('p', { class: 'form-error' }, 'Hata: ' + err.message)); return; }

            const toolbar = el('div', { class: 'admin-toolbar' },
                el('span', {}, `${content.items.length} dosya · Minecraft ${content.mcVersion} · ${LOADER_NAMES[content.loader] || content.loader}`),
                el('button', { class: 'btn btn-sm btn-primary', onclick: () => { close(); openModrinthFor(instance); } }, '+ Modrinth\'ten ekle'));

            const rows = content.items.map(item => el('div', { class: 'user-row content-row' },
                el('div', { class: 'content-main' },
                    item.iconUrl ? el('img', { class: 'content-icon', src: item.iconUrl, alt: '' }) : el('div', { class: 'content-icon plain' }),
                    el('div', {},
                        el('div', { class: 'uname' }, item.title),
                        el('div', { class: 'meta' },
                            [CONTENT_TYPE_NAMES[item.type], item.version, `${item.sizeMb} MB`].filter(Boolean).join(' · ')))),
                el('span', {}),
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
                }, 'Kaldır')));

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

/** Seçili paketin/instance'ın ana ekrandaki arka plan resmini değiştirir ya da kaldırır. */
function openBackgroundDialog(instance) {
    return openModal(close => {
        const error = el('div', { class: 'form-error' });

        async function run(command) {
            error.textContent = '';
            try {
                const result = await bridge.invoke(command, { id: instance.id });
                if (!result.changed) return; // dosya seçmeden kapattı
                close(true);
                await reloadInstances(instance.id);
            } catch (err) {
                error.textContent = err.message;
            }
        }

        return [
            el('div', { class: 'modal-head' }, `${instance.name} — arka plan`),
            el('div', { class: 'modal-body form' },
                el('p', { class: 'hint', style: 'margin-top:0' },
                    'Ana ekranda bu paketin arkasında görünecek resmi seç. PNG, JPG, WEBP ya da GIF; geniş (16:9) resimler en iyi sonucu verir.'),
                instance.kind === 'server' && instance.hasCustomBackground &&
                    el('p', { class: 'hint' }, 'Kaldırırsan paketin kendi arka planına dönülür.'),
                error),
            el('div', { class: 'modal-foot' },
                el('button', { class: 'btn', onclick: () => close(false) }, 'Vazgeç'),
                instance.hasCustomBackground &&
                    el('button', { class: 'btn btn-danger', onclick: () => run('instances.clearBackground') }, 'Kaldır'),
                el('button', { class: 'btn btn-primary', onclick: () => run('instances.pickBackground') }, 'Resim seç')),
        ];
    });
}
