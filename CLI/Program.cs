using System;
using VirtualMemory.Models;

public class Program
{
    private static readonly char[] separator = new char[] { ' ' };

    static void Main(string[] args)
    {
        Console.WriteLine("VM>");
        VirtualMemoryManager<int>? vmManager = null;

        while (true)
        {
            Console.Write("VM> ");
            string? input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
                continue;

            // Разбиваем ввод на команду и параметры
            string[] tokens = input.Split(separator, 2, StringSplitOptions.RemoveEmptyEntries);
            string command = tokens[0].ToLower();

            try
            {
                if (command == "help")
                {
                    ShowHelp();
                }
                else if (command == "create")
                {
                    if (tokens.Length < 2)
                    {
                        Console.WriteLine("Неверный формат команды create. Используйте: create <имя_файла> <тип массива>");
                        continue;
                    }
                    string parameters = tokens[1];
                    string[] parts = parameters.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                    {
                        Console.WriteLine("Неверный формат параметров для команды create.");
                        continue;
                    }
                    string fileName = parts[0];
                    string typeInfo = parts[1].ToLower();

                    if (typeInfo.StartsWith("int"))
                    {
                        // Для int можно задать количество элементов, например, 20000
                        long totalElements = 20000;
                        int elementsPerPage = pageSizeForInt();
                        vmManager = new VirtualMemoryManager<int>(fileName, totalElements, elementsPerPage);
                    }
                    // Аналогично можно реализовать для char и varchar
                    Console.WriteLine("Файл и структура виртуального массива созданы.");
                }
                else if (command == "input")
                {
                    if (tokens.Length < 2)
                    {
                        Console.WriteLine("Неверный формат команды input. Используйте: input <индекс> <значение>");
                        continue;
                    }
                    if (vmManager == null)
                    {
                        Console.WriteLine("Сначала выполните команду create.");
                        continue;
                    }
                    string[] parts = tokens[1].Split(separator, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                    {
                        Console.WriteLine("Неверный формат параметров для команды input.");
                        continue;
                    }
                    long index = long.Parse(parts[0]);
                    // Если значение строковое, оно должно быть заключено в кавычки.
                    int value = int.Parse(parts[1].Trim('"'));
                    vmManager.WriteElement(index, value);
                    Console.WriteLine("Значение записано.");
                }
                else if (command == "print")
                {
                    if (tokens.Length < 2)
                    {
                        Console.WriteLine("Неверный формат команды print. Используйте: print <индекс>");
                        continue;
                    }
                    if (vmManager == null)
                    {
                        Console.WriteLine("Сначала выполните команду create.");
                        continue;
                    }
                    long index = long.Parse(tokens[1]);
                    int value = vmManager.ReadElement(index);
                    Console.WriteLine($"Элемент[{index}] = {value}");
                }
                else if (command == "exit")
                {
                    vmManager?.Close();
                    break;
                }
                else
                {
                    Console.WriteLine("Неизвестная команда. Для справки введите help.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
            }
        }
    }

    private static int pageSizeForInt()
    {
        // Размер int = 4 байта, страница 512 байт минус размер битовой карты.
        // Предположим, что битовая карта занимает 16 байт (128 бит) – тогда:
        // Элементы на странице = (512 - 16) / 4 = 124
        return 124;
    }

    private static void ShowHelp()
    {
        Console.WriteLine("Форматы команд:");
        Console.WriteLine();
        Console.WriteLine("create <имя_файла> <тип массива>");
        Console.WriteLine("  Примеры:");
        Console.WriteLine("    create memory.dat int");
        Console.WriteLine("    create memory.dat char(20)");
        Console.WriteLine("    create memory.dat varchar(100)");
        Console.WriteLine();
        Console.WriteLine("input <индекс> <значение>");
        Console.WriteLine("  Примеры:");
        Console.WriteLine("    input 15 12345");
        Console.WriteLine("    input 20 \"строковое значение\"");
        Console.WriteLine();
        Console.WriteLine("print <индекс>");
        Console.WriteLine("  Пример:");
        Console.WriteLine("    print 15");
        Console.WriteLine();
        Console.WriteLine("exit");
        Console.WriteLine("  Завершает работу приложения.");
        Console.WriteLine();
        Console.WriteLine("help");
        Console.WriteLine("  Выводит данное справочное сообщение.");
    }
}