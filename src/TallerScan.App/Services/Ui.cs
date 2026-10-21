namespace TallerScan.App.Services;

// Diálogos y compartir, sin depender de una página concreta.
public static class Ui
{
    private static Page? CurrentPage => Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;

    public static Task AlertAsync(string title, string message) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (CurrentPage is { } page)
                await page.DisplayAlertAsync(title, message, "Aceptar");
        });

    public static Task<bool> ConfirmAsync(string title, string message, string accept, string cancel = "Cancelar") =>
        MainThread.InvokeOnMainThreadAsync(async () =>
            CurrentPage is { } page && await page.DisplayAlertAsync(title, message, accept, cancel));

    // Guarda el texto en la caché y abre el selector de compartir del sistema
    // (WhatsApp, mail, Drive...). Cancelar el selector no es un error.
    public static async Task ShareTextFileAsync(string fileName, string content, string title)
    {
        var path = Path.Combine(FileSystem.CacheDirectory, fileName);
        await File.WriteAllTextAsync(path, content);
        await ShareFileAsync(path, title);
    }

    public static Task ShareFileAsync(string path, string title) =>
        Share.Default.RequestAsync(new ShareFileRequest { Title = title, File = new ShareFile(path) });

    public static string SafeFileName(string text)
    {
        var clean = new string(text.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return clean.Length == 0 ? "sin-patente" : clean;
    }
}
