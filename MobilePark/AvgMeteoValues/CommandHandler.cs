namespace AvgMeteoValues;

public class CommandHandler
{
    private enum Sensor : byte
    {
        Temperature = 1,
        Humidity = 2,
        Pressure = 3
    }

    private static readonly IDictionary<Sensor, string> SensorNames = new Dictionary<Sensor, string>()
    {
        { Sensor.Temperature, "Датчик температуры" },
        { Sensor.Humidity, "Датчик влажности" },
        { Sensor.Pressure, "Датчик давления" },
    };

    private Task? _task;

    private uint _connected;
    private ulong _messageCount;
    private double _avgTemperature;
    private double _avgHumidity;
    private double _avgPressure;

    private volatile uint _exited;
    private volatile uint _stopped;

    public void Handle(string url)
    {
        var uri = new Uri(url);

        while (_exited == 0)
        {
            Console.WriteLine(@"
1. start - подключение к серверу и начало обработки сообщений
2. stop  - остановка обработки сообщений и отключение от сервера
3. info  - отобразить в консоли среднее значение для каждого датчика из последних 10 сообщений
4. statistics - отобразить состояние подключения и количество полученных сообщений
5. exit - завершить работу приложения, выход.

Выберите и введите команду");
            
            var command = Console.ReadLine();
            switch (command)
            {
                case "start":

                    if (_task != null)
                    {
                        Console.WriteLine("\nВ данный момент команда 'start' уже была запущена ранее, для завершения чтения " +
                                          "сообщений вызовите команду 'stop'");
                        break;
                    }

                    _task = Task.Run(async () =>
                    {
                        const int lengthSize = 2;
                        const int timeSize = 8;
                        const int idSize = 4;
                        const int typeSize = 1;
                        const int valueSize = 8;
                        const int messageSize = 1024;
                        const int batch = 10;

                        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                        await socket.ConnectAsync(uri.Host, uri.Port);

                        _connected = socket.Connected ? (uint)1 : 0;
                        Console.WriteLine($"\nПроизведено подключение к хосту {url}");
                        Console.WriteLine("Начало чтения сообщений");

                        Dictionary<Sensor, List<double>> values = new();
                        int skip = default;
                        while (_stopped == 0 && _exited == 0)
                        {
                            var receiveData = new byte[messageSize];
                            var result = await socket.ReceiveAsync(receiveData);

                            if (result <= 0)
                            {
                                await Task.Delay(2000);
                                continue;
                            }

                            var length = BitConverter.ToUInt16(receiveData.Take(lengthSize).ToArray(), 0);
                            var unixTime = BitConverter.ToInt64(receiveData.Skip(lengthSize).Take(timeSize).ToArray(),
                                0);
                            var id = BitConverter.ToInt32(
                                receiveData.Skip(lengthSize + timeSize).Take(idSize).ToArray(), 0);

                            var skipped = lengthSize + timeSize + idSize;

                            while (skipped < result)
                            {
                                var type = (Sensor)receiveData.Skip(skipped).First();
                                var value = BitConverter.ToDouble(receiveData.Skip(skipped + typeSize).Take(valueSize)
                                    .ToArray());

                                if (values.TryGetValue(type, out var val))
                                {
                                    val.Add(value);
                                }
                                else
                                {
                                    values.Add(type, new List<double>(1) { value });
                                }

                                skipped += typeSize + valueSize;
                            }

                            var sensorsData = values.Select(pair =>
                                    $"{SensorNames[pair.Key]} => значение = {Math.Round(pair.Value.Last(), 2)};")
                                .ToArray();

                            var message = $"\nНовое сообщение:\n" +
                                          $"Размер = {length} байт;\n" +
                                          $"Идентификатор эмулятора = {id};\n" +
                                          $"Время получения данных с датчика = {TimeSpan.FromTicks(unixTime)};\n" +
                                          $"{string.Join("\n", sensorsData)}";

                            Console.WriteLine(message);

                            Interlocked.Increment(ref _messageCount);

                            if (_messageCount > batch)
                                skip++;

                            Interlocked.Exchange(ref _avgTemperature, GetAvgValue(Sensor.Temperature));
                            Interlocked.Exchange(ref _avgHumidity, GetAvgValue(Sensor.Humidity));
                            Interlocked.Exchange(ref _avgPressure, GetAvgValue(Sensor.Pressure));

                            double GetAvgValue(Sensor type)
                            {
                                return values
                                    .First(x => x.Key == type)
                                    .Value
                                    .Skip(skip)
                                    .Take(batch)
                                    .Sum() / batch;
                            }
                        }

                        Console.WriteLine($"\nЗавершение сеанса соединения с хостом {url}...");
                        
                        socket.Shutdown(SocketShutdown.Receive);
                        await socket.DisconnectAsync(true);

                        Interlocked.Exchange(ref _connected, 0);
                        Interlocked.Exchange(ref _stopped, 0);
                        Interlocked.Exchange(ref _messageCount, 0);
                        Interlocked.Exchange(ref _avgTemperature, 0);
                        Interlocked.Exchange(ref _avgHumidity, 0);
                        Interlocked.Exchange(ref _avgPressure, 0);
                        
                        Console.WriteLine("Сеанс завершен");
                    });

                    break;

                case "stop":
                    
                    _stopped = 1;
                    _task?.Wait();
                    
                    _stopped = 0;
                    _task = null;
                    
                    break;

                case "info":

                    Console.WriteLine($"\nСреднее значение температуры = {Math.Round(_avgTemperature, 2)}\n" +
                                      $"Среднее значение влажности = {Math.Round(_avgHumidity, 2)}\n" +
                                      $"Среднее значение давления = {Math.Round(_avgPressure, 2)}\n");
                    
                    break;

                case "statistics":

                    Console.WriteLine(
                        $"\nСостояние подключения = {_connected == 1}, Количество сообщений = {_messageCount}");

                    break;
                case "exit":
                    
                    _exited = 1;
                    _task?.Wait();
                    
                    break;

                default:
                    
                    Console.WriteLine($"\nВведенная команда {command} не найдена...\n");
                    
                    break;
            }
        }
    }
}