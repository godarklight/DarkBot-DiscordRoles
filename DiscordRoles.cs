using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using DarkBot;

namespace DarkBot.DiscordRoles
{
    public class DiscordRoles : BotModule
    {
        private DiscordSocketClient _client;
        private Dictionary<ulong, ulong> rolesChannels = new Dictionary<ulong, ulong>();
        private Dictionary<ulong, Dictionary<string, string>> roles = new Dictionary<ulong, Dictionary<string, string>>();

        public async Task Initialize(IServiceProvider sc)
        {
            LoadRolesChannels();
            _client = (DiscordSocketClient)sc.GetService(typeof(DiscordSocketClient));
            _client.Ready += ReadyAsync;
            _client.MessageReceived += MessageReceivedAsync;
            _client.MessageUpdated += MessageUpdatedAsync;
            _client.ReactionAdded += ReactionAdded;
            _client.ReactionRemoved += ReactionRemoved;
            await Task.CompletedTask;
        }

        private void LoadRolesChannels()
        {
            lock (rolesChannels)
            {
                string rolesText = DataStore.Load("Roles");
                rolesChannels.Clear();
                if (rolesText != null)
                {
                    using (StringReader sr = new StringReader(rolesText))
                    {
                        string currentLine = null;
                        while ((currentLine = sr.ReadLine()) != null)
                        {
                            int splitIndex = currentLine.IndexOf("=");
                            if (splitIndex > -1)
                            {
                                if (ulong.TryParse(currentLine.Substring(0, splitIndex), out ulong serverID))
                                {
                                    if (ulong.TryParse(currentLine.Substring(splitIndex + 1), out ulong channelID))
                                    {
                                        rolesChannels.Add(serverID, channelID);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private void SaveRolesChannels()
        {
            lock (rolesChannels)
            {
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<ulong, ulong> kvp in rolesChannels)
                {
                    sb.AppendLine($"{kvp.Key}={kvp.Value}");
                }
                DataStore.Save("Roles", sb.ToString());
            }
        }

        public void SetRoleChannel(ulong serverID, ulong channelID)
        {
            lock (rolesChannels)
            {
                rolesChannels[serverID] = channelID;
            }
            SaveRolesChannels();
        }

        public async Task ReactionAdded(Cacheable<IUserMessage, ulong> cacheable, ISocketMessageChannel msg, SocketReaction reaction)
        {
            await ReactionChange(reaction, true);
        }

        public async Task ReactionRemoved(Cacheable<IUserMessage, ulong> cacheable, ISocketMessageChannel msg, SocketReaction reaction)
        {
            await ReactionChange(reaction, false);
        }

        public async Task ReactionChange(SocketReaction reaction, bool isAdd)
        {
            SocketGuildChannel guildChannel = reaction.Channel as SocketGuildChannel;
            //Bots shouldn't react to itself, should only react to roles emotes, and only to users.
            if (guildChannel == null || !roles.ContainsKey(reaction.MessageId) || reaction.UserId == _client.CurrentUser.Id || !reaction.User.IsSpecified)
            {
                return;
            }

            if (!rolesChannels.ContainsKey(guildChannel.Guild.Id))
            {
                return;
            }

            if (guildChannel.Id != rolesChannels[guildChannel.Guild.Id])
            {
                return;
            }

            IEmote emote = reaction.Emote;
            IGuildUser user = reaction.User.Value as IGuildUser;
            if (user != null && roles[reaction.MessageId].ContainsKey(emote.Name))
            {
                string roleString = roles[reaction.MessageId][emote.Name];
                foreach (var role in guildChannel.Guild.Roles)
                {
                    bool hasRole = false;
                    if (role.Mention == roleString)
                    {
                        foreach (ulong testID in user.RoleIds)
                        {
                            if (testID == role.Id)
                            {
                                hasRole = true;
                            }
                        }
                        if (isAdd)
                        {
                            if (!hasRole)
                            {
                                await user.AddRoleAsync(role);
                                Console.WriteLine($"{user.Username} (ID: {user.Id}) added to role {role.Name} ID: ({role.Id})");
                            }
                            else
                            {
                                Console.WriteLine($"{user.Username} (ID: {user.Id}) already has role {role.Name} ID: ({role.Id})");
                            }
                        }
                        if (!isAdd)
                        {
                            if (hasRole)
                            {
                                await user.RemoveRoleAsync(role);
                                Console.WriteLine($"{user.Username} (ID: {user.Id}) removed from role {role.Name} ID: ({role.Id})");
                            }
                            else
                            {
                                Console.WriteLine($"{user.Username} (ID: {user.Id}) does not have role {role.Name} ID: ({role.Id})");
                            }
                        }
                    }
                }
            }
        }

        // The Ready event indicates that the client has opened a
        // connection and it is now safe to access the cache.
        private async Task ReadyAsync()
        {
            await LoadRoles();
        }

        private async Task LoadRoles()
        {
            roles.Clear();

            foreach (var server in _client.Guilds)
            {
                if (!rolesChannels.ContainsKey(server.Id))
                {
                    continue;
                }
                foreach (var channel in server.Channels)
                {
                    var textChannel = channel as SocketTextChannel;
                    if (textChannel != null && textChannel.Id == rolesChannels[server.Id])
                    {
                        var messages = textChannel.GetMessagesAsync();
                        await foreach (var message in messages)
                        {
                            foreach (var msg in message)
                            {
                                List<string> emojisToCheck = new List<string>();
                                using (StringReader sr = new StringReader(msg.Content))
                                {
                                    string currentLine = null;
                                    while ((currentLine = sr.ReadLine()) != null)
                                    {
                                        //VALID LINE FORMAT <emoji> <@!role> - Ignored text
                                        //Get rid of any leading spaces
                                        currentLine = currentLine.TrimStart();
                                        if (!currentLine.Contains(" ") || !currentLine.Contains("<@&") || !currentLine.Contains(">"))
                                        {
                                            continue;
                                        }

                                        //Split the message emoji and role parts
                                        int firstSpace = currentLine.IndexOf(" ");
                                        int groupStart = currentLine.IndexOf("<@&");
                                        int group = currentLine.IndexOf(">", groupStart + 1);

                                        string emoji = currentLine.Substring(0, firstSpace);
                                        string role = currentLine.Substring(groupStart, group - groupStart + 1);
                                        if (!roles.ContainsKey(msg.Id))
                                        {
                                            roles.Add(msg.Id, new Dictionary<string, string>());
                                        }
                                        if (!roles[msg.Id].ContainsKey(emoji))
                                        {
                                            Console.WriteLine($"{emoji} => {role} on server '{server.Name}' on message {msg.Id}");
                                            string clippedName = GetClippedName(emoji);
                                            roles[msg.Id].Add(clippedName, role);
                                            emojisToCheck.Add(emoji);

                                        }
                                    }
                                }
                                try
                                {
                                    await DeleteReactionIfExisting(msg);
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine($"Error caught: {e}");
                                }

                                foreach (string emoji in emojisToCheck)
                                {
                                    try
                                    {
                                        await AddReactionIfMissing(msg, emoji);
                                    }
                                    catch (Exception e)
                                    {
                                        Console.WriteLine($"Error caught: {e}");
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private string GetClippedName(string name)
        {
            if (name.Contains(":"))
            {
                int startColon = name.IndexOf(":");
                int endColon = name.LastIndexOf(":");
                return name.Substring(startColon + 1, endColon - startColon - 1);
            }
            return name;
        }

        private async Task AddReactionIfMissing(IMessage msg, string emoji)
        {
            foreach (var existingEmoji in msg.Reactions.Keys)
            {
                if (existingEmoji.Name == emoji)
                {
                    return;
                }
            }
            IEmote e = null;
            if (!emoji.Contains(":"))
            {
                e = new Emoji(emoji);
            }
            else
            {
                if (Emote.TryParse(emoji, out Emote e2))
                {
                    e = e2;
                }
                else
                {
                    Console.WriteLine("Cannot add reaction " + emoji);
                    return;
                }
            }
            if (e != null)
            {
                await msg.AddReactionAsync(e);
            }
        }

        private async Task DeleteReactionIfExisting(IMessage msg)
        {
            if (!roles.ContainsKey(msg.Id))
            {
                return;
            }

            List<Emoji> clearList = new List<Emoji>();
            foreach (var existingEmoji in msg.Reactions.Keys)
            {
                if (!roles[msg.Id].ContainsKey(GetClippedName(existingEmoji.Name)))
                {
                    IEnumerable<IUser> users = await msg.GetReactionUsersAsync(existingEmoji, Int32.MaxValue).FlattenAsync();
                    foreach (IUser user in users)
                    {
                        Console.WriteLine($"Removing  {existingEmoji} by {user}");
                        await msg.RemoveReactionAsync(existingEmoji, user);
                    }
                }
            }
        }

        private async Task MessageReceivedAsync(SocketMessage message)
        {
            SocketGuildChannel guildChannel = message.Channel as SocketGuildChannel;
            // The bot should never respond to itself, and only respond to roles channels
            if (message.Author.Id == _client.CurrentUser.Id || guildChannel == null || !rolesChannels.ContainsKey(guildChannel.Guild.Id) || message.Channel.Id != rolesChannels[guildChannel.Guild.Id])
                return;

            Console.WriteLine("Reloading Roles from new message");
            await LoadRoles();
        }

        private async Task MessageUpdatedAsync(Cacheable<IMessage, ulong> cacheable, SocketMessage message, ISocketMessageChannel channel)
        {
            SocketGuildChannel guildChannel = message.Channel as SocketGuildChannel;
            // The bot should never respond to itself, and only respond to roles channels
            if (message.Author.Id == _client.CurrentUser.Id || guildChannel == null || !rolesChannels.ContainsKey(guildChannel.Guild.Id) || message.Channel.Id != rolesChannels[guildChannel.Guild.Id])
                return;

            Console.WriteLine("Reloading Roles from updated message");
            await LoadRoles();
        }

        public async Task SaySomething(string message, ulong serverID, ulong channelID)
        {
            if (message.Length > 1950)
            {
                message = message.Substring(0, 1950) + " (truncated)";
            }
            await _client.GetGuild(serverID).GetTextChannel(channelID).SendMessageAsync(message);
        }
    }
}
