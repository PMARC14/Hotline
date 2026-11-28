using Hotline.Core.Files;
using Hotline.Core.State;

namespace Hotline.Core.Windowing;

/// <summary>How a <see cref="ToolbarSession"/> save ended.</summary>
public enum ToolbarSaveStatus
{
    /// <summary>toolbar.json (and any staged pictures) were written.</summary>
    Saved,
    /// <summary>The edit changed nothing and no picture was staged.</summary>
    Unchanged,
    /// <summary>toolbar.json changed on disk since the session read or last saved it: nothing was written.</summary>
    ChangedOnDisk,
    /// <summary>Nothing was written; <see cref="ToolbarSaveResult.Error"/> says why.</summary>
    Failed,
}

/// <summary>The outcome of one save. <see cref="PicturesFailed"/> lists buttons whose staged picture could not be stored
/// after toolbar.json was saved: the button keeps the picture it had (or its glyph when it had none) until it is picked again.</summary>
public sealed record ToolbarSaveResult(ToolbarSaveStatus Status, string? Error = null, IReadOnlyList<string>? PicturesFailed = null)
{
    public bool Saved => Status == ToolbarSaveStatus.Saved;
    public bool ChangedOnDisk => Status == ToolbarSaveStatus.ChangedOnDisk;
}

/// <summary>
/// One Settings page's toolbar.json editing session: the model as read or last saved, the fingerprint that detects an outside
/// edit, every edit as a function on the model, and custom-button pictures staged in memory. A save refuses (and writes
/// nothing) when the file changed on disk since; pictures reach icons\ only after toolbar.json was written, so a failed
/// save leaves no stray picture and needs no rollback.
/// </summary>
public sealed class ToolbarSession
{
    private readonly string _path;
    private readonly IFileSystem? _files;
    private readonly StateStore? _state;
    private readonly Dictionary<string, byte[]> _pictures = new(StringComparer.OrdinalIgnoreCase);
    private string? _stamp;

    public ToolbarSession(string path, IFileSystem? fileSystem = null, StateStore? state = null)
    {
        _path = Path.GetFullPath(path);
        _files = fileSystem;
        _state = state;
        Load = ToolbarFile.Load(_path, fileSystem);
        _stamp = ToolbarFile.Fingerprint(_path, fileSystem);
    }

    /// <summary>The file as this session read or last saved it (defaults plus an error when it can't be used).</summary>
    public ToolbarLoad Load { get; private set; }
    public ToolbarModel Model => Load.Model;
    public string? Error => Load.Error;
    public string FilePath => _path;
    public string IconsFolder => CustomIcons.Folder(Path.GetDirectoryName(_path)!);

    /// <summary>True when toolbar.json's bytes differ from what this session read or last saved.</summary>
    public bool ChangedOnDisk => ToolbarFile.Fingerprint(_path, _files) != _stamp;

    /// <summary>Reads the file anew and takes its fingerprint; staged pictures are dropped (they belonged to the old read).</summary>
    public ToolbarLoad Reload()
    {
        _pictures.Clear();
        Load = ToolbarFile.Load(_path, _files);
        _stamp = ToolbarFile.Fingerprint(_path, _files);
        return Load;
    }

    /// <summary>Checks a picture file and holds its bytes for the next save of the button <paramref name="buttonId"/>
    /// (replacing one staged before). Nothing is written.</summary>
    public CustomIconProblem StagePicture(string buttonId, string sourcePath)
    {
        if (!ToolbarModel.IsCustomId(buttonId)) return CustomIconProblem.Unreadable;
        var (bytes, problem) = CustomIcons.Read(sourcePath);
        if (bytes is null) return problem;
        _pictures[buttonId] = bytes;
        return CustomIconProblem.None;
    }

    public bool HasStagedPicture(string buttonId) => _pictures.ContainsKey(buttonId);

    /// <summary>Runs one edit and saves it. A no-op edit saves nothing unless a picture is staged. Staged pictures are
    /// dropped whatever the outcome.</summary>
    public ToolbarSaveResult Edit(Func<ToolbarModel, ToolbarModel> edit)
    {
        try
        {
            if (Load.Error is { } unusable) return new(ToolbarSaveStatus.Failed, unusable);
            var next = edit(Load.Model);
            if (ReferenceEquals(next, Load.Model) && _pictures.Count == 0) return new(ToolbarSaveStatus.Unchanged);
            return Commit(next);
        }
        finally { _pictures.Clear(); }
    }

    /// <summary>Sets (or, with null, resets) one command's shortcut. Unlike <see cref="Edit"/> it checks the file first,
    /// so even an edit that changes nothing reports an outside change.</summary>
    public ToolbarSaveResult SetShortcut(string id, string? text)
    {
        if (ChangedOnDisk) { _pictures.Clear(); return new(ToolbarSaveStatus.ChangedOnDisk); }
        return Edit(m => ToolbarEditor.SetShortcut(m, id, text));
    }

    /// <summary>Puts every panel command's shortcut back to its default (<see cref="ToolbarEditor.ResetShortcuts"/>), keeping
    /// the layout. Like <see cref="SetShortcut"/> it checks the file first; an unreadable or newer file is refused.</summary>
    public ToolbarSaveResult ResetShortcuts()
    {
        if (ChangedOnDisk) { _pictures.Clear(); return new(ToolbarSaveStatus.ChangedOnDisk); }
        return Edit(ToolbarEditor.ResetShortcuts);
    }

    /// <summary>Puts the layout and look back to the defaults, keeping shortcuts. Works from the file as it is now (also when
    /// it is unreadable, which is what Reset is for); a newer file is refused.</summary>
    public ToolbarSaveResult ResetLayout()
    {
        var current = Reload();
        var next = ToolbarModel.ResetLayout(current.Error is null ? current.Model : null);
        var error = ToolbarFile.Save(_path, next, _files, _state);
        if (error is not null) return new(ToolbarSaveStatus.Failed, error);
        Load = new(next, null);
        _stamp = ToolbarFile.Fingerprint(_path, _files);
        return new(ToolbarSaveStatus.Saved);
    }

    /// <summary>One save is one operation with one backup set: toolbar.json first, then the staged pictures, then the pictures
    /// the saved bar no longer uses. A newer toolbar.json is never written, nor are its pictures.</summary>
    private ToolbarSaveResult Commit(ToolbarModel next)
    {
        if (ChangedOnDisk) return new(ToolbarSaveStatus.ChangedOnDisk);
        if (ToolbarFile.NewerVersionError(_path, _files) is { } newer) return new(ToolbarSaveStatus.Failed, newer);
        var backup = new BackupSet(Path.GetDirectoryName(_path)!, BackupReason.Save, _files);
        var previous = Load.Model;
        if (!ReferenceEquals(next, previous))
        {
            var error = ToolbarFile.Save(_path, next, _files, _state, backup);
            if (error is not null) return new(ToolbarSaveStatus.Failed, error);
            Load = new(next, null);
            _stamp = ToolbarFile.Fingerprint(_path, _files);
        }
        var failed = WritePictures(next, backup);
        RemoveUnusedPictures(previous, next, backup);
        return new(ToolbarSaveStatus.Saved, null, failed);
    }

    /// <summary>A button's own picture file (icons\&lt;id&gt;.png) is removed once the saved bar no longer uses it (the button
    /// was deleted, or went back to a built-in icon), backed up first into the save's set. Kept when the backup fails.</summary>
    private void RemoveUnusedPictures(ToolbarModel before, ToolbarModel after, BackupSet backup)
    {
        var files = _files ?? new RealFileSystem();
        foreach (var old in before.Buttons.Where(b => ToolbarModel.IsCustomId(b.Id) && CustomIcons.IsFileName(b.IconFile, b.Id)))
        {
            if (after.Buttons.Any(b => b.Id.Equals(old.Id, StringComparison.OrdinalIgnoreCase) && CustomIcons.IsFileName(b.IconFile, b.Id))) continue;
            var path = Path.Combine(IconsFolder, old.Id + ".png");
            try
            {
                if (!files.FileExists(path) || !backup.TryBackup(path)) continue;
                files.DeleteFile(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // the file stays: harmless
        }
    }

    /// <summary>Stores each staged picture whose button the saved model names; returns the ids that could not be stored.</summary>
    private List<string>? WritePictures(ToolbarModel saved, BackupSet backup)
    {
        List<string>? failed = null;
        foreach (var (id, bytes) in _pictures)
        {
            var button = saved.Buttons.FirstOrDefault(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (button is null || !CustomIcons.IsFileName(button.IconFile, button.Id)) continue;
            var result = new SafeFile(_files).Replace(Path.Combine(IconsFolder, button.Id + ".png"), bytes, backup, 0);
            if (result is not (SafeFileResult.Written or SafeFileResult.Unchanged)) (failed ??= []).Add(button.Id);
        }
        return failed;
    }
}
