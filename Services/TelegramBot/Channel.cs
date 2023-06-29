using Discord;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Botifex.Services.TelegramBot
{
    internal class Channel
    {
        private static readonly int API_LIMIT = 3000;
        private DateTime lastSent = DateTime.MinValue;
        private ConcurrentQueue<Task> messageQueue = new();
        private Task apiWorker;
        private TelegramBotClient bot;

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
        /// Empty out the queue
        /// </summary>
        private void ProcessQueue()
        {
            // if there's stuff in the queue and the worker is free, do the next task
            while (messageQueue.Count > 0)
            {
                Task request;
                if (messageQueue.TryDequeue(out request))
                {
                    request.Start();
                    Thread.Sleep(API_LIMIT); // wait for the API limit before interacting with the channel again
                }
            }
            apiWorker = new Task(ProcessQueue);
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
                var message = await bot.EditMessageTextAsync(Id, messageId, newText);
                if(callback is not null)
                    callback.Invoke(message);
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
