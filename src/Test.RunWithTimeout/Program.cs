namespace Test.RunWithTimeout
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using GetSomeInput;
    using TaskHandler;

    public static class Program
    {
        public static async Task Main(string[] args)
        {
            // string result = null;
            Person result1 = null;
            int delay;

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            CancellationToken token = tokenSource.Token;

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (i % 2 == 0) delay = 3000;
                    else delay = 500;

                    Func<CancellationToken, Task<Person>> task1 = async (CancellationToken token) =>
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
                    };

                    result1 = await TaskRunWithTimeout.Go(task1(token), 2500, tokenSource);

                    if (result1 != null)
                        Console.WriteLine(i + ": " + result1.FirstName + " " + result1.LastName);
                    else
                        Console.WriteLine(i + ": null");
                }
                catch (TimeoutException)
                {
                    Console.WriteLine(i + ": timeout");
                }
            }

            Console.WriteLine("");
            string input = Inputty.GetString("Text to echo:", null, false);

            Func<string, CancellationToken, Task<string>> task2 = async (string text, CancellationToken token) =>
            {
                return text;
            };

            string result2 = await TaskRunWithTimeout.Go(task2("hello world", token), 2500, tokenSource);
            Console.WriteLine("Returned: " + result2);

            Console.WriteLine("");
        }

        public class Person
        {
            public string FirstName { get; set; }
            public string LastName { get; set; }
        }
    }
}