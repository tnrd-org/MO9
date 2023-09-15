namespace MO9.Options;

public class DiscordOptions
{
    public const string SECTION = "Discord";

    public string Token { get; set; } = null!;
    public ulong GuildId { get; set; }
    public ulong ForumId { get; set; }
}
