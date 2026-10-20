using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace EquipmentAccounting.UiTests;

/// <summary>Тест, который требует Windows и собранного приложения. В других системах пропускается.</summary>
public sealed class WindowsUiFactAttribute : FactAttribute
{
    public WindowsUiFactAttribute()
    {
        if (!System.OperatingSystem.IsWindows()) Skip = "Требуется Windows (UI Automation)";
        else if (AppDriver.FindExe() == null) Skip = "Не найден EquipmentAccounting.exe: задайте переменную окружения EA_EXE (путь к exe)";
        else if (string.IsNullOrEmpty(AppDriver.Login) || string.IsNullOrEmpty(AppDriver.Password))
            Skip = "Задайте переменные окружения EA_LOGIN и EA_PASSWORD (учётная запись для входа). Пароль в код не записан.";
    }
}

/// <summary>
/// Запускает копию приложения во временной папке (рабочая база данных не затрагивается)
/// и даёт простые действия: ввести текст, нажать кнопку, дождаться окна.
/// </summary>
public sealed class AppDriver : IDisposable
{
    public static string? Login => Environment.GetEnvironmentVariable("EA_LOGIN");
    public static string? Password => Environment.GetEnvironmentVariable("EA_PASSWORD");

    private readonly string _tempDir;
    private readonly UIA3Automation _automation = new();
    public Application App { get; }

    public static string? FindExe()
    {
        var env = Environment.GetEnvironmentVariable("EA_EXE");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
        // По умолчанию: publish\EquipmentAccounting.exe относительно папки проекта (поднимаемся вверх от bin\Debug\...).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "publish", "EquipmentAccounting.exe");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    public AppDriver()
    {
        var exe = FindExe() ?? throw new FileNotFoundException("EquipmentAccounting.exe не найден");
        _tempDir = Path.Combine(Path.GetTempPath(), "ea-ui-" + Guid.NewGuid().ToString("N")[..8]);
        CopyDirectory(Path.GetDirectoryName(exe)!, _tempDir);
        App = Application.Launch(new ProcessStartInfo(Path.Combine(_tempDir, Path.GetFileName(exe))) { WorkingDirectory = _tempDir });
    }

    // ------------------------------------------------------------ окна

    public Window WaitWindow(Func<string, bool> titleMatches, int seconds = 20)
    {
        var w = Retry.WhileNull(() => App.GetAllTopLevelWindows(_automation).FirstOrDefault(x => titleMatches(x.Title ?? "")),
            TimeSpan.FromSeconds(seconds), TimeSpan.FromMilliseconds(200)).Result;
        return w ?? throw new TimeoutException("Окно не появилось. Открытые окна: " + string.Join(" | ", App.GetAllTopLevelWindows(_automation).Select(x => $"«{x.Title}»")));
    }

    public Window LoginWindow => WaitWindow(t => t.Contains("вход"));
    public Window MainWindow => WaitWindow(t => t == "Учёт оборудования");

    public bool IsRunning => !App.HasExited;

    /// <summary>Открытые окна-сообщения (MessageBox) и диалоги поверх основного окна.</summary>
    public IReadOnlyList<Window> Dialogs(Window owner) => owner.ModalWindows;

    public static string TextOf(Window w) =>
        string.Join(" ", w.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)).Select(t => t.Name).Where(n => !string.IsNullOrWhiteSpace(n)));

    // ------------------------------------------------------------ элементы

    /// <summary>Находит элемент по подписи (точной или содержащей текст): кнопку, пункт меню, надпись.</summary>
    public static AutomationElement Find(AutomationElement root, string text, int seconds = 10)
    {
        var el = Retry.WhileNull(() =>
        {
            var all = root.FindAllDescendants();
            return all.FirstOrDefault(e => e.Name == text) ?? all.FirstOrDefault(e => (e.Name ?? "").Contains(text, StringComparison.CurrentCultureIgnoreCase));
        }, TimeSpan.FromSeconds(seconds), TimeSpan.FromMilliseconds(200)).Result;
        return el ?? throw new TimeoutException($"Элемент «{text}» не найден в окне «{root.Name}»");
    }

    public static bool Exists(AutomationElement root, string text) =>
        root.FindAllDescendants().Any(e => (e.Name ?? "").Contains(text, StringComparison.CurrentCultureIgnoreCase));

    public static void Click(AutomationElement root, string text)
    {
        var el = Find(root, text);
        el.Focus();
        el.Click();
        Wait.UntilInputIsProcessed();
    }

    /// <summary>Вводит текст в n-е по порядку поле ввода окна (0 — первое). Пароли тоже вводятся с клавиатуры.</summary>
    public static void Type(Window window, int fieldIndex, string text)
    {
        var edits = window.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit)).Where(e => !e.Properties.IsOffscreen.ValueOrDefault).ToArray();
        if (fieldIndex >= edits.Length) throw new InvalidOperationException($"В окне только {edits.Length} полей ввода, нужно №{fieldIndex}");
        edits[fieldIndex].Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(text);
        Wait.UntilInputIsProcessed();
    }

    /// <summary>Закрывает окно-сообщение кнопкой по умолчанию (Enter).</summary>
    public static void Dismiss(Window dialog)
    {
        dialog.Focus();
        Keyboard.Type(VirtualKeyShort.ENTER);
        Wait.UntilInputIsProcessed();
    }

    public void Dispose()
    {
        try { if (!App.HasExited) App.Close(); } catch { /* уже закрыто */ }
        try { if (!App.HasExited) App.Kill(); } catch { /* уже закрыто */ }
        _automation.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* файл мог быть ещё занят */ }
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), overwrite: true);
        foreach (var d in Directory.GetDirectories(from)) CopyDirectory(d, Path.Combine(to, Path.GetFileName(d)));
    }
}
