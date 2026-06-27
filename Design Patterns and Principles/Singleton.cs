using System;

namespace SingletonPatternExample
{
    // Singleton Class
    public sealed class Logger
    {
        // Static instance created once
        private static readonly Logger instance = new Logger();

        // Private constructor
        private Logger()
        {
            Console.WriteLine("Logger Instance Created");
        }

        // Public method to access the instance
        public static Logger GetInstance()
        {
            return instance;
        }

        public void Log(string message)
        {
            Console.WriteLine($"[LOG] {message}");
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Logger logger1 = Logger.GetInstance();
            Logger logger2 = Logger.GetInstance();

            logger1.Log("Application Started");
            logger2.Log("User Logged In");

            Console.WriteLine();

            Console.WriteLine($"logger1 HashCode : {logger1.GetHashCode()}");
            Console.WriteLine($"logger2 HashCode : {logger2.GetHashCode()}");

            Console.WriteLine();

            if (logger1 == logger2)
                Console.WriteLine("Only one Logger instance exists (Singleton).");
            else
                Console.WriteLine("Multiple instances created.");
        }
    }
}