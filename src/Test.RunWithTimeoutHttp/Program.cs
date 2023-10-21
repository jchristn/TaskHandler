using System;
using System.Threading;
using System.Threading.Tasks;
using TaskHandler;
using Timestamps;
using GetSomeInput;
using RestWrapper;

namespace Test.RunWithTimeoutHttp
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            string url = Inputty.GetString("URL:", "http://localhost:8888", false);
            int count = Inputty.GetInteger("Count:", 100, true, false);

            for (int i = 0; i < count; i++)
            {
                await Task.Delay(6000);

                int counter = i; // to prevent multiple messages pointing to the same iteration
                Console.WriteLine("---" + Environment.NewLine + "Starting iteration " + counter);

                CancellationTokenSource cts = new CancellationTokenSource();
                CancellationToken token = cts.Token;

                using (Timestamp ts = new Timestamp())
                {
                    try
                    {
                        TaskRunWithTimeout.Logger = Console.WriteLine;

                        Func<CancellationToken, Task<string>> restRequest = async (CancellationToken token) =>
                        {
                            try
                            {
                                using (RestRequest req = new RestRequest(url))
                                {
                                    req.Logger = Console.WriteLine;

                                    using (RestResponse resp = await req.SendAsync(token))
                                    {
                                        if (resp == null)
                                        {
                                            Console.WriteLine(counter + ": (null response)");
                                            return null;
                                        }
                                        else
                                        {
                                            Console.WriteLine(counter + ": " + resp.StatusCode + " (" + ts.TotalMs + "ms)");
                                            return resp.DataAsString;
                                        }
                                    }
                                }
                            }
                            catch (Exception eInner)
                            {
                                Console.WriteLine(counter + ": inner exception: " + eInner.Message);
                                throw;
                            }
                        };

                        await TaskRunWithTimeout.Go(restRequest(token), 2500, cts);
                    }
                    catch (TimeoutException)
                    {
                        Console.WriteLine(counter + ": timeout after " + ts.TotalMs + "ms");
                    }
                    catch (Exception eOuter)
                    {
                        Console.WriteLine(counter + ": outer exception: " + eOuter.ToString());
                    }
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