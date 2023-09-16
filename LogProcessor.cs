using System.Collections;
using System.Text;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Extensions.Embeds;
using Remora.Rest.Core;
using Remora.Results;

namespace MO9;

public class LogProcessor
{
    private static readonly string[] outdatedMods = new[]
    {
        "net.tnrd.zeepkist.utilities",
        "com.metalted.zeepkist.blueprints",
        "com.metalted.zeepkist.dragselect",
        "com.metalted.zeepkist.hotbar",
        "com.metalted.zeepkist.selectioncountergui",
        "com.metalted.zeepkist.notooltip",
        "com.metalted.zeepkist.uiinjector",
        "UIInjector",
        "Hotbar",
        "Selection Counter GUI",
        "BlueprintsPlus",
        "Blueprints+",
        "Blueprints",
        "Level Editor Drag Select",
        "No Tooltip",
        "RecordsMod",
    };

    private readonly IDiscordRestChannelAPI channelApi;
    private readonly Snowflake channelId;
    private readonly IEnumerable<IAttachment> attachments;
    private readonly ILogger logger;
    private readonly HttpClient httpClient;
    private readonly Snowflake authorSnowflake;
    private readonly MessageReference messageReference;

    public LogProcessor(
        IDiscordRestChannelAPI channelApi,
        Snowflake channelId,
        IEnumerable<IAttachment> attachments,
        ILogger logger,
        HttpClient httpClient,
        Snowflake authorSnowflake,
        MessageReference messageReference
    )
    {
        this.channelApi = channelApi;
        this.channelId = channelId;
        this.attachments = attachments;
        this.logger = logger;
        this.httpClient = httpClient;
        this.authorSnowflake = authorSnowflake;
        this.messageReference = messageReference;
    }

    public async Task Process(CancellationToken ct)
    {
        IAttachment? attachment = GetAttachment();
        if (attachment == null)
            return;

        string? content = await GetAttachmentContent(attachment);

        if (string.IsNullOrEmpty(content))
        {
            // Should probably write a message that the log is invalid
            return;
        }

        string[] lines;

        if (content.Contains("\r\n"))
        {
            lines = content.Split("\r\n");
        }
        else if (content.Contains('\n'))
        {
            lines = content.Split("\n");
        }
        else
        {
            // TODO: Log invalid log file
            return;
        }

        bool hasMods = ProcessLog(lines,
            out List<string> mods,
            out List<string> errors,
            out List<string> existingOutdatedMods);

        EmbedBuilder embedBuilder = new EmbedBuilder()
            .WithTitle("Parse results")
            .WithAuthor("MO9");

        AddModsField(embedBuilder, hasMods, mods, existingOutdatedMods);
        AddOutdatedModsField(embedBuilder, existingOutdatedMods);
        AddErrorsField(embedBuilder, errors);

        Result<Embed> result = embedBuilder.Build();

        if (!result.IsSuccess)
        {
            logger.LogError("Unable to create embed: {Result}", result.Error.ToString());
        }

        await channelApi.CreateMessageAsync(channelId,
            embeds: new List<IEmbed>
            {
                result.Entity
            },
            messageReference: messageReference,
            ct: ct);

        await SendErrors(errors);

        if (hasMods)
        {
            string message = "<@" + authorSnowflake.Value +
                             "> you seem to have mods installed. It is essential that you remove these mods before reporting bugs.\n" +
                             "Mods can introduce unexpected behaviour and interfere with the inner workings of the game resulting in bugs that aren't caused by the game itself.\n\n" +
                             "Please remove all your mods and try to reproduce the bug.\n" +
                             "The easiest way to remove your mods is by renaming the `BepInEx` folder in your game directory to anything else.";
            await channelApi.CreateMessageAsync(channelId,
                message,
                messageReference: messageReference,
                ct: ct);
        }
    }

    private IAttachment? GetAttachment()
    {
        foreach (IAttachment attachment in attachments)
        {
            if (string.IsNullOrEmpty(attachment.Filename))
                continue;

            if (!attachment.Filename.StartsWith("Player"))
                continue;

            if (!attachment.Filename.EndsWith(".log"))
                continue;

            return attachment;
        }

        return null;
    }

    private async Task<string?> GetAttachmentContent(IAttachment attachment)
    {
        HttpRequestMessage request = new(HttpMethod.Get, attachment.Url);
        HttpResponseMessage response = await httpClient.SendAsync(request);

        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unable to download attachment");
            return null;
        }

        string content;

        try
        {
            content = await response.Content.ReadAsStringAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unable to read attachment");
            return null;
        }

        return content;
    }

    private static bool ProcessLog(
        IEnumerable<string> lines,
        out List<string> mods,
        out List<string> errors,
        out List<string> existingOutdatedMods
    )
    {
        bool hasMods = false;
        mods = new List<string>();
        errors = new List<string>();

        bool isReadingErrors = false;
        StringBuilder errorReader = new();

        foreach (string line in lines)
        {
            string trimmed = line.Trim();

            if (trimmed.Contains("BepInEx]"))
            {
                hasMods = true;
            }

            if (trimmed.Contains("BepInEx] Loading ["))
            {
                string mod = trimmed.Split("BepInEx] Loading [")[1].Split(']')[0];
                mods.Add(mod);
            }

            if (isReadingErrors)
            {
                if (!line.StartsWith(' ') || string.IsNullOrEmpty(line))
                {
                    errors.Add(errorReader.ToString());
                    isReadingErrors = false;
                }
            }

            if (trimmed.Contains("Exception:"))
            {
                isReadingErrors = true;
            }

            if (!isReadingErrors)
                continue;

            errorReader.AppendLine(trimmed);
        }

        if (isReadingErrors)
        {
            errors.Add(errorReader.ToString());
        }

        existingOutdatedMods = mods.Where(x => outdatedMods.Any(x.Contains)).ToList();
        return hasMods;
    }

    private static void AddModsField(
        EmbedBuilder embedBuilder,
        bool hasMods,
        IEnumerable<string> mods,
        IEnumerable<string> existingOutdatedMods
    )
    {
        StringBuilder modsBuilder = new();
        if (hasMods)
        {
            foreach (string mod in mods.Except(existingOutdatedMods).OrderBy(x => x))
            {
                modsBuilder.AppendLine("- " + mod);
            }
        }
        else
        {
            modsBuilder.AppendLine("No mods found");
        }

        embedBuilder.AddField(":tools: Installed mods", modsBuilder.ToString());
    }

    private static void AddOutdatedModsField(
        EmbedBuilder embedBuilder,
        IReadOnlyCollection<string> existingOutdatedMods
    )
    {
        StringBuilder outdatedModsBuilder = new();

        if (outdatedMods.Length > 0)
        {
            if (existingOutdatedMods.Count > 0)
            {
                foreach (string mod in existingOutdatedMods.OrderBy(x => x))
                {
                    outdatedModsBuilder.AppendLine("- " + mod);
                }
            }
            else
            {
                outdatedModsBuilder.AppendLine("No incompatible mods found");
            }
        }

        embedBuilder.AddField(":warning: Incompatible mods (remove these!)", outdatedModsBuilder.ToString());
    }

    private static void AddErrorsField(EmbedBuilder embedBuilder, ICollection errors)
    {
        if (errors.Count > 0)
            embedBuilder.AddField(":triangular_flag_on_post: Exceptions",
                $"Found {errors.Count} exceptions, see following messages");
        else
            embedBuilder.AddField(":triangular_flag_on_post: Exceptions", "No exceptions found");
    }

    private async Task SendErrors(IReadOnlyList<string> errors)
    {
        for (int i = 0; i < Math.Min(5, errors.Count); i++)
        {
            string error = errors[i];
            if (error.Length > 2000)
            {
                await SendSegmentedErrors(i + 1, error);
            }
            else
            {
                await channelApi.CreateMessageAsync(channelId,
                    content: $"Exception {(i + 1)}\n```{error}```",
                    messageReference: messageReference);
            }
        }
    }

    private async Task SendSegmentedErrors(int errorNumber, string error)
    {
        string[] errorLines = error.Split(Environment.NewLine);

        StringBuilder errorBuilder = new();

        List<string> chunks = new();

        foreach (string errorLine in errorLines)
        {
            if (errorBuilder.Length + errorLine.Length < 2000)
            {
                errorBuilder.AppendLine(errorLine.Trim());
            }
            else
            {
                chunks.Add(errorBuilder.ToString());
                errorBuilder.Clear();
            }
        }

        if (errorBuilder.Length > 0)
        {
            chunks.Add(errorBuilder.ToString());
        }

        for (int i = 0; i < chunks.Count; i++)
        {
            string chunk = chunks[i];

            await channelApi.CreateMessageAsync(channelId,
                content: $"Exception {errorNumber} ({i + 1}/{chunks.Count})\n```{chunk}```",
                messageReference: messageReference);
        }
    }
}
