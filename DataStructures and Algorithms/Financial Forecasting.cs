using System;

namespace FinancialForecasting
{
    class Program
    {
        // Recursive method to calculate future value
        static double PredictFutureValue(double currentValue, double growthRate, int years)
        {
            // Base Case
            if (years == 0)
                return currentValue;

            // Recursive Case
            return PredictFutureValue(currentValue * (1 + growthRate), growthRate, years - 1);
        }

        static void Main(string[] args)
        {
            double currentValue = 10000;     // Initial investment
            double growthRate = 0.10;        // 10% annual growth
            int years = 5;

            double futureValue = PredictFutureValue(currentValue, growthRate, years);

            Console.WriteLine($"Current Value : {currentValue:C}");
            Console.WriteLine($"Growth Rate   : {growthRate:P}");
            Console.WriteLine($"Years         : {years}");
            Console.WriteLine($"Future Value  : {futureValue:C}");
        }
    }
}