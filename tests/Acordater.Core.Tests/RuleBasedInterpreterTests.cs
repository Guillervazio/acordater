using System.Globalization;
using Acordater.Core.Interpretation;
using static Acordater.Core.Tests.TestClock;

namespace Acordater.Core.Tests;

// "Now" is Tuesday 2026-09-29 10:00 (UTC+2) in every case.
public class RuleBasedInterpreterTests
{
    [Theory]
    // No time: the scheduler applies the default.
    [InlineData("recordame comprar jabón", "comprar jabón", null)]
    [InlineData("Recordame comprar pan.", "comprar pan", null)]
    [InlineData("recordame de comprar leche", "comprar leche", null)]
    [InlineData("recordame que tengo que llamar a Juan", "llamar a Juan", null)]
    [InlineData("acordame regar las plantas", "regar las plantas", null)]
    // Relative.
    [InlineData("Recuérdame en 2 horas limpiar la caja del gato", "limpiar la caja del gato", "29 12:00")]
    [InlineData("recordame en 30 minutos sacar la pizza del horno", "sacar la pizza del horno", "29 10:30")]
    [InlineData("recordame en media hora llamar a mamá", "llamar a mamá", "29 10:30")]
    [InlineData("acordame en una hora y media de regar las plantas", "regar las plantas", "29 11:30")]
    [InlineData("recordame dentro de veinte minutos sacar la ropa", "sacar la ropa", "29 10:20")]
    [InlineData("recordame que en 20 minutos saque la carne del congelador", "saque la carne del congelador", "29 10:20")]
    // Absolute.
    [InlineData("recordame mañana a las 9 llamar al banco", "llamar al banco", "30 09:00")]
    [InlineData("recordame a las 18 regar las plantas", "regar las plantas", "29 18:00")]
    [InlineData("recordame a las 23 sacar la basura", "sacar la basura", "29 23:00")]
    [InlineData("recordame a la tarde a las 6 pagar la luz", "pagar la luz", "29 18:00")]
    [InlineData("recordame a las 6 de la tarde pagar la luz", "pagar la luz", "29 18:00")]
    [InlineData("recordame a las 8 y media de la mañana ir al gimnasio", "ir al gimnasio", "30 08:30")]
    [InlineData("recordame llamar a Juan a las 17:45", "llamar a Juan", "29 17:45")]
    [InlineData("recordame a las 9 de la noche sacar la basura", "sacar la basura", "29 21:00")]
    // 24-hour clock: the spoken hour is literal unless a period or am/pm says otherwise.
    [InlineData("recordame a las 9 sacar la basura", "sacar la basura", "30 09:00")]
    [InlineData("recordame a las 21 sacar la basura", "sacar la basura", "29 21:00")]
    [InlineData("recordame a las 11 sacar la basura", "sacar la basura", "29 11:00")]
    [InlineData("recordame mañana a las 3 ir al médico", "ir al médico", "30 03:00")]
    [InlineData("recordame a la una comer", "comer", "30 01:00")]
    [InlineData("recordame mañana a la mañana a las 9 llamar al banco", "llamar al banco", "30 09:00")]
    // Day or period without hour.
    [InlineData("recordame mañana comprar leche", "comprar leche", "30 09:00")]
    [InlineData("recordame comprar pan para mañana", "comprar pan", "30 09:00")]
    [InlineData("recordame esta noche cerrar la puerta", "cerrar la puerta", "29 20:00")]
    [InlineData("recordame a la tarde llamar a Ana", "llamar a Ana", "29 15:00")]
    // English.
    [InlineData("remind me to buy milk", "buy milk", null)]
    [InlineData("remind me in 20 minutes to take the pizza out", "take the pizza out", "29 10:20")]
    [InlineData("remind me in an hour and a half to feed the cats", "feed the cats", "29 11:30")]
    [InlineData("remind me in half an hour to call mom", "call mom", "29 10:30")]
    [InlineData("remind me to call mom tomorrow at 9am", "call mom", "30 09:00")]
    [InlineData("remind me at 6 pm to water the plants", "water the plants", "29 18:00")]
    [InlineData("remind me at 6:30 in the evening to water the plants", "water the plants", "29 18:30")]
    [InlineData("remind me tonight at 10 to lock the door", "lock the door", "29 22:00")]
    [InlineData("remind me tomorrow morning to pay the rent", "pay the rent", "30 09:00")]
    public async Task Interprets(string utterance, string expectedText, string? expectedAt)
    {
        var interpreter = new RuleBasedInterpreter(At(29, 10));

        var result = await interpreter.InterpretAsync(utterance);

        Assert.Equal(expectedText, result.Text);
        Assert.Equal(Parse(expectedAt), result.RequestedAt);
    }

    [Fact]
    public async Task HourAlreadyPassedTodayMeansTomorrow()
    {
        var interpreter = new RuleBasedInterpreter(At(29, 22));

        var result = await interpreter.InterpretAsync("recordame a las 9 regar");

        Assert.Equal(Local(30, 9), result.RequestedAt);
    }

    [Fact]
    public async Task UtteranceWithOnlyTriggerKeepsOriginalText()
    {
        var interpreter = new RuleBasedInterpreter(At(29, 10));

        var result = await interpreter.InterpretAsync("recordame");

        Assert.Equal("recordame", result.Text);
        Assert.Null(result.RequestedAt);
    }

    static DateTimeOffset? Parse(string? dayAndTime)
    {
        if (dayAndTime is null) return null;
        var parts = dayAndTime.Split(' ');
        var t = TimeOnly.Parse(parts[1], CultureInfo.InvariantCulture);
        return Local(int.Parse(parts[0], CultureInfo.InvariantCulture), t.Hour, t.Minute);
    }
}
