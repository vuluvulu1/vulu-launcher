// Keşfet: bir projenin ayrıntıları launcher'ın içinde (açıklama, galeri, sürümler) ve listedeki modpack özeti
// (Minecraft sürümü, mod sayısı). Açıklamalar başkalarının yazdığı HTML/markdown olduğu için yalnızca izin verilen
// etiketlerle yeniden kurulur: betik, stil, gömülü içerik ve olay öznitelikleri hiç geçmez; bağlantılar tarayıcıda açılır.

const MD_ALLOWED = new Set(['p', 'br', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'strong', 'b', 'em', 'i', 'u', 's', 'del', 'a', 'ul', 'ol', 'li',
    'img', 'blockquote', 'code', 'pre', 'hr', 'table', 'thead', 'tbody', 'tr', 'td', 'th', 'span', 'div', 'center', 'details', 'summary', 'sub', 'sup', 'kbd', 'figure', 'figcaption']);
const MD_DROP = new Set(['script', 'style', 'iframe', 'object', 'embed', 'form', 'input', 'button', 'textarea', 'select', 'link', 'meta', 'svg', 'math', 'video', 'audio', 'noscript', 'template']);

function safeHttpUrl(value) {
    try {
        const url = new URL(value, 'https://invalid.invalid/');
        return url.protocol === 'https:' || url.protocol === 'http:' ? (url.host === 'invalid.invalid' ? null : url.href) : null;
    } catch { return null; }
}

/** HTML'i izin verilen etiketlerle yeniden kurar. Bağlantılar data-href'e taşınır, tıklanınca dışarıda açılır. */
function sanitizeHtml(html) {
    const doc = new DOMParser().parseFromString(html || '', 'text/html');
    const out = document.createDocumentFragment();
    const walk = (node, parent) => {
        for (const child of node.childNodes) {
            if (child.nodeType === Node.TEXT_NODE) { parent.append(child.textContent); continue; }
            if (child.nodeType !== Node.ELEMENT_NODE) continue;
            const tag = child.tagName.toLowerCase();
            if (MD_DROP.has(tag)) continue;
            if (!MD_ALLOWED.has(tag)) { walk(child, parent); continue; }   // bilinmeyen etiket: içeriği kalır
            const copy = document.createElement(tag === 'center' ? 'div' : tag);
            if (tag === 'center' || (child.getAttribute('align') || '').toLowerCase() === 'center') copy.className = 'md-center';
            if (tag === 'a') {
                const href = safeHttpUrl(child.getAttribute('href'));
                if (href) { copy.dataset.href = href; copy.title = href; }
            } else if (tag === 'img') {
                const src = safeHttpUrl(child.getAttribute('src'));
                if (!src || !src.startsWith('https:')) continue;
                copy.src = src;
                copy.alt = child.getAttribute('alt') || '';
                copy.loading = 'lazy';
                copy.referrerPolicy = 'no-referrer';
                for (const dim of ['width', 'height']) {
                    const v = parseInt(child.getAttribute(dim), 10);
                    if (v > 0 && v < 4000) copy.setAttribute(dim, v);
                }
            } else if (tag === 'td' || tag === 'th') {
                for (const span of ['colspan', 'rowspan']) {
                    const v = parseInt(child.getAttribute(span), 10);
                    if (v > 1 && v < 50) copy.setAttribute(span, v);
                }
            } else if (tag === 'details' && child.hasAttribute('open')) copy.open = true;
            walk(child, copy);
            parent.append(copy);
        }
    };
    walk(doc.body, out);
    return out;
}

/** Küçük markdown çevirici (Modrinth açıklamaları); içindeki HTML olduğu gibi bırakılır, sonra sanitizeHtml'den geçer. */
function markdownToHtml(md) {
    const escape = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    const inline = s => s
        .replace(/`([^`]+)`/g, (_, c) => `<code>${escape(c)}</code>`)
        .replace(/!\[([^\]]*)\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g, '<img alt="$1" src="$2">')
        .replace(/\[([^\]]+)\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g, '<a href="$2">$1</a>')
        .replace(/(^|[\s(])(https?:\/\/[^\s<)]+)/g, '$1<a href="$2">$2</a>')
        .replace(/\*\*([^*]+)\*\*|__([^_]+)__/g, (_, a, b) => `<strong>${a || b}</strong>`)
        .replace(/(^|[^*])\*([^*\s][^*]*)\*/g, '$1<em>$2</em>')
        .replace(/~~([^~]+)~~/g, '<del>$1</del>');
    // Başvuru bağlantıları: "[metin][16]" ve aşağıdaki "[16]: https://…" satırı
    const refs = {};
    md = (md || '').replace(/\r\n?/g, '\n').replace(/^ {0,3}\[([^\]]+)\]:\s*<?(\S+?)>?(?:\s+["'(].*["')])?\s*$/gm, (_, key, url) => {
        refs[key.toLowerCase()] = url;
        return '';
    });
    md = md.replace(/(!?)\[([^\]]*)\]\[([^\]]*)\]/g, (whole, bang, text, key) => {
        const url = refs[(key || text).toLowerCase()];
        return url ? `${bang}[${text}](${url})` : whole;
    });
    // Bağlantılı resim: "[![rozet](resim)][16]"
    md = md.replace(/\[(!\[[^\]]*\]\([^)\s]+\))\]\[([^\]]*)\]/g, (whole, image, key) => {
        const url = refs[key.toLowerCase()];
        return url ? `[${image}](${url})` : whole;
    });
    const lines = md.split('\n');
    const html = [];
    let list = null, para = [], code = null, quote = [];
    const flushPara = () => { if (para.length) { html.push(`<p>${inline(para.join(' '))}</p>`); para = []; } };
    const flushList = () => { if (list) { html.push(`<${list.tag}>${list.items.map(i => `<li>${inline(i)}</li>`).join('')}</${list.tag}>`); list = null; } };
    const flushQuote = () => { if (quote.length) { html.push(`<blockquote>${markdownToHtml(quote.join('\n'))}</blockquote>`); quote = []; } };
    for (const line of lines) {
        if (code !== null) {
            if (/^\s*```/.test(line)) { html.push(`<pre><code>${escape(code.join('\n'))}</code></pre>`); code = null; } else code.push(line);
            continue;
        }
        if (/^\s*```/.test(line)) { flushPara(); flushList(); flushQuote(); code = []; continue; }
        if (/^\s*>/.test(line)) { flushPara(); flushList(); quote.push(line.replace(/^\s*>\s?/, '')); continue; }
        flushQuote();
        let m;
        if ((m = /^(#{1,6})\s+(.*)$/.exec(line))) { flushPara(); flushList(); html.push(`<h${m[1].length}>${inline(m[2])}</h${m[1].length}>`); continue; }
        if (/^\s*([-*_])(\s*\1){2,}\s*$/.test(line)) { flushPara(); flushList(); html.push('<hr>'); continue; }
        if ((m = /^\s*([-*+]|\d+[.)])\s+(.*)$/.exec(line))) {
            flushPara();
            const tag = /\d/.test(m[1]) ? 'ol' : 'ul';
            if (!list || list.tag !== tag) { flushList(); list = { tag, items: [] }; }
            list.items.push(m[2]);
            continue;
        }
        if (!line.trim()) { flushPara(); flushList(); continue; }
        if (/^\s*</.test(line)) { flushPara(); flushList(); html.push(line); continue; }   // satırın kendisi HTML
        if (list && /^\s{2,}\S/.test(line)) { list.items[list.items.length - 1] += ' ' + line.trim(); continue; }
        para.push(line.trim());
    }
    if (code !== null) html.push(`<pre><code>${escape(code.join('\n'))}</code></pre>`);
    flushPara(); flushList(); flushQuote();
    return html.join('\n');
}

/** Açıklama içindeki bağlantılar launcher'da değil tarayıcıda açılır. */
function wireExternalLinks(root) {
    root.addEventListener('click', event => {
        const a = event.target.closest('a[data-href]');
        if (!a) return;
        event.preventDefault();
        bridge.invoke('links.open', { url: a.dataset.href }).catch(() => { });
    });
}

// ── Listedeki özet ─────────────────────────────────────────────────────
/** "Minecraft 1.21.1" ya da birden çok sürümde "Minecraft 1.20.1 – 1.21.1". */
function mcVersionLabel(versions) {
    const list = [...new Set(versions || [])].filter(v => /^\d+\.\d+/.test(v));
    if (!list.length) return null;
    const key = v => v.split(/[.-]/).map(n => n.padStart(4, '0')).join('.');
    list.sort((a, b) => key(a).localeCompare(key(b)));
    return list.length === 1 ? `Minecraft ${list[0]}` : `Minecraft ${list[0]} – ${list[list.length - 1]}`;
}

const packInfoObserver = 'IntersectionObserver' in window ? new IntersectionObserver(entries => {
    for (const entry of entries) {
        if (!entry.isIntersecting) continue;
        packInfoObserver.unobserve(entry.target);
        entry.target.__loadPackInfo && entry.target.__loadPackInfo();
    }
}, { rootMargin: '200px' }) : null;

/**
 * Modpack kartına mod sayısı ve Minecraft sürümü: paketin manifest'i kart görünür olunca okunur
 * (paketin tamamı indirilmez). Okunamazsa yalnızca listedeki sürüm kalır.
 */
function attachPackInfo(card, hit, versionChip, countChip) {
    const source = modrinth.source;
    card.__loadPackInfo = async () => {
        try {
            const info = await bridge.invoke('content.packInfo', { source, id: hit.id, versionId: hit.latestVersion || '' });
            if (!info) { countChip.remove(); return; }
            hit.packInfo = info;
            if (info.mcVersion) versionChip.textContent = `Minecraft ${info.mcVersion}`;
            countChip.textContent = `${info.modCount} mod`;
            countChip.classList.remove('loading');
        } catch { countChip.remove(); }
    };
    if (packInfoObserver) packInfoObserver.observe(card);
    else card.__loadPackInfo();
}

// ── Ayrıntı penceresi ──────────────────────────────────────────────────
async function openProjectDetails(hit) {
    const source = modrinth.source;
    const isPack = hit.type === 'modpack';
    await openModal(close => {
        const body = el('div', { class: 'pd-content' }, el('p', { class: 'hint' }, 'Yükleniyor…'));
        const tabs = el('div', { class: 'segmented pd-tabs' });
        let details = null, versions = null, tab = 'about';

        const install = canInstallGames()
            ? el('button', { class: 'btn btn-primary' }, isPack ? 'Instance olarak kur' : hit.installed ? 'Güncelle' : 'Kur')
            : desktopOnlyButton();
        if (canInstallGames()) install.addEventListener('click', () => installModrinthHit(hit, install, ''));
        const page = el('button', {
            class: 'btn', onclick: () => (source === 'curseforge'
                ? bridge.invoke('cf.openPage', { url: hit.pageUrl })
                : bridge.invoke('modrinth.openPage', { type: hit.type, slug: hit.slug })).catch(() => { }),
        }, `${CONTENT_SOURCES[source].label} ↗`);
        const links = el('div', { class: 'pd-links' });

        const info = hit.packInfo;
        const chips = [
            mcVersionLabel(info && info.mcVersion ? [info.mcVersion] : hit.gameVersions),
            info && `${info.modCount} mod`,
            ...(hit.loaders || []).map(l => LOADER_NAMES[l] || l),
            ...(hit.categories || []).map(categoryLabel),
        ].filter(Boolean).map(text => el('span', { class: 'chip static' }, text));

        function renderTabs() {
            const gallery = details ? details.gallery.length : 0;
            tabs.replaceChildren(...[['about', 'Açıklama'], ['gallery', gallery ? `Galeri (${gallery})` : 'Galeri'], ['versions', 'Sürümler']]
                .map(([key, label]) => el('button', { type: 'button', class: key === tab ? 'active' : '', onclick: () => { tab = key; render(); } }, label)));
        }

        function render() {
            renderTabs();
            if (tab === 'about') {
                if (!details) return;
                const html = details.html != null ? details.html : markdownToHtml(details.markdown || '');
                const content = el('div', { class: 'md' });
                content.append(sanitizeHtml(html));
                if (!content.textContent.trim() && !content.querySelector('img')) content.append(el('p', { class: 'hint' }, hit.description || 'Açıklama yok.'));
                body.replaceChildren(content);
            } else if (tab === 'gallery') {
                if (!details) return;
                body.replaceChildren(details.gallery.length
                    ? el('div', { class: 'pd-gallery' }, details.gallery.map(g => el('figure', {},
                        el('img', { src: g.thumb, alt: g.title || '', loading: 'lazy', referrerpolicy: 'no-referrer', onclick: () => viewImage(g.url, g.title) }),
                        g.title && el('figcaption', {}, g.title))))
                    : el('p', { class: 'hint' }, 'Bu projede galeri yok.'));
            } else {
                if (!versions) { body.replaceChildren(el('p', { class: 'hint' }, 'Sürümler yükleniyor…')); loadVersions(); return; }
                const TYPE_NAMES = { release: 'Kararlı', beta: 'Beta', alpha: 'Alfa' };
                body.replaceChildren(versions.length ? el('div', { class: 'pd-versions' }, versions.map(v => el('div', { class: 'user-row' },
                    el('div', {},
                        el('div', { class: 'uname' }, v.name || v.number,
                            v.type !== 'release' && el('span', { class: 'badge' }, TYPE_NAMES[v.type] || v.type),
                            v.manualOnly && el('span', { class: 'badge' }, 'Elle indirilir')),
                        el('div', { class: 'meta' }, [v.gameVersions.slice(-3).join(', '), v.loaders.map(l => LOADER_NAMES[l] || l).join(', '), formatAgo(v.published)]
                            .filter(Boolean).join(' · '))),
                    el('span', {}),
                    canInstallGames() && el('button', { class: 'btn btn-sm', onclick: () => installModrinthHit(hit, install, v.id) }, 'Bu sürümü kur'))))
                    : el('p', { class: 'hint' }, 'Uyumlu sürüm bulunamadı.'));
            }
        }

        async function loadVersions() {
            try { versions = await bridge.invoke(contentCommand('versions'), { projectId: hit.id, instanceId: isPack ? '' : modrinth.targetId || '' }); }
            catch (err) { body.replaceChildren(el('p', { class: 'form-error' }, 'Sürümler alınamadı: ' + err.message)); return; }
            if (tab === 'versions') render();
        }

        bridge.invoke('content.details', { source, id: hit.id }).then(d => {
            details = d;
            links.replaceChildren(...d.links.map(l => el('button', { class: 'link-btn', onclick: () => bridge.invoke('links.open', { url: l.url }).catch(() => { }) }, l.label + ' ↗')));
            render();
        }).catch(err => { body.replaceChildren(el('p', { class: 'form-error' }, 'Ayrıntılar alınamadı: ' + err.message)); });
        wireExternalLinks(body);
        renderTabs();

        return [
            el('div', { class: 'modal-head pd-head' },
                hit.iconUrl ? el('img', { class: 'pd-icon', src: hit.iconUrl, alt: '' }) : el('div', { class: 'pd-icon plain' }),
                el('div', { class: 'pd-title' },
                    el('b', {}, hit.title, hit.manualOnly && el('span', { class: 'badge' }, 'Elle indirilir')),
                    el('span', { class: 'hint' }, [hit.author, `↓ ${formatCount(hit.downloads)}`, hit.updated && `Güncelleme: ${formatAgo(hit.updated)}`].filter(Boolean).join(' · ')),
                    el('span', { class: 'pd-summary' }, hit.description)),
                el('button', { class: 'icon-close', type: 'button', 'aria-label': 'Kapat', onclick: () => close() }, '✕')),
            el('div', { class: 'pd-bar' }, el('div', { class: 'pd-chips' }, chips), el('div', { class: 'pd-actions' }, links, page, install)),
            el('div', { class: 'pd-tabbar' }, tabs),
            el('div', { class: 'modal-body pd-body' }, body),
        ];
    }, { wide: true });
}

/** Galerideki resmi büyük gösterir. */
function viewImage(url, title) {
    openModal(close => [
        el('div', { class: 'pd-viewer', onclick: () => close() },
            el('img', { src: url, alt: title || '', referrerpolicy: 'no-referrer' }),
            title && el('p', {}, title)),
    ], { wide: true });
}
