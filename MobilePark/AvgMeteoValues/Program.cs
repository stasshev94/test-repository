Console.WriteLine("Старт работы приложения");

var handler = new CommandHandler();

var url = args.FirstOrDefault() ?? "http://localhost:5000";
handler.Handle(url);

Console.WriteLine("\nЗавершение работы приложения");