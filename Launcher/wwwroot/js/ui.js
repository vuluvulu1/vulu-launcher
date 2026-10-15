// Küçük arayüz yardımcıları: element oluşturma ve modal pencereler.
const $ = id => document.getElementById(id);

/** el('div', { class: 'x', onclick: fn }, 'metin', cocukEleman, ...) */
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

/** Tek butonlu bilgi penceresi. */
function alertDialog({ title, message, okText = 'Tamam' }) {
    return openModal(close => [
        el('div', { class: 'modal-head' }, title),
        el('div', { class: 'modal-body' }, message),
        el('div', { class: 'modal-foot' },
            el('button', { class: 'btn btn-primary', onclick: () => close(true) }, okText)),
    ]);
}
