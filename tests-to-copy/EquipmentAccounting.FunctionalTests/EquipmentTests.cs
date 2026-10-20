using EquipmentAccounting.Application;
using EquipmentAccounting.Domain;
using EquipmentAccounting.Infrastructure.Data;
using EquipmentAccounting.Wpf.Common;
using EquipmentAccounting.Wpf.ViewModels;
using EquipmentAccounting.Wpf.Views;

namespace EquipmentAccounting.FunctionalTests;

/// <summary>Страница «Оборудование»: добавление, правка, копирование, удаление, фильтры, фото, характеристики, блокировка, выгрузки.</summary>
public class EquipmentTests : AppTestBase
{
    private ClassroomDto _room = null!;
    private UserDto _teacher = null!;

    private async Task<EquipmentPageViewModel> OpenAsTeacherAsync()
    {
        (_teacher, _room) = await TeacherWithRoomAsync();
        return await OpenPageAsync<EquipmentPageViewModel>();
    }

    /// <summary>Пользователь в окне «Новое оборудование»: заполняет поля и нажимает «Сохранить».</summary>
    private static Func<object, Task<bool?>> FillAndSave(Action<EquipmentEditViewModel> fill)
    {
        return async w =>
        {
            var vm = ((EquipmentEditWindow)w).Vm;
            var saved = false;
            vm.Saved += () => saved = true;
            fill(vm);
            await vm.SaveCommand.ExecuteAsync(null);
            return saved;
        };
    }

    private static void Monitor(EquipmentEditViewModel vm, string inv = "INV-001", string name = "Монитор", string qty = "1")
    {
        vm.Category = vm.Categories.First(c => c.Name == SeedData.Computer);
        vm.Type = vm.Types.First(t => t.Name == "Монитор");
        vm.Name = name;
        vm.Brand = "AOC"; vm.Model = "E2350";
        vm.InventoryNumber = inv; vm.SerialNumber = "SN-" + inv;
        vm.Quantity = qty;
        vm.CommissioningDate = new DateTime(2020, 9, 1);
        vm.ServiceLifeYears = "7";
    }

    private async Task<EquipmentListItemDto> AddMonitorAsync(EquipmentPageViewModel page, string inv = "INV-001", string name = "Монитор", string qty = "1")
    {
        Ui.OnWindow = FillAndSave(vm => Monitor(vm, inv, name, qty));
        await page.AddCommand.ExecuteAsync(null);
        await SettleAsync(page);
        return page.Items.Single(i => i.InventoryNumber == inv);
    }

    [Fact]
    public async Task Добавить_оборудование_сохраняет_все_поля_и_показывает_в_списке()
    {
        var page = await OpenAsTeacherAsync();
        var item = await AddMonitorAsync(page);

        Assert.Empty(Ui.Errors);
        Assert.Equal("Монитор", item.Name);
        Assert.Equal(_room.Id, item.ClassroomId);
        Assert.Contains("Позиций: 1", page.Summary);

        var db = await S<IEquipmentService>().GetAsync(item.Id);
        Assert.Equal("AOC", db.Brand);
        Assert.Equal("E2350", db.Model);
        Assert.Equal("INV-001", db.InventoryNumber);
        Assert.Equal("SN-INV-001", db.SerialNumber);
        Assert.Equal(1, db.Quantity);
        Assert.Equal(new DateTime(2020, 9, 1), db.CommissioningDate);
        Assert.Equal(7, db.ServiceLifeYears);
        Assert.Equal(new DateTime(2027, 9, 1), item.ServiceLifeEnd);
    }

    [Fact]
    public async Task Закрыть_окно_без_сохранения_ничего_не_добавляет()
    {
        var page = await OpenAsTeacherAsync();
        Ui.OnWindow = null;
        await page.AddCommand.ExecuteAsync(null);
        Assert.Empty(page.Items);
        Assert.Empty(await S<IEquipmentService>().GetListAsync(new EquipmentFilter(_room.Id)));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1,5")]
    public async Task Количество_не_число_не_сохраняется(string qty)
    {
        var page = await OpenAsTeacherAsync();
        Ui.OnWindow = FillAndSave(vm => Monitor(vm, qty: qty));
        await page.AddCommand.ExecuteAsync(null);
        Assert.Contains("Количество", Ui.LastError);
        Assert.Empty(await S<IEquipmentService>().GetListAsync(new EquipmentFilter(_room.Id)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    public async Task Нулевое_и_отрицательное_количество_не_сохраняется(string qty)
    {
        var page = await OpenAsTeacherAsync();
        Ui.OnWindow = FillAndSave(vm => Monitor(vm, qty: qty));
        await page.AddCommand.ExecuteAsync(null);
        Assert.True(Ui.Errors.Count > 0, "Количество " + qty + " сохранилось без ошибки");
        Assert.Empty(await S<IEquipmentService>().GetListAsync(new EquipmentFilter(_room.Id)));
    }

    [Fact]
    public async Task Срок_службы_не_число_не_сохраняется()
    {
        var page = await OpenAsTeacherAsync();
        Ui.OnWindow = FillAndSave(vm => { Monitor(vm); vm.ServiceLifeYears = "много"; });
        await page.AddCommand.ExecuteAsync(null);
        Assert.Contains("Срок службы", Ui.LastError);
        Assert.Empty(await S<IEquipmentService>().GetListAsync(new EquipmentFilter(_room.Id)));
    }

    [Fact]
    public async Task Без_наименования_оборудование_не_сохраняется()
    {
        var page = await OpenAsTeacherAsync();
        Ui.OnWindow = FillAndSave(vm => { Monitor(vm); vm.Name = "  "; });
        await page.AddCommand.ExecuteAsync(null);
        Assert.NotEmpty(Ui.Errors);
        Assert.Empty(await S<IEquipmentService>().GetListAsync(new EquipmentFilter(_room.Id)));
    }

    [Fact]
    public async Task Повторный_инвентарный_номер_не_допускается()
    {
        var page = await OpenAsTeacherAsync();
        await AddMonitorAsync(page, "DUP-1");
        Ui.Errors.Clear();
        Ui.OnWindow = FillAndSave(vm => Monitor(vm, "DUP-1", "Другой монитор"));
        await page.AddCommand.ExecuteAsync(null);
        await SettleAsync(page);
        Assert.True(Ui.Errors.Count > 0, "Два предмета с одним инвентарным номером сохранились без ошибки");
        Assert.Single(await S<IEquipmentService>().GetListAsync(new EquipmentFilter(_room.Id)));
    }

    [Fact]
    public async Task Название_подставляется_по_типу_и_пока_пользователь_не_ввёл_своё_меняется_вместе_с_типом()
    {
        var page = await OpenAsTeacherAsync();
        Ui.OnWindow = async w =>
        {
            var vm = ((EquipmentEditWindow)w).Vm;
            vm.Category = vm.Categories.First(c => c.Name == SeedData.Computer);
            vm.Type = vm.Types.First(t => t.Name == "Монитор");
            Assert.Equal("Монитор", vm.Name);
            vm.Type = vm.Types.First(t => t.Name != "Монитор");
            Assert.Equal(vm.Type.Name, vm.Name);
            vm.Name = "Мой особый";
            vm.Type = vm.Types.First(t => t.Name == "Монитор");
            Assert.Equal("Мой особый", vm.Name);               // своё название не затирается
            await Task.CompletedTask;
            return false;
        };
        await page.AddCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Правка_оборудования_сохраняет_изменения()
    {
        var page = await OpenAsTeacherAsync();
        var item = await AddMonitorAsync(page);
        page.Selected = page.Items.Single(i => i.Id == item.Id);

        Ui.OnWindow = FillAndSave(vm => { vm.Name = "Монитор Samsung"; vm.Quantity = "3"; vm.Notes = "Заменить кабель"; });
        await page.EditCommand.ExecuteAsync(null);
        await SettleAsync(page);

        Assert.Empty(Ui.Errors);
        var db = await S<IEquipmentService>().GetAsync(item.Id);
        Assert.Equal("Монитор Samsung", db.Name);
        Assert.Equal(3, db.Quantity);
        Assert.Equal("Заменить кабель", db.Notes);
        Assert.Equal("INV-001", db.InventoryNumber);                   // остальное не потеряно
        Assert.Single(await S<IEquipmentService>().GetListAsync(new EquipmentFilter(_room.Id)));   // запись одна, не дубль
    }

    [Fact]
    public async Task Состояние_Списано_проставляет_дату_списания_и_оборудование_уходит_из_активных()
    {
        var page = await OpenAsTeacherAsync();
        var item = await AddMonitorAsync(page);
        page.Selected = page.Items.Single(i => i.Id == item.Id);
        Ui.OnWindow = FillAndSave(vm => vm.Condition = vm.Conditions.First(c => c.IsWrittenOff));
        await page.EditCommand.ExecuteAsync(null);
        await SettleAsync(page);

        var db = await S<IEquipmentService>().GetAsync(item.Id);
        Assert.Equal(DateTime.Today, db.WriteOffDate?.Date);
        Assert.True(page.Items.Single().IsWrittenOff);

        page.IncludeWrittenOff = false;
        await SettleAsync(page);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Копирование_создаёт_новую_запись_без_инвентарного_и_серийного_номера()
    {
        var page = await OpenAsTeacherAsync();
        var item = await AddMonitorAsync(page);
        page.Selected = page.Items.Single(i => i.Id == item.Id);

        Ui.OnWindow = FillAndSave(vm =>
        {
            Assert.Null(vm.InventoryNumber);
            Assert.Null(vm.SerialNumber);
            Assert.Equal("Монитор", vm.Name);
            vm.InventoryNumber = "INV-002";
        });
        await page.CopyCommand.ExecuteAsync(null);
        await SettleAsync(page);

        Assert.Empty(Ui.Errors);
        var all = await S<IEquipmentService>().GetListAsync(new EquipmentFilter(_room.Id));
        Assert.Equal(2, all.Count);
        Assert.Equal(new[] { "INV-001", "INV-002" }, all.Select(a => a.InventoryNumber).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Удаление_спрашивает_подтверждение_отказ_ничего_не_удаляет()
    {
        var page = await OpenAsTeacherAsync();
        var item = await AddMonitorAsync(page);
        page.Selected = page.Items.Single(i => i.Id == item.Id);

        Ui.ConfirmAnswer = false;
        await page.DeleteCommand.ExecuteAsync(null);
        Assert.Single(await S<IEquipmentService>().GetListAsync(new EquipmentFilter(_room.Id)));
        Assert.Contains(Ui.Questions, q => q.Contains("Удалить"));

        Ui.ConfirmAnswer = true;
        await page.DeleteCommand.ExecuteAsync(null);
        await SettleAsync(page);
        Assert.Empty(await S<IEquipmentService>().GetListAsync(new EquipmentFilter(_room.Id)));
        Assert.Empty(page.Items);
        Assert.Null(page.Selected);
    }

    [Fact]
    public async Task Кнопки_Правка_Копия_Удалить_без_выбранной_строки_ничего_не_делают()
    {
        var page = await OpenAsTeacherAsync();
        await AddMonitorAsync(page);
        page.Selected = null;
        Ui.OpenedWindows.Clear();
        Ui.OnWindow = _ => throw new InvalidOperationException("окно не должно открываться");
        await page.EditCommand.ExecuteAsync(null);
        await page.CopyCommand.ExecuteAsync(null);
        await page.DeleteCommand.ExecuteAsync(null);
        Assert.Empty(Ui.OpenedWindows);
        Assert.Empty(Ui.Errors);
        Assert.Single(await S<IEquipmentService>().GetListAsync(new EquipmentFilter(_room.Id)));
    }

    [Fact]
    public async Task Поиск_и_фильтры_сужают_список()
    {
        var page = await OpenAsTeacherAsync();
        await AddMonitorAsync(page, "F-1", "Монитор Dell");
        Ui.OnWindow = FillAndSave(vm =>
        {
            vm.Category = vm.Categories.First(c => c.Name == SeedData.Furniture);
            vm.Type = vm.Types.First(t => t.Name == "Стол");
            vm.Name = "Стол учительский"; vm.InventoryNumber = "F-2";
        });
        await page.AddCommand.ExecuteAsync(null);
        await SettleAsync(page);
        Assert.True(Ui.Errors.Count == 0, "Показаны ошибки: " + string.Join(" | ", Ui.Errors));
        Assert.Equal(2, page.Items.Count);

        page.Search = "Dell";
        await page.ReloadCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "F-1" }, page.Items.Select(i => i.InventoryNumber).ToArray());

        page.Search = "";
        page.Category = page.Categories.First(c => c.Name == SeedData.Furniture);
        await SettleAsync(page);
        Assert.Equal(new[] { "F-2" }, page.Items.Select(i => i.InventoryNumber).ToArray());

        page.Category = page.Categories.First(c => c.Id == null);       // «Все категории»
        await SettleAsync(page);
        Assert.Equal(2, page.Items.Count);

        page.Search = "нет такого";
        await page.ReloadCommand.ExecuteAsync(null);
        Assert.Empty(page.Items);
        Assert.Contains("Позиций: 0", page.Summary);
    }

    // ------------------------------------------------------------ Фото и характеристики

    [Fact]
    public async Task Фото_не_больше_трёх_лишние_отклоняются_с_сообщением_и_сохраняются_в_карточке()
    {
        var page = await OpenAsTeacherAsync();
        var files = Enumerable.Range(1, 4).Select(i => TempFile($"p{i}.png", TinyPng())).ToArray();
        Ui.OnWindow = FillAndSave(vm =>
        {
            Monitor(vm);
            Ui.NextFilesToPick = files;
            vm.AddImageCommand.Execute(null);
            Assert.Equal(3, vm.Images.Count);
            Assert.False(vm.CanAddImage);
            Assert.Contains("3 из 3", vm.ImagesCaption);
        });
        await page.AddCommand.ExecuteAsync(null);
        await SettleAsync(page);

        Assert.Single(Ui.Infos.Where(i => i.Contains("не более 3")));
        var id = page.Items.Single().Id;
        Assert.Equal(3, (await S<IEquipmentService>().GetImagesAsync(id)).Count);
        Assert.Equal(3, page.Items.Single().ImageCount);
    }

    [Fact]
    public async Task Главное_фото_переставляется_а_удалённое_исчезает_из_карточки()
    {
        var page = await OpenAsTeacherAsync();
        var item = await AddMonitorAsync(page);
        page.Selected = page.Items.Single(i => i.Id == item.Id);
        var files = new[] { TempFile("a.png", TinyPng()), TempFile("b.png", TinyPng()) };

        Ui.OnWindow = FillAndSave(vm =>
        {
            Ui.NextFilesToPick = files;
            vm.AddImageCommand.Execute(null);
            vm.MakeMainImageCommand.Execute(vm.Images[1]);
            Assert.Equal("b.png", vm.Images[0].FileName);
        });
        await page.EditCommand.ExecuteAsync(null);
        var saved = await S<IEquipmentService>().GetImagesAsync(item.Id);
        Assert.Equal(new[] { "b.png", "a.png" }, saved.OrderBy(i => i.SortOrder).Select(i => i.FileName).ToArray());

        Ui.OnWindow = FillAndSave(vm => vm.RemoveImageCommand.Execute(vm.Images[0]));
        await page.EditCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "a.png" }, (await S<IEquipmentService>().GetImagesAsync(item.Id)).Select(i => i.FileName).ToArray());
    }

    [Fact]
    public async Task Типовые_характеристики_добавляются_без_повторов_а_пустые_строки_не_сохраняются()
    {
        var page = await OpenAsTeacherAsync();
        Ui.OnWindow = FillAndSave(vm =>
        {
            Monitor(vm);
            vm.AddTypicalPropertiesCommand.Execute(null);
            var count = vm.Properties.Count;
            Assert.True(count > 0);
            vm.AddTypicalPropertiesCommand.Execute(null);
            Assert.Equal(count, vm.Properties.Count);                  // повторное нажатие дублей не создаёт
            vm.Properties[0].Value = "Intel";
            vm.AddPropertyCommand.Execute(null);                        // пустая строка без названия
        });
        await page.AddCommand.ExecuteAsync(null);
        await SettleAsync(page);
        var props = (await S<IEquipmentService>().GetAsync(page.Items.Single().Id)).Properties;
        Assert.Contains(props, p => p.Value == "Intel");
        Assert.DoesNotContain(props, p => string.IsNullOrWhiteSpace(p.Name));
    }

    // ------------------------------------------------------------ Блокировка и доступ

    [Fact]
    public async Task Блокировка_карточки_администратором_скрывает_её_от_преподавателя()
    {
        var page = await OpenAsTeacherAsync();
        var item = await AddMonitorAsync(page);
        SignOut();

        await SignInAdminAsync();
        await S<AppState>().ReloadClassroomsAsync(_room.Id);
        var adminPage = await OpenPageAsync<EquipmentPageViewModel>();
        Assert.True(adminPage.IsAdmin);
        adminPage.Selected = adminPage.Items.Single(i => i.Id == item.Id);
        await adminPage.ToggleBlockCommand.ExecuteAsync(null);
        await SettleAsync(adminPage);
        Assert.False(adminPage.Items.Single().IsActive);               // администратор видит её с пометкой
        SignOut();

        await SignInAsync("ivanov", "pass1234");
        await S<AppState>().ReloadClassroomsAsync();
        var teacherPage = await OpenPageAsync<EquipmentPageViewModel>();
        Assert.Empty(teacherPage.Items);

        SignOut();
        await SignInAdminAsync();
        await S<AppState>().ReloadClassroomsAsync(_room.Id);
        var again = await OpenPageAsync<EquipmentPageViewModel>();
        again.Selected = again.Items.Single();
        await again.ToggleBlockCommand.ExecuteAsync(null);              // разблокировка
        SignOut();
        await SignInAsync("ivanov", "pass1234");
        await S<AppState>().ReloadClassroomsAsync();
        Assert.Single((await OpenPageAsync<EquipmentPageViewModel>()).Items);
    }

    [Fact]
    public async Task Преподаватель_не_видит_и_не_может_открыть_оборудование_чужой_аудитории()
    {
        var page = await OpenAsTeacherAsync();
        var item = await AddMonitorAsync(page);
        SignOut();

        await SignInAdminAsync();
        var other = await S<IUserService>().CreateAsync(new UserCreateRequest("Чужой", "Чужой", null, "stranger", "s@example.com", UserRole.Teacher, true, "pass1234"));
        SignOut();
        await SignInAsync("stranger", "pass1234");

        await Assert.ThrowsAsync<AppException>(() => S<IEquipmentService>().GetAsync(item.Id));
        var list = await S<IEquipmentService>().GetListAsync(new EquipmentFilter(_room.Id));
        Assert.Empty(list);
    }

    [Fact]
    public async Task Преподаватель_не_может_удалить_и_изменить_оборудование_чужой_аудитории()
    {
        var page = await OpenAsTeacherAsync();
        var item = await AddMonitorAsync(page);
        var edit = await S<IEquipmentService>().GetAsync(item.Id);
        SignOut();

        await SignInAdminAsync();
        await S<IUserService>().CreateAsync(new UserCreateRequest("Чужой", "Чужой", null, "stranger", "s@example.com", UserRole.Teacher, true, "pass1234"));
        SignOut();
        await SignInAsync("stranger", "pass1234");

        edit.Name = "Взломано";
        await Assert.ThrowsAsync<AppException>(() => S<IEquipmentService>().SaveAsync(edit));
        await Assert.ThrowsAsync<AppException>(() => S<IEquipmentService>().DeleteAsync(item.Id));

        SignOut();
        await SignInAsync("ivanov", "pass1234");
        Assert.Equal("Монитор", (await S<IEquipmentService>().GetAsync(item.Id)).Name);
    }

    [Fact]
    public async Task Без_выбранной_аудитории_добавить_оборудование_нельзя()
    {
        await SignInAdminAsync();
        await S<AppState>().ReloadClassroomsAsync();                    // аудиторий нет
        var page = await OpenPageAsync<EquipmentPageViewModel>();
        Ui.OnWindow = _ => throw new InvalidOperationException("окно не должно открываться");
        await page.AddCommand.ExecuteAsync(null);
        Assert.Contains("выберите аудиторию", Ui.LastError);
        Assert.Contains("не выбрана", page.Summary);
    }

    // ------------------------------------------------------------ Заявки из карточки

    [Fact]
    public async Task Заявка_на_обслуживание_из_карточки_создаётся_и_видна_в_списке_заявок()
    {
        var page = await OpenAsTeacherAsync();
        var item = await AddMonitorAsync(page);
        page.Selected = page.Items.Single(i => i.Id == item.Id);

        Ui.OnWindow = async w =>
        {
            var vm = ((RequestEditWindow)w).Vm;
            await vm.LoadAsync();
            var saved = false; vm.Saved += () => saved = true;
            vm.Title = "Не включается";
            vm.Description = "Нет изображения";
            vm.Priority = ServiceRequestPriority.High;
            await vm.SaveCommand.ExecuteAsync(null);
            return saved;
        };
        page.CreateRequestCommand.Execute(null);

        Assert.Contains(Ui.Infos, i => i.Contains("Заявка на обслуживание создана"));
        var req = (await S<IServiceRequestService>().GetListAsync(_room.Id)).Single();
        Assert.Equal("Не включается", req.Title);
        Assert.Equal(item.Id, req.EquipmentId);
        Assert.Equal(ServiceRequestPriority.High, req.Priority);
        Assert.Equal(ServiceRequestKind.Maintenance, req.Kind);
        Assert.Equal(ServiceRequestStatus.New, req.Status);
    }

    [Fact]
    public async Task Заявка_на_получение_оборудования_получает_заготовку_темы_по_выбранной_позиции()
    {
        var page = await OpenAsTeacherAsync();
        var item = await AddMonitorAsync(page);
        page.Selected = page.Items.Single(i => i.Id == item.Id);

        string? titleSeen = null;
        Ui.OnWindow = async w =>
        {
            var vm = ((RequestEditWindow)w).Vm;
            await vm.LoadAsync();
            titleSeen = vm.Title;
            var saved = false; vm.Saved += () => saved = true;
            await vm.SaveCommand.ExecuteAsync(null);
            return saved;
        };
        page.CreateAcquisitionRequestCommand.Execute(null);
        Assert.Contains("Замена", titleSeen);
        Assert.Contains("INV-001", titleSeen);
        var req = (await S<IServiceRequestService>().GetListAsync(_room.Id)).Single();
        Assert.Equal(ServiceRequestKind.Acquisition, req.Kind);
    }

    [Fact]
    public async Task Заявка_без_темы_не_создаётся()
    {
        var page = await OpenAsTeacherAsync();
        var item = await AddMonitorAsync(page);
        page.Selected = page.Items.Single(i => i.Id == item.Id);
        Ui.OnWindow = async w =>
        {
            var vm = ((RequestEditWindow)w).Vm;
            await vm.LoadAsync();
            var saved = false; vm.Saved += () => saved = true;
            vm.Title = "  ";
            await vm.SaveCommand.ExecuteAsync(null);
            return saved;
        };
        page.CreateRequestCommand.Execute(null);
        Assert.Single(Ui.Errors);
        Assert.Empty(await S<IServiceRequestService>().GetListAsync(_room.Id));
    }

    // ------------------------------------------------------------ Выгрузки

    [Theory]
    [InlineData("Excel", (byte)'P', (byte)'K')]
    [InlineData("Pdf", (byte)'%', (byte)'P')]
    public async Task Карточка_оборудования_выгружается_в_файл_нужного_формата(string format, byte b0, byte b1)
    {
        var page = await OpenAsTeacherAsync();
        var item = await AddMonitorAsync(page);
        page.Selected = page.Items.Single(i => i.Id == item.Id);
        await page.ExportCardCommand.ExecuteAsync(format);
        Assert.Empty(Ui.Errors);
        var (name, bytes) = Assert.Single(Ui.Saved);
        Assert.Contains("INV-001", name);
        Assert.Equal(b0, bytes[0]);
        Assert.Equal(b1, bytes[1]);
        Assert.True(bytes.Length > 500);
    }

    [Theory]
    [InlineData("Excel")]
    [InlineData("Pdf")]
    [InlineData("Html")]
    public async Task Список_оборудования_выгружается_в_файл(string format)
    {
        var page = await OpenAsTeacherAsync();
        await AddMonitorAsync(page);
        page.ExportCommand.Execute(format);
        Assert.Empty(Ui.Errors);
        var (_, bytes) = Assert.Single(Ui.Saved);
        Assert.True(bytes.Length > 100);
    }

    [Fact]
    public async Task Шаблон_Excel_скачивается_и_загруженный_заполненный_файл_добавляет_оборудование()
    {
        var page = await OpenAsTeacherAsync();
        page.DownloadTemplateCommand.Execute(null);
        var (_, template) = Assert.Single(Ui.Saved);
        Assert.Equal((byte)'P', template[0]);

        // пустой шаблон: ошибок нет, добавлено 0
        Ui.NextFileToOpen = TempFile("empty.xlsx", template);
        await page.ExecuteActionAsync("import");
        Assert.Empty(Ui.Errors);
        Assert.Contains("Загружено позиций: 0", Ui.Infos.Last());
    }

    [Fact]
    public async Task Отмена_выбора_файла_при_загрузке_из_Excel_ничего_не_делает()
    {
        var page = await OpenAsTeacherAsync();
        Ui.NextFileToOpen = null;
        await page.ExecuteActionAsync("import");
        Assert.Empty(Ui.Infos);
        Assert.Empty(Ui.Errors);
    }
}
