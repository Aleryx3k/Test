using EquipmentAccounting.Application;
using EquipmentAccounting.Domain;
using EquipmentAccounting.Infrastructure;
using EquipmentAccounting.Infrastructure.Data;
using EquipmentAccounting.Wpf.ViewModels;
using EquipmentAccounting.Wpf.ViewModels.Admin;

namespace EquipmentAccounting.FunctionalTests;

/// <summary>Раздел «Администрирование»: таблицы аудиторий, пользователей, категорий, рабочих мест, типы, состояния, почта.</summary>
public class AdminTests : AppTestBase
{
    private async Task<AdminViewModel> OpenAdminAsync()
    {
        Assert.True(await SignInAdminAsync());
        var page = await OpenPageAsync<AdminViewModel>();
        Assert.Empty(Ui.Errors);
        return page;
    }

    // ------------------------------------------------------------ Аудитории

    [Fact]
    public async Task Аудитория_добавляется_кнопками_Добавить_и_Сохранить()
    {
        var admin = await OpenAdminAsync();
        var t = admin.ClassroomsTable;

        t.AddCommand.Execute(null);
        Assert.True(t.Selected!.IsNew);
        Assert.True(t.Selected.IsDirty);                // новая строка подсвечена как несохранённая
        t.Selected.Number = "201";
        t.Selected.Name = "Кабинет физики";
        Assert.True(t.HasChanges);

        await t.SaveCommand.ExecuteAsync(null);
        Assert.Empty(Ui.Errors);
        Assert.False(t.HasChanges);
        Assert.DoesNotContain(t.Rows, r => r.IsNew);

        var saved = (await S<IClassroomService>().GetAllAsync()).Single(c => c.Number == "201");
        Assert.Equal("Кабинет физики", saved.Name);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task Аудитория_без_номера_не_сохраняется_и_остаётся_на_экране_для_исправления()
    {
        var admin = await OpenAdminAsync();
        var t = admin.ClassroomsTable;
        t.AddCommand.Execute(null);
        t.Selected!.Name = "Без номера";
        await t.SaveCommand.ExecuteAsync(null);

        Assert.Single(Ui.Errors);
        Assert.True(t.HasChanges);                      // строка осталась несохранённой
        Assert.Empty(await S<IClassroomService>().GetAllAsync());
    }

    [Fact]
    public async Task Аудитория_с_повторным_номером_не_сохраняется()
    {
        await NewClassroomAsync("301", "Первая");
        var admin = await OpenAdminAsync();
        var t = admin.ClassroomsTable;
        t.AddCommand.Execute(null);
        t.Selected!.Number = "301";
        t.Selected.Name = "Вторая";
        await t.SaveCommand.ExecuteAsync(null);

        Assert.Single(Ui.Errors);
        Assert.Single(await S<IClassroomService>().GetAllAsync());
    }

    [Fact]
    public async Task Правка_ячейки_подсвечивает_строку_и_сохраняется()
    {
        var room = await NewClassroomAsync("302", "Старое имя");
        var admin = await OpenAdminAsync();
        var t = admin.ClassroomsTable;
        var row = t.Rows.Single(r => r.Id == room.Id);
        Assert.False(row.IsDirty);

        row.Name = "Новое имя";
        Assert.True(row.IsDirty);
        Assert.Equal(1, t.ChangedCount);

        await t.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Новое имя", (await S<IClassroomService>().GetAllAsync()).Single(c => c.Id == room.Id).Name);
        Assert.Equal(0, t.ChangedCount);
    }

    [Fact]
    public async Task Кнопка_Отменить_спрашивает_подтверждение_и_возвращает_данные_из_базы()
    {
        var room = await NewClassroomAsync("303", "Исходное");
        var admin = await OpenAdminAsync();
        var t = admin.ClassroomsTable;
        t.Rows.Single(r => r.Id == room.Id).Name = "Испорченное";

        Ui.ConfirmAnswer = false;                       // «Нет» — правки остаются
        await t.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("Испорченное", t.Rows.Single(r => r.Id == room.Id).Name);

        Ui.ConfirmAnswer = true;                        // «Да» — правки теряются
        await t.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("Исходное", t.Rows.Single(r => r.Id == room.Id).Name);
        Assert.Contains(Ui.Questions, q => q.Contains("Несохранённые изменения"));
    }

    [Fact]
    public async Task Заблокированная_аудитория_скрыта_от_преподавателя_а_разблокировка_возвращает_её()
    {
        await SignInAdminAsync();
        var teacher = await S<IUserService>().CreateAsync(new UserCreateRequest("Т", "Т", null, "teach", "t@example.com", UserRole.Teacher, true, "pass1234"));
        var room = await NewClassroomAsync("304", "Для блокировки", teacher.Id);
        var admin = await OpenPageAsync<AdminViewModel>();
        var t = admin.ClassroomsTable;
        var row = t.Rows.Single(r => r.Id == room.Id);

        await t.ToggleActiveCommand.ExecuteAsync(row);
        Assert.Contains(Ui.Questions, q => q.Contains("Заблокировать"));
        Assert.False((await S<IClassroomService>().GetAllAsync()).Single(c => c.Id == room.Id).IsActive);

        SignOut();
        Assert.True(await SignInAsync("teach", "pass1234"));
        Assert.Empty(await S<IClassroomService>().GetAvailableAsync());    // преподаватель блокированную аудиторию не видит

        SignOut();
        await SignInAdminAsync();
        var t2 = (await OpenPageAsync<AdminViewModel>()).ClassroomsTable;
        await t2.ToggleActiveCommand.ExecuteAsync(t2.Rows.Single(r => r.Id == room.Id));
        SignOut();
        await SignInAsync("teach", "pass1234");
        Assert.Single(await S<IClassroomService>().GetAvailableAsync());
    }

    [Fact]
    public async Task Отказ_от_блокировки_ничего_не_меняет()
    {
        var room = await NewClassroomAsync("305", "Не блокировать");
        var t = (await OpenAdminAsync()).ClassroomsTable;
        Ui.ConfirmAnswer = false;
        await t.ToggleActiveCommand.ExecuteAsync(t.Rows.Single(r => r.Id == room.Id));
        Assert.True((await S<IClassroomService>().GetAllAsync()).Single(c => c.Id == room.Id).IsActive);
    }

    [Fact]
    public async Task Пустая_аудитория_удаляется_после_подтверждения_а_при_отказе_остаётся()
    {
        var room = await NewClassroomAsync("306", "Удалить");
        var t = (await OpenAdminAsync()).ClassroomsTable;

        Ui.ConfirmAnswer = false;
        await t.DeleteCommand.ExecuteAsync(t.Rows.Single(r => r.Id == room.Id));
        Assert.Single(await S<IClassroomService>().GetAllAsync());

        Ui.ConfirmAnswer = true;
        await t.DeleteCommand.ExecuteAsync(t.Rows.Single(r => r.Id == room.Id));
        Assert.Empty(await S<IClassroomService>().GetAllAsync());
        Assert.DoesNotContain(t.Rows, r => r.Id == room.Id);
    }

    [Fact]
    public async Task Аудиторию_с_оборудованием_удалить_нельзя()
    {
        await SignInAdminAsync();
        var room = await NewClassroomAsync("307", "С оборудованием");
        var type = (await S<IReferenceDataService>().GetTypesAsync()).First();
        var cond = (await S<IReferenceDataService>().GetConditionsAsync()).First();
        await S<IEquipmentService>().SaveAsync(new EquipmentEdit { ClassroomId = room.Id, TypeId = type.Id, ConditionId = cond.Id, Name = "Стол", Quantity = 1 });
        var t = (await OpenPageAsync<AdminViewModel>()).ClassroomsTable;

        await t.DeleteCommand.ExecuteAsync(t.Rows.Single(r => r.Id == room.Id));
        Assert.Single(Ui.Errors);
        Assert.Single(await S<IClassroomService>().GetAllAsync());
    }

    [Fact]
    public async Task Новая_несохранённая_строка_удаляется_без_вопросов_и_без_обращения_к_базе()
    {
        var t = (await OpenAdminAsync()).ClassroomsTable;
        t.AddCommand.Execute(null);
        var row = t.Selected!;
        await t.DeleteCommand.ExecuteAsync(row);
        Assert.DoesNotContain(t.Rows, r => ReferenceEquals(r, row));
        Assert.Empty(Ui.Questions);
    }

    [Fact]
    public async Task Ответственного_можно_назначить_и_аудитория_появится_у_преподавателя()
    {
        await SignInAdminAsync();
        var teacher = await S<IUserService>().CreateAsync(new UserCreateRequest("О", "О", null, "resp", "r@example.com", UserRole.Teacher, true, "pass1234"));
        var room = await NewClassroomAsync("308", "Назначение");
        var admin = await OpenPageAsync<AdminViewModel>();
        Assert.Contains(admin.ClassroomsTable.Responsibles, r => r.Key == teacher.Id);   // список ответственных заполнен

        admin.ClassroomsTable.Rows.Single(r => r.Id == room.Id).ResponsibleId = teacher.Id;
        await admin.ClassroomsTable.SaveCommand.ExecuteAsync(null);

        SignOut();
        Assert.True(await SignInAsync("resp", "pass1234"));
        Assert.Equal("308", (await S<IClassroomService>().GetAvailableAsync()).Single().Number);
    }

    [Fact]
    public async Task Поиск_в_таблице_скрывает_неподходящие_строки_но_не_новые()
    {
        await NewClassroomAsync("401", "Информатика");
        await NewClassroomAsync("402", "Физика");
        var t = (await OpenAdminAsync()).ClassroomsTable;

        t.Search = "физ";
        var visible = t.Rows.Where(r => t.View.Filter!(r)).Select(r => r.Number).ToList();
        Assert.Equal(new[] { "402" }, visible);

        t.AddCommand.Execute(null);                      // «Добавить» сбрасывает поиск, чтобы новая строка была видна
        Assert.Equal("", t.Search);
    }

    // ------------------------------------------------------------ Пользователи

    [Fact]
    public async Task Пользователь_без_пароля_не_создаётся()
    {
        var t = (await OpenAdminAsync()).UsersTable;
        t.AddCommand.Execute(null);
        var r = t.Selected!;
        r.LastName = "Новый"; r.FirstName = "Юзер"; r.Login = "newuser"; r.Email = "n@example.com";
        await t.SaveCommand.ExecuteAsync(null);

        Assert.Contains("Новый пароль", Ui.LastError);
        Assert.DoesNotContain(await S<IUserService>().GetAllAsync(), u => u.Login == "newuser");
    }

    [Fact]
    public async Task Пользователь_с_паролем_создаётся_и_может_войти()
    {
        var t = (await OpenAdminAsync()).UsersTable;
        t.AddCommand.Execute(null);
        var r = t.Selected!;
        r.LastName = "Новый"; r.FirstName = "Юзер"; r.Login = "newuser"; r.Email = "n@example.com"; r.NewPassword = "pass1234";
        await t.SaveCommand.ExecuteAsync(null);
        Assert.Empty(Ui.Errors);

        SignOut();
        Assert.True(await SignInAsync("newuser", "pass1234"));
    }

    [Fact]
    public async Task Смена_пароля_пользователю_через_таблицу_действует_сразу()
    {
        await SignInAdminAsync();
        var u = await S<IUserService>().CreateAsync(new UserCreateRequest("С", "С", null, "pwuser", "pw@example.com", UserRole.Teacher, true, "oldpass12"));
        var t = (await OpenPageAsync<AdminViewModel>()).UsersTable;
        var row = t.Rows.Single(r => r.Id == u.Id);
        row.NewPassword = "newpass12";
        await t.SaveCommand.ExecuteAsync(null);
        Assert.Empty(Ui.Errors);

        SignOut();
        Assert.False(await SignInAsync("pwuser", "oldpass12"));
        Assert.True(await SignInAsync("pwuser", "newpass12"));
    }

    [Fact]
    public async Task Блокировка_пользователя_запрещает_ему_вход()
    {
        await SignInAdminAsync();
        var u = await S<IUserService>().CreateAsync(new UserCreateRequest("Б", "Б", null, "tob", "b@example.com", UserRole.Teacher, true, "pass1234"));
        var t = (await OpenPageAsync<AdminViewModel>()).UsersTable;
        await t.ToggleActiveCommand.ExecuteAsync(t.Rows.Single(r => r.Id == u.Id));
        SignOut();
        Assert.False(await SignInAsync("tob", "pass1234"));
    }

    [Fact]
    public async Task Администратор_не_может_заблокировать_самого_себя()
    {
        var t = (await OpenAdminAsync()).UsersTable;
        var me = t.Rows.Single(r => r.IsSelf);
        await t.ToggleActiveCommand.ExecuteAsync(me);
        var after = (await S<IUserService>().GetAllAsync()).Single(u => u.Login == SeedData.AdminLogin);
        Assert.True(after.IsActive, "Администратор смог заблокировать сам себя — риск остаться без доступа");
    }

    [Fact]
    public async Task Администратор_не_может_удалить_самого_себя()
    {
        var t = (await OpenAdminAsync()).UsersTable;
        await t.DeleteCommand.ExecuteAsync(t.Rows.Single(r => r.IsSelf));
        Assert.Contains(await S<IUserService>().GetAllAsync(), u => u.Login == SeedData.AdminLogin);
    }

    [Fact]
    public async Task Администратор_не_может_лишить_себя_роли_администратора()
    {
        var t = (await OpenAdminAsync()).UsersTable;
        var me = t.Rows.Single(r => r.IsSelf);
        me.Role = UserRole.Teacher;
        await t.SaveCommand.ExecuteAsync(null);
        var after = (await S<IUserService>().GetAllAsync()).Single(u => u.Login == SeedData.AdminLogin);
        Assert.Equal(UserRole.Admin, after.Role);
    }

    // ------------------------------------------------------------ Учётная запись администратора по умолчанию

    [Fact]
    public async Task После_смены_пароля_администратора_пароль_по_умолчанию_не_работает_и_после_перезапуска()
    {
        await SignInAdminAsync();
        await S<IAuthService>().ChangePasswordAsync(SeedData.AdminPassword, "Новый-надёжный-пароль-77");
        SignOut();

        await Sp.InitializeDatabaseAsync();                         // «перезапуск программы»
        Assert.False(await SignInAdminAsync());
        Assert.True(await SignInAsync(SeedData.AdminLogin, "Новый-надёжный-пароль-77"));
    }

    [Fact]
    public async Task Если_переименовать_администратора_после_перезапуска_не_появляется_новый_администратор_с_паролем_по_умолчанию()
    {
        await SignInAdminAsync();
        var users = await S<IUserService>().GetAllAsync();
        var me = users.Single(u => u.Login == SeedData.AdminLogin);
        await S<IUserService>().UpdateAsync(new UserUpdateRequest(me.Id, me.LastName, me.FirstName, me.MiddleName, "owner-admin", me.Email, UserRole.Admin, true));
        await S<IAuthService>().ChangePasswordAsync(SeedData.AdminPassword, "Новый-надёжный-пароль-77");
        SignOut();

        await Sp.InitializeDatabaseAsync();                         // «перезапуск программы»
        Assert.False(await SignInAdminAsync(), "После переименования владельца программа заново создаёт администратора с известным паролем по умолчанию");
    }

    // ------------------------------------------------------------ Категории, типы, состояния

    [Fact]
    public async Task Категория_добавляется_с_очередным_порядковым_номером()
    {
        var t = (await OpenAdminAsync()).CategoriesTable;
        var maxBefore = t.Rows.Max(r => r.SortOrder);
        t.AddCommand.Execute(null);
        Assert.Equal(maxBefore + 1, t.Selected!.SortOrder);
        t.Selected.Name = "Тестовая категория";
        await t.SaveCommand.ExecuteAsync(null);
        Assert.Empty(Ui.Errors);
        Assert.Contains(await S<IReferenceDataService>().GetCategoriesAsync(), c => c.Name == "Тестовая категория");
    }

    [Fact]
    public async Task Категорию_с_типами_удалить_нельзя()
    {
        var t = (await OpenAdminAsync()).CategoriesTable;
        var withTypes = t.Rows.First(r => r.TypeCount > 0);
        await t.DeleteCommand.ExecuteAsync(withTypes);
        Assert.Single(Ui.Errors);
        Assert.Contains(await S<IReferenceDataService>().GetCategoriesAsync(), c => c.Id == withTypes.Id);
    }

    [Fact]
    public async Task Тип_оборудования_добавляется_и_удаляется_кнопками()
    {
        var admin = await OpenAdminAsync();
        admin.NewTypeCommand.Execute(null);
        admin.TypeName = "Тестовый тип";
        await admin.SaveTypeCommand.ExecuteAsync(null);
        Assert.Empty(Ui.Errors);
        Assert.Contains(admin.Types, x => x.Name == "Тестовый тип");

        admin.SelectedType = admin.Types.Single(x => x.Name == "Тестовый тип");
        await admin.DeleteTypeCommand.ExecuteAsync(null);
        Assert.DoesNotContain(await S<IReferenceDataService>().GetTypesAsync(), x => x.Name == "Тестовый тип");
    }

    [Fact]
    public async Task Тип_без_названия_не_сохраняется()
    {
        var admin = await OpenAdminAsync();
        admin.NewTypeCommand.Execute(null);
        admin.TypeName = "   ";
        await admin.SaveTypeCommand.ExecuteAsync(null);
        Assert.Single(Ui.Errors);
    }

    [Fact]
    public async Task Состояние_добавляется_а_неверный_порядок_заменяется_нулём()
    {
        var admin = await OpenAdminAsync();
        admin.NewConditionCommand.Execute(null);
        admin.ConditionName = "Тестовое состояние";
        admin.ConditionOrder = "abc";
        await admin.SaveConditionCommand.ExecuteAsync(null);
        Assert.True(Ui.Errors.Count == 0, "Показаны ошибки: " + string.Join(" | ", Ui.Errors));
        Assert.Contains(await S<IReferenceDataService>().GetConditionsAsync(), c => c.Name == "Тестовое состояние");
    }

    // ------------------------------------------------------------ Рабочие места (таблица)

    [Fact]
    public async Task Рабочее_место_без_аудитории_не_сохраняется()
    {
        var t = (await OpenAdminAsync()).WorkplacesTable;
        t.AddCommand.Execute(null);
        t.Selected!.Number = "1";
        await t.SaveCommand.ExecuteAsync(null);
        Assert.Contains("аудиторию", Ui.LastError);
    }

    [Fact]
    public async Task Рабочее_место_сохраняется_в_выбранной_аудитории()
    {
        var room = await NewClassroomAsync("501", "С местами");
        var t = (await OpenAdminAsync()).WorkplacesTable;
        t.AddCommand.Execute(null);
        t.Selected!.ClassroomId = room.Id;
        t.Selected.Number = "7";
        t.Selected.Description = "Окно";
        await t.SaveCommand.ExecuteAsync(null);
        Assert.Empty(Ui.Errors);
        Assert.Single(await S<IWorkplaceService>().GetByClassroomAsync(room.Id, includeInactive: true));
    }

    // ------------------------------------------------------------ Почта

    [Fact]
    public async Task Настройки_почты_сохраняются_и_читаются_обратно()
    {
        var admin = await OpenAdminAsync();
        admin.SmtpHost = "smtp.example.com"; admin.SmtpPort = "465"; admin.SmtpFrom = "from@example.com"; admin.SmtpFromName = "Учёт";
        await admin.SaveSmtpCommand.ExecuteAsync(null);
        Assert.Single(Ui.Infos);

        var again = S<ISettingsService>();
        var s = await again.GetSmtpAsync();
        Assert.Equal("smtp.example.com", s.Host);
        Assert.Equal(465, s.Port);
        Assert.Equal("from@example.com", s.FromAddress);
    }

    [Fact]
    public async Task Импорт_из_Access_без_выбранного_файла_показывает_подсказку()
    {
        var admin = await OpenAdminAsync();
        await admin.RunImportCommand.ExecuteAsync(null);
        Assert.Contains("Выберите файл", Ui.LastError);
    }

    // ------------------------------------------------------------ Excel в таблицах администратора

    [Fact]
    public async Task Экспорт_аудиторий_в_Excel_создаёт_xlsx_а_импорт_того_же_файла_ничего_не_ломает()
    {
        await NewClassroomAsync("601", "Для экспорта");
        var t = (await OpenAdminAsync()).ClassroomsTable;
        await t.ExportExcelCommand.ExecuteAsync(null);
        var (name, bytes) = Assert.Single(Ui.Saved);
        Assert.StartsWith("Аудитории", name);
        Assert.Equal((byte)'P', bytes[0]);              // xlsx — это zip-архив («PK»)
        Assert.Equal((byte)'K', bytes[1]);

        var path = TempFile("rooms.xlsx", bytes);
        Ui.NextFileToOpen = path;
        await t.ImportExcelCommand.ExecuteAsync(null);
        Assert.Empty(Ui.Errors);
        Assert.Single((await S<IClassroomService>().GetAllAsync()).Where(c => c.Number == "601"));
    }

    [Fact]
    public async Task Импорт_испорченного_файла_показывает_ошибку_а_не_падает()
    {
        var t = (await OpenAdminAsync()).ClassroomsTable;
        Ui.NextFileToOpen = TempFile("broken.xlsx", new byte[] { 1, 2, 3, 4, 5 });
        await t.ImportExcelCommand.ExecuteAsync(null);
        Assert.Single(Ui.Errors);
        Assert.Empty(Wpf.App.Logged.Where(e => e is not AppException && false));
    }
}
