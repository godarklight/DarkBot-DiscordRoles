using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;

namespace DarkBot.DiscordRoles
{
    public class DiscordRolesCommand : ModuleBase<SocketCommandContext>
    {
        public DiscordRoles DiscordRoles { get; set; }

        [Command("setrole")]
        [RequireContext(ContextType.Guild, ErrorMessage = "Sorry, this command must be ran from within a server, not a DM!")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SetRole()
        {
            DiscordRoles.SetRoleChannel(Context.Guild.Id, Context.Channel.Id);
            var newMessage = await ReplyAsync("Role channel set");
            await Context.Message.DeleteAsync();
            await Task.Delay(5000);
            await newMessage.DeleteAsync();
        }
    }
}