using System.Globalization;
using System.Net.Http;
using Acordater.Core.Interpretation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;
using static Acordater.Core.Tests.TestClock;

namespace Acordater.Core.Tests;

// "Now" is Tuesday 2026-09-29 10:00 (UTC+2) in every case. No test calls a real provider.
public class ChatInterpreterTests
{
    const string Utterance = "recordame a las 18 regar las plantas";

    readonly FakeTimeProvider time = At(29, 10);
    readonly FakeChatClient chat = new();

    ChatInterpreter Interpreter() => new(chat, "Claude", time, new CultureInfo("es-ES"));

    [Fact]
    public async Task UsesTheValidAnswer()
    {
        chat.Answer = """{"text": "regar las plantas", "when": "2026-09-29T18:00"}""";

        var result = await Interpreter().InterpretAsync(Utterance);

        Assert.Equal("regar las plantas", result.Text);
        Assert.Equal(Local(29, 18), result.RequestedAt);
        Assert.Equal("Claude", result.Interpreter);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task NoTimeMeansTheDefault()
    {
        chat.Answer = """{"text": "comprar jabón", "when": null}""";

        var result = await Interpreter().InterpretAsync("recordame comprar jabón");

        Assert.Equal("comprar jabón", result.Text);
        Assert.Null(result.RequestedAt);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task AcceptsJsonWrappedInMarkdown()
    {
        chat.Answer = "```json\n{\"text\": \"llamar a Juan\", \"when\": \"2026-09-30T09:00\"}\n```";

        var result = await Interpreter().InterpretAsync("recordame mañana llamar a Juan");

        Assert.Equal("llamar a Juan", result.Text);
        Assert.Equal(Local(30, 9), result.RequestedAt);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task PastTimeMeansTheDefault()
    {
        chat.Answer = """{"text": "regar las plantas", "when": "2026-09-29T08:00"}""";

        var result = await Interpreter().InterpretAsync("recordame hoy a las 8 regar las plantas");

        Assert.Equal("regar las plantas", result.Text);
        Assert.Null(result.RequestedAt);
        Assert.Null(result.Failure);
    }

    [Theory]
    [InlineData("Claro, te lo recuerdo a las 18.")]
    [InlineData("""{"text": "regar las plantas", "when": "a las 18"}""")]
    [InlineData("""{"text": "regar las plantas", "when": 18}""")]
    [InlineData("""{"when": "2026-09-29T18:00"}""")]
    [InlineData("""{"text": "regar las plantas", "when": "2026-09-29T18:00" """)]
    [InlineData("""["regar las plantas"]""")]
    public async Task InvalidAnswerFallsBackToTheRules(string answer)
    {
        chat.Answer = answer;

        var result = await Interpreter().InterpretAsync(Utterance);

        AssertRulesResult(result, InterpretationFailureKind.InvalidResponse);
    }

    [Theory]
    [InlineData("""{"text": "", "when": "2026-09-29T18:00"}""")]
    [InlineData("""{"text": "   ", "when": null}""")]
    public async Task EmptyTaskFallsBackToTheRules(string answer)
    {
        chat.Answer = answer;

        var result = await Interpreter().InterpretAsync(Utterance);

        AssertRulesResult(result, InterpretationFailureKind.InvalidResponse);
    }

    [Fact]
    public async Task TimeoutFallsBackToTheRules()
    {
        chat.NeverAnswers = true;

        var pending = Interpreter().InterpretAsync(Utterance);
        Assert.False(pending.IsCompleted);
        time.Advance(ChatInterpreter.DefaultTimeout);
        var result = await pending;

        AssertRulesResult(result, InterpretationFailureKind.Timeout);
    }

    [Fact]
    public async Task NoConnectionFallsBackToTheRules()
    {
        chat.Error = new HttpRequestException("No such host is known.");

        var result = await Interpreter().InterpretAsync(Utterance);

        AssertRulesResult(result, InterpretationFailureKind.Network);
    }

    [Fact]
    public async Task ProviderErrorFallsBackToTheRules()
    {
        chat.Error = new InvalidOperationException("401 invalid x-api-key");

        var result = await Interpreter().InterpretAsync(Utterance);

        AssertRulesResult(result, InterpretationFailureKind.ProviderError);
        Assert.Equal("401 invalid x-api-key", result.Failure!.Detail);
    }

    [Fact]
    public async Task CancellingIsNotAFailure()
    {
        chat.NeverAnswers = true;
        using var cancel = new CancellationTokenSource();

        var pending = Interpreter().InterpretAsync(Utterance, cancel.Token);
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task SendsDateTimeZoneAndLanguage()
    {
        chat.Answer = """{"text": "regar las plantas", "when": null}""";

        await Interpreter().InterpretAsync(Utterance);

        var messages = chat.LastMessages!;
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Contains("24-hour clock", messages[0].Text);
        var context = messages[1].Text;
        Assert.Contains("2026-09-29T10:00 (Tuesday)", context);
        Assert.Contains("time zone Test+2 (UTC+02:00)", context);
        Assert.Contains("language es-ES", context);
        Assert.Contains(Utterance, context);
    }

    [Fact]
    public async Task PassesTheRequestOptions()
    {
        chat.Answer = """{"text": "regar las plantas", "when": null}""";
        var options = new ChatOptions { ModelId = "claude-haiku-4-5" };

        await new ChatInterpreter(chat, "Claude", time, CultureInfo.InvariantCulture, options).InterpretAsync(Utterance);

        Assert.Equal("claude-haiku-4-5", chat.LastOptions?.ModelId);
    }

    static void AssertRulesResult(InterpretedReminder result, InterpretationFailureKind kind)
    {
        Assert.Equal("regar las plantas", result.Text);
        Assert.Equal(Local(29, 18), result.RequestedAt);
        Assert.Equal(RuleBasedInterpreter.Name, result.Interpreter);
        Assert.Equal(new InterpretationFailure("Claude", kind, result.Failure?.Detail ?? ""), result.Failure);
    }

    sealed class FakeChatClient : IChatClient
    {
        public string Answer { get; set; } = "";
        public Exception? Error { get; set; }
        public bool NeverAnswers { get; set; }
        public IList<ChatMessage>? LastMessages { get; private set; }
        public ChatOptions? LastOptions { get; private set; }

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToList();
            LastOptions = options;
            if (NeverAnswers) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (Error is not null) throw Error;
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, Answer));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
