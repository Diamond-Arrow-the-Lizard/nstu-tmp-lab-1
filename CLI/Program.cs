using VirtualMemory.Interfaces;
using VirtualMemory.Models;

namespace VirtualMemory.CLI
{
    public static class Program
    {
        private static IVirtualMemoryManager<int>? _intManager;
        private static IVirtualMemoryManager<string>? _fixedStringManager;
        private static IVirtualMemoryManager<string>? _varStringManager; 
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
                    _fixedStringManager?.Dispose();
                    _varStringManager?.Dispose(); 
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
            if (parts.Length < 3)
                throw new ArgumentException("Usage: create <filename> <type> [args]");

            var filename = parts[1];
            var typeSpec = parts[2].ToLower();

            if (typeSpec == "int")
            {
                _intManager = new IntMemoryManager(
                    bufferSize: 3,
                    serializer: new IntSerializer(),
                    filename: filename
                );
                _fixedStringManager = null;
                _varStringManager = null; 
                Console.WriteLine($"Integer array file '{filename}' created.");
            }
            else if (typeSpec.StartsWith("char(") && typeSpec.EndsWith(")"))
            {
                int length = int.Parse(typeSpec[5..^1]);
                _fixedStringManager = new FixedStringMemoryManager(
                    bufferSize: 3,
                    filename: filename,
                    stringLength: length
                );
                _intManager = null;
                _varStringManager = null; 
                Console.WriteLine($"Fixed-length string array (length={length}) file '{filename}' created.");
            }
            else if (typeSpec.StartsWith("varchar(") && typeSpec.EndsWith(")"))  
            {
                int maxLength = int.Parse(typeSpec[8..^1]);
                _varStringManager = new VarCharMemoryManager(
                    bufferSize: 3,
                    pageFileName: $"{filename}_pages", // Separate file for pages
                    stringFileName: $"{filename}_strings", // Separate file for strings
                    maxStringLength: maxLength
                );
                _intManager = null;
                _fixedStringManager = null;
                Console.WriteLine($"Variable-length string array (max length={maxLength}) files '{filename}_pages' and '{filename}_strings' created.");
            }
            else
            {
                throw new ArgumentException("Invalid type. Use 'int', 'char(length)', or 'varchar(maxLength)'");
            }
        }

        private static void HandleInputCommand(string[] parts)
        {
            try
            {
                if (_intManager == null && _fixedStringManager == null && _varStringManager == null) 
                    throw new InvalidOperationException("No virtual memory file opened. Use 'create' first.");

                if (parts.Length < 3)
                    throw new ArgumentException("Usage: input <index> <value>");

                if (!long.TryParse(parts[1], out long index))
                    throw new ArgumentException("Index must be a number.");

                if (_intManager != null)
                {
                    if (!int.TryParse(parts[2], out int value))
                        throw new ArgumentException("Value must be an integer for int array.");
                    _intManager.WriteElement(index, value);
                    _intManager.FlushModifiedPages();
                    Console.WriteLine($"Written integer {value} at index {index}.");
                }
                else if (_fixedStringManager != null)
                {
                    string value = parts[2];
                    if (value.StartsWith('"') && value.EndsWith('"'))
                        value = value[1..^1];
                    _fixedStringManager.WriteElement(index, value);
                    _fixedStringManager.FlushModifiedPages();
                    Console.WriteLine($"Written string \"{value}\" at index {index}.");
                }
                else if (_varStringManager != null)  
                {
                    string value = parts[2];
                    if (value.StartsWith('"') && value.EndsWith('"'))
                        value = value[1..^1];
                    _varStringManager.WriteElement(index, value);
                    _varStringManager.FlushModifiedPages();
                    Console.WriteLine($"Written string \"{value}\" at index {index}.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Write failed: {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine($"Details: {ex.InnerException.Message}");
            }
        }

        private static void HandlePrintCommand(string[] parts)
        {
            if (_intManager == null && _fixedStringManager == null && _varStringManager == null) 
                throw new InvalidOperationException("No virtual memory file opened. Use 'create' first.");

            if (parts.Length < 2)
                throw new ArgumentException("Usage: print <index>");

            if (!long.TryParse(parts[1], out long index))
                throw new ArgumentException("Index must be a number.");

            if (_intManager != null)
            {
                var value = _intManager.ReadElement(index);
                Console.WriteLine($"Value at index {index}: {value} (int)");
            }
            else if (_fixedStringManager != null)
            {
                var value = _fixedStringManager.ReadElement(index);
                Console.WriteLine($"Value at index {index}: \"{value}\" (string)");
            }
            else if (_varStringManager != null)  
            {
                var value = _varStringManager.ReadElement(index);
                Console.WriteLine($"Value at index {index}: \"{value}\" (varchar)");
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Available commands:");
            Console.WriteLine("  create <filename> int - Create integer array");
            Console.WriteLine("  create <filename> char(<length>) - Create fixed-length string array");
            Console.WriteLine("  create <filename> varchar(<maxLength>) - Create variable-length string array"); 
            Console.WriteLine("  input <index> <value> - Write value at index (strings in quotes)");
            Console.WriteLine("  print <index> - Read value at index");
            Console.WriteLine("  exit - Close the program");
            Console.WriteLine("  help - Show this help");
        }
    }
}