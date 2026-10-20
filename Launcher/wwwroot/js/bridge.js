// C# tarafıyla konuşan katman.
//   bridge.invoke(cmd, args)  → Promise (komutun cevabı)
//   bridge.send(cmd, args)    → cevap beklemeyen komut (pencere işlemleri)
//   bridge.on(event, handler) → C#'ın gönderdiği olayları dinler
const bridge = (() => {
    // Mesajlar Windows'ta Photino (window.external), Android'de MAUI HybridWebView üzerinden gider
    const transport = (() => {
        if (window.external && typeof window.external.sendMessage === 'function')
            return {
                platform: 'windows',
                post: raw => window.external.sendMessage(raw),
                listen: handler => window.external.receiveMessage(handler)
            };
        if (window.HybridWebView && typeof window.HybridWebView.SendRawMessage === 'function')
            return {
                platform: 'android',
                post: raw => window.HybridWebView.SendRawMessage(raw),
                listen: handler => window.addEventListener('HybridWebViewMessageReceived', e => handler(e.detail.message))
            };
        return null;
    })();
    const isNative = !!transport;
    const platform = transport ? transport.platform : 'browser';
    document.documentElement.dataset.platform = platform;
    const pending = new Map();
    const listeners = new Map();
    let nextId = 1;

    function dispatch(message) {
        if (message.event) {
            (listeners.get(message.event) || []).forEach(handler => handler(message.data));
            return;
        }
        const request = pending.get(message.id);
        if (!request) return;
        pending.delete(message.id);
        if (message.ok) request.resolve(message.result);
        else request.reject(new Error(message.error || 'Bilinmeyen hata'));
    }

    if (isNative) {
        transport.listen(raw => {
            try { dispatch(JSON.parse(raw)); }
            catch (err) { console.error('Geçersiz mesaj', err); }
        });
    }

    function invoke(cmd, args) {
        if (!isNative) return mockBackend.invoke(cmd, args || {});
        return new Promise((resolve, reject) => {
            const id = String(nextId++);
            pending.set(id, { resolve, reject });
            transport.post(JSON.stringify({ id, cmd, args: args || {} }));
        });
    }

    function send(cmd, args) {
        if (!isNative) return;
        transport.post(JSON.stringify({ cmd, args: args || {} }));
    }

    function on(event, handler) {
        if (!listeners.has(event)) listeners.set(event, []);
        listeners.get(event).push(handler);
    }

    return { invoke, send, on, isNative, platform, _dispatch: dispatch };
})();

// Sayfa launcher dışında (normal tarayıcıda) açılırsa arayüz üzerinde çalışabilmek için
// sahte bir arka uç. Gerçek launcher'da hiç kullanılmaz.
const mockBackend = (() => {
    const mockFlags = {};
    let mockBanner = null;
    let mockAnnouncement = { id: 'a1', text: 'Bu akşam 21:00\'de AGLR SMP\'de etkinlik var, herkesi bekliyoruz!', author: 'vulu', createdAt: new Date(Date.now() - 2 * 3600000).toISOString(), expiresAt: null };
    const mockComments = {};
    const feedImg = (c, t) => 'data:image/svg+xml;utf8,' + encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" width="480" height="270"><rect width="480" height="270" fill="${c}"/><rect y="180" width="480" height="90" fill="#222" opacity=".45"/><text x="20" y="50" font-size="28" fill="#fff" font-family="sans-serif">${t}</text></svg>`);
    let mockFeed = [
        ['Steve_TR', 'Sonunda üssün çatısı bitti 🏠', 'AGLR SMP', 4, true, '#4f6b3d'],
        ['halil', '', 'Create Macera', 2, false, '#6b4f3d'],
        ['KaiKaenn', 'Bu gün batımı', 'AGLR SMP', 9, false, '#6b3d5e'],
        ['vulu', 'Yeni harita', 'Vanilla', 1, false, '#3d4f6b'],
        ['Steve_TR', 'Tren hattı', 'Create Macera', 0, false, '#3d6b6b'],
        ['halil', 'Nether portalı', 'AGLR SMP', 3, false, '#6b3d3d'],
        ['KaiKaenn', '', 'Hafif Survival', 5, true, '#556b3d'],
    ].map(([author, caption, packName, likes, liked, color], i) => ({
        id: String(i + 1).padStart(24, 'a'), author, caption, packName, likes, liked, canDelete: author === 'vulu',
        createdAt: new Date(Date.now() - i * 5 * 3600000).toISOString(), thumbUrl: feedImg(color, caption || packName), width: 1920, height: 1080,
    }));
    let mockShots = Array.from({ length: 9 }, (_, i) => `2026-10-${String(17 - i).padStart(2, '0')}_21.0${i}.12.png`);
    const mockDm = {
        conversations: [{ username: 'Steve_TR', lastAt: new Date(Date.now() - 300000).toISOString(), unread: 2 }, { username: 'halil', lastAt: new Date(Date.now() - 86400000).toISOString(), unread: 0 }],
        threads: {
            Steve_TR: [
                { id: 'a1', from: 'vulu', to: 'Steve_TR', mine: true, text: 'Bu akşam AGLR\'ye giriyor musun?', problem: null, createdAt: new Date(Date.now() - 900000).toISOString(), read: true, peer: 'Steve_TR' },
                { id: 'a2', from: 'Steve_TR', to: 'vulu', mine: false, text: 'Evet, 9 gibi.\nYACL\'ı kurmayı unutma 😄', problem: null, createdAt: new Date(Date.now() - 600000).toISOString(), read: true, peer: 'Steve_TR' },
                { id: 'a3', from: 'Steve_TR', to: 'vulu', mine: false, text: null, problem: 'Bu mesaj eski bir anahtarınla şifrelenmiş; okunamıyor.', createdAt: new Date(Date.now() - 300000).toISOString(), read: false, peer: 'Steve_TR' },
            ],
            halil: [],
        },
    };
    let mockServers = [
        { id: 's1', name: 'AGLR SMP', address: 'aglr.vulu.net', description: 'Ana survival sunucusu', packId: 'p1', mcVersion: '', instanceId: null },
        { id: 's2', name: 'Vanilla Lobi', address: 'kapali.vulu.net:25570', description: '', packId: null, mcVersion: '1.21.1', instanceId: 'pack:vanilla' },
    ];
    const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
    const log = (text, level = 'info', replace = false) =>
        bridge._dispatch({ event: 'log', data: [{ time: new Date().toTimeString().slice(0, 8), text, level, replace }] });

    let registrations = [
        { id: 'r1', username: 'Steve_TR', email: 'steve@ornek.com', createdAt: new Date(Date.now() - 50 * 60000).toISOString() },
        { id: 'r2', username: 'madencibaba', email: 'maden@ornek.com', createdAt: new Date(Date.now() - 26 * 3600000).toISOString() },
    ];
    let users = [
        { id: '1', username: 'vulu', email: 'vulu@ornek.com', isActive: true, isAdmin: true, lastSeenAt: new Date().toISOString() },
        { id: '2', username: 'misafir', email: '', isActive: true, isAdmin: false, lastSeenAt: new Date(Date.now() - 3 * 86400000).toISOString() },
        { id: '3', username: 'eskiuye', email: 'eski@ornek.com', isActive: false, isAdmin: false, lastSeenAt: null },
    ];

    let settings = {
        defaultRamGb: 0, javaMode: 'auto', customJavaPath: '', jvmArgs: '', gameWidth: 0, gameHeight: 0, fullscreen: false,
        onGameStart: 'keep', skipSplash: false, consoleExpanded: false, discordPresence: true, musicEnabled: false, musicVolume: 40, accentColor: '#c27e3e',
    };

    let instances = [
        { id: 'pack:vanilla', kind: 'server', name: 'Vanilla', description: 'Modsuz, sade Minecraft.', mcVersion: '1.20.1', loader: 'vanilla', loaderVersion: null, recommendedRamGb: 4, logo: 'assets/minecraftlogo.png', background: 'assets/launchervanillabg.jpg', backgroundStyle: 'cover', thumbnail: 'assets/launchervanillabg.jpg', iconUrl: null },
        { id: 'inst:fabric-121', kind: 'custom', name: 'Fabric 1.21', description: 'Kişisel instance', mcVersion: '1.21.1', loader: 'fabric', loaderVersion: '0.16.14', recommendedRamGb: 8, logo: null, background: null, backgroundStyle: 'cover', thumbnail: null, iconUrl: null },
    ];

    const daysAgoIso = n => new Date(Date.now() - n * 86400000).toISOString();
    // İndirme panelini denemek için: C#'taki DownloadJob'un göndereceği durumları taklit eder
    const MOCK_MODS = ['Create', 'Sodium', 'Iris Shaders', 'Farmer\'s Delight', 'JourneyMap', 'Jade', 'EMI', 'Supplementaries', 'Waystones', 'FTB Quests', 'Architectury API', 'Cloth Config API', 'Balm', 'Collective', 'Moonlight Lib', 'Create: Steam \'n\' Rails'];
    let mockJobId = 0;
    async function simulateDownload(title, count) {
        const id = ++mockJobId;
        const items = Array.from({ length: Math.min(count, 40) }, (_, i) => ({ name: MOCK_MODS[i % MOCK_MODS.length] + (i >= MOCK_MODS.length ? ' ' + (i + 1) : ''), size: Math.round((0.2 + Math.random() * 6) * 1024 * 1024), done: 0, state: 'queued' }));
        items.push({ name: 'Paket dosyaları (config, görevler…)', size: 18 * 1024 * 1024, done: 0, state: 'queued' });
        const total = items.reduce((s, i) => s + i.size, 0);
        const started = Date.now();
        const emit = (state, message = '') => {
            const done = items.reduce((s, i) => s + i.done, 0);
            const elapsed = (Date.now() - started) / 1000;
            const speed = elapsed > 0.3 ? done / elapsed : 0;
            bridge._dispatch({ event: 'download', data: {
                id, title, phase: state === 'running' ? (items.at(-1).state === 'queued' ? 'Modlar indiriliyor' : 'Paket dosyaları indiriliyor') : '', state, message,
                files: { done: items.filter(i => i.state === 'done').length, total: items.length },
                bytes: { done, total }, speed, eta: speed > 0 ? (total - done) / speed : null, elapsed: Math.round(elapsed),
                active: items.filter(i => i.state === 'active').map(({ name, size, done }) => ({ name, size, done })),
                queued: items.filter(i => i.state === 'queued').length,
                recent: items.filter(i => i.state === 'done').slice(-5).reverse().map(({ name, size }) => ({ name, size })),
                failed: [],
            } });
        };
        let next = 0;
        while (items.some(i => i.state !== 'done')) {
            while (items.filter(i => i.state === 'active').length < 6 && next < items.length) items[next++].state = 'active';
            for (const item of items.filter(i => i.state === 'active')) {
                item.done = Math.min(item.size, item.done + 180 * 1024 + Math.random() * 600 * 1024);
                if (item.done >= item.size) item.state = 'done';
            }
            emit('running');
            log(`Modlar indiriliyor... ${items.filter(i => i.state === 'done').length}/${items.length}`, 'accent', true);
            await wait(160);
        }
        emit('done', title.replace(/ kuruluyor$/, ' kuruldu'));
    }

    const minutesAgoIso = n => new Date(Date.now() - n * 60000).toISOString();
    let feedback = [
        { id: 'f1', pack: 'p1', kind: 'request', text: 'Create: Steam \'n\' Rails eklenebilir mi? Trenler olmadan uzun mesafeler çok sıkıcı oluyor.', author: 'KaiKaenn', authorAvatarVersion: 0, status: 'open', reply: '', repliedBy: '', createdAt: minutesAgoIso(14), mine: false, canDelete: true },
        { id: 'f2', pack: 'p1', kind: 'feedback', text: 'Nether\'a girince oyun çöküyor, crash raporunda Oculus yazıyor.', author: 'ChillManHDM', authorAvatarVersion: 0, status: 'open', reply: '', repliedBy: '', createdAt: minutesAgoIso(190), mine: false, canDelete: true },
        { id: 'f3', pack: 'p1', kind: 'request', text: 'JEI yerine EMI olsun', author: 'Enwlyd', authorAvatarVersion: 0, status: 'done', reply: 'rev 3\'te değiştirildi.', repliedBy: 'vulu', createdAt: daysAgoIso(4), mine: false, canDelete: true },
        { id: 'f4', pack: 'general', kind: 'request', text: 'Biraz daha hafif bir survival paketi olabilir mi? Laptopta 142 modu kaldıramıyorum.', author: 'KaiKaenn', authorAvatarVersion: 0, status: 'open', reply: '', repliedBy: '', createdAt: daysAgoIso(1), mine: false, canDelete: true },
    ];
    let packs = [
        { id: 'p1', name: 'Create Macera', description: 'Create ve FTB Quests ile ilerlenen teknoloji/macera paketi. Görev kitabı baştan sona Türkçe.', author: 'vulu', mcVersion: '1.20.1', loader: 'forge', loaderVersion: '47.4.10', revision: 3, updatedAt: daysAgoIso(2), modCount: 142, recommendedRamGb: 12, resourcePackCount: 2, shaderCount: 1, sizeMb: 512.4, includes: ['config', 'kubejs', 'defaultconfigs', 'resourcepacks', 'shaderpacks'], coverUrl: 'assets/mainmenu.png', installed: null },
        { id: 'p2', name: 'Hafif Survival', description: 'Performans modları ve birkaç küçük eklenti. Düşük bilgisayarlar için.', author: 'vulu', mcVersion: '1.21.1', loader: 'fabric', loaderVersion: '0.16.14', revision: 2, updatedAt: daysAgoIso(9), modCount: 24, recommendedRamGb: 6, sizeMb: 61.7, includes: ['config'], coverUrl: 'assets/launchervanillabg.jpg', installed: { instanceId: 'inst:fabric-121', revision: 1 } },
        { id: 'p3', name: 'Skyblock', description: '', author: 'vulu', mcVersion: '1.20.1', loader: 'forge', loaderVersion: '47.4.10', revision: 1, updatedAt: daysAgoIso(30), modCount: 68, sizeMb: 140.2, includes: ['config', 'kubejs'], coverUrl: null, installed: null },
    ];
    packs[1].changelog = [
        { revision: 2, at: daysAgoIso(9), by: 'vulu', note: 'Sodium güncellendi, Iris eklendi. Shader açmak isteyen Ayarlar > Shader’dan seçebilir.', added: ['Iris Shaders'], removed: ['OptiFabric'], updated: [{ title: 'Sodium', from: '0.5.8', to: '0.6.1' }], filesChanged: true },
        { revision: 1, at: daysAgoIso(20), by: 'vulu', note: '', added: [], removed: [], updated: [], filesChanged: false },
    ];
    packs[0].changelog = [{ revision: 3, at: daysAgoIso(2), by: 'vulu', note: 'Görev kitabına 2. bölüm eklendi.', added: ['Create: Steam ’n’ Rails'], removed: [], updated: [], filesChanged: true }];

    const members = [
        { username: 'vulu', bio: 'Launcher\'ı yapan kişi. Genelde Create paketlerinde takılırım.', isAdmin: true, avatarVersion: 0, createdAt: daysAgoIso(140), lastSeenAt: daysAgoIso(0), packCount: 3 },
        { username: 'misafir', bio: '', isAdmin: false, avatarVersion: 0, createdAt: daysAgoIso(60), lastSeenAt: daysAgoIso(3), packCount: 0 },
        { username: 'halil', bio: 'Skyblock bağımlısı.', isAdmin: false, avatarVersion: 0, createdAt: daysAgoIso(20), lastSeenAt: daysAgoIso(1), packCount: 0 },
        { username: 'eskiuye', bio: '', isAdmin: false, avatarVersion: 0, createdAt: daysAgoIso(300), lastSeenAt: null, packCount: 0 },
    ];

    let content = [
        { file: 'mods/sodium-fabric-0.6.jar', type: 'mod', title: 'Sodium', version: 'mc1.21.1-0.6.13', iconUrl: null, sizeMb: 1.2, enabled: true, projectId: 'AANobbMI' },
        { file: 'mods/zoomify.jar.disabled', type: 'mod', title: 'Zoomify', version: '2.17.0', iconUrl: null, sizeMb: 0.3, enabled: false, projectId: 'w7ThoJFB' },
        { file: 'mods/elle-eklenen.jar', type: 'mod', title: 'elle-eklenen.jar', version: null, iconUrl: null, sizeMb: 0.4 },
        { file: 'shaderpacks/ComplementaryReimagined.zip', type: 'shader', title: 'Complementary Reimagined', version: 'r5.5', iconUrl: null, sizeMb: 0.6 },
    ];

    const sampleHits = type => ['Sodium', 'Iris Shaders', 'Lithium', 'Mod Menu', 'Fabric API', 'Entity Culling'].map((title, index) => ({
        id: 'p' + index, slug: title.toLowerCase(), type, title, author: 'yazar' + index, iconUrl: null,
        description: 'Örnek açıklama: oyunun performansını artıran, çok kullanılan bir içerik. Uzun açıklamalar iki satırda kesilir ve sonuna üç nokta konur ki liste düzenli kalsın.',
        downloads: 242793522 / (index + 1), follows: 41673 / (index + 1), updated: new Date(Date.now() - index * 40 * 86400000).toISOString(),
        categories: ['optimization', 'utility'].slice(0, 1 + index % 2), loaders: ['fabric', 'neoforge', 'quilt'].slice(0, 1 + index % 3), installed: index === 0 && type === 'mod',
        gameVersions: index % 2 ? ['1.20.1', '1.21.1'] : ['1.20.1', '1.21.1', '1.21.4'],
    }));

    const handlers = {
        'app.init': async () => ({ version: '0.7', savedUsername: 'vulu', hasSavedPassword: true, ramOptionsGb: [4, 8, 12, 16], settings }),
        'app.wake': async () => { await wait(600); return { reachable: true }; },
        'auth.login': async args => {
            await wait(500);
            if (args.username === 'hata') return { success: false, error: 'Kullanıcı adı veya şifre hatalı.' };
            if (registrations.some(r => r.username === args.username))
                return { success: false, error: 'Kaydın henüz onaylanmadı. Bir admin onayladığında giriş yapabilirsin.' };
            return { success: true, username: args.username || 'vulu', isAdmin: true };
        },
        'auth.register': async args => {
            await wait(500);
            if (args.username.toLowerCase() === 'vulu') throw new Error('Bu kullanıcı adı alınmış.');
            registrations.push({ id: 'r' + Date.now(), username: args.username, email: args.email.toLowerCase(), createdAt: new Date().toISOString() });
            return { success: true };
        },
        'admin.registrations': async () => { await wait(250); return registrations.map(r => ({ ...r })); },
        'admin.approveRegistration': async args => {
            const reg = registrations.find(r => r.id === args.id);
            registrations = registrations.filter(r => r.id !== args.id);
            users.push({ id: 'u' + Date.now(), username: reg.username, isActive: true, isAdmin: false });
            return { success: true };
        },
        'admin.rejectRegistration': async args => { registrations = registrations.filter(r => r.id !== args.id); return { success: true }; },
        'auth.logout': async () => ({ success: true }),
        'auth.forgetPassword': async () => ({ success: true }),
        'launcher.check': async () => { log('Launcher güncel.', 'muted'); return { status: 'upToDate' }; },
        'game.play': async args => {
            // Bir kez eksik bağımlılık senaryosu (Fabric 1.21)
            if (args.instanceId === 'inst:fabric-121' && !args.ignoreMissing && !mockFlags.depsShown) {
                mockFlags.depsShown = true;
                await wait(400);
                return { started: false, error: null, missing: [
                    { id: 'yet_another_config_lib_v3', requiredBy: ['Zoomify'], projectId: '1eAoo2KR', title: 'YetAnotherConfigLib (YACL)', iconUrl: null },
                    { id: 'secretlib', requiredBy: ['Gizli Mod', 'Başka Mod'], projectId: null, title: null, iconUrl: null },
                ] };
            }
            log(`${args.instanceId} hazırlanıyor...`, 'accent');
            await wait(700);
            for (let n = 0; n <= 2400; n += 600) { log(`Dosyalar kontrol ediliyor... ${n}/2400  assets`, 'accent', true); await wait(250); }
            log('Java 21 bulunamadı, Adoptium\'dan indiriliyor...', 'accent');
            for (let mb = 0; mb <= 45; mb += 15) { log(`Java 21 indiriliyor... ${mb} MB`, 'accent', true); await wait(200); }
            log('Java 21 kuruldu.', 'ok');
            log('Oyun başlatıldı! İyi oyunlar.', 'ok');
            log('[Render thread/INFO]: Setting user: vulu', 'game');
            setTimeout(() => { log('Oyun kapatıldı. İyi günler!', 'warn'); bridge._dispatch({ event: 'gameExited' }); }, 4000);
            return { started: true };
        },

        'profile.list': async () => { await wait(250); return members; },
        'profile.me': async () => members[0],
        'announcement.get': async () => ({ announcement: mockAnnouncement }),
        'announcement.post': async args => { mockAnnouncement = { id: 'a' + Date.now(), text: args.text, author: 'vulu', createdAt: new Date().toISOString(), expiresAt: args.hours ? new Date(Date.now() + args.hours * 3600000).toISOString() : null }; return { announcement: mockAnnouncement }; },
        'announcement.delete': async () => { mockAnnouncement = null; return { success: true }; },
        'admin.storage': async () => { await wait(300); return { limitBytes: 512 * 1048576, usedBytes: 381 * 1048576, parts: [
            { key: 'packs', label: 'Paket dosyaları', bytes: 262 * 1048576 }, { key: 'shots', label: 'Ekran görüntüleri', bytes: 71 * 1048576 },
            { key: 'images', label: 'Avatar, banner ve skinler', bytes: 9 * 1048576 }, { key: 'messages', label: 'Mesajlar', bytes: 2 * 1048576 }] }; },
        'app.takeAction': async () => null,
        'shortcuts.icon': async () => null,
        'shortcuts.create': async () => ({ path: 'C:\\Users\\vulu\\Desktop\\x.lnk' }),
        'social.shot': async args => mockFeed.find(s => s.id === args.id),
        'social.comments': async args => { await wait(200); return [...(mockComments[args.id] = mockComments[args.id] || [
            { id: 'c1', author: 'halil', text: 'Çok güzel olmuş!', createdAt: new Date(Date.now() - 3600000).toISOString(), canDelete: false }])]; },
        'social.comment': async args => { const c = { id: 'c' + Date.now(), author: 'vulu', text: args.text, createdAt: new Date().toISOString(), canDelete: true }; (mockComments[args.id] = mockComments[args.id] || []).push(c); return { ...c, commentCount: mockComments[args.id].length }; },
        'social.deleteComment': async args => { mockComments[args.id] = (mockComments[args.id] || []).filter(c => c.id !== args.commentId); return { success: true }; },
        'profile.get': async args => {
            await wait(250);
            const user = members.find(m => m.username === args.username);
            if (!user) throw new Error('Kullanıcı bulunamadı.');
            return { ...user, playtime: user.username === 'halil' ? { totalSeconds: 3600 * 47 + 1200, top: [{ name: 'AGLR SMP', seconds: 3600 * 31 }, { name: 'Create Macera', seconds: 3600 * 12 }, { name: 'Vanilla', seconds: 3600 * 4 + 1200 }] } : null,
                packs: user.username === 'vulu' ? packs.map(p => ({ id: p.id, name: p.name, revision: p.revision, mcVersion: p.mcVersion, loader: p.loader })) : [] };
        },
        'profile.saveBio': async args => { await wait(300); members[0].bio = args.bio.trim(); return members[0]; },
        'profile.pickAvatar': async () => null,
        'profile.removeAvatar': async () => { members[0].avatarVersion = 0; return members[0]; },
        'profile.pickBanner': async () => {
            const c = document.createElement('canvas'); c.width = 2400; c.height = 800;
            const g = c.getContext('2d'); const grad = g.createLinearGradient(0, 0, 2400, 800);
            grad.addColorStop(0, '#f7a35c'); grad.addColorStop(1, '#5a2a6b'); g.fillStyle = grad; g.fillRect(0, 0, 2400, 800);
            return { dataUrl: c.toDataURL('image/png'), type: 'image/png', size: 900000 };
        },
        'profile.uploadBanner': async args => { mockBanner = args.dataUrl; members[0].bannerVersion = Date.now(); return members[0]; },
        'profile.removeBanner': async () => { mockBanner = null; members[0].bannerVersion = 0; return members[0]; },
        // Tarayıcı ön izlemesinde skin dosyası yok; önizlemeyi denemek için skinUrl'e bir data: adresi verilebilir
        'profile.pickSkin': async args => { await wait(300); Object.assign(members[0], { skinHash: 'demo', skinModel: args.model || 'default' }); return members[0]; },
        'profile.setSkinModel': async args => { members[0].skinModel = args.model; return members[0]; },
        'profile.removeSkin': async () => { Object.assign(members[0], { skinHash: '', skinModel: 'default' }); return members[0]; },
        'profile.changePassword': async args => {
            await wait(400);
            if (args.currentPassword !== 'sifre') throw new Error('Mevcut şifre yanlış.');
            log('Şifren değiştirildi.', 'ok');
            return { success: true };
        },

        'packs.list': async () => {
            await wait(400);
            const open = key => feedback.filter(f => f.pack === key && f.status === 'open').length;
            return {
                configured: true, isAdmin: true, feedbackEnabled: true, generalFeedback: open('general'),
                packs: packs.map(p => ({ ...p, openFeedback: open(p.id) })),
            };
        },
        'feedback.list': async args => { await wait(300); return feedback.filter(f => f.pack === args.pack); },
        'feedback.post': async args => {
            await wait(250);
            const item = { id: 'f' + Date.now(), pack: args.pack, kind: args.kind, text: args.text.trim(), author: 'vulu', authorAvatarVersion: 0, status: 'open', reply: '', repliedBy: '', createdAt: new Date().toISOString(), mine: true, canDelete: true };
            feedback.unshift(item);
            return item;
        },
        'feedback.update': async args => {
            const item = feedback.find(f => f.id === args.id);
            if (args.status !== undefined) item.status = args.status;
            if (args.reply !== undefined) { item.reply = args.reply.trim(); item.repliedBy = item.reply ? 'vulu' : ''; }
            return { ...item };
        },
        'feedback.delete': async args => { feedback = feedback.filter(f => f.id !== args.id); return { success: true }; },
        'packs.install': async args => {
            const pack = packs.find(p => p.id === args.id);
            log(`${pack.name} kuruluyor...`, 'accent');
            await simulateDownload(`${pack.name} kuruluyor`, pack.modCount);
            const id = 'inst:' + pack.name.toLowerCase().replace(/[^a-z0-9]+/g, '-');
            instances = instances.concat({ id, kind: 'custom', locked: false, sharedPackId: pack.id, name: pack.name, description: pack.description, mcVersion: pack.mcVersion, loader: pack.loader, loaderVersion: pack.loaderVersion, recommendedRamGb: 8, logo: null, background: null, backgroundStyle: 'cover', thumbnail: null, iconUrl: null });
            pack.installed = { instanceId: id, revision: pack.revision };
            log(`${pack.name} kuruldu.`, 'ok');
            return { id };
        },
        'packs.update': async args => {
            const pack = packs.find(p => p.id === args.id);
            log(`${pack.name} güncelleniyor...`, 'accent'); await wait(800);
            pack.installed.revision = pack.revision;
            log(`${pack.name} güncellendi.`, 'ok');
            return { id: pack.installed.instanceId };
        },
        'packs.shareInfo': async () => {
            await wait(300);
            return {
                name: 'Fabric 1.21', description: '', mcVersion: '1.21.1', loader: 'fabric', modrinthCount: 2, existing: null, recommendedRamGb: 8, ramOptionsGb: [4, 8, 12, 16],
                entries: [
                    { path: 'config', isDir: true, files: 184, sizeMb: 2.4, checkedByDefault: true, note: null },
                    { path: 'kubejs', isDir: true, files: 37, sizeMb: 5.1, checkedByDefault: true, note: null },
                    { path: 'mods', isDir: true, files: 1, sizeMb: 0.4, checkedByDefault: true, note: 'Modrinth dışından eklenenler' },
                    { path: 'resourcepacks', isDir: true, files: 2, sizeMb: 18.2, checkedByDefault: true, note: "Modrinth'te bulunanlar bağlantı olur · açık olanlar kuranlarda da açık gelir" },
                    { path: 'shaderpacks', isDir: true, files: 1, sizeMb: 3.1, checkedByDefault: true, note: "Modrinth'te bulunanlar bağlantı olur · seçili shader config/ ile gider" },
                    { path: 'optionsshaders.txt', isDir: false, files: 1, sizeMb: 0.01, checkedByDefault: true, note: null },
                    { path: 'options.txt', isDir: false, files: 1, sizeMb: 0.01, checkedByDefault: false, note: null },
                    { path: 'servers.dat', isDir: false, files: 1, sizeMb: 0.01, checkedByDefault: false, note: null },
                ],
            };
        },
        'discord.page': async () => null,
        'mods.installMissing': async args => { log(`Eksik bağımlılıklar kuruluyor: ${args.projectIds.join(', ')}`, 'accent'); await wait(600); return { installed: ['YetAnotherConfigLib (YACL)'] }; },
        'packs.pickCover': async () => ({ path: 'C:\\kapak.jpg', url: 'assets/mainmenu.png' }),
        'packs.publish': async args => {
            log(`${args.name} hazırlanıyor...`, 'accent'); await wait(500);
            log(`${args.name} yükleniyor (221 dosya, 2 Modrinth içeriği)...`, 'accent', true); await wait(700);
            packs.unshift({ id: 'p' + Date.now(), name: args.name, description: args.description, author: 'vulu', mcVersion: '1.21.1', loader: 'fabric', loaderVersion: '0.16.14', revision: 1, updatedAt: new Date().toISOString(), modCount: 2, recommendedRamGb: args.ramGb, sizeMb: 26.3, includes: args.includes, coverUrl: args.coverPath ? 'assets/mainmenu.png' : null, installed: null });
            log(`${args.name} vulu Packs'te yayınlandı (rev 1).`, 'ok');
            return { id: packs[0].id, revision: 1 };
        },
        'packs.delete': async args => { packs = packs.filter(p => p.id !== args.id); return { success: true }; },

        'game.versions': async () => { await wait(300); return ['1.21.11', '1.21.10', '1.21.4', '1.21.1', '1.20.6', '1.20.4', '1.20.1', '1.19.4', '1.19.2', '1.18.2', '1.16.5', '1.12.2', '1.8.9']; },
        'instances.list': async () => instances,
        'instances.details': async args => {
            await wait(350);
            const daysAgo = n => new Date(Date.now() - n * 86400000).toISOString();
            if (args.id === 'pack:vanilla') return { installed: true, modCount: 0, sizeMb: 612, updatedAt: daysAgo(0), playTime: { seconds: 45296, sessions: 31, lastPlayed: daysAgo(1) } };
            return { installed: false, modCount: 0, sizeMb: 0, updatedAt: null, playTime: { seconds: 0, sessions: 0, lastPlayed: null } };
        },
        'instances.options': async () => { await wait(300); return { minecraftVersions: ['1.21.11', '1.21.1', '1.20.1', '1.19.2', '1.16.5', '1.12.2'], loaders: ['vanilla', 'fabric', 'quilt', 'forge', 'neoforge'] }; },
        'instances.loaderVersions': async args => { await wait(300); return args.loader === 'neoforge' && args.mcVersion === '1.20.1' ? [] : ['0.16.14', '0.16.13', '0.16.10']; },
        'instances.create': async args => {
            const id = 'inst:' + args.name.toLowerCase().replace(/[^a-z0-9]+/g, '-');
            instances = instances.concat({ id, kind: 'custom', name: args.name, description: 'Kişisel instance', mcVersion: args.mcVersion, loader: args.loader, loaderVersion: args.loaderVersion || null, recommendedRamGb: 8, logo: null, background: null, backgroundStyle: 'cover', thumbnail: null, iconUrl: null });
            return { id };
        },
        'instances.update': async args => {
            instances = instances.map(i => i.id === args.id ? { ...i, name: args.name, mcVersion: args.mcVersion, loader: args.loader, loaderVersion: args.loaderVersion || null } : i);
            return { id: args.id };
        },
        'instances.delete': async args => { instances = instances.filter(i => i.id !== args.id); return { success: true }; },
        'instances.content': async () => ({ name: 'Fabric 1.21', mcVersion: '1.21.1', loader: 'fabric', items: content }),
        'instances.removeContent': async args => { content = content.filter(c => c.file !== args.file); return { success: true }; },
        'instances.setContentEnabled': async args => {
            const item = content.find(c => c.file === args.file);
            const base = args.file.replace(/\.disabled$/, '');
            if (item) { item.enabled = args.enabled; item.file = args.enabled ? base : base + '.disabled'; }
            return { file: item && item.file, enabled: args.enabled };
        },
        'instances.checkUpdates': async () => { await wait(700); return { updates: [
            { file: 'mods/sodium-fabric-0.6.jar', projectId: 'AANobbMI', versionId: 'v2', from: 'mc1.21.1-0.6.13', to: 'mc1.21.1-0.6.14', type: 'release', stable: true },
            { file: 'shaderpacks/ComplementaryReimagined.zip', projectId: 'HVnmMxH1', versionId: 'v3', from: 'r5.5', to: 'r5.6-beta', type: 'beta', stable: false },
        ] }; },
        'mods.update': async args => { await wait(500); const c = content.find(x => x.projectId === args.projectId); if (c) c.version = 'güncel'; return { installed: [args.projectId] }; },
        'crash.openReport': async () => ({ opened: true }),
        'dm.status': async () => ({ ready: true, error: null }),
        'dm.conversations': async () => mockDm.conversations,
        'dm.history': async args => {
            await wait(200);
            return { messages: (mockDm.threads[args.user] || []).filter(m => !args.before || m.createdAt < args.before),
                peerReady: args.user !== 'eskiuye', peerError: null, keyChanged: args.user === 'halil', verified: false };
        },
        'dm.send': async args => {
            const m = { id: 'm' + Date.now(), from: 'vulu', to: args.user, mine: true, text: args.text, card: args.card || null, problem: null, createdAt: new Date().toISOString(), read: false, peer: args.user };
            (mockDm.threads[args.user] = mockDm.threads[args.user] || []).push(m);
            return { message: m, keyChanged: false };
        },
        'dm.read': async () => ({ updated: 0 }),
        'dm.delete': async () => ({ success: true }),
        'dm.safety': async () => ({ code: '88891 95687 76438 74195 64398 37156', verified: false, changed: false }),
        'dm.verify': async () => ({ verified: true }),
        'community.presence': async () => [
            { username: 'Steve_TR', state: 'playing', instance: 'AGLR SMP', mcVersion: '26.3', loader: 'neoforge', packId: 'p1', server: 'aglr.vulu.net', since: new Date(Date.now() - 1500000).toISOString() },
            { username: 'ardak', state: 'launcher', instance: '', mcVersion: '', loader: '', packId: null, server: '', since: new Date().toISOString() },
            { username: 'vulu', state: 'launcher', instance: '', mcVersion: '', loader: '', packId: null, server: '', since: new Date().toISOString() },
        ],
        'community.servers': async () => { await wait(300); return mockServers; },
        'community.ping': async args => {
            await wait(400 + Math.random() * 500);
            return args.address.startsWith('kapali')
                ? { online: false }
                : { online: true, players: 3, maxPlayers: 20, version: 'NeoForge 26.3', motd: 'AGLR SMP · Hoş geldin!', ping: 38, sample: ['Steve_TR', 'ardak', 'Notch'] };
        },
        'servers.save': async args => {
            const entry = { id: args.id || 's' + Date.now(), name: args.name, address: args.address, description: args.description, packId: args.packId || null, mcVersion: args.packId ? '' : args.mcVersion, instanceId: args.packId ? null : 'pack:vanilla' };
            mockServers = mockServers.filter(x => x.id !== entry.id).concat(entry);
            return entry;
        },
        'servers.delete': async args => { mockServers = mockServers.filter(x => x.id !== args.id); return { success: true }; },

        'modrinth.search': async args => { await wait(350); return { total: 38, offset: args.offset, hits: sampleHits(args.type) }; },
        'modrinth.filters': async args => {
            await wait(200);
            const byType = {
                mod: [{ header: 'categories', names: ['adventure', 'decoration', 'equipment', 'library', 'magic', 'optimization', 'storage', 'technology', 'utility', 'worldgen'] }],
                modpack: [{ header: 'categories', names: ['adventure', 'challenging', 'combat', 'kitchen-sink', 'lightweight', 'magic', 'optimization', 'quests', 'technology'] }],
                resourcepack: [{ header: 'categories', names: ['realistic', 'simplistic', 'themed'] }, { header: 'resolutions', names: ['16x', '32x', '64x'] }],
                shader: [{ header: 'categories', names: ['cartoon', 'fantasy', 'realistic'] }, { header: 'features', names: ['bloom', 'shadows', 'reflections'] }, { header: 'performance impact', names: ['low', 'medium', 'high'] }],
            };
            return { categories: byType[args.type] || [], sorts: ['relevance', 'downloads', 'follows', 'newest', 'updated'], minecraftVersions: ['1.21.11', '1.21.1', '1.20.1', '1.19.2'], loaders: ['fabric', 'quilt', 'forge', 'neoforge'] };
        },
        'modrinth.versions': async () => {
            await wait(250);
            return [
                { id: 'v3', name: 'Sodium 0.6.13', number: 'mc1.21.1-0.6.13', type: 'release', published: new Date(Date.now() - 20 * 86400000).toISOString(), gameVersions: ['1.21', '1.21.1'], loaders: ['fabric', 'quilt'] },
                { id: 'v2', name: 'Sodium 0.6.12 Beta 2', number: 'mc1.21.1-0.6.12-beta.2', type: 'beta', published: new Date(Date.now() - 90 * 86400000).toISOString(), gameVersions: ['1.21.1'], loaders: ['fabric'] },
                { id: 'v1', name: 'Sodium 0.6.0', number: 'mc1.21.1-0.6.0', type: 'release', published: new Date(Date.now() - 400 * 86400000).toISOString(), gameVersions: ['1.21.1'], loaders: ['fabric'] },
            ];
        },
        'modrinth.openPage': async () => ({ opened: true }),
        'instances.pickBackground': async args => {
            instances = instances.map(i => i.id === args.id ? { ...i, background: 'assets/launchervanillabg.jpg', backgroundStyle: 'cover', hasCustomBackground: true, thumbnail: i.kind === 'custom' ? 'assets/launchervanillabg.jpg' : i.thumbnail } : i);
            return { changed: true };
        },
        'instances.pickLogo': async args => {
            instances = instances.map(i => i.id === args.id ? { ...i, logo: 'assets/minecraftlogo.png', hasCustomLogo: true } : i);
            return { changed: true };
        },
        'instances.clearLogo': async args => {
            instances = instances.map(i => i.id === args.id ? { ...i, logo: i.kind === 'custom' ? null : 'assets/minecraftlogo.png', hasCustomLogo: false } : i);
            return { changed: true };
        },
        'instances.clearBackground': async args => {
            instances = instances.map(i => i.id === args.id ? { ...i, background: i.kind === 'custom' ? null : i.background, hasCustomBackground: false, thumbnail: i.kind === 'custom' ? null : i.thumbnail } : i);
            return { changed: true };
        },
        'modrinth.install': async () => {
            log('Iris Shaders indiriliyor...', 'accent'); await wait(900);
            log('Sodium indiriliyor...', 'accent'); await wait(900);
            return { success: true, installed: ['Iris Shaders', 'Sodium'] };
        },
        'modrinth.installModpack': async () => {
            for (let n = 0; n <= 120; n += 30) { log(`Modpack dosyaları indiriliyor... ${n}/120`, 'accent', true); await wait(400); }
            instances = instances.concat({ id: 'inst:fabulously', kind: 'custom', name: 'Fabulously Optimized', description: 'Modrinth modpack · 6.1.0', mcVersion: '1.21.1', loader: 'fabric', loaderVersion: '0.16.14', recommendedRamGb: 8, logo: null, background: null, backgroundStyle: 'cover', thumbnail: null, iconUrl: null });
            return { success: true, id: 'inst:fabulously', name: 'Fabulously Optimized' };
        },

        'settings.get': async () => ({ settings, ramOptionsGb: [4, 8, 12, 16], installedJava: [17, 21], version: '0.7', gameFolder: 'D:\\Launchers\\vulu Launcher\\', hasSavedPassword: true, savedUsername: 'vulu' }),
        'settings.save': async args => { settings = { ...args }; return { settings }; },
        'settings.pickJava': async () => ({ path: 'C:\\Program Files\\Java\\jdk-21\\bin\\java.exe' }),
        'settings.removeJava': async () => ({ removed: 2 }),
        'files.open': async () => ({ opened: true }),
        'screenshots.list': async () => {
            await wait(250);
            const colors = ['#3d6b4f', '#6b4f3d', '#3d4f6b', '#6b3d5e', '#556b3d', '#3d6b6b'];
            const shot = (c, i) => 'data:image/svg+xml;utf8,' + encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" width="1920" height="1080"><rect width="1920" height="1080" fill="${c}"/><rect y="700" width="1920" height="380" fill="#2b2b2b" opacity=".5"/><text x="80" y="160" font-size="96" fill="#fff" font-family="sans-serif">Ekran görüntüsü ${i + 1}</text></svg>`);
            const shots = mockShots.map((n, i) => ({ name: n, url: shot(colors[i % colors.length], i), takenAt: new Date(Date.now() - i * 7200000).toISOString(), sizeKb: 2400 + i * 37 }));
            return { total: shots.length, totalMb: shots.reduce((a, s) => a + s.sizeKb, 0) / 1024, shots };
        },
        'social.feed': async args => {
            await wait(300);
            const list = mockFeed.filter(s => (!args.author || s.author === args.author) && (!args.before || s.createdAt < args.before));
            return { shots: list.slice(0, 6), more: list.length > 6 };
        },
        'social.image': async args => { await wait(400); return { url: mockFeed.find(s => s.id === args.id).thumbUrl }; },
        'social.like': async args => { const s = mockFeed.find(x => x.id === args.id); if (args.like !== s.liked) { s.liked = !!args.like; s.likes += args.like ? 1 : -1; } return { likes: s.likes, liked: s.liked }; },
        'social.delete': async args => { mockFeed = mockFeed.filter(s => s.id !== args.id); return { success: true }; },
        'social.save': async () => ({ path: 'C:\\Users\\vulu\\Pictures\\vulu Launcher\\x.webp' }),
        'social.localImage': async () => {
            const c = document.createElement('canvas'); c.width = 1920; c.height = 1080;
            const g = c.getContext('2d'); g.fillStyle = '#3d6b4f'; g.fillRect(0, 0, 1920, 1080); g.fillStyle = '#fff'; g.font = '96px sans-serif'; g.fillText('Yerel görüntü', 80, 160);
            return { dataUrl: c.toDataURL('image/png') };
        },
        'social.share': async args => {
            await wait(500);
            const s = { id: String(Date.now()).padStart(24, '0'), author: 'vulu', caption: args.caption, packName: 'Fabric 1.21', likes: 0, liked: false, canDelete: true, createdAt: new Date().toISOString(), thumbUrl: args.thumb, width: args.width, height: args.height };
            mockFeed.unshift(s);
            return s;
        },
        'screenshots.open': async () => ({ opened: true }),
        'screenshots.reveal': async () => ({ opened: true }),
        'screenshots.copy': async () => ({ copied: true }),
        'screenshots.delete': async args => { mockShots = mockShots.filter(n => n !== args.name); return { deleted: true }; },
        'packs.updates': async () => {
            const target = instances.find(i => i.kind === 'custom');
            return { updates: target ? [{ instanceId: target.id, packId: 'p1', name: target.name, from: 3, to: 4 }] : [] };
        },

        'admin.users': async () => { await wait(250); return users.map(u => ({ ...u })); },
        'admin.saveUser': async args => {
            await wait(250);
            if (args.id) {
                const self = users.find(u => u.id === args.id).username === 'vulu';
                if (self && (!args.isAdmin || !args.isActive)) throw new Error('Kendi adminliğini kaldıramaz ya da kendi hesabını kapatamazsın.');
                users = users.map(u => u.id === args.id ? { ...u, isAdmin: args.isAdmin, isActive: args.isActive } : u);
                return users.find(u => u.id === args.id);
            }
            if (users.some(u => u.username.toLowerCase() === args.username.toLowerCase())) throw new Error('Bu kullanıcı adı alınmış ya da bekleyen bir kayıt isteğinde.');
            if (args.password.length < 6) throw new Error('Şifre 6-72 karakter olmalı.');
            const user = { id: String(Date.now()), username: args.username, email: args.email.toLowerCase(), isAdmin: args.isAdmin, isActive: args.isActive, lastSeenAt: null };
            users.push(user);
            return user;
        },
        'admin.deleteUser': async args => { users = users.filter(u => u.id !== args.id); return { success: true }; },
    };

    return {
        invoke: (cmd, args) => handlers[cmd] ? handlers[cmd](args) : Promise.reject(new Error('Bilinmeyen komut: ' + cmd)),
    };
})();
