# Botifex
Dual-platform bot library for Discord & Telegram

I have a few different ideas for apps based in messenger bots, with use cases on both Discord and Telegram. The purpose of this library is to build up a system that can be used by all my other projects. Each project will require somewhat different functionality, so maybe in the end this library will be very full-featured or maybe it will be a very limited Frankenstein. Basically I don't know exactly what this is going to end up being, but it will be useful to me in any case!


## Config Setup
The program using this library will need to use the JSON configuration file system supplied by Microsoft and have the following sections in the configuration:
 ```
     "Discord": {
        "DiscordBotToken": "",
        "DiscordLogChannel": 0,
        "DiscordStatusChannel": 0,
        "DiscordStatusChannelInvite": "",
        "DiscordAdminAllowlist": [
            ""
        ]
    },
    "Telegram": {
        "TelegramBotToken": "",
        "TelegramLogChannel": 0,
        "TelegramStatusChannel": 0,
        "TelegramStatusChannelInvite": "",
        "TelegramAdminAllowlist": [
            ""
        ]
    }
 ```

For both services:
XBotToken = The string required to connect to your bot
XLogChannel = The numerical ID of the channel or group the logs should go to
XStatusChannel = The numerical ID of the channel or group continual status updates for your users should go to (if your app uses these)
XStatusChannelInvite = An invite link to the status channel, which your app can give out if needed
XAdminAllowList = An array of usernames that will always be considered an admin (and currently, these are the only users that will be considered admins regardless of whether or not they are assigned as admins in any channel)


## Enabling in your Application
This is designed to be used through a hosted service with dependency injection. While building the host, use AddBotifexServices when you are configuring the services to register Botifex classes with the host. The Discord and Telegram services will start automatically using the information in the configuration file.


## Registering Slash Commands
Any slash commands your app needs should be registered immediately in your app's constructor since any commands that exist are currently only registered once during execution, right after the messenger bots signal that they are ready.

```
botifex.AddCommand(new SlashCommand(adminOnly: true/false)
{
    Name = "",
    Description = "",
    Options = new List<CommandField> 
    {
        new CommandField
        {
            Name = "",
            Description = "",
            Required = true/false
        }
    }
});
```
For a SlashCommand, Name and Description are required but Options are optional. Without Options, it will be a simple command with no arguments.

With one or more CommandFields in the Options list, commands will have fields in Discord like /command field1:data field2:data etc, and Discord handles whether they are required or not.

In Telegram, if there are fields, if you type /command lorem ipsum dolor, all the text after the command will be assumed to belong to the first field. Without any text, or if there is more than one field, it will ask the user "What is _____?" for each field, with the blank filled in by the CommandField's Description. So make sure the Description reads well with "what is" before it. I haven't implemented optional fields for Telegram yet.


## Registering Events
### RegisterTextHandler(EventHandler<InteractionReceivedEventArgs> handler)
Register a handler for when the bot reads text from a user. The EventArgs have a property Interaction that provides the Interaction object, which has all the information about what's going on.

### RegisterCommandHandler(EventHandler<InteractionReceivedEventArgs> handler)
Register a handler for when a user uses a slash command with the bot. The EventArgs have a property Interaction that provides the Interaction object, which has all the information about what's going on.

### RegisterReadyHandler(EventHandler<EventArgs> handler)
Register a handler for when a messenger is ready. For instance, after the Discord bot is ready to go, it will trigger this event. Then if the Telegram bot is ready to go after that, it will also trigger this event. So it should be used for general things that need to be done to all messengers when they're ready, but individually. There are no EventArgs for this right now (passing the Messenger object that is now ready would make some sense in the future)

### RegisterUserUpdateHandler(EventHandler<UserUpdateEventArgs> handler)
Register a hander for when a user's display name or user name is updated. The EventArgs contain a User property that contains the BotifexUser object for this user.


## Handling Commands
If you receive a command from a user, the Interaction object will implement ICommandInteraction and have some useful fields:

* BotifexCommand - The SlashCommand object that defines the command that was used
* CommandFields - A Dictionary<string,string> where the Key string is the name of the command field and the Value string is what the user specified for that field

Using these your app can determine how to respond.


## Responding to Interactions
All interactions have the following ways to reply:

### async Reply(string text)
Send a text reply to the user's message or command

### async ReplyWithOptions(ReplyMenu menu, string? text=null)
Send a menu of options in reply to the user's message or command. If a text string is supplied, this will be appended before the menu.

To set up a reply menu object:
```
Dictionary<string, string> options = new Dictionary<string, string>
{
    { "ABC", "Description 1" },
    { "123", "Description 2" }
};

ReplyMenu menu = new ReplyMenu("name", options, CallbackMethod);

menu.NumberedChoices = true/false;
```
In the options Dictionary, the Key string should be some unique ID related to the option such as a database ID or unique emojis to display to the user if the options aren't tied to an object. If you're using database IDs, you can set NumberedChoices to true to replace the IDs with 1 2 3 etc to be more user friendly.

The CallbackMethod will contain the code to process the reply. It should be defined like an event handler, with (object? sender, MenuReplyReceivedEventArgs e) parameters.

MenuReplyReceivedEventArgs have the following properties:
* Reply - The text the user sent back, which ideally will be one of the menu options specified as a Key in the dictionary above
* Interaction - The Interaction object all this is related to

With the Reply string, you'll be able to process based on what the user specified, and then send a Reply to the interaction based on that.

### async End()
Signal that this interaction has been completed.


## Sending Messages Outside of Interactions
### async LogAll(string message)
Send a message to all the logs: the console (if enabled in your app), disk (if output is redirected to a file), the Discord Log channel (if specified in configuration), and the Telegram Log chat/group/channel (if specified in configuration)

### async SendStatusUpdate(string message)
Create or update the status message in the status channel, if one has been specified in configuration

### async SendOneTimeStatusUpdate(string message, bool notification = false)
Send a fresh message to the status channel which won't replace the continually updating status message and won't be updated itself. Setting notification to true will find a way to ping users in each messenger app -- @here in Discord, pinning and unpinning in Telegram

### async ReplaceStatusMessage(string newMessage)
This will replace the continually updating status message with the text in newMessage, and then repost the continually updating status message after that

### GetUser(IMessengerUser messengerAccount)
Find the BotifexUser associated with a particular IMessengerUser messenger account object

### async SendToUser(BotifexUser? user, string message)
Send a message as a DM to a particular BotifexUser



