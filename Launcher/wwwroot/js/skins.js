// Skin: profil sayfasında önizleme, yükleme, beden tipi ve kaldırma. Oyun skinleri vulu-api'den alır
// (authlib-injector); değişiklik oyunun bir sonraki açılışında geçerli olur.

function skinUrl(user) {
    return user && user.skinHash ? `${state.apiBase}/yggdrasil/textures/${user.skinHash}` : null;
}

/**
 * Skinin önden görünüşünü çizer: baş, gövde, kollar, bacaklar ve üstlerindeki ikinci katman.
 * 64×32 (eski biçim) skinlerde sol kol ve bacak sağdakilerin aynası, ikinci katman yalnızca başta vardır.
 */
function drawSkinFront(canvas, image, slim) {
    const scale = canvas.width / 16;
    const ctx = canvas.getContext('2d');
    ctx.imageSmoothingEnabled = false;
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    const legacy = image.naturalHeight === 32;
    const arm = slim ? 3 : 4;

    // (kaynak x, y, genişlik, yükseklik) → (hedef x, y), birim: skin pikseli
    const part = (sx, sy, w, h, dx, dy, mirror = false) => {
        ctx.save();
        if (mirror) {
            ctx.translate((dx + w) * scale, dy * scale);
            ctx.scale(-1, 1);
            ctx.drawImage(image, sx, sy, w, h, 0, 0, w * scale, h * scale);
        } else {
            ctx.drawImage(image, sx, sy, w, h, dx * scale, dy * scale, w * scale, h * scale);
        }
        ctx.restore();
    };

    // Taban katman
    part(8, 8, 8, 8, 4, 0);                 // baş
    part(20, 20, 8, 12, 4, 8);              // gövde
    part(44, 20, arm, 12, 4 - arm, 8);      // sağ kol (izleyenin solu)
    part(4, 20, 4, 12, 4, 20);              // sağ bacak
    if (legacy) {
        part(44, 20, arm, 12, 12, 8, true);
        part(4, 20, 4, 12, 8, 20, true);
    } else {
        part(36, 52, arm, 12, 12, 8);        // sol kol
        part(20, 52, 4, 12, 8, 20);          // sol bacak
    }

    // İkinci katman (şapka, ceket, kollar, pantolon)
    part(40, 8, 8, 8, 4, 0);
    if (!legacy) {
        part(20, 36, 8, 12, 4, 8);
        part(44, 36, arm, 12, 4 - arm, 8);
        part(52, 52, arm, 12, 12, 8);
        part(4, 36, 4, 12, 4, 20);
        part(4, 52, 4, 12, 8, 20);
    }
}

function skinPreview(user) {
    const box = el('div', { class: 'skin-preview' + (user.skinHash ? '' : ' empty') });
    const url = skinUrl(user);
    if (!url) {
        box.append(el('span', {}, 'Varsayılan skin'));
        return box;
    }
    const canvas = el('canvas', { width: 128, height: 256, 'aria-label': `${user.username} skini`, role: 'img' });
    const image = new Image();
    image.crossOrigin = 'anonymous';
    image.onload = () => drawSkinFront(canvas, image, user.skinModel === 'slim');
    image.onerror = () => box.replaceChildren(el('span', {}, 'Skin yüklenemedi'));
    image.src = url;
    box.append(canvas);
    return box;
}

/** Profil sayfasındaki skin bölümü; kendi profilinde düzenleme düğmeleriyle. */
function skinSection(user, own) {
    if (!own && !user.skinHash) return null;
    const slim = user.skinModel === 'slim';

    const run = async (command, args, failTitle) => {
        try {
            const updated = await bridge.invoke(command, args);
            if (updated) afterMyProfileChange(updated);
        } catch (err) { await alertDialog({ title: failTitle, message: err.message }); }
    };

    const controls = own && el('div', { class: 'skin-controls' },
        el('div', { class: 'skin-buttons' },
            el('button', { class: 'btn btn-sm btn-primary', onclick: () => run('profile.pickSkin', { model: user.skinModel }, 'Skin yüklenemedi') },
                user.skinHash ? 'Skini değiştir' : 'Skin yükle'),
            user.skinHash && el('button', { class: 'btn btn-sm', onclick: () => run('profile.removeSkin', {}, 'Skin kaldırılamadı') }, 'Kaldır')),
        el('label', {}, 'Beden tipi'),
        el('div', { class: 'segmented' },
            el('button', {
                type: 'button', class: slim ? '' : 'active', disabled: !user.skinHash,
                onclick: () => slim && run('profile.setSkinModel', { model: 'default' }, 'Değiştirilemedi'),
            }, 'Klasik (Steve)'),
            el('button', {
                type: 'button', class: slim ? 'active' : '', disabled: !user.skinHash,
                onclick: () => !slim && run('profile.setSkinModel', { model: 'slim' }, 'Değiştirilemedi'),
            }, 'İnce (Alex)')),
        el('p', { class: 'hint' },
            '64×64 PNG. Tek oyunculu dünyada, LAN\'da ve vulu sunucularında görünür; değişiklik oyunu bir sonraki açışında geçerli olur.'));

    return el('section', { class: 'profile-section skin-section' },
        el('h2', {}, 'Skin'),
        el('div', { class: 'skin-row' }, skinPreview(user), controls));
}
