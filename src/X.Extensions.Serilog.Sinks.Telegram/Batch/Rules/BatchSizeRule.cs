﻿using System.Threading;
using X.Extensions.Serilog.Sinks.Telegram.Batch.Contracts;
using X.Extensions.Serilog.Sinks.Telegram.Configuration;

namespace X.Extensions.Serilog.Sinks.Telegram.Batch.Rules;

/// <summary>
/// Emit logs batch when in the logs queue is not less than <see cref="TelegramSinkConfiguration.BatchPostingLimit"/> logs in queue.
/// </summary>
public class BatchSizeRule : IRule
{
    private readonly ILogsQueueAccessor _accessContext;
    private readonly int _batchSize;

    public BatchSizeRule(ILogsQueueAccessor accessContext, int batchSize)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentException("Invalid batch size! It must be greater than 0!");
        }

        _batchSize = batchSize;
        _accessContext = accessContext;
    }

    public bool IsPassed()
    {
        return _accessContext.GetSize() >= _batchSize;
    }
}