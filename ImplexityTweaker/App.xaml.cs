using System.Windows;
using ImplexityTweaker.Services;

namespace ImplexityTweaker
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var settings = AppSettings.Load();
            ThemeApplier.Apply(settings.ThemeId);
        }
    }
}
