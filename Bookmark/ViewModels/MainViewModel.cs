using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ReactiveUI;
using Avalonia.Controls;
using System.Reactive;
using Avalonia.Platform.Storage;
using System.Linq;
using System.Reactive.Linq;

namespace Bookmark.ViewModels;

public class MainViewModel : ReactiveObject
{
    private IStorageFolder? _lastSelectedFolder;
    private string? _selectedFolder;
    private string? _fileContent;
    private IStorageFile? _selectedFile;
    private ObservableCollection<IStorageFile> _files = new();
    private string? _errorMessage;
    private string? _currentActiveBookmarkId;
    private ObservableCollection<string> _bookmarks = new();
    private string? _selectedBookmarkId;

    private readonly string _bookmarkFileName = "bookmarkDemo.json";
    private readonly string _applicationFolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AvaloniaDemo");

    /// <summary>
    /// Path to a file in the app's private data directory for storing bookmark IDs.
    /// </summary>
    private string BookmarkFilePath => Path.Combine(_applicationFolderPath, _bookmarkFileName);

    /// <summary>
    /// Gets or sets the collection of files in the selected folder.
    /// </summary>
    public ObservableCollection<IStorageFile> Files
    {
        get => _files;
        set => this.RaiseAndSetIfChanged(ref _files, value);
    }

    /// <summary>
    /// Gets or sets the path of the currently selected folder.
    /// </summary>
    public string? SelectedFolder
    {
        get => _selectedFolder;
        set => this.RaiseAndSetIfChanged(ref _selectedFolder, value);
    }

    /// <summary>
    /// Gets or sets the IStorageFolder representing the last selected folder.
    /// </summary>
    public IStorageFolder? LastSelectedFolder
    {
        get => _lastSelectedFolder;
        set => this.RaiseAndSetIfChanged(ref _lastSelectedFolder, value);
    }

    /// <summary>
    /// Gets or sets the currently selected file from the list.
    /// </summary>
    public IStorageFile? SelectedFile
    {
        get => _selectedFile;
        set => this.RaiseAndSetIfChanged(ref _selectedFile, value);
    }

    /// <summary>
    /// Gets or sets the content of the currently selected file.
    /// </summary>
    public string? FileContent
    {
        get => _fileContent;
        set => this.RaiseAndSetIfChanged(ref _fileContent, value);
    }

    /// <summary>
    /// Gets or sets the error message to be displayed.
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    /// <summary>
    /// Gets or sets the bookmark ID of the currently loaded folder.
    /// </summary>
    public string? CurrentActiveBookmarkId
    {
        get => _currentActiveBookmarkId;
        set => this.RaiseAndSetIfChanged(ref _currentActiveBookmarkId, value);
    }

    /// <summary>
    /// Gets or sets the collection of loaded bookmark IDs for user selection.
    /// </summary>
    public ObservableCollection<string> Bookmarks
    {
        get => _bookmarks;
        set => this.RaiseAndSetIfChanged(ref _bookmarks, value);
    }

    /// <summary>
    /// Gets or sets the currently selected bookmark ID from the list.
    /// </summary>
    public string? SelectedBookmarkId
    {
        get => _selectedBookmarkId;
        set => this.RaiseAndSetIfChanged(ref _selectedBookmarkId, value);
    }

    /// <summary>
    /// Gets a value indicating whether an error message is visible.
    /// </summary>
    public bool IsErrorMessageVisible => !string.IsNullOrEmpty(ErrorMessage);

    public ReactiveCommand<Control, Unit> SelectFolderCommand { get; }
    public ReactiveCommand<Control, Unit> OverwriteCommand { get; }
    public ReactiveCommand<Control, Unit> SaveAsCommand { get; }
    public ReactiveCommand<Control, Unit> SaveBookmarksCommand { get; }
    public ReactiveCommand<Control, Unit> LoadBookmarksCommand { get; }
    public ReactiveCommand<Control, Unit> ReleaseBookmarkCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteFileCommand { get; }
    public ReactiveCommand<Control, Unit> LoadSelectedBookmarkCommand { get; }

    public MainViewModel()
    {
        SelectFolderCommand = ReactiveCommand.CreateFromTask<Control>(SelectFolderAsync);
        OverwriteCommand = ReactiveCommand.CreateFromTask<Control>(OverwriteAsync, this.WhenAnyValue(x => x.SelectedFile).Select(x => x != null));
        SaveAsCommand = ReactiveCommand.CreateFromTask<Control>(SaveAsAsync);
        SaveBookmarksCommand = ReactiveCommand.CreateFromTask<Control>(SaveBookmarksAsync, this.WhenAnyValue(x => x.LastSelectedFolder).Select(x => x != null));
        LoadBookmarksCommand = ReactiveCommand.CreateFromTask<Control>(LoadBookmarksAsync);
        ReleaseBookmarkCommand = ReactiveCommand.CreateFromTask<Control>(ReleaseBookmarkAsync, this.WhenAnyValue(x => x.SelectedBookmarkId).Select(x => x != null));
        DeleteFileCommand = ReactiveCommand.CreateFromTask(DeleteFileAsync, this.WhenAnyValue(x => x.SelectedFile).Select(x => x != null));
        LoadSelectedBookmarkCommand = ReactiveCommand.CreateFromTask<Control>(LoadSelectedBookmarkAsync, this.WhenAnyValue(x => x.SelectedBookmarkId).Select(x => x != null));

        SubscribeToCommandExceptions();

        this.WhenAnyValue(x => x.SelectedFile)
            .Where(file => file != null)
            .Subscribe(async file => await LoadFileContentAsync(file));

        this.WhenAnyValue(x => x.ErrorMessage)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(IsErrorMessageVisible)));
    }

    /// <summary>
    /// Handle errors via ThrownExceptions from commands.
    /// </summary>
    private void SubscribeToCommandExceptions()
    {
        SelectFolderCommand.ThrownExceptions
            .Merge(OverwriteCommand.ThrownExceptions)
            .Merge(SaveAsCommand.ThrownExceptions)
            .Merge(SaveBookmarksCommand.ThrownExceptions)
            .Merge(LoadBookmarksCommand.ThrownExceptions)
            .Merge(ReleaseBookmarkCommand.ThrownExceptions)
            .Merge(DeleteFileCommand.ThrownExceptions)
            .Merge(LoadSelectedBookmarkCommand.ThrownExceptions)
            .Subscribe(ex => ErrorMessage = $"Error: {ex.Message}");
    }

    /// <summary>
    /// Opens a folder picker dialog and updates the file list with .txt files from the selected folder.
    /// </summary>
    /// <param name="control">The UI control that triggered the command.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task SelectFolderAsync(Control control)
    {
        ErrorMessage = null;
        var toplevel = TopLevel.GetTopLevel(control);
        if (toplevel?.StorageProvider != null)
        {
            var folders = await toplevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions());
            var selectedFolder = folders?.FirstOrDefault();

            if (selectedFolder != null)
            {
                LastSelectedFolder = selectedFolder;
                await ListFilesAsync(selectedFolder);
                SelectedFolder = selectedFolder.Path.LocalPath;
                CurrentActiveBookmarkId = null; // Clear active bookmark ID when a new folder is selected manually
                Bookmarks.Clear(); // Clear the bookmark selection list
            }
        }
    }

    /// <summary>
    /// Lists all .txt files in a given folder and updates the Files observable collection.
    /// </summary>
    /// <param name="folder">The storage folder to list files from.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task ListFilesAsync(IStorageFolder folder)
    {
        var files = new List<IStorageFile>();
        await foreach (var item in folder.GetItemsAsync())
        {
            if (item is IStorageFile file && file.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                files.Add(file);
            }
        }
        Files = new ObservableCollection<IStorageFile>(files);
        ErrorMessage = null;
    }

    /// <summary>
    /// Overwrites the content of the currently selected file with the text in the editor.
    /// </summary>
    /// <param name="control">The UI control that triggered the command.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task OverwriteAsync(Control control)
    {
        ErrorMessage = null;
        if (SelectedFile != null && FileContent != null)
        {
            await using var stream = await SelectedFile.OpenWriteAsync();
            await using var streamWriter = new StreamWriter(stream, Encoding.UTF8);
            await streamWriter.WriteAsync(FileContent);
            ErrorMessage = $"File '{SelectedFile.Name}' overwritten successfully!";
        }
    }

    /// <summary>
    /// Opens a save file picker dialog to save the current content as a new file.
    /// </summary>
    /// <param name="control">The UI control that triggered the command.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task SaveAsAsync(Control control)
    {
        ErrorMessage = null;
        var toplevel = TopLevel.GetTopLevel(control);
        if (toplevel?.StorageProvider != null && FileContent != null)
        {
            var fileToSave = await toplevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save File As...",
                SuggestedFileName = SelectedFile?.Name ?? "new_file.txt",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Text Files")
                    {
                        Patterns = new[] { "*.txt" },
                        AppleUniformTypeIdentifiers = new[] { "public.plain-text" }
                    }
                }
            });

            if (fileToSave != null)
            {
                await using var stream = await fileToSave.OpenWriteAsync();
                await using var streamWriter = new StreamWriter(stream, Encoding.UTF8);
                await streamWriter.WriteAsync(FileContent);
                ErrorMessage = $"File '{fileToSave.Name}' saved successfully!";

                if (LastSelectedFolder != null)
                {
                    await ListFilesAsync(LastSelectedFolder);
                }
            }
        }
    }

    /// <summary>
    /// Loads the content of the given file into the editor.
    /// </summary>
    /// <param name="file">The file to load.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task LoadFileContentAsync(IStorageFile? file)
    {
        if (file == null)
        {
            ErrorMessage = "Error: File is null. Cannot load content.";
            return;
        }

        await using var stream = await file.OpenReadAsync();
        using var streamReader = new StreamReader(stream, Encoding.UTF8);
        FileContent = await streamReader.ReadToEndAsync();
        ErrorMessage = null;
    }

    /// <summary>
    /// Saves a bookmark ID for the currently selected folder to a local file.
    /// </summary>
    /// <param name="control">The UI control that triggered the command.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task SaveBookmarksAsync(Control control)
    {
        ErrorMessage = null;
        if (LastSelectedFolder == null)
        {
            ErrorMessage = "Error: No folder selected to save a bookmark.";
            return;
        }

        var bookmarkId = await LastSelectedFolder.SaveBookmarkAsync();

        if (bookmarkId != null)
        {
            var directory = Path.GetDirectoryName(BookmarkFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var bookmarks = File.Exists(BookmarkFilePath)
                ? JsonSerializer.Deserialize<HashSet<string>>(await File.ReadAllTextAsync(BookmarkFilePath)) ?? new()
                : new HashSet<string>();

            bookmarks.Add(bookmarkId);
            await File.WriteAllTextAsync(BookmarkFilePath, JsonSerializer.Serialize(bookmarks));
            CurrentActiveBookmarkId = bookmarkId;
            ErrorMessage = "Bookmark saved successfully!";
        }
        else
        {
            ErrorMessage = "Error: Could not save bookmark. The OS denied the request.";
        }
    }

    /// <summary>
    /// Loads all bookmark IDs from a local file and populates the bookmarks selection list.
    /// </summary>
    /// <param name="control">The UI control that triggered the command.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task LoadBookmarksAsync(Control control)
    {
        ErrorMessage = null;
        if (File.Exists(BookmarkFilePath))
        {
            var bookmarks = JsonSerializer.Deserialize<HashSet<string>>(await File.ReadAllTextAsync(BookmarkFilePath));
            if (bookmarks != null && bookmarks.Count > 0)
            {
                Bookmarks = new ObservableCollection<string>(bookmarks);
                ErrorMessage = "Bookmarks loaded. Please select a bookmark from the list.";
            }
            else
            {
                Bookmarks.Clear();
                ErrorMessage = "No bookmarks found.";
            }
        }
        else
        {
            Bookmarks.Clear();
            ErrorMessage = "Bookmark file not found.";
        }
    }

    /// <summary>
    /// Loads the folder corresponding to the selected bookmark ID.
    /// </summary>
    /// <param name="control">The UI control that triggered the command.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task LoadSelectedBookmarkAsync(Control control)
    {
        ErrorMessage = null;
        var toplevel = TopLevel.GetTopLevel(control);
        if (toplevel?.StorageProvider != null && !string.IsNullOrEmpty(SelectedBookmarkId))
        {
            var folder = await toplevel.StorageProvider.OpenFolderBookmarkAsync(SelectedBookmarkId);

            if (folder != null)
            {
                LastSelectedFolder = folder;
                SelectedFolder = folder.Path.LocalPath;
                await ListFilesAsync(folder);
                CurrentActiveBookmarkId = SelectedBookmarkId;

                if (string.IsNullOrEmpty(ErrorMessage))
                {
                    ErrorMessage = "Bookmark loaded successfully, file list updated!";
                }
                else
                {
                    ErrorMessage = $"Bookmark loaded successfully, but could not access folder contents. This may be due to permission restrictions. Please select the folder again.";
                }
            }
            else
            {
                CurrentActiveBookmarkId = null;
                ErrorMessage = "Could not access the bookmarked folder. The bookmark may be invalid or permissions have been revoked. Please select the folder again.";
            }
        }
    }

    /// <summary>
    /// Releases the active bookmark and removes the corresponding bookmark ID from the local file.
    /// </summary>
    /// <param name="control">The UI control that triggered the command.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task ReleaseBookmarkAsync(Control control)
    {
        ErrorMessage = null;
        if (!string.IsNullOrEmpty(SelectedBookmarkId))
        {
            var bookmarks = File.Exists(BookmarkFilePath)
                ? JsonSerializer.Deserialize<HashSet<string>>(await File.ReadAllTextAsync(BookmarkFilePath)) ?? new()
                : new HashSet<string>();

            bookmarks.Remove(SelectedBookmarkId);
            await File.WriteAllTextAsync(BookmarkFilePath, JsonSerializer.Serialize(bookmarks));

            if (SelectedBookmarkId == CurrentActiveBookmarkId)
            {
                LastSelectedFolder = null;
                SelectedFolder = null;
                Files.Clear();
                FileContent = null;
                SelectedFile = null;
                CurrentActiveBookmarkId = null;
            }

            SelectedBookmarkId = null;
            ErrorMessage = "Bookmark released and removed from file successfully.";
            await LoadBookmarksAsync(control);
        }
        else
        {
            ErrorMessage = "Error: No bookmark is selected to release.";
        }
    }

    /// <summary>
    /// Deletes the currently selected file and refreshes the file list.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task DeleteFileAsync()
    {
        ErrorMessage = null;
        if (SelectedFile != null && LastSelectedFolder != null)
        {
            await SelectedFile.DeleteAsync();
            FileContent = null;
            SelectedFile = null;
            await ListFilesAsync(LastSelectedFolder);
            ErrorMessage = "File deleted successfully!";
        }
    }
}
