namespace GMSL.Logger;

public static class Logger
{
    private static void Log(object message, LogLevel level)
    {
        switch (level)
        {
            case LogLevel.Info:
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"  [INFO] {message}");
                break;
            
            case LogLevel.Warning:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[WARNING] {message}");
                break;
            
            case LogLevel.Error:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [ERROR] {message}");
                break;
        }
    }

    public static void Info(object message) => Log(message, LogLevel.Info);
    public static void Warn(object message) => Log(message, LogLevel.Warning);
    public static void Error(object message) => Log(message, LogLevel.Error);
}