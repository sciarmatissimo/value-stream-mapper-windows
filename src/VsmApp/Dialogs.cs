// Small modal helpers: text prompt, message, confirmation, and file pickers.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace VsmApp;

public static class Dialogs
{
    public static async Task<string?> Prompt(Window owner, string title, string message, string initial = "")
    {
        var box = new TextBox { Text = initial, Width = 320 };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        var dlg = new Window
        {
            Title = title, Width = 400, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    box,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } },
                },
            },
        };
        string? result = null;
        ok.Click += (_, _) => { result = box.Text; dlg.Close(); };
        cancel.Click += (_, _) => dlg.Close();
        dlg.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        await dlg.ShowDialog(owner);
        return result;
    }

    public static async Task Message(Window owner, string title, string message)
    {
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Right };
        var dlg = new Window
        {
            Title = title, Width = 440, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 14,
                Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, ok },
            },
        };
        ok.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(owner);
    }

    public static async Task<bool> Confirm(Window owner, string title, string message, string confirmLabel = "Delete")
    {
        var yes = new Button { Content = confirmLabel, MinWidth = 80 };
        var no = new Button { Content = "Cancel", IsCancel = true, IsDefault = true, MinWidth = 80 };
        var dlg = new Window
        {
            Title = title, Width = 420, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 14,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { no, yes } },
                },
            },
        };
        bool result = false;
        yes.Click += (_, _) => { result = true; dlg.Close(); };
        no.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(owner);
        return result;
    }

    public static async Task<string?> OpenFile(Window owner, string title, params (string Name, string[] Patterns)[] types)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = false,
            FileTypeFilter = types.Select(t => new FilePickerFileType(t.Name) { Patterns = t.Patterns }).ToList(),
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public static async Task<string?> SaveFile(Window owner, string title, string suggestedName, string extension, string typeName)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title, SuggestedFileName = suggestedName, DefaultExtension = extension,
            FileTypeChoices = new[] { new FilePickerFileType(typeName) { Patterns = new[] { "*." + extension } } },
        });
        return file?.TryGetLocalPath();
    }
}
