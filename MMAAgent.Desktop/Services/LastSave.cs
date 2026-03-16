using System;
using System.IO;
namespace MMAAgent.Desktop.Services;
public static class LastSave
{
    public static string GetBaseDir()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MMAAgent");

    public static void Save(string savePath)
    {
        Directory.CreateDirectory(GetBaseDir());
        File.WriteAllText(Path.Combine(GetBaseDir(), "last_save.txt"), savePath);
    }

    public static string? Load()
    {
        var p = Path.Combine(GetBaseDir(), "last_save.txt");
        if (!File.Exists(p)) return null;

        var savePath = File.ReadAllText(p).Trim();
        return File.Exists(savePath) ? savePath : null;
    }
}