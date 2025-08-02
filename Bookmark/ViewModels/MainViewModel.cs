namespace Bookmark.ViewModels;

using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ReactiveUI;
using Avalonia.Controls;
using Avalonia;
using System.Reactive;
using Avalonia.Platform.Storage;
using System.Linq;
using System.Reactive.Linq;

public class MainViewModel : ReactiveObject
{
    private IStorageFolder? _lastSelectedFolder;
    private string? _selectedFolder;
    private string? _fileContent;
    private IStorageFile? _selectedFile;
    private ObservableCollection<IStorageFile> _files = new();
    private string? _fileNameToSave;
    private string? _errorMessage;
    private string? _bookmarkContent;

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
    /// Gets or sets the filename for saving a file.
    /// </summary>
    public string? FileNameToSave
    {
        get => _fileNameToSave;
        set => this.RaiseAndSetIfChanged(ref _fileNameToSave, value);
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
    /// Displays the raw bookmark ID string.
    /// </summary>
    public string? BookmarkContent
    {
        get => _bookmarkContent;
        set => this.RaiseAndSetIfChanged(ref _bookmarkContent, value);
    }

    /// <summary>
    /// Gets a value indicating whether an error message is visible.
    /// </summary>
    public bool IsErrorMessageVisible => !string.IsNullOrEmpty(ErrorMessage);

    public ReactiveCommand<Control, Unit> SelectFolderCommand { get; }
    public ReactiveCommand<Control, Unit> OverwriteCommand { get; }
    public ReactiveCommand<Control, Unit> SaveAsCommand { get; }
    public ReactiveCommand<Control, Unit> SaveBookmarkCommand { get; }
    public ReactiveCommand<Control, Unit> LoadBookmarkCommand { get; }
    public ReactiveCommand<Unit, Unit> ReleaseBookmarkCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteFileCommand { get; }

    public MainViewModel()
    {
        SelectFolderCommand = ReactiveCommand.CreateFromTask<Control>(SelectFolderAsync);

        var canOverwrite = this.WhenAnyValue(x => x.SelectedFile).Select(x => x != null);
        OverwriteCommand = ReactiveCommand.CreateFromTask<Control>(OverwriteAsync, canOverwrite);
        SaveAsCommand = ReactiveCommand.CreateFromTask<Control>(SaveAsAsync);

        var canSaveBookmark = this.WhenAnyValue(x => x.LastSelectedFolder).Select(x => x != null);
        SaveBookmarkCommand = ReactiveCommand.CreateFromTask<Control>(SaveBookmarkAsync, canSaveBookmark);
        LoadBookmarkCommand = ReactiveCommand.CreateFromTask<Control>(LoadBookmarksAsync);
        var canReleaseBookmark = this.WhenAnyValue(x => x.LastSelectedFolder).Select(x => x != null);
        ReleaseBookmarkCommand = ReactiveCommand.CreateFromTask(ReleaseBookmarkAsync, canReleaseBookmark);

        var canDeleteFile = this.WhenAnyValue(x => x.SelectedFile).Select(x => x != null);
        DeleteFileCommand = ReactiveCommand.CreateFromTask(DeleteFileAsync, canDeleteFile);

        SelectFolderCommand.ThrownExceptions.Subscribe(ex => ErrorMessage = $"Error: An exception occurred while selecting a folder - {ex.Message}");
        OverwriteCommand.ThrownExceptions.Subscribe(ex => ErrorMessage = $"Error: An exception occurred while overwriting the file - {ex.Message}");
        SaveAsCommand.ThrownExceptions.Subscribe(ex => ErrorMessage = $"Error: An exception occurred while saving as a new file - {ex.Message}");
        SaveBookmarkCommand.ThrownExceptions.Subscribe(ex => ErrorMessage = $"Error: An exception occurred while saving the bookmark - {ex.Message}");
        LoadBookmarkCommand.ThrownExceptions.Subscribe(ex => ErrorMessage = $"Error: An exception occurred while loading the bookmark - {ex.Message}");
        ReleaseBookmarkCommand.ThrownExceptions.Subscribe(ex => ErrorMessage = $"Error: An exception occurred while releasing the bookmark - {ex.Message}");
        DeleteFileCommand.ThrownExceptions.Subscribe(ex => ErrorMessage = $"Error: An exception occurred while deleting the file - {ex.Message}");

        this.WhenAnyValue(x => x.SelectedFile)
            .Where(file => file != null)
            .Subscribe(async file =>
            {
                await LoadFileContentAsync(file);
            });

        this.WhenAnyValue(x => x.ErrorMessage)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(IsErrorMessageVisible)));
    }

    /// <summary>
    /// Opens a folder picker dialog and updates the file list with .txt files from the selected folder.
    /// </summary>
    /// <param name="control">The UI control that triggered the command.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task SelectFolderAsync(Control control)
    {
        ErrorMessage = null;
        try
        {
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
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: An exception occurred while selecting a folder - {ex.Message}";
        }
    }

    /// <summary>
    /// Lists all .txt files in a given folder and updates the Files observable collection.
    /// </summary>
    /// <param name="folder">The storage folder to list files from.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task ListFilesAsync(IStorageFolder folder)
    {
        try
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
        catch (Exception ex)
        {
            ErrorMessage = $"Error: An exception occurred while listing folder files - {ex.Message}";
        }
    }

    /// <summary>
    /// Overwrites the content of the currently selected file with the text in the editor.
    /// </summary>
    /// <param name="control">The UI control that triggered the command.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task OverwriteAsync(Control control)
    {
        ErrorMessage = null;
        try
        {
            if (SelectedFile != null && FileContent != null)
            {
                await using var stream = await SelectedFile.OpenWriteAsync();
                await using var streamWriter = new StreamWriter(stream, Encoding.UTF8);
                await streamWriter.WriteAsync(FileContent);
                ErrorMessage = $"File '{SelectedFile.Name}' overwritten successfully!";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: An exception occurred while overwriting the file - {ex.Message}";
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
        try
        {
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
        catch (Exception ex)
        {
            ErrorMessage = $"Error: An exception occurred while saving the file - {ex.Message}";
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

        try
        {
            await using var stream = await file.OpenReadAsync();
            using var streamReader = new StreamReader(stream, Encoding.UTF8);
            FileContent = await streamReader.ReadToEndAsync();
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: An exception occurred while loading file content - {ex.Message}";
        }
    }

    /// <summary>
    /// Saves a bookmark ID for the currently selected folder to a local file.
    /// </summary>
    /// <param name="control">The UI control that triggered the command.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task SaveBookmarkAsync(Control control)
    {
        ErrorMessage = null;
        try
        {
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
                BookmarkContent = bookmarkId;
                ErrorMessage = "Bookmark saved successfully!";
            }
            else
            {
                ErrorMessage = "Error: Could not save bookmark. The OS denied the request.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: An exception occurred while saving the bookmark - {ex.Message}";
        }
    }

    /// <summary>
    /// Loads a bookmark ID from a local file and reopens the folder using it.
    /// </summary>
    /// <param name="control">The UI control that triggered the command.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task LoadBookmarksAsync(Control control)
    {
        ErrorMessage = null;
        try
        {
            var toplevel = TopLevel.GetTopLevel(control);
            if (toplevel?.StorageProvider != null && File.Exists(BookmarkFilePath))
            {
                var bookmarks = JsonSerializer.Deserialize<HashSet<string>>(await File.ReadAllTextAsync(BookmarkFilePath));
                if (bookmarks != null && bookmarks.Count > 0)
                {
                    var bookmarkId = bookmarks.First();
                    BookmarkContent = bookmarkId;

                    var folder = await toplevel.StorageProvider.OpenFolderBookmarkAsync(bookmarkId);

                    if (folder != null)
                    {
                        LastSelectedFolder = folder;
                        SelectedFolder = folder.Path.LocalPath;
                        await ListFilesAsync(folder);

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
                        BookmarkContent = null;
                        ErrorMessage = "Could not access the bookmarked folder. The bookmark may be invalid or permissions have been revoked. Please select the folder again.";
                    }
                }
                else
                {
                    BookmarkContent = null;
                    ErrorMessage = "No bookmarks found.";
                }
            }
            else
            {
                ErrorMessage = "Bookmark file not found.";
            }
        }
        catch (Exception ex)
        {
            BookmarkContent = null;
            ErrorMessage = $"Error: An exception occurred while loading the bookmark - {ex.Message}";
        }
    }

    /// <summary>
    /// Releases the active bookmark and removes the local bookmark ID file.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task ReleaseBookmarkAsync()
    {
        ErrorMessage = null;
        try
        {
            if (LastSelectedFolder is IStorageBookmarkItem folderWithBookmark)
            {
                await folderWithBookmark.ReleaseBookmarkAsync();

                LastSelectedFolder = null;
                SelectedFolder = null;
                Files.Clear();
                FileContent = null;
                FileNameToSave = null;
                BookmarkContent = null;

                // Write an empty JSON array to clear the file without corrupting the JSON format.
                File.WriteAllText(BookmarkFilePath, "[]");

                ErrorMessage = "Bookmark released successfully. Please select a folder again.";
            }
            else
            {
                ErrorMessage = "Error: No active folder bookmark to release.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: An exception occurred while releasing the bookmark - {ex.Message}";
        }
    }

    /// <summary>
    /// Deletes the currently selected file and refreshes the file list.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task DeleteFileAsync()
    {
        ErrorMessage = null;
        try
        {
            if (SelectedFile != null && LastSelectedFolder != null)
            {
                await SelectedFile.DeleteAsync();
                FileContent = null;
                SelectedFile = null;
                await ListFilesAsync(LastSelectedFolder);
                ErrorMessage = "File deleted successfully!";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: An exception occurred while deleting the file - {ex.Message}";
        }
    }
}