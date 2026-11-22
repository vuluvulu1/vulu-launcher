namespace VuluLauncher
{
    /// <summary>
    /// Mod güncellemesinde geri alma: güncellemeden önce projenin dosyaları mod-yedekleri klasörüne kopyalanır, güncelleme
    /// başarılı olursa kayıt instance'a yazılır (proje başına en son güncelleme). "Geri al" yeni dosyayı kaldırıp eskisini
    /// ve eski içerik kaydını yerine koyar. Güncelleme başarısız olursa ya da hiçbir şey değişmezse yedek silinir.
    /// </summary>
    internal static class ModBackups
    {
        public const string Folder = "mod-yedekleri";
        private const int MaxRecords = 30;
        private const string DisabledSuffix = ".disabled";

        private static string SourceKey(string? source) => string.IsNullOrEmpty(source) || source == "modrinth" ? "modrinth" : source;
        public static bool SameSource(string? a, string? b) => SourceKey(a) == SourceKey(b);

        private static InstalledContent? Tracked(InstanceMeta meta, string projectId, string? source) =>
            meta.Content.FirstOrDefault(c => c.ProjectId == projectId && SameSource(c.Source, source));

        /// <summary>İzlenen içeriğin diskteki dosyaları (açık ve kapatılmış hali).</summary>
        public static List<string> FilesOf(string dir, InstalledContent content) =>
            new[] { content.File, content.File + DisabledSuffix }
                .Select(f => Downloader.ResolveInside(dir, f)).Where(p => p != null && File.Exists(p)).Select(p => p!).ToList();

        /// <summary>Güncellemeden önce: dosyaların yedeği ve eski kayıt (henüz instance'a yazılmaz).</summary>
        public static ModUpdateRecord Prepare(string dir, InstanceMeta meta, string projectId, string? source, IEnumerable<string> files)
        {
            var old = Tracked(meta, projectId, source);
            var record = new ModUpdateRecord
            {
                Source = SourceKey(source) == "modrinth" ? null : source,
                ProjectId = projectId,
                Title = old?.Title ?? "",
                From = old?.VersionNumber ?? "",
                OldContent = old == null ? null : Clone(old),
            };
            string backupDir = Path.Combine(dir, Folder);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            foreach (var path in files.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists))
            {
                Directory.CreateDirectory(backupDir);
                string name = $"{stamp}-{Path.GetFileName(path)}";
                for (int i = 2; File.Exists(Path.Combine(backupDir, name)); i++) name = $"{stamp}-{i}-{Path.GetFileName(path)}";
                File.Copy(path, Path.Combine(backupDir, name));
                record.Backups.Add(new ModBackupFile { File = ModpackFiles.Normalize(Path.GetRelativePath(dir, path)), Backup = $"{Folder}/{name}" });
            }
            if (record.Title == "" && record.Backups.Count > 0)
                record.Title = Path.GetFileNameWithoutExtension(record.Backups[0].File.Replace(DisabledSuffix, ""));
            if (record.From == "" && record.Backups.Count > 0)
                record.From = Path.GetFileName(record.Backups[0].File.Replace(DisabledSuffix, ""));
            return record;
        }

        /// <summary>Güncelleme bitti: bir şey değiştiyse kayıt yazılır (aynı projenin önceki kaydı ve yedeği gider).</summary>
        public static bool Commit(string dir, InstanceMeta meta, ModUpdateRecord record)
        {
            var current = Tracked(meta, record.ProjectId, record.Source);
            bool changed = current != null && current.VersionId != record.OldContent?.VersionId;
            if (!changed || record.Backups.Count == 0) { Discard(dir, record); return false; }
            record.To = current!.VersionNumber;
            if (string.IsNullOrEmpty(record.Title) || record.OldContent == null) record.Title = current.Title;
            foreach (var previous in meta.UpdateHistory.Where(r => r.ProjectId == record.ProjectId && SameSource(r.Source, record.Source)).ToList())
                Forget(dir, meta, previous);
            meta.UpdateHistory.Add(record);
            while (meta.UpdateHistory.Count > MaxRecords) Forget(dir, meta, meta.UpdateHistory[0]);
            return true;
        }

        /// <summary>Yedek dosyaları silinir (kayıt instance'ta değilse).</summary>
        public static void Discard(string dir, ModUpdateRecord record)
        {
            foreach (var b in record.Backups)
                if (Downloader.ResolveInside(dir, b.Backup) is { } path && File.Exists(path))
                    try { File.Delete(path); } catch (IOException) { }
        }

        /// <summary>Kayıt ve yedeği kaldırılır (örn. mod silinince).</summary>
        public static void Forget(string dir, InstanceMeta meta, ModUpdateRecord record)
        {
            Discard(dir, record);
            meta.UpdateHistory.Remove(record);
        }

        public static ModUpdateRecord? Latest(InstanceMeta meta, string projectId, string? source) =>
            meta.UpdateHistory.LastOrDefault(r => r.ProjectId == projectId && SameSource(r.Source, source));

        /// <summary>Son güncellemeyi geri alır: yeni dosyalar kaldırılır, yedekler ve eski kayıt yerine konur.</summary>
        public static ModUpdateRecord Rollback(string dir, InstanceMeta meta, string projectId, string? source)
        {
            var record = Latest(meta, projectId, source) ?? throw new InvalidOperationException("Bu mod için geri alınacak güncelleme yok.");
            var restore = record.Backups.Select(b => (Backup: Downloader.ResolveInside(dir, b.Backup), Target: Downloader.ResolveInside(dir, b.File))).ToList();
            if (restore.Any(r => r.Backup == null || r.Target == null || !File.Exists(r.Backup)))
            {
                Forget(dir, meta, record);
                throw new InvalidOperationException("Eski sürümün yedeği bulunamadı; geri alınamıyor.");
            }

            foreach (var content in meta.Content.Where(c => c.ProjectId == projectId && SameSource(c.Source, source)).ToList())
            {
                foreach (var path in FilesOf(dir, content)) { Downloader.MakeWritable(path); File.Delete(path); }
                meta.Content.Remove(content);
            }
            meta.PendingManual.RemoveAll(p => p.ProjectId == projectId);
            foreach (var (backup, target) in restore)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target!)!);
                File.Move(backup!, target!, overwrite: true);
            }
            if (record.OldContent != null) meta.Content.Add(record.OldContent);
            meta.UpdateHistory.Remove(record);
            return record;
        }

        private static InstalledContent Clone(InstalledContent c) => new()
        {
            Source = c.Source, ProjectId = c.ProjectId, VersionId = c.VersionId, Title = c.Title, VersionNumber = c.VersionNumber,
            Type = c.Type, File = c.File, IconUrl = c.IconUrl, FromPack = c.FromPack,
        };
    }
}
