using Acordater.App.Interpretation;
using Acordater.App.ViewModels;
using Acordater.App.Voice;
using Acordater.Core.Alerts;
using Acordater.Core.Interpretation;
using Acordater.Core.Scheduling;
using Acordater.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Acordater.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		var databasePath = Path.Combine(FileSystem.AppDataDirectory, "acordater.db");
		builder.Services.AddDbContextFactory<AcordaterDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));

		builder.Services.AddSingleton(TimeProvider.System);
		builder.Services.AddSingleton(Preferences.Default);
		builder.Services.AddSingleton(SecureStorage.Default);
		builder.Services.AddSingleton<QuietHoursSettings>();
		builder.Services.AddSingleton<IQuietHoursProvider>(services => services.GetRequiredService<QuietHoursSettings>());
		builder.Services.AddSingleton<ReminderScheduler>();
		builder.Services.AddSingleton<AiSettings>();
		builder.Services.AddSingleton<IReminderInterpreter, ConfiguredInterpreter>();
		builder.Services.AddSingleton<IReminderStore, ReminderStore>();
		builder.Services.AddSingleton<ReminderService>();
		builder.Services.AddSingleton<IAlarmScheduler, AndroidAlarmScheduler>();
		builder.Services.AddSingleton<IReminderNotifier, AndroidReminderNotifier>();
		builder.Services.AddSingleton<ISpeechRecognizer, AndroidSpeechRecognizer>();
		builder.Services.AddSingleton<WakeWordSettings>();
		builder.Services.AddSingleton<IWakeWordDetector, AndroidWakeWordDetector>();
		builder.Services.AddSingleton(TextToSpeech.Default);
		builder.Services.AddSingleton<VoiceFeedback>();
		builder.Services.AddSingleton<ReminderTimeFormatter>();
		builder.Services.AddTransient<MainViewModel>();
		builder.Services.AddTransient<MainPage>();
		builder.Services.AddTransient<ReminderViewModel>();
		builder.Services.AddTransient<ReminderPage>();
		builder.Services.AddTransient<SettingsViewModel>();
		builder.Services.AddTransient<SettingsPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();

		using (var db = app.Services.GetRequiredService<IDbContextFactory<AcordaterDbContext>>().CreateDbContext())
			db.Database.Migrate();

		return app;
	}
}
