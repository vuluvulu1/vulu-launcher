namespace VuluLauncher
{
    /// <summary>Masaüstü kısayolundan gelen istek: bir paketi oyna ya da bir sunucuya katıl.</summary>
    internal sealed record StartupAction(string Kind, string Id)
    {
        /// <summary>"--play &lt;instance kimliği&gt;" ya da "--join &lt;sunucu kimliği&gt;"; yoksa null.</summary>
        public static StartupAction? Parse(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--play") return Valid("play", args[i + 1]);
                if (args[i] == "--join") return Valid("join", args[i + 1]);
            }
            return null;
        }

        public static StartupAction? FromMessage(string message)
        {
            int tab = message.IndexOf('\t');
            return tab < 0 ? null : Valid(message[..tab], message[(tab + 1)..].Trim());
        }

        public string ToMessage() => $"{Kind}\t{Id}";

        private static StartupAction? Valid(string kind, string id) =>
            (kind == "play" && id.Length is > 0 and <= 120 && !id.Any(char.IsControl))
            || (kind == "join" && System.Text.RegularExpressions.Regex.IsMatch(id, "^[0-9a-f]{24}$"))
                ? new StartupAction(kind, id) : null;
    }
}
