using System;
using System.Reflection.Metadata.Ecma335;
using System.Threading.Tasks;
using TaskHandler;

namespace Test.RunWithTimeout
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            string result = null;
            int delay;

            for (int i = 0; i < 100; i++)
            {
                try
                {
                    if (i % 2 == 0) delay = 3000;
                    else delay = 500;
                    result = await TaskRunWithTimeout.Go(
                        Task.Run(async () =>
                        { 
                            await Task.Delay(delay); 
                            return "hello from " + i + "!"; 
                        }), 
                        2500);
                    Console.WriteLine(i + ": " + result);
                }
                catch (TimeoutException)
                {
                    Console.WriteLine(i + ": timeout");
                }
            }
        }
    }
}