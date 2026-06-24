using System.Text.Json;
using System.Windows.Forms;

namespace RefractorForge.Viewer;

/// <summary>
/// Native Windows folder/file pickers. WinForms dialogs are modal and run on a dedicated STA thread,
/// so they work from the ordinary (MTA) program thread without an [STAThread] Main or a message loop.
/// On .NET these use the modern Vista-style dialogs by default.
/// </summary>
public static class Picker
{
    public static string? Folder(string title, string? startAt)
        => RunSta(() =>
        {
            using var d = new FolderBrowserDialog
            {
                Description = title,
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false,
            };
            if (!string.IsNullOrEmpty(startAt) && Directory.Exists(startAt)) d.SelectedPath = startAt;
            return d.ShowDialog() == DialogResult.OK ? d.SelectedPath : null;
        });

    public static string? File(string title, string filter, string? startNear)
        => RunSta(() =>
        {
            using var d = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
            if (!string.IsNullOrEmpty(startNear))
            {
                var dir = Directory.Exists(startNear) ? startNear : Path.GetDirectoryName(startNear);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) d.InitialDirectory = dir;
            }
            return d.ShowDialog() == DialogResult.OK ? d.FileName : null;
        });

    /// <summary>Multi-select file picker (Ctrl/Shift-click). Returns the chosen paths, or empty if cancelled.</summary>
    public static string[] Files(string title, string filter, string? startNear)
    {
        string[] result = Array.Empty<string>();
        var t = new Thread(() =>
        {
            using var d = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true, Multiselect = true };
            if (!string.IsNullOrEmpty(startNear))
            {
                var dir = Directory.Exists(startNear) ? startNear : Path.GetDirectoryName(startNear);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) d.InitialDirectory = dir;
            }
            if (d.ShowDialog() == DialogResult.OK) result = d.FileNames;
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        return result;
    }

    /// <summary>Native save-as picker. Returns the chosen path (overwrite already confirmed), or null if cancelled.</summary>
    public static string? Save(string title, string filter, string? defaultName, string? startNear)
        => RunSta(() =>
        {
            using var d = new SaveFileDialog { Title = title, Filter = filter, OverwritePrompt = true, AddExtension = true };
            if (!string.IsNullOrEmpty(defaultName)) d.FileName = defaultName;
            if (!string.IsNullOrEmpty(startNear))
            {
                var dir = Directory.Exists(startNear) ? startNear : Path.GetDirectoryName(startNear);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) d.InitialDirectory = dir;
            }
            return d.ShowDialog() == DialogResult.OK ? d.FileName : null;
        });

    /// <summary>Modal warning dialog on its own STA thread (safe to call from the MTA program thread).
    /// Used to report a failed level load instead of letting the app hard-crash on startup.</summary>
    public static void Error(string message, string title = "RefractorForge")
    {
        var t = new Thread(() => MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning));
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
    }

    private static string? RunSta(Func<string?> show)
    {
        string? result = null;
        var t = new Thread(() => result = show());
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        return result;
    }
}

/// <summary>
/// Persists the list of recently opened .rfproj paths in %APPDATA%\RefractorForge\recent.json.
/// This is the only user-level state stored outside a project file.
/// </summary>
public static class RecentProjects
{
    private static string FilePath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RefractorForge");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "recent.json");
        }
    }

    public static string[] Load()
    {
        try
        {
            return System.IO.File.Exists(FilePath)
                ? JsonSerializer.Deserialize<string[]>(System.IO.File.ReadAllText(FilePath)) ?? Array.Empty<string>()
                : Array.Empty<string>();
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>Prepend <paramref name="projectPath"/> to the list (max 10, deduped) and save.</summary>
    public static void Add(string projectPath)
    {
        try
        {
            var list = Load().ToList();
            list.RemoveAll(p => p.Equals(projectPath, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, projectPath);
            if (list.Count > 10) list = list.Take(10).ToList();
            System.IO.File.WriteAllText(FilePath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>Remove <paramref name="projectPath"/> from the list (e.g. after a failed load).</summary>
    public static void Remove(string projectPath)
    {
        try
        {
            var list = Load().ToList();
            if (list.RemoveAll(p => p.Equals(projectPath, StringComparison.OrdinalIgnoreCase)) == 0) return;
            System.IO.File.WriteAllText(FilePath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
