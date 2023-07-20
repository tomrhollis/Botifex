using System.Collections.Concurrent;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Botifex.Services.TelegramBot
{
    internal class Channel
    {
        private static readonly int API_LIMIT_ACTIONS = 20; // telegram will get mad if you send more than 20 messages a minute
        private static readonly int API_LIMIT_SECONDS = 60; // it's unclear if that's just messages or any interaction with a channel
                                                            // erring on the safe side and queueing ALL interactions with a channel,
                                                            // handling them once every 3 seconds
        private Queue<DateTime> interactionRecord = new Queue<DateTime>();


        private ConcurrentQueue<Task> messageQueue = new();
        private Task apiWorker;
        private TelegramBotClient bot;

        public bool Stopping { get; set; } = false;

        public ChatId Id { get; private set; }

        internal Channel(TelegramBotClient tgBot, long id)
        {
            Id = new ChatId(id);
            bot = tgBot;
            apiWorker = new Task(ProcessQueue);
        }

        internal Channel(TelegramBotClient tgBot, ChatId id)
        {
            Id = id;
            bot = tgBot;
            apiWorker = new Task(ProcessQueue);
        }


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
            if(!Stopping)
                apiWorker = new Task(ProcessQueue);
        }

        /// <summary>
        /// Adjust the sleep time to allow snappy responses in underused channels and up to twice the throttling in overused channels
        /// </summary>
        /// <returns>An <see cref="int"/> representing the milliseconds the thread should sleep</returns>
        private int GetSleepTime()
        {
            if (interactionRecord.Count == 0) return 1;
            
            while (DateTime.Now.Subtract(interactionRecord.Peek()).TotalSeconds >= API_LIMIT_SECONDS)
                interactionRecord.Dequeue();

            int averageSleep = API_LIMIT_SECONDS / API_LIMIT_ACTIONS * 1000;
            return (int)(averageSleep * ((float)interactionRecord.Count * 2.0 / (float)API_LIMIT_ACTIONS));
        }

        public void DoTyping()
        {
            AddToQueue(new Task(async () =>
            {
                await bot.SendChatActionAsync(Id, Telegram.Bot.Types.Enums.ChatAction.Typing);
            }));
        }

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

        public void Pin(int messageId, bool? disableNotification = null)
        {
            AddToQueue(new Task(async () =>
            {
                await bot.PinChatMessageAsync(Id, messageId, disableNotification: disableNotification);
            }));
        }

        public void Unpin(int messageId)
        {
            AddToQueue(new Task(async () =>
            {
                await bot.UnpinChatMessageAsync(Id, messageId);
            }));
        }

        public void Copy(int messageId, bool disableNotification = true, Action<MessageId>? callback = null)
        {
            AddToQueue(new Task(async () =>
            {
                MessageId newMessageId = await bot.CopyMessageAsync(Id, Id, messageId, disableNotification: disableNotification);
                if (callback is not null)
                    callback.Invoke(newMessageId);
            }));
            
        }

        public void Delete(int messageId)
        {
            AddToQueue(new Task(async () =>
            {
                await bot.DeleteMessageAsync(Id, messageId);
            }));
        }

    }
}
