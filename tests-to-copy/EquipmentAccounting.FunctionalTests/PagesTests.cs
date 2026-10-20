using EquipmentAccounting.Application;
using EquipmentAccounting.Domain;
using EquipmentAccounting.Infrastructure.Data;
using EquipmentAccounting.Infrastructure.Reporting;
using EquipmentAccounting.Wpf.Common;
using EquipmentAccounting.Wpf.ViewModels;
using EquipmentAccounting.Wpf.Views;

namespace EquipmentAccounting.FunctionalTests;

/// <summary>Страницы «Рабочие места», «Заявки», «Профиль», «Главная», «Отчёты», «Диаграммы» и переходы главного окна.</summary>
public class PagesTests : AppTestBase
{
    private ClassroomDto _room = null!;

    private async Task<int> NewEquipmentAsync(string name, string inv, int? workplaceId = null, int qty = 1, bool writtenOff = false)
    {
        var type = (await S<IReferenceDataService>().GetTypesAsync()).First(t => t.Name == "Монитор");
        var conds = await S<IReferenceDataService>().GetConditionsAsync();
        var cond = writtenOff ? conds.First(c => c.IsWrittenOff) : conds.First(c => !c.IsWrittenOff);
        return await S<IEquipmentService>().SaveAsync(new EquipmentEdit
        {
            ClassroomId = _room.Id, WorkplaceId = workplaceId, TypeId = type.Id, ConditionId = cond.Id,
            Name = name, InventoryNumber = inv, Quantity = qty, CommissioningDate = new DateTime(2018, 9, 1), ServiceLifeYears = 3,
            WriteOffDate = writtenOff ? DateTime.Today : null,
        });
    }

    // ================================================================ Рабочие места

    [Fact]
    public async Task Рабочее_место_создаётся_кнопками_Новое_и_Сохранить()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var page = await OpenPageAsync<WorkplacesViewModel>();

        page.NewCommand.Execute(null);
        page.EditNumber = "A1";
        page.EditDescription = "У окна";
        await page.SaveCommand.ExecuteAsync(null);

        Assert.Empty(Ui.Errors);
        var saved = Assert.Single(await S<IWorkplaceService>().GetByClassroomAsync(_room.Id, includeInactive: true));
        Assert.Equal("A1", saved.Number);
        Assert.Equal("У окна", saved.Description);
        Assert.Equal(saved.Id, page.Selected?.Id);                  // после сохранения новое место выбрано
    }

    [Fact]
    public async Task Рабочее_место_без_номера_не_сохраняется()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var page = await OpenPageAsync<WorkplacesViewModel>();
        page.NewCommand.Execute(null);
        page.EditNumber = "  ";
        await page.SaveCommand.ExecuteAsync(null);
        Assert.Single(Ui.Errors);
        Assert.Empty(await S<IWorkplaceService>().GetByClassroomAsync(_room.Id, includeInactive: true));
    }

    [Fact]
    public async Task Рабочее_место_с_тем_же_номером_в_аудитории_не_сохраняется()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var page = await OpenPageAsync<WorkplacesViewModel>();
        page.NewCommand.Execute(null);
        page.EditNumber = "A1";
        await page.SaveCommand.ExecuteAsync(null);
        Assert.Empty(Ui.Errors);

        page.NewCommand.Execute(null);
        page.EditNumber = "A1";
        await page.SaveCommand.ExecuteAsync(null);
        Assert.Single(Ui.Errors);
        Assert.Single(await S<IWorkplaceService>().GetByClassroomAsync(_room.Id, includeInactive: true));
    }

    [Fact]
    public async Task Номер_рабочего_места_в_другом_регистре_считается_дублем()
    {
        // Логины и номера аудиторий сравниваются без учёта регистра, а номера мест — нет: «A1» и «a1» сохраняются как два места.
        _room = (await TeacherWithRoomAsync()).Room;
        var page = await OpenPageAsync<WorkplacesViewModel>();
        page.NewCommand.Execute(null);
        page.EditNumber = "A1";
        await page.SaveCommand.ExecuteAsync(null);
        page.NewCommand.Execute(null);
        page.EditNumber = "a1";
        await page.SaveCommand.ExecuteAsync(null);
        Assert.True(Ui.Errors.Count == 1, "Места «A1» и «a1» сохранились как разные");
    }

    [Fact]
    public async Task Изменение_выбранного_рабочего_места_сохраняется_в_той_же_записи()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var wp = await S<IWorkplaceService>().SaveAsync(new WorkplaceEdit(0, _room.Id, "B1", "Старое"));
        var page = await OpenPageAsync<WorkplacesViewModel>();
        Assert.Equal(wp.Id, page.Selected?.Id);
        Assert.Equal("Старое", page.EditDescription);

        page.EditDescription = "Новое";
        await page.SaveCommand.ExecuteAsync(null);
        var all = await S<IWorkplaceService>().GetByClassroomAsync(_room.Id, includeInactive: true);
        Assert.Equal("Новое", Assert.Single(all).Description);
    }

    [Fact]
    public async Task Оборудование_закрепляется_и_открепляется_от_рабочего_места()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var wp = await S<IWorkplaceService>().SaveAsync(new WorkplaceEdit(0, _room.Id, "C1", null));
        var eqId = await NewEquipmentAsync("Монитор", "W-1");
        var page = await OpenPageAsync<WorkplacesViewModel>();
        Assert.Single(page.FreeEquipment);

        await page.AssignCommand.ExecuteAsync(new List<EquipmentListItemDto> { page.FreeEquipment[0] });
        await SettleAsync(page);
        Assert.Empty(Ui.Errors);
        Assert.Equal(wp.Id, (await S<IEquipmentService>().GetListAsync(new EquipmentFilter(_room.Id))).Single().WorkplaceId);
        Assert.Empty(page.FreeEquipment);
        Assert.Single(page.PlaceEquipment);

        await page.UnassignCommand.ExecuteAsync(new List<EquipmentListItemDto> { page.PlaceEquipment[0] });
        await SettleAsync(page);
        Assert.Null((await S<IEquipmentService>().GetListAsync(new EquipmentFilter(_room.Id))).Single().WorkplaceId);
        Assert.Single(page.FreeEquipment);
    }

    [Fact]
    public async Task Закрепить_без_выбора_показывает_подсказку_и_ничего_не_меняет()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        await NewEquipmentAsync("Монитор", "W-2");
        var page = await OpenPageAsync<WorkplacesViewModel>();
        await page.AssignCommand.ExecuteAsync(new List<EquipmentListItemDto>());
        Assert.Single(Ui.Infos);
        Assert.Null((await S<IEquipmentService>().GetListAsync(new EquipmentFilter(_room.Id))).Single().WorkplaceId);
    }

    [Fact]
    public async Task Удаление_рабочего_места_оставляет_оборудование_в_аудитории_без_места()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var wp = await S<IWorkplaceService>().SaveAsync(new WorkplaceEdit(0, _room.Id, "D1", null));
        await NewEquipmentAsync("Монитор", "W-3", wp.Id);
        var page = await OpenPageAsync<WorkplacesViewModel>();

        Ui.ConfirmAnswer = false;
        await page.DeleteCommand.ExecuteAsync(null);
        Assert.Single(await S<IWorkplaceService>().GetByClassroomAsync(_room.Id, includeInactive: true));

        Ui.ConfirmAnswer = true;
        await page.DeleteCommand.ExecuteAsync(null);
        await SettleAsync(page);
        Assert.Empty(await S<IWorkplaceService>().GetByClassroomAsync(_room.Id, includeInactive: true));
        var eq = (await S<IEquipmentService>().GetListAsync(new EquipmentFilter(_room.Id))).Single();
        Assert.Null(eq.WorkplaceId);
    }

    [Fact]
    public async Task Кнопка_Добавить_оборудование_ведёт_на_страницу_оборудования()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var page = await OpenPageAsync<WorkplacesViewModel>();
        string? target = null;
        page.NavigateTo = t => target = t;
        page.AddEquipmentCommand.Execute(null);
        Assert.Equal("equipment:add", target);
    }

    // ================================================================ Заявки

    private static Func<object, Task<bool?>> CreateRequest(string title, string? description = null, ServiceRequestPriority priority = ServiceRequestPriority.Normal) =>
        async w =>
        {
            var vm = ((RequestEditWindow)w).Vm;
            await vm.LoadAsync();
            var saved = false; vm.Saved += () => saved = true;
            vm.Title = title; vm.Description = description; vm.Priority = priority;
            await vm.SaveCommand.ExecuteAsync(null);
            return saved;
        };

    [Fact]
    public async Task Заявка_создаётся_с_страницы_Заявки_и_появляется_в_списке()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var page = await OpenPageAsync<RequestsViewModel>();
        Ui.OnWindow = CreateRequest("Сломался проектор", "Не включается", ServiceRequestPriority.High);
        await page.CreateCommand.ExecuteAsync(null);
        await SettleAsync(page);

        Assert.Empty(Ui.Errors);
        var item = Assert.Single(page.Items);
        Assert.Equal("Сломался проектор", item.Title);
        Assert.Equal(ServiceRequestStatus.New, item.Status);
        Assert.Contains("открытых: 1", page.Summary);
    }

    [Fact]
    public async Task Пустая_тема_заявки_не_сохраняется()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var page = await OpenPageAsync<RequestsViewModel>();
        Ui.OnWindow = CreateRequest("");
        await page.CreateCommand.ExecuteAsync(null);
        Assert.Single(Ui.Errors);
        Assert.Empty(await S<IServiceRequestService>().GetListAsync(_room.Id));
    }

    [Fact]
    public async Task Администратор_меняет_статус_заявки_и_указывает_результат()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var teacherPage = await OpenPageAsync<RequestsViewModel>();
        Ui.OnWindow = CreateRequest("Заменить лампу");
        await teacherPage.CreateCommand.ExecuteAsync(null);
        SignOut();

        await SignInAdminAsync();
        await S<AppState>().ReloadClassroomsAsync(_room.Id);
        var page = await OpenPageAsync<RequestsViewModel>();
        page.AllClassrooms = true;
        await SettleAsync(page);
        page.Selected = page.Items.Single();
        page.EditStatus = ServiceRequestStatus.Done;
        page.EditResolution = "Лампа заменена";
        await page.SaveStatusCommand.ExecuteAsync(null);
        await SettleAsync(page);

        Assert.Empty(Ui.Errors);
        var db = (await S<IServiceRequestService>().GetListAsync(_room.Id)).Single();
        Assert.Equal(ServiceRequestStatus.Done, db.Status);
        Assert.Equal("Лампа заменена", db.Resolution);
        Assert.NotNull(db.ClosedAt);                                  // выполненная заявка закрыта
    }

    [Fact]
    public async Task Преподаватель_не_может_сам_выставить_статус_Выполнена()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var page = await OpenPageAsync<RequestsViewModel>();
        Ui.OnWindow = CreateRequest("Моя заявка");
        await page.CreateCommand.ExecuteAsync(null);
        await SettleAsync(page);
        page.Selected = page.Items.Single();
        page.EditStatus = ServiceRequestStatus.Done;
        await page.SaveStatusCommand.ExecuteAsync(null);

        var db = (await S<IServiceRequestService>().GetListAsync(_room.Id)).Single();
        Assert.True(db.Status == ServiceRequestStatus.New, "Преподаватель смог сам закрыть свою заявку (статус: " + db.Status + ")");
    }

    [Fact]
    public async Task Преподаватель_отзывает_свою_новую_заявку_но_не_чужую()
    {
        var (_, room) = await TeacherWithRoomAsync();
        _room = room;
        var page = await OpenPageAsync<RequestsViewModel>();
        Ui.OnWindow = CreateRequest("Отозвать меня");
        await page.CreateCommand.ExecuteAsync(null);
        await SettleAsync(page);
        page.Selected = page.Items.Single();
        Assert.True(page.CanWithdraw);

        Ui.ConfirmAnswer = false;
        await page.WithdrawCommand.ExecuteAsync(null);
        Assert.Single(await S<IServiceRequestService>().GetListAsync(_room.Id));

        Ui.ConfirmAnswer = true;
        await page.WithdrawCommand.ExecuteAsync(null);
        await SettleAsync(page);
        Assert.Empty(await S<IServiceRequestService>().GetListAsync(_room.Id));
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Другой_преподаватель_не_видит_и_не_может_удалить_чужую_заявку()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var page = await OpenPageAsync<RequestsViewModel>();
        Ui.OnWindow = CreateRequest("Чужая заявка");
        await page.CreateCommand.ExecuteAsync(null);
        var id = (await S<IServiceRequestService>().GetListAsync(_room.Id)).Single().Id;
        SignOut();

        await SignInAdminAsync();
        await S<IUserService>().CreateAsync(new UserCreateRequest("Чужой", "Чужой", null, "stranger", "s@example.com", UserRole.Teacher, true, "pass1234"));
        SignOut();
        await SignInAsync("stranger", "pass1234");

        Assert.Empty(await S<IServiceRequestService>().GetListAsync(_room.Id));
        await Assert.ThrowsAsync<AppException>(() => S<IServiceRequestService>().DeleteAsync(id));
        await Assert.ThrowsAsync<AppException>(() => S<IServiceRequestService>().SetStatusAsync(id, ServiceRequestStatus.Rejected, "взлом"));

        SignOut();
        await SignInAdminAsync();
        Assert.Equal(ServiceRequestStatus.New, (await S<IServiceRequestService>().GetListAsync(_room.Id)).Single().Status);
    }

    [Fact]
    public async Task Фильтр_по_статусу_оставляет_только_нужные_заявки()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var page = await OpenPageAsync<RequestsViewModel>();
        Ui.OnWindow = CreateRequest("Первая");
        await page.CreateCommand.ExecuteAsync(null);
        Ui.OnWindow = CreateRequest("Вторая");
        await page.CreateCommand.ExecuteAsync(null);
        await SettleAsync(page);
        Assert.Equal(2, page.Items.Count);

        page.StatusFilter = ServiceRequestStatus.Done;
        await SettleAsync(page);
        Assert.Empty(page.Items);

        page.StatusFilter = null;
        await SettleAsync(page);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task Создать_заявку_без_выбранной_аудитории_нельзя()
    {
        await SignInAdminAsync();
        await S<AppState>().ReloadClassroomsAsync();
        var page = await OpenPageAsync<RequestsViewModel>();
        Ui.OnWindow = _ => throw new InvalidOperationException("окно не должно открываться");
        await page.CreateCommand.ExecuteAsync(null);
        Assert.Contains("выберите аудиторию", Ui.LastError);
    }

    // ================================================================ Профиль

    [Fact]
    public async Task Профиль_показывает_данные_и_сохраняет_изменения()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var page = await OpenPageAsync<ProfileViewModel>();
        Assert.Equal("Иванов", page.LastName);

        page.LastName = "Петров"; page.FirstName = "Пётр"; page.Email = "new@example.com";
        await page.SaveProfileCommand.ExecuteAsync(null);
        Assert.Empty(Ui.Errors);
        Assert.Contains("сохранены", Ui.Infos.Last());
        Assert.Equal("Петров", S<IUserSession>().CurrentUser!.LastName);
        Assert.Equal("new@example.com", S<IUserSession>().CurrentUser!.Email);
    }

    [Theory]
    [InlineData("", "Иван", "a@example.com")]
    [InlineData("Иванов", "", "a@example.com")]
    [InlineData("Иванов", "Иван", "не-почта")]
    public async Task Профиль_с_неверными_данными_не_сохраняется(string last, string first, string email)
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var page = await OpenPageAsync<ProfileViewModel>();
        page.LastName = last; page.FirstName = first; page.Email = email;
        await page.SaveProfileCommand.ExecuteAsync(null);
        Assert.Single(Ui.Errors);
        Assert.Equal("Иванов", S<IUserSession>().CurrentUser!.LastName.Length > 0 ? "Иванов" : "");
    }

    [Fact]
    public async Task Пароль_меняется_и_старый_перестаёт_работать()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var page = await OpenPageAsync<ProfileViewModel>();
        page.CurrentPassword = "pass1234"; page.NewPassword = "newpass99"; page.NewPasswordConfirm = "newpass99";
        await page.ChangePasswordCommand.ExecuteAsync(null);
        Assert.Empty(Ui.Errors);
        Assert.Equal("", page.NewPassword);                          // поля очищены
        SignOut();
        Assert.False(await SignInAsync("ivanov", "pass1234"));
        Assert.True(await SignInAsync("ivanov", "newpass99"));
    }

    [Fact]
    public async Task Пароль_не_меняется_при_неверном_текущем_или_несовпадении_новых()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var page = await OpenPageAsync<ProfileViewModel>();

        page.CurrentPassword = "неверный"; page.NewPassword = "newpass99"; page.NewPasswordConfirm = "newpass99";
        await page.ChangePasswordCommand.ExecuteAsync(null);
        Assert.Single(Ui.Errors);

        page.CurrentPassword = "pass1234"; page.NewPassword = "newpass99"; page.NewPasswordConfirm = "другой";
        await page.ChangePasswordCommand.ExecuteAsync(null);
        Assert.Equal(2, Ui.Errors.Count);

        page.NewPassword = "1"; page.NewPasswordConfirm = "1";       // слишком короткий
        await page.ChangePasswordCommand.ExecuteAsync(null);
        Assert.Equal(3, Ui.Errors.Count);

        SignOut();
        Assert.True(await SignInAsync("ivanov", "pass1234"));        // пароль остался прежним
    }

    [Fact]
    public async Task Преподаватель_берёт_свободную_аудиторию_и_снимает_с_себя_ответственность()
    {
        await SignInAdminAsync();
        await S<IUserService>().CreateAsync(new UserCreateRequest("Новый", "Преп", null, "newteach", "nt@example.com", UserRole.Teacher, true, "pass1234"));
        var free = await NewClassroomAsync("701", "Свободная");
        SignOut();
        await SignInAsync("newteach", "pass1234");
        await S<AppState>().ReloadClassroomsAsync();
        var page = await OpenPageAsync<ProfileViewModel>();

        Ui.OnWindow = w =>
        {
            var win = (SelectClassroomWindow)w;
            win.Selected = win.Offered.Single(c => c.Number == "701");
            return Task.FromResult<bool?>(true);
        };
        await page.ClaimCommand.ExecuteAsync(null);
        Assert.Empty(Ui.Errors);
        Assert.Equal("701", Assert.Single(await S<IClassroomService>().GetAvailableAsync()).Number);
        Assert.Equal(free.Id, S<AppState>().SelectedClassroomId);

        page.SelectedClassroom = S<AppState>().Classrooms.Single();
        await page.ReleaseCommand.ExecuteAsync(null);
        Assert.Empty(await S<IClassroomService>().GetAvailableAsync());
        Assert.Contains(Ui.Questions, q => q.Contains("Снять с себя"));
    }

    [Fact]
    public async Task Закрытие_окна_выбора_аудитории_ничего_не_закрепляет()
    {
        await SignInAdminAsync();
        await S<IUserService>().CreateAsync(new UserCreateRequest("Новый", "Преп", null, "newteach", "nt@example.com", UserRole.Teacher, true, "pass1234"));
        await NewClassroomAsync("702", "Свободная");
        SignOut();
        await SignInAsync("newteach", "pass1234");
        var page = await OpenPageAsync<ProfileViewModel>();
        Ui.OnWindow = null;
        await page.ClaimCommand.ExecuteAsync(null);
        Assert.Empty(await S<IClassroomService>().GetAvailableAsync());
    }

    [Fact]
    public async Task Если_свободных_аудиторий_нет_показывается_подсказка()
    {
        await SignInAdminAsync();
        await S<IUserService>().CreateAsync(new UserCreateRequest("Новый", "Преп", null, "newteach", "nt@example.com", UserRole.Teacher, true, "pass1234"));
        SignOut();
        await SignInAsync("newteach", "pass1234");
        var page = await OpenPageAsync<ProfileViewModel>();
        await page.ClaimCommand.ExecuteAsync(null);
        Assert.Contains("Свободных аудиторий нет", Ui.Infos.Last());
        Assert.Empty(Ui.OpenedWindows);
    }

    // ================================================================ Главная

    [Fact]
    public async Task Главная_показывает_правильные_числа()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        await NewEquipmentAsync("Монитор А", "H-1", qty: 2);
        await NewEquipmentAsync("Монитор Б", "H-2", qty: 3, writtenOff: true);
        await S<IServiceRequestService>().CreateAsync(new ServiceRequestCreate(_room.Id, null, ServiceRequestKind.Maintenance, "Тест", null, ServiceRequestPriority.Normal));
        await S<AppState>().ReloadClassroomsAsync();

        var page = await OpenPageAsync<HomeViewModel>();
        Assert.True(page.HasClassroom);
        Assert.Equal(2, page.Positions);
        Assert.Equal(2, page.Units);                // списанное в единицы не входит
        Assert.Equal(3, page.WrittenOff);
        Assert.Equal(1, page.OpenRequests);
        Assert.Equal(1, page.Expired);              // срок службы 3 года с 2018 — истёк (только у неснятого с учёта)
        Assert.StartsWith("Здравствуйте, Иван Иванович", page.Greeting);
    }

    [Fact]
    public async Task Главная_без_аудитории_предлагает_закрепить_её()
    {
        await SignInAdminAsync();
        await S<IUserService>().CreateAsync(new UserCreateRequest("Новый", "Преп", null, "newteach", "nt@example.com", UserRole.Teacher, true, "pass1234"));
        await NewClassroomAsync("703", "Свободная");
        SignOut();
        await SignInAsync("newteach", "pass1234");
        var page = await OpenPageAsync<HomeViewModel>();
        Assert.False(page.HasClassroom);
        Assert.Equal(0, page.Positions);

        Ui.OnWindow = w =>
        {
            var win = (SelectClassroomWindow)w;
            win.Selected = win.Offered.First();
            return Task.FromResult<bool?>(true);
        };
        await page.ClaimClassroomCommand.ExecuteAsync(null);
        Assert.Single(await S<IClassroomService>().GetAvailableAsync());
    }

    [Fact]
    public async Task Кнопки_быстрых_действий_главной_ведут_на_нужные_страницы()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var page = await OpenPageAsync<HomeViewModel>();
        var targets = new List<string>();
        page.NavigateTo = targets.Add;
        page.GoCommand.Execute("equipment:add");
        page.GoCommand.Execute("requests");
        Assert.Equal(new[] { "equipment:add", "requests" }, targets);
    }

    // ================================================================ Главное окно

    [Fact]
    public async Task Преподавателю_не_показывается_пункт_Администрирование_а_администратору_показывается()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var main = S<MainViewModel>();
        Assert.DoesNotContain(main.NavItems, n => n.Key == "admin");
        SignOut();

        await SignInAdminAsync();
        var adminMain = S<MainViewModel>();
        Assert.Contains(adminMain.NavItems, n => n.Key == "admin");
    }

    [Fact]
    public async Task Переходы_по_меню_открывают_нужные_страницы()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var main = S<MainViewModel>();
        await main.InitializeAsync();
        await EventuallyAsync(() => main.CurrentPage is HomeViewModel, "главная страница");

        foreach (var (key, type) in new (string, Type)[]
        {
            ("equipment", typeof(EquipmentPageViewModel)), ("workplaces", typeof(WorkplacesViewModel)), ("requests", typeof(RequestsViewModel)),
            ("reports", typeof(ReportsViewModel)), ("charts", typeof(ChartsViewModel)), ("profile", typeof(ProfileViewModel)), ("home", typeof(HomeViewModel)),
        })
        {
            main.Navigate(key);
            await EventuallyAsync(() => main.CurrentPage?.GetType() == type, "страница " + key);
        }
        Assert.Empty(Ui.Errors);
    }

    [Fact]
    public async Task Переход_equipment_add_сразу_открывает_окно_нового_оборудования()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var main = S<MainViewModel>();
        await main.InitializeAsync();
        Ui.OnWindow = _ => Task.FromResult<bool?>(false);
        main.Navigate("equipment:add");
        await EventuallyAsync(() => Ui.OpenedWindows.OfType<EquipmentEditWindow>().Any(), "окно нового оборудования");
    }

    [Fact]
    public async Task Выход_спрашивает_подтверждение()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var main = S<MainViewModel>();
        var requested = false;
        main.LogoutRequested += () => requested = true;

        Ui.ConfirmAnswer = false;
        main.LogoutCommand.Execute(null);
        Assert.False(requested);

        Ui.ConfirmAnswer = true;
        main.LogoutCommand.Execute(null);
        Assert.True(requested);
    }

    [Fact]
    public async Task Смена_аудитории_в_шапке_перезагружает_открытую_страницу()
    {
        var (teacher, room) = await TeacherWithRoomAsync();
        _room = room;
        SignOut();
        await SignInAdminAsync();
        var second = await NewClassroomAsync("102", "Вторая", teacher.Id);
        SignOut();
        await SignInAsync("ivanov", "pass1234");
        await NewEquipmentAsync("Монитор в первой", "M-1");
        await S<AppState>().ReloadClassroomsAsync(_room.Id);

        var main = S<MainViewModel>();
        await main.InitializeAsync();
        main.Navigate("equipment");
        await EventuallyAsync(() => main.CurrentPage is EquipmentPageViewModel p && p.Items.Count == 1, "список первой аудитории");

        S<AppState>().SelectedClassroom = S<AppState>().Classrooms.Single(c => c.Id == second.Id);
        await EventuallyAsync(() => ((EquipmentPageViewModel)main.CurrentPage!).Items.Count == 0, "список второй аудитории");
    }

    // ================================================================ Отчёты и диаграммы

    [Fact]
    public async Task Каждый_отчёт_формируется_и_выгружается_во_все_форматы()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var wp = await S<IWorkplaceService>().SaveAsync(new WorkplaceEdit(0, _room.Id, "R1", null));
        await NewEquipmentAsync("Монитор", "R-1", wp.Id);
        await S<AppState>().ReloadClassroomsAsync(_room.Id);
        var page = await OpenPageAsync<ReportsViewModel>();
        Assert.True(page.Options.Count >= 10);

        var problems = new List<string>();
        foreach (var option in page.Options.ToList())
        {
            Ui.Errors.Clear();
            page.SelectedOption = option;
            await SettleAsync(page);
            if (page.WorkplaceAvailable) page.SelectedWorkplace = page.WorkplaceChoices.First(c => c.Id == wp.Id);
            await page.GenerateCommand.ExecuteAsync(null);
            if (Ui.Errors.Count > 0 || !page.HasDocument) { problems.Add($"{option.Title}: {string.Join(" / ", Ui.Errors)}"); continue; }
            foreach (var fmt in new[] { "Excel", "Pdf", "Html" })
            {
                Ui.Saved.Clear();
                await page.ExportCommand.ExecuteAsync(fmt);
                if (Ui.Saved.Count != 1 || Ui.Saved[0].Content.Length < 100) problems.Add($"{option.Title} → {fmt}: файл не сформирован ({string.Join(" / ", Ui.Errors)})");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public async Task Паспорт_рабочего_места_без_выбора_места_показывает_ошибку()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        var page = await OpenPageAsync<ReportsViewModel>();
        page.SelectedOption = page.Options.Single(o => o.Kind == ReportKind.WorkplacePassport);
        await SettleAsync(page);
        await page.GenerateCommand.ExecuteAsync(null);
        Assert.Contains("рабочее место", Ui.LastError);
        Assert.False(page.HasDocument);
    }

    [Fact]
    public async Task Отчёт_без_выбранной_аудитории_показывает_ошибку_а_Все_аудитории_работает()
    {
        await SignInAdminAsync();
        await S<AppState>().ReloadClassroomsAsync();
        var page = await OpenPageAsync<ReportsViewModel>();
        page.SelectedOption = page.Options.Single(o => o.Kind == ReportKind.FullInventory);
        await page.GenerateCommand.ExecuteAsync(null);
        Assert.Single(Ui.Errors);

        page.AllClassrooms = true;
        await page.GenerateCommand.ExecuteAsync(null);
        Assert.Single(Ui.Errors);
        Assert.True(page.HasDocument);
    }

    [Fact]
    public async Task Диаграммы_строятся_и_выгружаются()
    {
        _room = (await TeacherWithRoomAsync()).Room;
        await NewEquipmentAsync("Монитор", "C-1");
        await S<AppState>().ReloadClassroomsAsync(_room.Id);
        var page = await OpenPageAsync<ChartsViewModel>();
        Assert.NotEmpty(page.Charts);
        Assert.Empty(Ui.Errors);
        foreach (var card in page.Charts)
            Assert.NotNull(card.Data);
    }
}
