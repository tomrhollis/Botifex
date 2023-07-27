using System.Collections.Concurrent;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Botifex.Services.TelegramBot
{
    /// <summary>
    /// Represents a Telegram channel. Everything that needs to go to a Telegram DM, group, supergroup, or channel should go through one of these objects.
    /// This enforces the strict rate limits Telegram has on interactions with specific channels.
    /// </summary>
    internal class Channel
    {
        private static readonly int API_LIMIT_ACTIONS = 20; // telegram will get mad if you send more than 20 messages a minute
        private static readonly int API_LIMIT_SECONDS = 60; // it's unclear if that's just messages or any interaction with a channel
                                                            // erring on the safe side and queueing ALL interactions with a channel

        private Queue<DateTime> interactionRecord = new Queue<DateTime>(); // keep a record of the interactions with this channel over the past API_LIMIT_SECONDS
                                                                           // see GetSleepTime() for implementation

        private ConcurrentQueue<Task> messageQueue = new();
        private Task apiWorker;
        private TelegramBotClient bot;

        /// <summary>
        /// If everything needs to stop going to this channel for some reason
        /// </summary>
        public bool Stopping { get; set; } = false;

        /// <summary>
        /// The <see cref="ChatId"/> the telegram library needs to refer to this channel
        /// </summary>
        public ChatId Id { get; private set; }

        /// <summary>
        /// Constructor using a long ID
        /// </summary>
        /// <param name="tgBot">The bot client used by the <see cref="TelegramService"/></param>
        /// <param name="id">The numerical id of the channel/DM/group this <see cref="Channel"/> represents</param>
        internal Channel(TelegramBotClient tgBot, long id)
        {
            Id = new ChatId(id);
            bot = tgBot;
            apiWorker = new Task(ProcessQueue);
        }

        /// <summary>
        /// Constructor using a <see cref="ChatId"/> object
        /// </summary>
        /// <param name="tgBot">The bot client used by the <see cref="TelegramService"/></param>
        /// <param name="id">The <see cref="ChatId"/> object referring to the channel/DM/group this <see cref="Channel"/> represents</param>
        internal Channel(TelegramBotClient tgBot, ChatId id)
        {
            Id = id;
            bot = tgBot;
            apiWorker = new Task(ProcessQueue);
        }

        /// <summary>
        /// Add a task to the queue containing one of the interaction methods below
        /// </summary>
        /// <param name="task"></param>
        private void AddToQueue(Task task)
        {
            messageQueue.Enqueue(task);

            if (apiWorker.Status == TaskStatus.Created)
            {
                apiWorker.Start();
            }
        }


        /// <summary>
        /// Work on the queue
        /// </summary>
        private void ProcessQueue()
        {
            // if there's stuff in the queue and the worker is free, do the next task
            while (messageQueue.Count > 0 && !Stopping)
            {
                Task request;
                if (messageQueue.TryDequeue(out request))
                {
                    request.Start();
                    interactionRecord.Enqueue(DateTime.Now);
                    Thread.Sleep(GetSleepTime()); // wait for the API limit before interacting with the channel again
                }
            }
            // when the queue is empty, recreate the worker task in an unstarted state
            if(!Stopping)
                apiWorker = new Task(ProcessQueue);
        }

        /// <summary>
        /// Adjust the sleep time to allow snappy responses in underused channels and up to twice the throttling in overused channels
        /// </summary>
        /// <returns>An <see cref="int"/> representing the milliseconds the thread should sleep</returns>
        private int GetSleepTime()
        {
            if (interactionRecord.Count == 0) return 1; // no interactions lately
            
            // remove all interactions that happened longer ago than the API limit cares about
            while (DateTime.Now.Subtract(interactionRecord.Peek()).TotalSeconds >= API_LIMIT_SECONDS)
                interactionRecord.Dequeue();

            // the midpoint of the dynamic limit should act like if we applied a static rate limit to all interactions all the time
            int averageSleep = API_LIMIT_SECONDS / API_LIMIT_ACTIONS * 1000;

            // use a time delay that would result in instant interactions if there haven't been any, and twice the delay from a static limit if we're about to anger the API
            return (int)(averageSleep * ((float)interactionRecord.Count * 2.0 / (float)API_LIMIT_ACTIONS)) + 1;
        }

        /// <summary>
        /// Send an action to the chat so the users see "BotName is typing..." in this channel for a couple seconds
        /// </summary>
        public void DoTyping()
        {
            AddToQueue(new Task(async () =>
            {
                await bot.SendChatActionAsync(Id, Telegram.Bot.Types.Enums.ChatAction.Typing);
            }));
        }

        /// <summary>
        /// Send a message to this channel
        /// </summary>
        /// <param name="text">The text of the message</param>
        /// <param name="replyToMessageId">The ID of the message to reply to, if any</param>
        /// <param name="markup">Menu options for this message, if any</param>
        /// <param name="disableNotification">Whether to create a notification for users when this message hits the channel</param>
        /// <param name="callback">An optional callback action taking a <see cref="Message"/> object, in case the caller needs to access message info afterward</param>
        public void Send(string text, int? replyToMessageId = null, ReplyKeyboardMarkup? markup = null, bool disableNotification = false, Action<Message>? callback = null)
        {
            AddToQueue(new Task(async () =>
            {
                Message newMessage = new Message();
                try
                {
                    newMessage = await bot.SendTextMessageAsync(Id, text, replyToMessageId: replyToMessageId, replyMarkup: markup, disableNotification: disableNotification);
                    if (callback is not null)
                        callback.Invoke(newMessage);
                }
                catch (ApiRequestException) // if replyToMessageId is included, this can occur on restart or if the user deletes a message before a response comes back.
                {                           // so just try again without the reply
                    AddToQueue(new Task(async () =>
                    {
                        newMessage = await bot.SendTextMessageAsync(Id, text, replyMarkup: markup, disableNotification: disableNotification);
                        if (callback is not null)
                            callback.Invoke(newMessage);
                    }));
                }
            }));
        }

        /// <summary>
        /// Edit an existing message
        /// </summary>
        /// <param name="messageId">The ID of the message to edit</param>
        /// <param name="newText">The new text of the message</param>
        /// <param name="callback">An optional callback action taking a <see cref="Message"/> object, in case the caller needs to access message info afterward</param>
        public void Edit(int messageId, string newText, Action<Message>? callback = null)
        {
            AddToQueue(new Task(async () =>
            {
                try
                {
                    var message = await bot.EditMessageTextAsync(Id, messageId, newText);
                    if (callback is not null)
                        callback.Invoke(message);
                }
                catch (ApiRequestException) // in case the message was deleted, send a new one
                {
                    Send(newText, callback: callback);
                }
            }));
        }

        /// <summary>
        /// Pin a message in a channel
        /// </summary>
        /// <param name="messageId">The ID of the message to pin</param>
        /// <param name="disableNotification">Whether users should not receive a notification for this pin</param>
        public void Pin(int messageId, bool? disableNotification = null)
        {
            AddToQueue(new Task(async () =>
            {
                await bot.PinChatMessageAsync(Id, messageId, disableNotification: disableNotification);
            }));
        }

        /// <summary>
        /// Unpin a message in a channel
        /// </summary>
        /// <param name="messageId">The ID of the message to unpin</param>
        public void Unpin(int messageId)
        {
            AddToQueue(new Task(async () =>
            {
                await bot.UnpinChatMessageAsync(Id, messageId);
            }));
        }

        /// <summary>
        /// Create a copy of an existing message
        /// </summary>
        /// <param name="messageId">The ID of the message to copy</param>
        /// <param name="disableNotification">Whether notifications should be disabled for this action</param>
        /// <param name="callback">An optional callback action taking a <see cref="Message"/> object, in case the caller needs to access message info afterward<</param>
        public void Copy(int messageId, bool disableNotification = true, Action<MessageId>? callback = null)
        {
            AddToQueue(new Task(async () =>
            {
                MessageId newMessageId = await bot.CopyMessageAsync(Id, Id, messageId, disableNotification: disableNotification);
                if (callback is not null)
                    callback.Invoke(newMessageId);
            }));
            
        }

        /// <summary>
        /// Delete a message in a channel
        /// </summary>
        /// <param name="messageId">The ID of the message to delete</param>
        public void Delete(int messageId)
        {
            AddToQueue(new Task(async () =>
            {
                await bot.DeleteMessageAsync(Id, messageId);
            }));
        }

    }
}
