using System.Reflection;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace EquipmentAccounting.FunctionalTests;

/// <summary>
/// «Экран» для тестов: всё, что приложение показало бы пользователю (сообщения, вопросы, сохранённые файлы),
/// и ответы «пользователя» (Да/Нет, выбранные файлы, действия внутри открытых окон).
/// </summary>
public static class Ui
{
    public static List<string> Infos { get; } = new();
    public static List<string> Errors { get; } = new();
    public static List<string> Questions { get; } = new();
    public static List<(string Name, byte[] Content)> Saved { get; } = new();

    /// <summary>Ответ на вопрос «Вы уверены?». По умолчанию — Да.</summary>
    public static bool ConfirmAnswer { get; set; } = true;
    public static string[]? NextFilesToPick { get; set; }
    public static string? NextFileToOpen { get; set; }
    public static string? NextPin { get; set; }

    /// <summary>Что делает «пользователь» в открывшемся окне. Возвращает true — окно закрыто кнопкой «Сохранить/ОК».</summary>
    public static Func<object, Task<bool?>>? OnWindow { get; set; }
    public static List<object> OpenedWindows { get; } = new();

    public static void Reset()
    {
        Infos.Clear(); Errors.Clear(); Questions.Clear(); Saved.Clear(); OpenedWindows.Clear();
        ConfirmAnswer = true;
        NextFilesToPick = null; NextFileToOpen = null; NextPin = null; OnWindow = null;
        Wpf.App.Logged.Clear();
    }

    public static bool? RunWindow(object window)
    {
        OpenedWindows.Add(window);
        var handler = OnWindow;
        if (handler == null) return false; // пользователь закрыл окно крестиком
        // Обработчик работает в пуле потоков, чтобы не зависеть от контекста синхронизации теста.
        return Task.Run(() => handler(window)).GetAwaiter().GetResult();
    }

    public static string LastError => Errors.Count > 0 ? Errors[^1] : "";
}

/// <summary>Тест, который требует Windows (например, ПИН-код хранится через системную защиту данных). На Linux пропускается, а не «проходит».</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Требуется Windows (защита данных DPAPI)";
    }
}
