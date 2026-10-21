// Küçük arayüz yardımcıları: element oluşturma ve modal pencereler.
const $ = id => document.getElementById(id);

/** el('div', { class: 'x', onclick: fn }, 'metin', cocukEleman, ...) */
/** Masaüstüne özgü işlemler (klasör açma, masaüstü kısayolu) Android'de gösterilmez. */
const isDesktopApp = () => bridge.platform !== 'android';

/** Oyun dosyası indiren kurulumlar yalnızca oyunun açılabildiği cihazda (Android'de henüz değil). */
const canInstallGames = () => state.canLaunchGame !== false;

/** Telefonda kurulum düğmelerinin yerine: neden kurulamadığını söyleyen pasif düğme. */
const desktopOnlyButton = (label = 'Bilgisayarda kur', cls = 'btn btn-sm') =>
    el('button', { class: cls + ' desktop-only', disabled: true, title: 'Oyun şimdilik yalnızca bilgisayarda açılıyor' }, label);

function el(tag, props, ...children) {
    const node = document.createElement(tag);
    for (const [key, value] of Object.entries(props || {})) {
        if (value === null || value === undefined || value === false) continue;
        if (key === 'class') node.className = value;
        else if (key.startsWith('on')) node.addEventListener(key.slice(2), value);
        else if (key in node && key !== 'list') node[key] = value;
        else node.setAttribute(key, value);
    }
    for (const child of children.flat()) {
        if (child === null || child === undefined || child === false) continue;
        node.append(child.nodeType ? child : document.createTextNode(String(child)));
    }
    return node;
}

/** Sabit SVG/HTML parçasından eleman üretir (yalnızca koddaki sabit işaretleme için; kullanıcı verisi verilmez). */
function fromMarkup(markup) {
    const template = document.createElement('template');
    template.innerHTML = markup.trim();
    return template.content.firstElementChild;
}

/**
 * Modal açar. build(close) içeriği döndürür; close(deger) modalı kapatıp Promise'i çözer.
 * Esc tuşu ve (dismissable ise) arka plana tıklama close(undefined) çağırır.
 */
function openModal(build, { wide = false, dismissable = true } = {}) {
    return new Promise(resolve => {
        const backdrop = el('div', { class: 'modal-backdrop' });
        const modal = el('div', { class: 'modal' + (wide ? ' modal-wide' : ''), role: 'dialog' });

        function close(value) {
            document.removeEventListener('keydown', onKey);
            backdrop.remove();
            resolve(value);
        }
        function onKey(event) {
            // En üstteki modal kapanır
            if (event.key === 'Escape' && dismissable && backdrop === $('modal-root').lastElementChild) close(undefined);
        }

        modal.append(...[].concat(build(close)));
        backdrop.append(modal);
        if (dismissable) backdrop.addEventListener('mousedown', event => { if (event.target === backdrop) close(undefined); });
        document.addEventListener('keydown', onKey);
        $('modal-root').append(backdrop);

        const focusTarget = modal.querySelector('[autofocus]') || modal.querySelector('.btn-primary');
        if (focusTarget) focusTarget.focus();
    });
}

/** Evet/Hayır sorusu. Evet → true. */
function confirmDialog({ title, message, okText = 'Evet', cancelText = 'Hayır', danger = false }) {
    return openModal(close => [
        el('div', { class: 'modal-head' }, title),
        el('div', { class: 'modal-body' }, message),
        el('div', { class: 'modal-foot' },
            el('button', { class: 'btn', onclick: () => close(false) }, cancelText),
            el('button', { class: 'btn ' + (danger ? 'btn-danger' : 'btn-primary'), onclick: () => close(true) }, okText)),
    ]).then(result => result === true);
}

/** Tek satırlık metin isteyen pencere; vazgeçilirse null döner. */
function promptDialog({ title, label, value = '', okText = 'Tamam', cancelText = 'Vazgeç', maxLength = 40 }) {
    return openModal(close => {
        const input = el('input', { type: 'text', value, maxLength, spellcheck: false, autofocus: true });
        const submit = event => { event.preventDefault(); const v = input.value.trim(); if (v) close(v); };
        queueMicrotask(() => { input.focus(); input.select(); });
        return el('form', { onsubmit: submit, style: 'display:contents' },
            el('div', { class: 'modal-head' }, title),
            el('div', { class: 'modal-body form' }, el('label', {}, label), input),
            el('div', { class: 'modal-foot' },
                el('button', { class: 'btn', type: 'button', onclick: () => close(null) }, cancelText),
                el('button', { class: 'btn btn-primary', type: 'submit' }, okText)));
    }).then(v => (typeof v === 'string' ? v : null));
}

/** Tek butonlu bilgi penceresi. */
function alertDialog({ title, message, okText = 'Tamam' }) {
    return openModal(close => [
        el('div', { class: 'modal-head' }, title),
        el('div', { class: 'modal-body' }, message),
        el('div', { class: 'modal-foot' },
            el('button', { class: 'btn btn-primary', onclick: () => close(true) }, okText)),
    ]);
}

/** Admin işareti: yazı yerine küçük bir kalkan; üzerine gelince "Admin" yazar. */
function adminMark() {
    const mark = fromMarkup('<span class="admin-mark" title="Admin" role="img" aria-label="Admin"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 2.5l8 3.2v5.6c0 5-3.4 8.9-8 10.2-4.6-1.3-8-5.2-8-10.2V5.7z"/><path class="tick" d="M8.5 12.2l2.4 2.4 4.6-4.8"/></svg></span>');
    return mark;
}
