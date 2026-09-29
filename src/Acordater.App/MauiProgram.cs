using Acordater.App.ViewModels;
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
		builder.Services.AddSingleton(QuietHours.Default);
		builder.Services.AddSingleton<ReminderScheduler>();
		builder.Services.AddSingleton<IReminderInterpreter, RuleBasedInterpreter>();
		builder.Services.AddSingleton<ReminderStore>();
		builder.Services.AddSingleton<ReminderTimeFormatter>();
		builder.Services.AddTransient<MainViewModel>();
		builder.Services.AddTransient<MainPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();

		using (var db = app.Services.GetRequiredService<IDbContextFactory<AcordaterDbContext>>().CreateDbContext())
			db.Database.Migrate();

		return app;
	}
}
