using System;
using VirtualMemory.Models;
using VirtualMemory.Interfaces;

public class Program
{
    private static readonly char[] separator = new char[] { ' ' };

    static void Main(string[] args)
    {
        object? vmManager = null; // Будет хранить VirtualMemoryManager<T>

        while (true)
        {
            Console.Write("VM> ");
            string? input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
                continue;

            // Разбиваем ввод на команду и параметры
            string[] tokens = input.Split(separator, 2, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                continue;

            string command = tokens[0].ToLower();

            try
            {
                switch (command)
                {
                    case "help":
                        ShowHelp();
                        break;

                    case "create":
                        if (tokens.Length < 2)
                        {
                            Console.WriteLine("Неверный формат команды create. Используйте: create <имя_файла> <тип массива>");
                            break;
                        }
                        CreateCommand(tokens[1], ref vmManager);
                        break;

                    case "input":
                        if (tokens.Length < 2)
                        {
                            Console.WriteLine("Неверный формат команды input. Используйте: input <индекс> <значение>");
                            break;
                        }
                        ProcessInputCommand(tokens[1], vmManager);
                        break;

                    case "print":
                        if (tokens.Length < 2)
                        {
                            Console.WriteLine("Неверный формат команды print. Используйте: print <индекс>");
                            break;
                        }
                        ProcessPrintCommand(tokens[1], vmManager);
                        break;

                    case "exit":
                        switch (vmManager)
                        {
                            case VirtualMemoryManager<int> vmInt:
                                vmInt.Close();
                                break;
                            case VirtualMemoryManager<string> vmString:
                                vmString.Close();
                                break;
                        }
                        return;

                    default:
                        Console.WriteLine("Неизвестная команда. Для справки введите help.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
            }
        }
    }

    private static void CreateCommand(string parameters, ref object? vmManager)
    {
        string[] parts = parameters.Split(separator, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            Console.WriteLine("Неверный формат параметров для команды create.");
            return;
        }

        string fileName = parts[0];
        string typeInfo = parts[1].ToLower();

        vmManager = typeInfo switch
        {
            string s when s.StartsWith("int") =>
                new VirtualMemoryManager<int>(
                    fileName,
                    20000,
                    PageElementsForInt(),
                    new IntSerializer()),
            string s when s.StartsWith("char") =>
                new VirtualMemoryManager<string>(
                    fileName,
                    20000,
                    PageElementsForChar(ParseLength(s, "char")),
                    new FixedStringSerializer(ParseLength(s, "char") * 2) // фиксированный размер в байтах: length * 2
                ),
            string s when s.StartsWith("varchar") =>
                new VirtualMemoryManager<string>(
                    fileName,
                    20000,
                    PageElementsForVarchar(ParseLength(s, "varchar")),
                    new VarcharSerializer(4 + (ParseLength(s, "varchar") * 2)) // 4 байта на длину + символы
                ),
            _ => null
        };

        if (vmManager == null)
        {
            Console.WriteLine("Неверный тип массива. Поддерживаются int, char и varchar.");
        }
        else
        {
            Console.WriteLine("Файл и структура виртуального массива созданы.");
        }
    }

    private static void ProcessInputCommand(string parameters, object? vmManager)
    {
        if (vmManager == null)
        {
            Console.WriteLine("Сначала выполните команду create.");
            return;
        }

        string[] parts = parameters.Split(separator, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            Console.WriteLine("Неверный формат параметров для команды input.");
            return;
        }

        long index = long.Parse(parts[0]);

        switch (vmManager)
        {
            case VirtualMemoryManager<int> vmInt:
                int intValue = int.Parse(parts[1]);
                vmInt.WriteElement(index, intValue);
                Console.WriteLine("Значение записано.");
                break;
            case VirtualMemoryManager<string> vmString:
                string strValue = parts[1].Trim();
                if (strValue.StartsWith("\"") && strValue.EndsWith("\""))
                {
                    strValue = strValue.Substring(1, strValue.Length - 2);
                }
                vmString.WriteElement(index, strValue);
                Console.WriteLine("Значение записано.");
                break;
            default:
                Console.WriteLine("Неверный тип виртуального массива для операции input.");
                break;
        }
    }

    private static void ProcessPrintCommand(string parameter, object? vmManager)
    {
        if (vmManager == null)
        {
            Console.WriteLine("Сначала выполните команду create.");
            return;
        }

        long index = long.Parse(parameter);

        switch (vmManager)
        {
            case VirtualMemoryManager<int> vmInt:
                int intValue = vmInt.ReadElement(index);
                Console.WriteLine($"Элемент[{index}] = {intValue}");
                break;
            case VirtualMemoryManager<string> vmString:
                string strValue = vmString.ReadElement(index);
                Console.WriteLine($"Элемент[{index}] = \"{strValue}\"");
                break;
            default:
                Console.WriteLine("Неверный тип виртуального массива для операции print.");
                break;
        }
    }

    private static int ParseLength(string typeInfo, string typeKeyword)
    {
        int start = typeInfo.IndexOf('(');
        int end = typeInfo.IndexOf(')');
        if (start > 0 && end > start)
        {
            string lenStr = typeInfo.Substring(start + 1, end - start - 1);
            if (int.TryParse(lenStr, out int length))
            {
                return length;
            }
        }
        return -1;
    }

    // Вычисление максимального числа элементов для int
    private static int PageElementsForInt()
    {
        int n = 0;
        for (int i = 1; i < 512; i++)
        {
            int bitMap = (i + 7) / 8;
            if (i * 4 + bitMap <= 512)
                n = i;
            else
                break;
        }
        return n;
    }

    // Вычисление числа элементов для фиксированных строк (char)
    private static int PageElementsForChar(int fixedLength)
    {
        int elemSize = fixedLength * 2;
        int n = 0;
        for (int i = 1; i < 512; i++)
        {
            int bitMap = (i + 7) / 8;
            if (i * elemSize + bitMap <= 512)
                n = i;
            else
                break;
        }
        return n;
    }

    // Вычисление числа элементов для varchar
    private static int PageElementsForVarchar(int maxLength)
    {
        int elemSize = 4 + maxLength * 2;
        int n = 0;
        for (int i = 1; i < 512; i++)
        {
            int bitMap = (i + 7) / 8;
            if (i * elemSize + bitMap <= 512)
                n = i;
            else
                break;
        }
        return n;
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
