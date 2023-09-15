using Microsoft.Extensions.Options;
using MO9.Options;
using MO9.Responders;
using Remora.Discord.API.Abstractions.Gateway.Commands;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Gateway;
using Remora.Discord.Gateway.Extensions;
using Remora.Discord.Hosting.Extensions;
using Remora.Rest.Core;
using Remora.Results;
using Serilog;

namespace MO9;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        IHost host = Host.CreateDefaultBuilder(args)
            .UseConsoleLifetime()
            .UseSerilog((context, services, configuration) =>
            {
                IOptions<SeqOptions> seqOptions = services.GetRequiredService<IOptions<SeqOptions>>();

                configuration
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("Source", "MO9")
                    .MinimumLevel.Debug()
                    .WriteTo.Seq(seqOptions.Value.Url, apiKey: seqOptions.Value.Key)
                    .WriteTo.Console();
            })
            .ConfigureServices((context, services) =>
            {
                services.Configure<SeqOptions>(context.Configuration.GetSection(SeqOptions.SECTION));
                services.Configure<DiscordOptions>(context.Configuration.GetSection(DiscordOptions.SECTION));
                services.Configure<DiscordGatewayClientOptions>(g =>
                    g.Intents |= GatewayIntents.Guilds | GatewayIntents.MessageContents);
                services.AddSingleton<ThreadRepository>();
                services.AddDiscordService(GetDiscordToken);
                services.AddResponder<MessageCreateResponder>();
                services.AddResponder<ThreadCreateResponder>();
            })
            .Build();

        await LoadThreads(host.Services);
        await host.RunAsync();
    }

    private static async Task LoadThreads(IServiceProvider provider)
    {
        ThreadRepository threadRepository = provider.GetRequiredService<ThreadRepository>();
        DiscordOptions discordOptions = provider.GetRequiredService<IOptions<DiscordOptions>>().Value;
        IDiscordRestGuildAPI guildApi = provider.GetRequiredService<IDiscordRestGuildAPI>();
        Result<IGuildThreadQueryResponse> threadsResult =
            await guildApi.ListActiveGuildThreadsAsync(new Snowflake(discordOptions.GuildId));

        if (!threadsResult.IsSuccess)
        {
            Log.Error("Failed to get guild threads: {Error}", threadsResult.Error.ToString());
            return;
        }

        Snowflake forumFlake = new(discordOptions.ForumId);

        foreach (IChannel channel in threadsResult.Entity.Threads)
        {
            if (!channel.ParentID.HasValue)
                continue;
            if (channel.ParentID != forumFlake)
                continue;

            threadRepository.AddThread(channel.ID);
            threadRepository.MarkThreadProcessed(channel.ID);
        }
    }

    private static string GetDiscordToken(IServiceProvider serviceProvider)
    {
        IOptions<DiscordOptions> discordOptions = serviceProvider.GetRequiredService<IOptions<DiscordOptions>>();
        return discordOptions.Value.Token;
    }
}
