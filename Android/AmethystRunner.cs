using Android.Runtime;

namespace VuluLauncher
{
    /// <summary>
    /// Gömülü oyun motoru (Amethyst çatalı, Java) ile köprü. Java tarafında tek kapı net.kdt.pojavlaunch.VuluGame'dir;
    /// bağlama (binding) projesi yerine doğrudan JNI ile çağrılır, böylece motorun yüzlerce sınıfı C#'a açılmaz.
    /// </summary>
    internal sealed class AmethystRunner : IGameRunner
    {
        private const string BridgeClass = "net/kdt/pojavlaunch/VuluGame";
        private static IntPtr _class;

        /// <summary>Motor APK'da var mı (derlemeye eklenmediyse uygulama oyunsuz çalışır).</summary>
        public static bool Available { get; private set; }

        /// <summary>Uygulamanın her sürecinde, açılışta: Amethyst'in klasörlerini ve ayarlarını hazırlar.</summary>
        public static void Initialize(Android.App.Application app)
        {
            try { _class = JNIEnv.FindClass(BridgeClass); }
            catch (Java.Lang.Throwable) { Available = false; return; }
            IntPtr method = JNIEnv.GetStaticMethodID(_class, "initialize", "(Landroid/app/Application;)V");
            JNIEnv.CallStaticVoidMethod(_class, method, new JValue(app));
            Available = true;
        }

        public string GameRoot => CallString("gameRoot");
        public string InstancesRoot => Path.Combine(CallString("gameHome"), "instances");
        public static string GameHome => CallString("gameHome");

        private static string CallString(string name)
        {
            IntPtr method = JNIEnv.GetStaticMethodID(_class, name, "()Ljava/lang/String;");
            IntPtr value = JNIEnv.CallStaticObjectMethod(_class, method);
            return JNIEnv.GetString(value, JniHandleOwnership.TransferLocalRef) ?? "";
        }

        /// <summary>
        /// Oyunu açar. Uygun Java yoksa motor önce onu indirir (birkaç dakika sürebilir); bu yüzden arka planda çalışır.
        /// Oyun ayrı bir süreçte (:game) açılır; launcher arkada kalır.
        /// </summary>
        public Task LaunchAsync(GameRun run) => Task.Run(() =>
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity
                ?? throw new InvalidOperationException("Launcher ekranı bulunamadı.");
            IntPtr method = JNIEnv.GetStaticMethodID(_class, "launch",
                "(Landroid/app/Activity;Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;ILjava/lang/String;Ljava/lang/String;Ljava/lang/String;)V");
            using var gameDir = new Java.Lang.String(run.GameDir);
            using var version = new Java.Lang.String(run.VersionId);
            using var javaArgs = new Java.Lang.String(run.JavaArgs);
            using var username = new Java.Lang.String(run.Username);
            using var token = new Java.Lang.String(run.AccessToken);
            using var uuid = new Java.Lang.String(run.Uuid);
            JNIEnv.CallStaticVoidMethod(_class, method, new JValue(activity), new JValue(gameDir), new JValue(version),
                new JValue(javaArgs), new JValue(run.RamMb), new JValue(username), new JValue(token), new JValue(uuid));
        });
    }
}
