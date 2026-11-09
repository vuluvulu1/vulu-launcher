# vulu Sosyal: mobil uygulama planı

Launcher'ın sosyal tarafını telefona taşıyan ayrı ve hafif bir Android uygulaması. Minecraft açmaz; arkadaşlar,
uçtan uca şifreli mesajlar, ekran görüntüsü akışı, bildirimler ve adminler için bir yönetim paneli içerir.

## Kararlar

| Konu | Karar |
| --- | --- |
| Platform | Yalnızca Android. iOS, App Store dışında pratik olarak dağıtılamadığı için şimdilik yok. |
| Dağıtım | Siteden APK (`vuluvulu.com.tr`). Google Play ve App Store şimdilik planlanmıyor. |
| Teknoloji | Avalonia (Android) + ortak .NET kütüphanesi. Windows arayüzü de Avalonia; tema ve ViewModel'ler taşınabilir. |
| Paket kimliği | `com.vuluvulu.social`. Mevcut `com.vuluvulu.launcher` (Minecraft'ı açan uygulama) ile yan yana kurulabilir. |
| Güncelleme | Uygulama içinden, `AndroidUpdateFeed` mantığıyla: `releases/social/social.json` + `vulu-social.apk`, SHA-256 denetimi. |
| Push | Firebase Cloud Messaging (Play'de yayın gerektirmez). Google hizmeti olmayan telefonlarda WorkManager ile 15 dk'da bir yedek kontrol. |

## Repo yapısı

```
Social/   ← ortak: API istemcisi, giriş, mesaj şifrelemesi, modeller (Core ve Mobile kullanır)
Mobile/   ← Avalonia Android uygulaması
Core/     ← launcher'ın geri kalanı (oyun, paketler…), Social'ı kullanır
Tests/    ← şifreleme uyumluluk testleri: masaüstünde şifrelenen mesaj mobilde açılıyor mu
```

## Ekranlar

Alt sekmeler: **Akış · Mesajlar · Arkadaşlar · Bildirimler · Profil**, adminlerde ayrıca **Admin**.

- **Giriş:** vulu hesabı. Şifre yalnızca ilk girişte mesaj anahtarının yedeğini açmak için kullanılır; anahtar sonra
  Android Keystore ile şifrelenip saklanır, şifre saklanmaz.
- **Arkadaşlar:** liste, kim ne oynuyor (`/users/presence`), istek gönder / kabul / reddet.
- **Mesajlar:** konuşmalar, geçmiş, gönderme, okundu, güvenlik kodu doğrulama. Kripto `MessageCrypto` ile birebir aynı.
- **Akış:** ekran görüntüleri, beğeni, yorum. Resimler oturumla indirilip önbelleğe alınır.
- **Profil:** avatar, banner, biyografi, oynama süresi.
- **Admin** (yalnızca `isAdmin`; yetkiyi sunucu her istekte ayrıca denetler):
  - Özet kartları: bekleyen kayıt, çevrimiçi üye
  - Kayıt istekleri: liste, detay (kullanıcı adı, e-posta, tarih, e-posta doğrulandı mı), Onayla / Reddet
  - Kullanıcılar: arama, aktif/pasif, admin, roller, şifre sıfırlama
  - Ayarlar: kayıt onayı (otomatik / admin), Minecraft sahipliği şartı
  - Duyuru yaz / sil
  - Masaüstünde kalanlar: paket yükleme, radyo, depolama, rol oluşturma

## Kayıt isteği bildirimi

```
Kayıt (/auth/register) ──► e-posta doğrulaması (/auth/verify-email)
                                   │  registrationApproval = 'admin'
                                   ▼
                      istek onaylanabilir hâle gelir  ← bildirim burada
                                   ▼
          aktif ve isAdmin kullanıcıların cihazları → FCM
                                   ▼
          "Yeni kayıt isteği · ahmet_42"  → dokununca Admin › Kayıt istekleri
```

- Adminlik **gönderim anında** veritabanından kontrol edilir (FCM topic kullanılmaz): adminliği alınan hemen bildirim
  almayı bırakır.
- Bir admin onaylayınca/reddedince öbür adminlere sessiz `registration.resolved` gider; telefon bildirimi kaldırır.
- Push içeriğinde e-posta yok (Google'ın sunucularından geçer); yalnızca kullanıcı adı ve istek kimliği.
- Android'de ayrı "Kayıt istekleri" bildirim kanalı; sunucuda `notifyRegistrations` tercihi.
- Kayıtlara zaten sınır var (`MAX_PENDING`, IP başına günlük `MAX_PER_IP_PER_DAY`), bildirim yağmuru olmaz.

**Masaüstü (yapıldı):** launcher açıkken adminlerde bekleyen istekler 60 sn'de bir (bildirim alanındayken 120 sn)
sorulur; e-postasını doğrulamış yeni istek Windows bildirimi olur, tıklayınca Admin › Kayıt istekleri açılır. Admin
sekmesinin rozeti de güncel kalır. Ayarlar › Launcher › "Kayıt isteği bildirimi" ile kapatılabilir.

## Push kurulumu (bir kez)

Kod hazır (vulu-api ve Mobile); çalışması için bir Firebase projesi gerekir:

1. [Firebase Console](https://console.firebase.google.com) → proje oluştur (Analytics gerekmez).
2. Projeye **Android uygulaması** ekle, paket adı `com.vuluvulu.social`. İnen `google-services.json`'un içeriğini
   vulu-launcher reposunda **Settings → Secrets and variables → Actions → New repository secret** ile
   `GOOGLE_SERVICES_JSON` adıyla kaydet. Test APK'sı bundan sonra push'lu derlenir.
3. **Proje ayarları → Hizmet hesapları → Yeni özel anahtar oluştur**. İnen JSON'u VDS'e koy (ör.
   `/etc/vulu/firebase-service-account.json`, yalnızca API servisinin okuyabileceği izinlerle) ve `.env`'e
   `FCM_SERVICE_ACCOUNT_FILE=/etc/vulu/firebase-service-account.json` ekle. Bu dosya gizlidir; repoya girmez.
4. vulu-api'deki değişiklik `main`'e alınınca sunucuya yüklenir.

## Sunucu (vulu-api) değişiklikleri

vulu-api: Node.js + Express 5 + MongoDB (mongoose). Oturum JWT (HS256, 7 gün, `tokenVersion` ile şifre değişince
iptal). `requireAuth` kullanıcıyı her istekte veritabanından okuduğu için adminlik/aktiflik kontrolü zaten anlık.

| İş | Nerede |
| --- | --- |
| `Device` modeli: `username`, `token`, `platform`, `createdAt`, `lastSeenAt` | `models/Device.js` |
| `POST /users/me/devices { token, platform }`, `DELETE /users/me/devices/:token` | `routes/users.js` |
| FCM HTTP v1 gönderimi (servis hesabı ortam değişkeninden); `UNREGISTERED` dönen token silinir | `lib/push.js` |
| `notifyAdminsOfRegistration(reg)`: aktif adminlerin cihazlarına push | `lib/registrations.js` |
| Tetikleme: `/auth/verify-email` (ayar `admin` ise) ve `/auth/register` (e-posta kapalıysa) | `routes/auth.js` |
| `registration.resolved` push'u: onay ve red sonrası | `routes/admin.js` |
| `notifyRegistrations` tercihi (`User` alanı, `PUT /users/me`) | `models/User.js`, `routes/users.js` |
| Mesaj, arkadaşlık isteği, beğeni/yorum push'ları (mesajda içerik yok: "Yeni mesaj") | ilgili route'lar |
| Mobil için uzun oturum: yenileme token'ı ya da cihaz başına oturum, iptal edilebilir | `middleware/auth.js`, `routes/auth.js` |

## Fazlar

| Faz | İçerik | Süre |
| --- | --- | --- |
| 0 | `Social/` kütüphanesini Core'dan çıkarmak; `Mobile/` iskeleti, Meşale teması | ~1 hafta |
| 1 | Giriş, arkadaşlar, mesajlar, akış, profil, admin paneli; uygulama içi güncelleme | ~4–5 hafta |
| 2 | Sunucuda push altyapısı; mesaj, istek, beğeni/yorum ve adminlere kayıt bildirimi; WorkManager yedeği | ~2 hafta |
| 2.5 | Bildirimden Onayla/Reddet (Reddet onay ister); `android.json`/`social.json` imzası (`ReleaseSigner`) | ~1 hafta |
| Sonra | Engelleme ve şikâyet, davet kartları (`DmCard`), "birlikte oyna" bağlantısı, grup sohbeti | — |

## Riskler ve notlar

- **APK imza anahtarı** kaybolursa güncelleme yapılamaz (herkes silip yeniden kurar). En az iki güvenli yedek.
- **android.json imzasız**: masaüstündeki `release.json` gibi imzalanmalı. Android farklı anahtarla imzalı APK'yı
  güncelleme olarak kabul etmediği için risk sınırlı ama kapatılmalı.
- **Google geliştirici doğrulaması**: Google, mağaza dışı yüklenen uygulamalar için de geliştirici kimlik kaydını
  aşamalı olarak zorunlu kılıyor; Türkiye için tarih takip edilmeli.
- **Huawei vb.** (Google hizmeti yok): push gelmez, 15 dk'lık yedek kontrol ve uygulama açılınca liste.
- **KVKK**: mağaza şartı olmasa da hesap silme talebi için bir yol (admin üzerinden de olabilir).
