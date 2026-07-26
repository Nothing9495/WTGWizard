using System;
using System.Threading.Tasks;

namespace WTGWizard.Worker;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("WTGWizard Worker started");

        await Task.Delay(1000);
    }
}
