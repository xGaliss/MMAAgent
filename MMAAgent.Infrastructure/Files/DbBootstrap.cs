using System;
using System.IO;

namespace MMAAgent.Infrastructure.Files
{
    public sealed class DbBootstrap
    {
        /// <summary>
        /// Crea una nueva DB de partida copiando una plantilla (readonly) a una ruta de saves.
        /// Devuelve la ruta completa del archivo nuevo.
        /// </summary>
        public string CreateNewSaveFromTemplate(string templateDbPath, string? saveName = null)
        {
            if (!File.Exists(templateDbPath))
                throw new FileNotFoundException($"No se encontró la DB plantilla en: {templateDbPath}");

            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MMAAgent",
                "Saves"
            );

            Directory.CreateDirectory(baseDir);

            // Nombre del save
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var safeName = SanitizeFileName(saveName);
            var fileName = string.IsNullOrWhiteSpace(safeName)
                ? $"save_{stamp}.db"
                : $"save_{safeName}_{stamp}.db";

            var savePath = Path.Combine(baseDir, fileName);

            File.Copy(templateDbPath, savePath, overwrite: false);

            return savePath;
        }

        public string[] ListSaves()
        {
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MMAAgent",
                "Saves"
            );

            if (!Directory.Exists(baseDir)) return Array.Empty<string>();
            return Directory.GetFiles(baseDir, "*.db", SearchOption.TopDirectoryOnly);
        }

        private static string? SanitizeFileName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            return name.Trim();
        }
    }
}