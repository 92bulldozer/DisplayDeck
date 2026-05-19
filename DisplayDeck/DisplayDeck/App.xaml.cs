using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace DisplayDeck
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 시스템 강조색(보라색)을 따르지 않고 앱 강조색을 파란색으로 고정.
            // 추가 버튼·항상 위 토글 등 Primary/강조 컨트롤에 적용된다.
            ApplicationAccentColorManager.Apply(
                Color.FromRgb(0x25, 0x52, 0xB0),
                ApplicationTheme.Dark);
        }
    }
}
