# vulu Sosyal (mobil)

Launcher'ın sosyal tarafını telefona taşıyan Android uygulaması. Plan: [docs/mobile-plan.md](../docs/mobile-plan.md).

| Proje | İçerik |
| --- | --- |
| `Vulu.Mobile/` | Arayüz (Avalonia): ekranlar, sayfa modelleri, Meşale teması. Platformdan bağımsız. |
| `Vulu.Mobile.Android/` | Android kabuğu (`com.vuluvulu.social`). |
| `Vulu.Mobile.Desktop/` | Geliştirme için: arayüzü bilgisayarda telefon boyutunda bir pencerede açar. Yayınlanmaz. |

Sunucuyla konuşma, giriş ve mesaj şifrelemesi launcher'la ortak olan [`Social/`](../Social) kütüphanesindedir.

## Çalıştırma

Bilgisayarda denemek için (.NET 10 SDK yeterli):

```
dotnet run --project Mobile/Vulu.Mobile.Desktop
```

Android APK'sı için Android iş yükü gerekir:

```
dotnet workload install android
dotnet publish Mobile/Vulu.Mobile.Android -c Release
```

GitHub'da her `Mobile/` değişikliğinde deneme APK'sı derlenir (Actions → Mobile test APK → artifact).

## Durum

Faz 0: giriş, alt sekmeler, profil ve admin panelinde kayıt istekleri (listele, onayla, reddet). Akış, mesajlar ve
arkadaşlar Faz 1'de.
