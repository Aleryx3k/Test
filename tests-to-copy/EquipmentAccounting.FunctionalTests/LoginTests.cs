using EquipmentAccounting.Application;
using EquipmentAccounting.Domain;
using EquipmentAccounting.Infrastructure.Data;
using EquipmentAccounting.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentAccounting.FunctionalTests;

/// <summary>Окно входа: что происходит при нажатии «Войти», «Зарегистрироваться», ПИН-код.</summary>
public class LoginTests : AppTestBase
{
    [Fact]
    public async Task Войти_с_верным_паролем_открывает_главное_окно()
    {
        Assert.True(await SignInAdminAsync());
        Assert.True(S<IUserSession>().IsAdmin);
        Assert.Empty(Ui.Errors);
    }

    [Fact]
    public async Task Войти_с_неверным_паролем_показывает_ошибку_и_не_пускает()
    {
        Assert.False(await SignInAsync(SeedData.AdminLogin, "неверный"));
        Assert.Single(Ui.Errors);
        Assert.Null(S<IUserSession>().CurrentUser);
    }

    [Fact]
    public async Task Войти_с_пустыми_полями_не_пускает()
    {
        Assert.False(await SignInAsync("", ""));
        Assert.NotEmpty(Ui.Errors);
        Assert.Null(S<IUserSession>().CurrentUser);
    }

    [Fact]
    public async Task Войти_под_несуществующим_логином_не_пускает()
    {
        Assert.False(await SignInAsync("nobody", "whatever1"));
        Assert.NotEmpty(Ui.Errors);
    }

    [Fact]
    public async Task Регистрация_создаёт_преподавателя_и_после_неё_можно_войти()
    {
        var vm = S<LoginViewModel>();
        vm.ShowRegisterCommand.Execute(null);
        Assert.True(vm.IsRegisterMode);

        vm.RegLastName = "Петров"; vm.RegFirstName = "Пётр"; vm.RegMiddleName = "Петрович";
        vm.RegLogin = "petrov"; vm.RegEmail = "petrov@example.com";
        vm.RegPassword = "pass1234"; vm.RegPasswordConfirm = "pass1234";
        await vm.RegisterCommand.ExecuteAsync(null);

        Assert.Empty(Ui.Errors);
        Assert.Single(Ui.Infos);
        Assert.True(vm.IsLoginMode);               // после регистрации окно возвращается ко входу
        Assert.Equal("petrov", vm.Login);          // логин подставлен
        Assert.Equal("", vm.RegPassword);          // пароль очищен

        Assert.True(await SignInAsync("petrov", "pass1234"));
        Assert.False(S<IUserSession>().IsAdmin);   // новый пользователь — не администратор
    }

    [Fact]
    public async Task Регистрация_с_разными_паролями_отклоняется_и_ничего_не_создаёт()
    {
        var vm = S<LoginViewModel>();
        vm.RegLastName = "Сидоров"; vm.RegFirstName = "Сидор"; vm.RegLogin = "sidorov"; vm.RegEmail = "s@example.com";
        vm.RegPassword = "pass1234"; vm.RegPasswordConfirm = "pass12345";
        await vm.RegisterCommand.ExecuteAsync(null);

        Assert.Contains("не совпадают", Ui.LastError);
        Assert.False(await SignInAsync("sidorov", "pass1234"));
    }

    [Fact]
    public async Task Регистрация_с_занятым_логином_отклоняется_без_учёта_регистра()
    {
        var first = S<LoginViewModel>();
        first.RegLastName = "А"; first.RegFirstName = "Б"; first.RegLogin = "user1"; first.RegEmail = "u1@example.com";
        first.RegPassword = first.RegPasswordConfirm = "pass1234";
        await first.RegisterCommand.ExecuteAsync(null);
        Assert.Empty(Ui.Errors);

        var second = S<LoginViewModel>();
        second.RegLastName = "В"; second.RegFirstName = "Г"; second.RegLogin = "USER1"; second.RegEmail = "u2@example.com";
        second.RegPassword = second.RegPasswordConfirm = "pass1234";
        await second.RegisterCommand.ExecuteAsync(null);
        Assert.Single(Ui.Errors);
    }

    [Theory]
    [InlineData("", "Иван", "ivan", "i@example.com", "pass1234")]          // нет фамилии
    [InlineData("Иванов", "", "ivan", "i@example.com", "pass1234")]         // нет имени
    [InlineData("Иванов", "Иван", "", "i@example.com", "pass1234")]         // нет логина
    [InlineData("Иванов", "Иван", "ivan", "не-почта", "pass1234")]          // неверная почта
    [InlineData("Иванов", "Иван", "ivan", "i@example.com", "1")]            // слишком короткий пароль
    public async Task Регистрация_с_неверными_данными_отклоняется(string last, string first, string login, string email, string pass)
    {
        var vm = S<LoginViewModel>();
        vm.RegLastName = last; vm.RegFirstName = first; vm.RegLogin = login; vm.RegEmail = email;
        vm.RegPassword = vm.RegPasswordConfirm = pass;
        await vm.RegisterCommand.ExecuteAsync(null);
        Assert.NotEmpty(Ui.Errors);
        Assert.Empty(Ui.Infos);
    }

    [Fact]
    public async Task Заблокированный_пользователь_не_может_войти()
    {
        await SignInAdminAsync();
        var u = await S<IUserService>().CreateAsync(new UserCreateRequest("Блок", "Блок", null, "blocked", "b@example.com", UserRole.Teacher, true, "pass1234"));
        await S<IUserService>().UpdateAsync(new UserUpdateRequest(u.Id, u.LastName, u.FirstName, null, u.Login, u.Email, UserRole.Teacher, false));
        SignOut();

        Assert.False(await SignInAsync("blocked", "pass1234"));
        Assert.Single(Ui.Errors);
    }

    [WindowsFact]
    public async Task ПИН_создаётся_по_паролю_и_позволяет_войти_без_пароля()
    {
        var vm = S<LoginViewModel>();
        vm.Login = SeedData.AdminLogin; vm.Password = SeedData.AdminPassword;
        Ui.NextPin = "1234";
        await vm.CreatePinCommand.ExecuteAsync(null);
        Assert.True(Ui.Errors.Count == 0, "Показаны ошибки: " + string.Join(" | ", Ui.Errors));
        Assert.True(vm.PinAvailable);
        Assert.Equal("", vm.Password);   // пароль очищен после создания ПИН-кода

        var vm2 = S<LoginViewModel>();
        var entered = false; vm2.LoggedIn += () => entered = true;
        vm2.Login = SeedData.AdminLogin; vm2.Pin = "1234";
        await vm2.PinSignInCommand.ExecuteAsync(null);
        Assert.True(entered);
    }

    [WindowsFact]
    public async Task Неверный_ПИН_не_пускает()
    {
        var setup = S<LoginViewModel>();
        setup.Login = SeedData.AdminLogin; setup.Password = SeedData.AdminPassword;
        Ui.NextPin = "1234";
        await setup.CreatePinCommand.ExecuteAsync(null);

        var vm = S<LoginViewModel>();
        var entered = false; vm.LoggedIn += () => entered = true;
        vm.Login = SeedData.AdminLogin; vm.Pin = "0000";
        await vm.PinSignInCommand.ExecuteAsync(null);
        Assert.False(entered);
        Assert.NotEmpty(Ui.Errors);
    }

    [Fact]
    public async Task ПИН_нельзя_создать_без_пароля()
    {
        var vm = S<LoginViewModel>();
        vm.Login = SeedData.AdminLogin; vm.Password = "";
        Ui.NextPin = "1234";
        await vm.CreatePinCommand.ExecuteAsync(null);
        Assert.Single(Ui.Errors);
        Assert.False(vm.PinAvailable);
    }

    [WindowsFact]
    public async Task ПИН_удаляется_и_после_этого_не_работает()
    {
        var vm = S<LoginViewModel>();
        vm.Login = SeedData.AdminLogin; vm.Password = SeedData.AdminPassword;
        Ui.NextPin = "1234";
        await vm.CreatePinCommand.ExecuteAsync(null);

        vm.Password = SeedData.AdminPassword;
        await vm.RemovePinCommand.ExecuteAsync(null);
        Assert.False(vm.PinAvailable);
        Assert.Contains(Ui.Questions, q => q.Contains("Удалить ПИН"));

        var vm2 = S<LoginViewModel>();
        var entered = false; vm2.LoggedIn += () => entered = true;
        vm2.Login = SeedData.AdminLogin; vm2.Pin = "1234";
        await vm2.PinSignInCommand.ExecuteAsync(null);
        Assert.False(entered);
    }

    [Fact]
    public async Task Кнопки_переключают_режимы_окна_входа()
    {
        var vm = S<LoginViewModel>();
        Assert.True(vm.IsLoginMode);
        vm.ShowRegisterCommand.Execute(null); Assert.True(vm.IsRegisterMode);
        vm.ShowRecoverCommand.Execute(null); Assert.True(vm.IsRecoverMode);
        vm.ShowLoginCommand.Execute(null); Assert.True(vm.IsLoginMode);
        await Task.CompletedTask;
    }
}
