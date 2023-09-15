using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Gateway.Responders;
using Remora.Results;

namespace MO9.Responders;

public class MessageCreateResponder : IResponder<IMessageCreate>
{
    private readonly ThreadRepository threadRepository;
    private readonly IDiscordRestChannelAPI channelApi;
    private readonly HttpClient httpClient;
    private readonly ILogger<MessageCreateResponder> logger;

    public MessageCreateResponder(
        ThreadRepository threadRepository,
        IDiscordRestChannelAPI channelApi,
        HttpClient httpClient,
        ILogger<MessageCreateResponder> logger
    )
    {
        this.threadRepository = threadRepository;
        this.channelApi = channelApi;
        this.httpClient = httpClient;
        this.logger = logger;
    }

    public async Task<Result> RespondAsync(IMessageCreate gatewayEvent, CancellationToken ct = new())
    {
        if (!threadRepository.HasProcessedThread(gatewayEvent.ChannelID))
            return Result.FromSuccess();

        Result<IChannel> channelResult = await channelApi.GetChannelAsync(gatewayEvent.ChannelID, ct);
        if (!channelResult.IsSuccess)
        {
            // TODO: Log this
            return Result.FromSuccess();
        }

        if (!channelResult.Entity.OwnerID.HasValue)
        {
            // TODO: Log
            return Result.FromSuccess();
        }

        if (channelResult.Entity.OwnerID.Value != gatewayEvent.Author.ID)
            return Result.FromSuccess();


        LogProcessor logProcessor = new(channelApi,
                gatewayEvent.ChannelID,
                gatewayEvent.Attachments,
                logger,
                httpClient,
                gatewayEvent.Author.ID);
        await logProcessor.Process(ct);

        return Result.FromSuccess();
    }
}
