using System;
using VirtualMemory.Models;
using VirtualMemory.Interfaces;

namespace VirtualMemory.CLI
{
    public static class Program
    {
        private static IVirtualMemoryManager<int>? _intManager;
        private static bool _isRunning = true;

        public static void Main()
        {
            Console.WriteLine("Virtual Memory CLI (Type 'help' for commands)");
            
            while (_isRunning)
            {
                Console.Write("VM> ");
                var input = Console.ReadLine()?.Trim();
                
                if (string.IsNullOrEmpty(input))
                    continue;

                try
                {
                    ProcessCommand(input);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        private static void ProcessCommand(string input)
        {
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0].ToLower();

            switch (command)
            {
                case "create":
                    HandleCreateCommand(parts);
                    break;
                
                case "input":
                    HandleInputCommand(parts);
                    break;
                
                case "print":
                    HandlePrintCommand(parts);
                    break;
                
                case "exit":
                    _isRunning = false;
                    _intManager?.Dispose();
                    Console.WriteLine("Exiting...");
                    break;
                
                case "help":
                    PrintHelp();
                    break;
                
                default:
                    Console.WriteLine($"Unknown command: {command}");
                    break;
            }
        }

        private static void HandleCreateCommand(string[] parts)
        {
            if (parts.Length < 2)
                throw new ArgumentException("Usage: create <filename>");

            var filename = parts[1];

            _intManager = new IntMemoryManager(
                bufferSize: 3,
                serializer: new IntSerializer(),
                filename: filename
            );

            Console.WriteLine($"Virtual memory file '{filename}' created/opened.");
        }

        private static void HandleInputCommand(string[] parts)
        {
            if (_intManager == null)
                throw new InvalidOperationException("No virtual memory file opened. Use 'create' first.");

            if (parts.Length < 3)
                throw new ArgumentException("Usage: input <index> <value>");

            if (!long.TryParse(parts[1], out long index))
                throw new ArgumentException("Index must be a number.");

            if (!int.TryParse(parts[2], out int value))
                throw new ArgumentException("Value must be an integer.");

            _intManager.WriteElement(index, value);
            _intManager.FlushModifiedPages(); // Принудительно сохраняем изменения на диск
            Console.WriteLine($"Written value {value} at index {index}.");
        }

        private static void HandlePrintCommand(string[] parts)
        {
            if (_intManager == null)
                throw new InvalidOperationException("No virtual memory file opened. Use 'create' first.");
            
            if (parts.Length < 2)
                throw new ArgumentException("Usage: print <index>");
            
            if (!long.TryParse(parts[1], out long index))
                throw new ArgumentException("Index must be a number.");
            
            var value = _intManager.ReadElement(index);
            Console.WriteLine($"Value at index {index}: {value}");
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Available commands:");
            Console.WriteLine("  create <filename>  - Create/open a virtual memory file");
            Console.WriteLine("  input <index> <value> - Write an integer value at index");
            Console.WriteLine("  print <index> - Read value at index");
            Console.WriteLine("  exit - Close the program");
            Console.WriteLine("  help - Show this help");
        }
    }
}