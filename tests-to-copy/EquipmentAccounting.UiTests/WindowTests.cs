using FlaUI.Core.AutomationElements;

namespace EquipmentAccounting.UiTests;

/// <summary>
/// Настоящее окно приложения: вход, меню, открытие страниц и диалогов. Запускается копия программы во временной папке.
/// Поля ввода находятся по порядку (в разметке нет AutomationId), кнопки — по подписи.
/// </summary>
public class WindowTests
{
    private static Window SignIn(AppDriver app, string login, string password)
    {
        var win = app.LoginWindow;
        AppDriver.Type(win, 0, login);       // 0 — логин
        AppDriver.Type(win, 1, password);    // 1 — пароль
        AppDriver.Click(win, "Войти");
        return win;
    }

    [WindowsUiFact]
    public void Окно_входа_открывается_и_переключается_на_регистрацию_и_обратно()
    {
        using var app = new AppDriver();
        var win = app.LoginWindow;
        Assert.True(AppDriver.Exists(win, "Регистрация"));

        AppDriver.Click(win, "Регистрация");
        Assert.True(AppDriver.Exists(win, "Зарегистрироваться"));

        AppDriver.Click(win, "Назад ко входу");
        Assert.True(AppDriver.Exists(win, "Забыли пароль"));
    }

    [WindowsUiFact]
    public void Неверный_пароль_показывает_сообщение_и_остаётся_окно_входа()
    {
        using var app = new AppDriver();
        var win = SignIn(app, AppDriver.Login!, "точно-неверный-пароль-123");

        var dialog = Assert.Single(app.Dialogs(win));
        Assert.False(string.IsNullOrWhiteSpace(AppDriver.TextOf(dialog)));
        AppDriver.Dismiss(dialog);
        Assert.True(app.IsRunning);
        Assert.Contains("вход", app.LoginWindow.Title);               // главное окно не открылось
    }

    [WindowsUiFact]
    public void Пустые_поля_входа_не_пускают_в_программу()
    {
        using var app = new AppDriver();
        var win = app.LoginWindow;
        AppDriver.Click(win, "Войти");
        var dialog = Assert.Single(app.Dialogs(win));
        AppDriver.Dismiss(dialog);
        Assert.Contains("вход", app.LoginWindow.Title);
    }

    [WindowsUiFact]
    public void Верный_пароль_открывает_главное_окно_и_все_разделы_меню_открываются_без_ошибок()
    {
        using var app = new AppDriver();
        SignIn(app, AppDriver.Login!, AppDriver.Password!);
        var main = app.MainWindow;

        var sections = new[] { "Главная", "Оборудование", "Рабочие места", "Заявки", "Отчёты", "Диаграммы", "Профиль" };
        foreach (var name in sections.Concat(AppDriver.Exists(main, "Администрирование") ? new[] { "Администрирование" } : Array.Empty<string>()))
        {
            AppDriver.Click(main, name);
            Thread.Sleep(700);                                        // страница загружает данные
            Assert.True(app.IsRunning, $"Программа закрылась на разделе «{name}»");
            Assert.Empty(app.Dialogs(main));                          // никаких окон с ошибкой
        }
    }

    [WindowsUiFact]
    public void Кнопка_Добавить_на_странице_оборудования_открывает_окно_которое_закрывается_кнопкой_Отмена()
    {
        using var app = new AppDriver();
        SignIn(app, AppDriver.Login!, AppDriver.Password!);
        var main = app.MainWindow;
        AppDriver.Click(main, "Оборудование");
        Thread.Sleep(700);

        // Без выбранной аудитории программа вместо окна покажет подсказку — это тоже допустимый исход.
        AppDriver.Click(main, "Добавить");
        Thread.Sleep(500);
        var dialog = app.Dialogs(main).FirstOrDefault();
        Assert.NotNull(dialog);
        if (dialog!.Title.Contains("Новое оборудование"))
        {
            Assert.True(AppDriver.Exists(dialog, "Сохранить"));
            AppDriver.Click(dialog, "Отмена");
        }
        else
        {
            AppDriver.Dismiss(dialog);                                // сообщение «Сначала выберите аудиторию»
        }
        Assert.Empty(app.Dialogs(main));
    }

    [WindowsUiFact]
    public void Выход_из_системы_спрашивает_подтверждение_и_возвращает_к_окну_входа()
    {
        using var app = new AppDriver();
        SignIn(app, AppDriver.Login!, AppDriver.Password!);
        var main = app.MainWindow;
        AppDriver.Click(main, "Выйти");
        var question = Assert.Single(app.Dialogs(main));
        AppDriver.Click(question, "Да");
        Assert.Contains("вход", app.LoginWindow.Title);
    }
}
