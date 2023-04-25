﻿using Botifex.Models;
using Botifex.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Botifex
{
    public class Botifex : IBotifex 
    {
        private IHostApplicationLifetime appLifetime;
        private IConfiguration config;
        private ILogger<Botifex> log;
        private ICommandLibrary commandLibrary;

        private EventHandler<InteractionReceivedEventArgs>? onCommand;
        private EventHandler<InteractionReceivedEventArgs>? onText;
        private EventHandler<EventArgs> onReady;

        private Messenger[] messengers;
        private List<BotifexUser> knownUsers = new List<BotifexUser>();

        public Botifex(IHostApplicationLifetime lifetime, IConfiguration cfg, ILogger<Botifex> log, ICommandLibrary lib, IDiscord dc, ITelegram tg)
        {
            appLifetime = lifetime;
            config = cfg;
            this.log = log;
            commandLibrary = lib;
            messengers = new Messenger[] { (Services.DiscordService)dc, (Services.TelegramService)tg };

            appLifetime.ApplicationStarted.Register(OnStarted);
            appLifetime.ApplicationStopping.Register(OnStopping);
            appLifetime.ApplicationStopped.Register(OnStopped);

            onReady += DoReadyTasks;

            if (onReady != null)
                foreach (var messenger in messengers)
                    messenger.OnFirstReady += onReady;
        }

        private void OnStarted()
        {
            log.LogDebug("OnStarted has been called.");

            if (onCommand != null)
                foreach (var messenger in messengers)
                    messenger.OnCommandReceived += onCommand;

            if (onText != null)
                foreach (var messenger in messengers)
                    messenger.OnMessageReceived += onText;
        }

        private void OnStopping()
        {
            log.LogDebug("Botifex is shutting down");
            log.LogDebug("OnStopping has been called.");
        }

        private void OnStopped()
        {
            log.LogDebug("OnStopped has been called.");
        }

        public async Task LogAll(string message)
        {
            await Task.WhenAll(messengers.Select((m)=>m.Log(message, LogLevel.Information)));
        }        

        public void RegisterTextHandler(EventHandler<InteractionReceivedEventArgs> handler)
        {
            onText = handler;
        }

        public void RegisterCommandHandler(EventHandler<InteractionReceivedEventArgs> handler)
        {
            onCommand = handler;
        }

        public void RegisterReadyHandler(EventHandler<EventArgs> handler)
        {
            onReady = handler;
        }

        public void AddCommand(SlashCommand command)
        {
            commandLibrary.RegisterCommand(command);
        }

        private async void DoReadyTasks(object sender, EventArgs e)
        {
            await PushCommands((Messenger)sender);
        }

        public async Task PushCommands(Messenger m)
        {
            await m.LoadCommands();
        }

        public async Task SendStatusUpdate(string message)
        {
            await Task.WhenAll(messengers.Select((m) => m.CreateOrUpdateStatus(message)));
        }

        public async Task SendOneTimeStatusUpdate(string message, bool notification = false)
        {
            await Task.WhenAll(messengers.Select((m) => m.SendOneTimeStatus(message, notification)));
        }

        public BotifexUser? GetUser(IMessengerUser messengerAccount)
        {
            return knownUsers.Find((u) => u.Accounts.Contains(messengerAccount));
        }
    }
}