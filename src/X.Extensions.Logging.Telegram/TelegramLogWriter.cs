using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace X.Extensions.Logging.Telegram;

[PublicAPI]
public interface ILogWriter
{
    Task Write(string message);
    
    Task Write(string message, CancellationToken cancellationToken);
}

public class TelegramLogWriter : ILogWriter
{
    private readonly string _chatId;
    private readonly ITelegramBotClient _client;

    public TelegramLogWriter(string accessToken, string chatId)
        : this(new TelegramBotClient(accessToken), chatId)
    {
    }

    public TelegramLogWriter(ITelegramBotClient client, string chatId)
    {
        _chatId = chatId;
        _client = client;
    }

    public async Task Write(string message)
    {
        await Write(message, CancellationToken.None);
    }

    public async Task Write(string message, CancellationToken cancellationToken)
    {
        var messageThreadId = Convert.ToInt32(ParseMode.Html);
        
        await _client.SendTextMessageAsync(_chatId, message, messageThreadId, cancellationToken: cancellationToken);
    }
}