using EquipmentAccounting.Application;
using EquipmentAccounting.Domain;
using EquipmentAccounting.Import.Access;
using EquipmentAccounting.Infrastructure;
using EquipmentAccounting.Infrastructure.Data;
using EquipmentAccounting.Wpf.Common;
using EquipmentAccounting.Wpf.ViewModels;
using EquipmentAccounting.Wpf.ViewModels.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentAccounting.FunctionalTests;

/// <summary>Запускает «приложение» так же, как App.xaml.cs: те же сервисы, те же модели окон, но во временной базе.</summary>
public abstract class AppTestBase : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ea-func-" + Guid.NewGuid().ToString("N")[..8]);
    protected ServiceProvider Sp = null!;

    protected T S<T>() where T : notnull => Sp.GetRequiredService<T>();

    public async Task InitializeAsync()
    {
        Ui.Reset();
        Directory.CreateDirectory(_dir);
        var services = new ServiceCollection();
        services.AddInfrastructure($"Data Source={Path.Combine(_dir, "test.db")};Pooling=False");
        services.AddSingleton<ILegacyImporter, AccessImporter>();
        services.AddSingleton<AppState>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<EquipmentPageViewModel>();
        services.AddTransient<WorkplacesViewModel>();
        services.AddTransient<RequestsViewModel>();
        services.AddTransient<ReportsViewModel>();
        services.AddTransient<ChartsViewModel>();
        services.AddTransient<AdminViewModel>();
        services.AddTransient<UsersTableViewModel>();
        services.AddTransient<ClassroomsTableViewModel>();
        services.AddTransient<WorkplacesTableViewModel>();
        services.AddTransient<CategoriesTableViewModel>();
        services.AddTransient<ProfileViewModel>();
        services.AddTransient<EquipmentEditViewModel>();
        Sp = services.BuildServiceProvider();
        await Sp.InitializeDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        await Sp.DisposeAsync();
        try { Directory.Delete(_dir, recursive: true); } catch { /* временная папка */ }
        // Любая «Непредвиденная ошибка» (не AppException) — это сбой, который пользователь увидел бы как аварийное сообщение.
        var unexpected = Wpf.App.Logged.Select(e => e.GetType().Name + ": " + e.Message).ToList();
        Assert.True(unexpected.Count == 0, "Приложение записало непредвиденные ошибки: " + string.Join(" | ", unexpected));
    }

    // ---------------------------------------------------------------- «действия пользователя»

    /// <summary>Окно входа: ввести логин и пароль и нажать «Войти». Возвращает, открылось ли главное окно.</summary>
    protected async Task<bool> SignInAsync(string login, string password)
    {
        var vm = S<LoginViewModel>();
        var entered = false;
        vm.LoggedIn += () => entered = true;
        vm.Login = login;
        vm.Password = password;
        await vm.SignInCommand.ExecuteAsync(null);
        return entered;
    }

    protected Task<bool> SignInAdminAsync() => SignInAsync(SeedData.AdminLogin, SeedData.AdminPassword);

    protected void SignOut() { S<IUserSession>().SignOut(); S<AppState>().Clear(); }

    /// <summary>Администратор создаёт аудиторию (через сервис — подготовка данных, не предмет проверки).</summary>
    protected async Task<ClassroomDto> NewClassroomAsync(string number, string name, int? responsibleId = null)
    {
        if (S<IUserSession>().CurrentUser == null) await SignInAdminAsync();
        return await S<IClassroomService>().SaveAsync(new ClassroomEdit(0, number, name, null, responsibleId));
    }

    /// <summary>Подготовка: администратор создаёт преподавателя и аудиторию за ним, затем входит преподаватель.</summary>
    protected async Task<(UserDto Teacher, ClassroomDto Room)> TeacherWithRoomAsync(string login = "ivanov", string room = "101")
    {
        await SignInAdminAsync();
        var t = await S<IUserService>().CreateAsync(new UserCreateRequest("Иванов", "Иван", "Иванович", login, login + "@example.com", UserRole.Teacher, true, "pass1234"));
        var c = await NewClassroomAsync(room, "Кабинет " + room, t.Id);
        SignOut();
        Assert.True(await SignInAsync(login, "pass1234"));
        await S<AppState>().ReloadClassroomsAsync();
        return (t, c);
    }

    /// <summary>Открывает страницу как главное окно: создаёт модель страницы и активирует её.</summary>
    protected async Task<T> OpenPageAsync<T>() where T : PageViewModel
    {
        var page = S<T>();
        await page.ActivateAsync();
        await SettleAsync(page);
        return page;
    }

    /// <summary>Ждёт, пока фоновые загрузки страницы завершатся (часть команд запускает их без ожидания).</summary>
    protected static async Task SettleAsync(params ViewModelBase[] vms)
    {
        var quiet = 0;
        for (var i = 0; i < 400 && quiet < 4; i++)
        {
            await Task.Delay(25);
            quiet = vms.Any(v => v.IsBusy) ? 0 : quiet + 1;
        }
    }

    protected static async Task EventuallyAsync(Func<bool> condition, string what, int ms = 5000)
    {
        for (var waited = 0; waited < ms; waited += 25)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
        Assert.True(condition(), "Не дождались: " + what);
    }

    protected string TempFile(string name, byte[] content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>Крошечное, но настоящее PNG-изображение 1×1.</summary>
    protected static byte[] TinyPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
}
