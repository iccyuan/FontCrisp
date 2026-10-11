using System.IO;
using System.Text.RegularExpressions;

namespace FontCrisp.Core;

/// <summary>
/// Windows Terminal draws with grayscale antialiasing unless the profile says otherwise. Flipping
/// profiles.defaults.antialiasingMode to "cleartype" is a text edit of settings.json; Terminal hot-reloads it.
/// Edits are textual on purpose: the file carries comments that a JSON round-trip would destroy.
/// </summary>
public static class TerminalFix
{
    static readonly Regex ModeRx = new("\"antialiasingMode\"\\s*:\\s*\"[a-zA-Z]+\"", RegexOptions.Compiled);
    static readonly Regex DefaultsRx = new("\"defaults\"\\s*:\\s*\\{", RegexOptions.Compiled);

    public static IEnumerable<string> SettingsFiles()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string pkgs = Path.Combine(local, "Packages");
        if (Directory.Exists(pkgs))
            foreach (var dir in Directory.GetDirectories(pkgs, "Microsoft.WindowsTerminal*"))
            {
                var f = Path.Combine(dir, "LocalState", "settings.json");
                if (File.Exists(f)) yield return f;
            }
        var unpackaged = Path.Combine(local, "Microsoft", "Windows Terminal", "settings.json");
        if (File.Exists(unpackaged)) yield return unpackaged;
    }

    /// <summary>Current mode of the first settings file, or null when Terminal is not installed; "grayscale" when unset (Terminal's default).</summary>
    public static string? CurrentMode()
    {
        var f = SettingsFiles().FirstOrDefault();
        if (f == null) return null;
        var m = ModeRx.Match(File.ReadAllText(f));
        return m.Success ? m.Value.Split('"')[3] : "grayscale";
    }

    /// <summary>Returns the number of files changed.</summary>
    public static int Apply(string mode)
    {
        int n = 0;
        foreach (var f in SettingsFiles())
        {
            string text = File.ReadAllText(f);
            string updated;
            if (ModeRx.IsMatch(text))
                updated = ModeRx.Replace(text, $"\"antialiasingMode\": \"{mode}\"");
            else
            {
                var m = DefaultsRx.Match(text);
                if (!m.Success) continue;
                // avoid a trailing comma when "defaults" is an empty object
                int next = m.Index + m.Length;
                while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
                string comma = next < text.Length && text[next] == '}' ? "" : ",";
                updated = text.Insert(m.Index + m.Length, $"\n        \"antialiasingMode\": \"{mode}\"{comma}");
            }
            if (updated == text) continue;
            File.Copy(f, f + ".fontcrisp.bak", true);
            File.WriteAllText(f, updated);
            n++;
        }
        return n;
    }
}
