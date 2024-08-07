﻿using System.Collections.Immutable;
using System.Threading;
using System.Threading.Channels;
using Serilog.Core;
using X.Extensions.Logging.Telegram;
using X.Extensions.Serilog.Sinks.Telegram.Batch;
using X.Extensions.Serilog.Sinks.Telegram.Configuration;
using X.Extensions.Serilog.Sinks.Telegram.Filters;
using X.Extensions.Serilog.Sinks.Telegram.Formatters;

namespace X.Extensions.Serilog.Sinks.Telegram;

public class TelegramSink : ILogEventSink, IDisposable, IAsyncDisposable
{
    private readonly BatchCycleManager _batchCycleManager;

    private readonly CancellationTokenSource _cancellationTokenSource;

    private readonly ChannelWriter<LogEvent> _channelWriter;
    private readonly LogFilterManager? _logFilterManager;
    private readonly ILogsQueueAccessor _logsQueueAccessor;

    private readonly IMessageFormatter _messageFormatter;
    private readonly ILogWriter _logWriter;

    private readonly TelegramSinkConfiguration _sinkConfiguration;

    public TelegramSink(
        ChannelWriter<LogEvent> channelWriter,
        ILogsQueueAccessor logsQueueAccessor,
        TelegramSinkConfiguration sinkConfiguration,
        IMessageFormatter? messageFormatter)
    {
        _channelWriter = channelWriter;
        _logsQueueAccessor = logsQueueAccessor;
        _sinkConfiguration = sinkConfiguration;
        _messageFormatter = messageFormatter ??
                            TelegramSinkDefaults.GetDefaultMessageFormatter(_sinkConfiguration.Mode);

        _cancellationTokenSource = new CancellationTokenSource();

        _logWriter = new TelegramLogWriter(_sinkConfiguration.Token, _sinkConfiguration.ChatId);

        _batchCycleManager = new BatchCycleManager(sinkConfiguration.BatchEmittingRulesConfiguration);

        if (sinkConfiguration.LogFiltersConfiguration is { ApplyLogFilters: true })
        {
            _logFilterManager = new LogFilterManager(sinkConfiguration.LogFiltersConfiguration);
        }

        ExecuteLogsProcessingLoop(CancellationToken);
    }

    private CancellationToken CancellationToken => _cancellationTokenSource.Token;

    public async ValueTask DisposeAsync()
    {
        _batchCycleManager.Dispose();
        _channelWriter.Complete();

        if (_logsQueueAccessor.GetSize() >= 0)
        {
            await FlushAsync();
        }
    }

    public void Dispose()
    {
        _ = Task.Run(DisposeAsync, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var isFilterPassed = _logFilterManager?.ApplyFilter(logEvent);
        if (isFilterPassed is false)
        {
            return;
        }

        _channelWriter.TryWrite(logEvent);
    }

    private void ExecuteLogsProcessingLoop(CancellationToken cancellationToken)
    {
        Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _batchCycleManager.WhenNextAvailableAsync(cancellationToken);
                await EmitBatchAsync(cancellationToken);
                await _batchCycleManager.OnBatchProcessedAsync(cancellationToken);
            }
        }, cancellationToken);
    }

    private async Task EmitBatchAsync(CancellationToken cancellationToken)
    {
        var batchSize = _sinkConfiguration.BatchPostingLimit;
        await EmitBatchInternalAsync(batchSize, cancellationToken);
    }

    private async Task EmitBatchInternalAsync(int batchSize, CancellationToken cancellationToken)
    {
        var messages = await GetMessagesFromQueueAsync(batchSize);
        await SendMessagesAsync(cancellationToken, messages);
    }

    private async Task<IImmutableList<string>> GetMessagesFromQueueAsync(int amount)
    {
        var logsBatch = await _logsQueueAccessor.DequeueSeveralAsync(amount);
        var events = logsBatch
            .Where(LogFilteringPredicate)
            .Select(LogEntry.MapFrom)
            .ToList();

        if (events.Count == 0)
        {
            return ImmutableArray<string>.Empty;
        }

        return _messageFormatter
            .Format(events, _sinkConfiguration.FormatterConfiguration)
            .ToImmutableList();

        bool LogFilteringPredicate(LogEvent log) =>
            _logFilterManager == null || _logFilterManager.ApplyFilter(log);
    }

    private async Task SendMessagesAsync(CancellationToken cancellationToken, IImmutableList<string> messages)
    {
        if (!messages.Any())
        {
            return;
        }

        foreach (var message in messages)
        {
            await _logWriter.Write(message, cancellationToken);
        }
    }

    private async Task FlushAsync()
    {
        var batchSize = _sinkConfiguration.BatchPostingLimit;
        var requiredBatches = Math.Floor(_logsQueueAccessor.GetSize() / (double)batchSize);

        while (requiredBatches > 0)
        {
            await EmitBatchInternalAsync(batchSize, CancellationToken.None);
            requiredBatches--;
        }
    }
}