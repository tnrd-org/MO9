using Microsoft.Extensions.Options;
using MO9.Options;
using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Gateway.Responders;
using Remora.Rest.Core;
using Remora.Results;

namespace MO9.Responders;

public class ThreadCreateResponder : IResponder<IThreadCreate>
{
    private readonly IDiscordRestChannelAPI channelApi;
    private readonly ThreadRepository threadRepository;
    private readonly DiscordOptions options;
    private readonly HttpClient httpClient;
    private readonly ILogger<ThreadCreateResponder> logger;

    public ThreadCreateResponder(
        IDiscordRestChannelAPI channelApi,
        ThreadRepository threadRepository,
        IOptions<DiscordOptions> options,
        HttpClient httpClient,
        ILogger<ThreadCreateResponder> logger
    )
    {
        this.channelApi = channelApi;
        this.threadRepository = threadRepository;
        this.httpClient = httpClient;
        this.logger = logger;
        this.options = options.Value;
    }

    public async Task<Result> RespondAsync(IThreadCreate gatewayEvent, CancellationToken ct = new())
    {
        if (!IsValidThread(gatewayEvent))
            return Result.FromSuccess();

        Snowflake threadId = gatewayEvent.ID;

        if (threadRepository.HasThread(threadId))
            return Result.FromSuccess();

        threadRepository.AddThread(threadId);

        Result<IMessage> starterMessage = await channelApi.GetChannelMessageAsync(threadId, threadId, ct);

        if (!starterMessage.IsSuccess)
        {
            // Log an error
            return Result.FromSuccess();
        }

        IMessage message = starterMessage.Entity;

        if (message.Attachments.Count == 0)
        {
            await SendMissingLog(threadId, ct);
            goto END;
        }

        IAttachment? playerLogAttachment = GetPlayerLogAttachment(message);

        if (playerLogAttachment == null)
        {
            await SendMissingLog(threadId, ct);
            goto END;
        }

        MessageReference messageReference = new(threadId, threadId, gatewayEvent.GuildID);

        LogProcessor logProcessor = new(channelApi,
            threadId,
            message.Attachments,
            logger,
            httpClient,
            message.Author.ID,
            messageReference);

        await logProcessor.Process(ct);

        END:
        threadRepository.MarkThreadProcessed(threadId);
        return Result.FromSuccess();
    }

    private bool IsValidThread(IThreadCreate gatewayEvent)
    {
        if (!gatewayEvent.ParentID.HasValue)
            return false;

        Snowflake parentId = gatewayEvent.ParentID.Value!.Value;

        return parentId == options.ForumId;
    }

    private static IAttachment? GetPlayerLogAttachment(IMessage message)
    {
        IAttachment? playerLogAttachment = null;

        foreach (IAttachment attachment in message.Attachments)
        {
            if (string.IsNullOrEmpty(attachment.Filename))
                continue;

            if (!attachment.Filename.StartsWith("player", StringComparison.InvariantCultureIgnoreCase))
                continue;

            if (!attachment.Filename.EndsWith(".log", StringComparison.InvariantCultureIgnoreCase))
                continue;

            playerLogAttachment = attachment;
            break;
        }

        return playerLogAttachment;
    }

    private async Task SendMissingLog(Snowflake channelId, CancellationToken ct)
    {
        await channelApi.CreateMessageAsync(channelId,
            "Please attach a valid `Player.log` file to this thread.\n" +
            @"This can be found at `%localappdata%low\SteelPan Interactive\Zeepkist`",
            ct: ct);
    }
}
