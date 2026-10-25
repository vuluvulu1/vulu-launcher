# Üçüncü taraf bileşenler

vulu Launcher aşağıdaki bileşenleri içerir ya da kullanır. Bunlar kendi yazarlarına aittir ve kendi lisanslarıyla dağıtılır; vulu Launcher'ın AGPL-3.0 lisansı onları kapsamaz.

| Bileşen | Lisans |
|---|---|
| .NET çalışma zamanı ve System.Security.Cryptography.ProtectedData (Microsoft) | MIT |
| Photino.NET | Apache 2.0 |
| hls.js (radyo yayınları) | Apache 2.0 |
| .NET MAUI ve .NET for Android (Microsoft; Android sürümü) | MIT |
| CmlLib.Core ve CmlLib.Core.Installer.Forge | MIT |
| authlib-injector (yushijinhun; pakete gömülmez, ilk ihtiyaçta resmî adresinden indirilir) | GNU AGPL 3.0 |
| IBM Plex Sans ve IBM Plex Mono yazı tipleri | SIL Open Font License 1.1 ([metin](Launcher/wwwroot/fonts/OFL-IBMPlex.txt)) |
| Great Vibes yazı tipi | SIL Open Font License 1.1 ([metin](Launcher/wwwroot/fonts/OFL-GreatVibes.txt)) |
| Silkscreen yazı tipi | SIL Open Font License 1.1 ([metin](Launcher/wwwroot/fonts/OFL-Silkscreen.txt)) |

## Android oyun motoru

Android sürümü Minecraft'ı [Amethyst](https://github.com/AngelAuraMC/Amethyst-Android) (PojavLauncher çatalı, GNU LGPL 3.0) ile açar. vulu'nun değiştirdiği kaynak [vuluvulu1/vulu-amethyst](https://github.com/vuluvulu1/vulu-amethyst) deposunun `vulu` dalındadır; motoru derleyip APK'ya koyan betikler `tools/android-engine.ps1` ve `tools/android-build.ps1`.

Motorla gelen bileşenlerin tam listesi ve lisans metinleri [Launcher/wwwroot/licenses](Launcher/wwwroot/licenses) klasöründedir; uygulamada **Ayarlar → Açık kaynak lisansları** altında da görünür. Başlıcaları:

| Bileşen | Lisans |
|---|---|
| Amethyst / PojavLauncher / Boardwalk | GNU LGPL 3.0 |
| MobileGlues, OpenAL Soft | GNU LGPL 2.1 |
| LWJGL 3 | BSD 3-Clause |
| ANGLE | BSD tarzı |
| SDL3, sdl2-compat | zlib |
| SPIRV-Cross, shaderc, Oboe, JNA ve diğer Apache kütüphaneleri | Apache 2.0 |
| imgui-java, bytehook | MIT |
| caciocavallo; oyunun indirdiği OpenJDK | GNU GPL 2.0, Classpath istisnasıyla |

Minecraft, Mojang AB / Microsoft'un ticari markasıdır. vulu Launcher, Mojang ya da Microsoft ile bağlantılı değildir ve onlar tarafından desteklenmez.
