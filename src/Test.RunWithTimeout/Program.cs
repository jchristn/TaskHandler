using System;
using System.Reflection.Metadata.Ecma335;
using System.Threading;
using System.Threading.Tasks;
using TaskHandler;

namespace Test.RunWithTimeout
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            // string result = null;
            Person result = null;
            int delay;

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            CancellationToken token = tokenSource.Token;

            for (int i = 0; i < 100; i++)
            {
                try
                {
                    if (i % 2 == 0) delay = 3000;
                    else delay = 500;
                    result = await TaskRunWithTimeout.Go(
                        Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(delay);
                                // return "hello from " + i + "!"; 
                                return new Person { FirstName = "Hello", LastName = i.ToString() };
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e.ToString());
                                return null;
                            }
                        }), 
                        2500,
                        token);

                    Console.WriteLine(i + ": " + result.FirstName + " " + result.LastName);
                }
                catch (TimeoutException)
                {
                    Console.WriteLine(i + ": timeout");
                }
            }
        }

        public class Person
        {
            public string FirstName { get; set; }
            public string LastName { get; set; }
        }
    }
}