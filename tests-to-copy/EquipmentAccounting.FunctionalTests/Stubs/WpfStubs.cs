// Заглушки для типов Windows/WPF, которые используют модели окон. Сами модели не меняются.
// Всё, что «показывается пользователю», записывается в Ui и проверяется в тестах.
using System.ComponentModel;
using EquipmentAccounting.Application;

namespace System.ComponentModel
{
    /// <summary>Представление таблицы с фильтром (в WPF — из WindowsBase).</summary>
    public interface ICollectionView
    {
        Predicate<object>? Filter { get; set; }
        void Refresh();
    }
}

namespace System.Windows.Data
{
    public static class CollectionViewSource
    {
        private sealed class View : ICollectionView
        {
            private readonly System.Collections.IEnumerable _source;
            public View(System.Collections.IEnumerable source) => _source = source;
            public Predicate<object>? Filter { get; set; }
            public void Refresh() { }
            public IEnumerable<object> Visible => _source.Cast<object>().Where(o => Filter == null || Filter(o));
        }

        public static ICollectionView GetDefaultView(System.Collections.IEnumerable source) => new View(source);
    }
}

namespace Microsoft.Win32
{
    public class OpenFileDialog
    {
        public string Filter { get; set; } = "";
        public bool Multiselect { get; set; }
        public string Title { get; set; } = "";
        public string[] FileNames { get; private set; } = Array.Empty<string>();
        public bool? ShowDialog()
        {
            var files = EquipmentAccounting.FunctionalTests.Ui.NextFilesToPick;
            if (files == null) return false;
            FileNames = files;
            return true;
        }
    }
}

namespace EquipmentAccounting.Wpf
{
    public static class App
    {
        public static List<Exception> Logged { get; } = new();
        public static void LogError(Exception ex) => Logged.Add(ex);
    }
}

namespace EquipmentAccounting.Wpf.Common
{
    /// <summary>Вместо ImageHelper из WPF: возвращает файл как есть (уменьшение картинки здесь не проверяется).</summary>
    public static class ImageHelper
    {
        public static byte[] LoadAndShrink(string path) => File.ReadAllBytes(path);
    }

    /// <summary>
    /// Вместо окон-сообщений: запоминает показанные пользователю сообщения и отвечает на вопросы
    /// так, как задано в тесте (по умолчанию — «Да»).
    /// </summary>
    public static class Dialogs
    {
        public static void Info(string message) => EquipmentAccounting.FunctionalTests.Ui.Infos.Add(message);
        public static void Error(string message) => EquipmentAccounting.FunctionalTests.Ui.Errors.Add(message);

        public static bool Confirm(string message)
        {
            EquipmentAccounting.FunctionalTests.Ui.Questions.Add(message);
            return EquipmentAccounting.FunctionalTests.Ui.ConfirmAnswer;
        }

        /// <summary>Открытие окна: тест «работает за пользователя» внутри окна через зарегистрированный обработчик.</summary>
        public static bool? ShowDialog(object window) => EquipmentAccounting.FunctionalTests.Ui.RunWindow(window);

        public static string? OpenFile(string filter, string? title = null) => EquipmentAccounting.FunctionalTests.Ui.NextFileToOpen;

        public static string Filter(ExportFormat format) => format.ToString();

        public static void SaveAndOffer(string suggestedName, string filter, byte[] content) =>
            EquipmentAccounting.FunctionalTests.Ui.Saved.Add((suggestedName, content));
    }
}

namespace EquipmentAccounting.Wpf.Views
{
    /// <summary>Окна-заглушки: хранят модель, как настоящие окна, и ничего не рисуют.</summary>
    public class EquipmentEditWindow
    {
        public EquipmentEditWindow(ViewModels.EquipmentEditViewModel vm) => Vm = vm;
        public ViewModels.EquipmentEditViewModel Vm { get; }
    }

    public class RequestEditWindow
    {
        public RequestEditWindow(ViewModels.RequestEditViewModel vm) => Vm = vm;
        public ViewModels.RequestEditViewModel Vm { get; }
    }

    public class ImageViewWindow
    {
        public ImageViewWindow(IList<EquipmentImageDto> images, int index) { Images = images; Index = index; }
        public IList<EquipmentImageDto> Images { get; }
        public int Index { get; }
    }

    public class SelectClassroomWindow
    {
        public SelectClassroomWindow(IEnumerable<ClassroomDto> classrooms) => Offered = classrooms.ToList();
        public IReadOnlyList<ClassroomDto> Offered { get; }
        public ClassroomDto? Selected { get; set; }
    }

    public static class PinInputDialog
    {
        public static string? RequestPin() => EquipmentAccounting.FunctionalTests.Ui.NextPin;
    }
}
