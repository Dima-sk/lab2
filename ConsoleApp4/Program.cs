using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

class Program
{
    static ITelegramBotClient botClient;
    static List<UserTask> tasks = new List<UserTask>();
    static CancellationTokenSource reminderCancellationTokenSource = new CancellationTokenSource();

    static async Task Main()
    {
        botClient = new TelegramBotClient("7329538832:AAHhnJa2J3AXPAEtfdXlVBLg4oo82q_FKc8"); // Замените на токен вашего бота

        Console.WriteLine($"Бот запущен: @{(await botClient.GetMeAsync()).Username}");

        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        // Запускаем задачу для отправки напоминаний
        Task.Run(() => ReminderLoop(reminderCancellationTokenSource.Token));

        // Настройка получения обновлений
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>() // Получать все типы обновлений
        };

        botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: cancellationToken
        );

        Console.WriteLine("Нажмите любую клавишу для остановки...");
        Console.ReadKey();

        cts.Cancel();
        reminderCancellationTokenSource.Cancel();
    }

    static async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
    {
        // Обрабатываем только текстовые сообщения
        if (update.Type != UpdateType.Message || update.Message!.Type != MessageType.Text)
            return;

        var chatId = update.Message.Chat.Id;
        var messageText = update.Message.Text;

        if (messageText == "/start")
        {
            await bot.SendTextMessageAsync(
                chatId: chatId,
                text: "Привет! Я бот для задач. Используйте кнопки ниже для управления задачами.",
                replyMarkup: GetMainMenuKeyboard(),
                cancellationToken: cancellationToken
            );
        }
        else if (messageText == "Добавить задачу")
        {
            await bot.SendTextMessageAsync(
                chatId: chatId,
                text: "Введите задачу в формате: описание;ГГГГ-ММ-ДД ЧЧ:ММ",
                cancellationToken: cancellationToken
            );
        }
        else if (messageText == "Посмотреть задачи")
        {
            if (tasks.Count == 0)
            {
                await bot.SendTextMessageAsync(chatId, "Задач пока нет.", cancellationToken: cancellationToken);
                return;
            }

            string taskList = "Ваши задачи:\n";
            for (int i = 0; i < tasks.Count; i++)
            {
                taskList += $"{i + 1}. {tasks[i].Description} (Напоминание: {tasks[i].ReminderTime})\n";
            }

            await bot.SendTextMessageAsync(chatId, taskList, cancellationToken: cancellationToken);
        }
        else if (messageText == "Удалить задачу")
        {
            if (tasks.Count == 0)
            {
                await bot.SendTextMessageAsync(chatId, "Нет задач для удаления.", cancellationToken: cancellationToken);
                return;
            }

            string taskList = "Выберите задачу для удаления (введите номер):\n";
            for (int i = 0; i < tasks.Count; i++)
            {
                taskList += $"{i + 1}. {tasks[i].Description} (Напоминание: {tasks[i].ReminderTime})\n";
            }

            await bot.SendTextMessageAsync(chatId, taskList, cancellationToken: cancellationToken);
        }
        else if (int.TryParse(messageText, out int taskNumber) && taskNumber > 0 && taskNumber <= tasks.Count)
        {
            var removedTask = tasks[taskNumber - 1];
            tasks.RemoveAt(taskNumber - 1);
            await bot.SendTextMessageAsync(chatId, $"Задача \"{removedTask.Description}\" удалена.", cancellationToken: cancellationToken);
        }
        else if (messageText.Contains(";"))
        {
            string[] parts = messageText.Split(';');
            if (parts.Length == 2 && DateTime.TryParse(parts[1], out DateTime reminderTime))
            {
                tasks.Add(new UserTask { Description = parts[0], ReminderTime = reminderTime, ChatId = chatId });
                await bot.SendTextMessageAsync(chatId, "Задача добавлена!", cancellationToken: cancellationToken);
            }
            else
            {
                await bot.SendTextMessageAsync(chatId, "Неверный формат. Используйте: описание;ГГГГ-ММ-ДД ЧЧ:ММ", cancellationToken: cancellationToken);
            }
        }
    }

    static Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken cancellationToken)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiRequestException => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        Console.WriteLine(errorMessage);
        return Task.CompletedTask;
    }

    static async Task ReminderLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var tasksToRemove = new List<UserTask>();

            foreach (var task in tasks)
            {
                if (!task.Reminded && task.ReminderTime <= DateTime.Now)
                {
                    await botClient.SendTextMessageAsync(
                        chatId: task.ChatId,
                        text: $"Напоминание: {task.Description}"
                    );
                    task.Reminded = true;
                    tasksToRemove.Add(task);
                }
            }

            // Удаляем завершённые задачи
            foreach (var task in tasksToRemove)
            {
                tasks.Remove(task);
            }

            await Task.Delay(10000, cancellationToken); // Проверка каждые 10 секунд
        }
    }

    static IReplyMarkup GetMainMenuKeyboard()
    {
        return new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { "Добавить задачу", "Посмотреть задачи" },
            new KeyboardButton[] { "Удалить задачу" }
        })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = false
        };
    }

    class UserTask
    {
        public string Description { get; set; }
        public DateTime ReminderTime { get; set; }
        public long ChatId { get; set; }
        public bool Reminded { get; set; } = false;
    }
}
