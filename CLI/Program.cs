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
            //string? input = "create";
            if (string.IsNullOrWhiteSpace(input))
                continue;
            string[] tokens = input.Split(separator, 2, StringSplitOptions.RemoveEmptyEntries);
            string command = tokens[0].ToLower();

            try
            {
                if (command == "create")
                {
                    // Пример: Create memory.dat int
                    string parameters = tokens[1];
                    string[] parts = parameters.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    string fileName = parts[0];
                    string typeInfo = parts[1].ToLower();

                    if (typeInfo.StartsWith("int"))
                    {
                        // Для int можно задать количество элементов, например, 20000
                        long totalElements = 20000;
                        int elementsPerPage = pageSizeForInt(); // функция вычисления числа элементов на странице для int
                        vmManager = new VirtualMemoryManager<int>(fileName, totalElements, elementsPerPage) ?? throw new ArgumentNullException(nameof(vmManager
                        ));
                    }
                    // Реализуйте аналогичную логику для char и varchar
                    Console.WriteLine("Файл и структура виртуального массива созданы.");
                }
                else if (command == "input")
                {
                    ArgumentNullException.ThrowIfNull(vmManager);
                    // Пример: Input 15 12345
                    string[] parts = tokens[1].Split(separator, 2, StringSplitOptions.RemoveEmptyEntries);
                    long index = long.Parse(parts[0]);
                    // Для строковых типов – убрать кавычки
                    int value = int.Parse(parts[1]);
                    vmManager.WriteElement(index, value);
                    Console.WriteLine("Значение записано.");
                }
                else if (command == "print")
                {
                    long index = long.Parse(tokens[1]);
                    if (vmManager != null)
                    {
                        int value = vmManager.ReadElement(index);
                        Console.WriteLine($"Элемент[{index}] = {value}");
                    }
                    else throw new ArgumentNullException(nameof(vmManager));

                }
                else if (command == "exit")
                {
                    vmManager?.Close();
                    break;
                }
                else
                {
                    Console.WriteLine("Неизвестная команда.");
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
}