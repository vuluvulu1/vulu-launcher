// İlk açılışta tema seçimi. Daha önce tema seçilmemişse girişten önce gösterilir; bir karta tıklamak temayı
// geçiş efektiyle hemen uygular, "Devam et" seçimi kaydeder. Sonradan Ayarlar'dan değiştirilebilir.
const THEMES = [
    { key: 'classic', name: 'Klasik', text: 'Koyu ve sıcak; bakır tonları, sade ve dengeli.' },
    { key: 'neon', name: 'Neon', text: 'Siberpunk: parlak neon renkler, farklı bir yerleşim.' },
    { key: 'atlas', name: 'Atlas', text: 'Kâşif defteri: açık renkli kâğıt, mürekkep ve harita.' },
];

/** Hiç tema seçilmemiş mi? (yeni kurulum ya da tema seçiminin olmadığı eski sürümden gelen) */
function needsThemePick() {
    return store.get('uiTheme', '') === '';
}

/** Küçük bir ön izleme: temanın renklerini ve yerleşimini kaba hatlarıyla çizer (temadan bağımsız sabit renkler). */
function themePreview(key) {
    const parts = {
        classic: ['rail', 'title', 'line', 'line short', 'dock', 'play'],
        neon: ['nav', 'strip', 'card', 'card two', 'title', 'line', 'panel', 'play'],
        atlas: ['tabs', 'toc', 'toc two', 'toc three', 'title', 'line', 'photo', 'seal'],
    }[key];
    // Parçaların sınıfları önekli: uygulamanın .rail, .dock gibi sınıflarıyla çakışıp tema stillerini almasınlar
    return el('div', { class: `tp-preview tp-${key}`, 'aria-hidden': 'true' },
        ...parts.map(part => el('i', { class: part.split(' ').map(c => 'p-' + c).join(' ') })));
}

function pickThemeFirstRun() {
    return new Promise(resolve => {
        let current = ['neon', 'atlas'].includes(document.documentElement.dataset.ui) ? document.documentElement.dataset.ui : 'classic';
        const cards = el('div', { class: 'tp-cards' });

        function render() {
            cards.replaceChildren(...THEMES.map(theme => el('button', {
                type: 'button', class: 'tp-card' + (theme.key === current ? ' selected' : ''), 'aria-pressed': String(theme.key === current),
                onclick: async () => {
                    if (theme.key === current) return;
                    current = theme.key;
                    render();
                    await setUiTheme(theme.key);
                },
            },
                themePreview(theme.key),
                el('div', { class: 'tp-name' }, theme.name, el('span', { class: 'tp-check' }, '✓')),
                el('div', { class: 'tp-text' }, theme.text))));
        }

        const done = el('button', { type: 'button', class: 'btn btn-primary tp-done' }, 'Devam et');
        done.addEventListener('click', () => {
            store.set('uiTheme', current);
            $('view-theme').hidden = true;
            resolve();
        });

        render();
        $('view-theme').replaceChildren(el('div', { class: 'tp-wrap' },
            el('h1', {}, 'Görünümünü seç'),
            el('p', { class: 'tp-sub' }, 'Üç temanın her birinin kendi renkleri, yerleşimi ve açılış animasyonu var. Sonradan Ayarlar\'dan değiştirebilirsin.'),
            cards,
            done));

        // Açılıştaki siyah ekran kalkar, seçim ekranı gelir
        $('view-splash').hidden = true;
        $('view-theme').hidden = false;
        done.focus();
    });
}
