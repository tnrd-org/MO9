using System.Collections;
using System.Text;
using Remora.Commands.Attributes;
using Remora.Commands.Groups;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Commands.Feedback.Services;
using Remora.Discord.Extensions.Embeds;
using Remora.Results;
using Serilog;

namespace MO9.Commands;

public class Commands : CommandGroup
{
    private readonly FeedbackService feedbackService;
    private readonly ICommandContext commandContext;
    private readonly IInteractionContext interactionContext;
    private readonly HttpClient httpClient;

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

    public Commands(
        FeedbackService feedbackService,
        ICommandContext commandContext,
        IInteractionContext interactionContext,
        HttpClient httpClient
    )
    {
        this.feedbackService = feedbackService;
        this.commandContext = commandContext;
        this.interactionContext = interactionContext;
        this.httpClient = httpClient;
    }

    [Command("parse")]
    public async Task<IResult> Parse(IPartialAttachment attachment)
    {
        if (!attachment.Filename.HasValue)
        {
            await feedbackService.SendContextualWarningAsync("Attachment has no filename.");
            return Result.FromSuccess();
        }

        string filename = attachment.Filename.Value;
        if (!filename.EndsWith(".log"))
        {
            await feedbackService.SendContextualWarningAsync("Attachment is not a .log file.");
            return Result.FromSuccess();
        }

        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, attachment.Url.Value);
        HttpResponseMessage response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            await feedbackService.SendContextualErrorAsync("Unable to download attachment.");
            return Result.FromSuccess();
        }

        string content;

        try
        {
            content = await response.Content.ReadAsStringAsync();
        }
        catch (Exception)
        {
            await feedbackService.SendContextualErrorAsync("Unable to read attachment.");
            return Result.FromSuccess();
        }

        if (string.IsNullOrEmpty(content))
        {
            await feedbackService.SendContextualErrorAsync("Attachment is empty.");
            return Result.FromSuccess();
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
            await feedbackService.SendContextualWarningAsync("Attachment has no line breaks.");
            return Result.FromSuccess();
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
            Log.Error("Unable to create embed: {Result}", result.Error.ToString());
        }

        await feedbackService.SendContextualEmbedAsync(result.Entity);

        await SendErrors(errors);

        return Result.FromSuccess();
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
            embedBuilder.AddField(":triangular_flag_on_post: Exceptions", $"Found {errors.Count} exceptions, see following messages");
        else
            embedBuilder.AddField(":triangular_flag_on_post: Exceptions", "No exceptions found");
    }

    private async Task SendErrors(IReadOnlyList<string> errors)
    {
        for (int i = 0; i < errors.Count; i++)
        {
            string error = errors[i];
            if (error.Length > 2000)
            {
                await SendSegmentedErrors(i + 1, error);
            }
            else
            {
                await feedbackService.SendContextualAsync($"Exception {(i + 1)}\n```{error}```");
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
            await feedbackService.SendContextualAsync($"Exception {errorNumber} ({i + 1}/{chunks.Count})\n```{chunk}```");
        }
    }
}
