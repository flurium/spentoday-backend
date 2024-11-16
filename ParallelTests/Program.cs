using Data;
using Data.Models.ShopTables;
using Lib;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

// Testing parralel excecution of queries

Env.LoadFile(Path.Combine(Directory.GetCurrentDirectory(), ".env"));
//var testShopId = "466f6435-3246-4986-9179-01ca220c4a82";
var dbConnectionString = Env.GetRequired("DB_CONNECTION_STRING");
var dbOptions = new DbContextOptionsBuilder().UseNpgsql(dbConnectionString).Options;
{
    using Db db = new(dbOptions);
}
int chunkSize = 25000;
int chunks = 5;

var stopWatch = new Stopwatch();

long sumGetSubscriptionsSync = 0;
long sumGetSubscriptionsAsync = 0;
long sumGetSubscriptionsTpl = 0;
long sumGetSubscriptionsMultithreading = 0;
long sumGetSubscriptionsMulticore = 0;
long sumGetSubscriptionsParallel = 0;
float testRuns = 100;

for (int i = 0; i < testRuns; i++)
{
    stopWatch.Start();
    GetSubscriptionsSync();
    stopWatch.Stop();
    sumGetSubscriptionsSync += stopWatch.ElapsedMilliseconds;
    Console.WriteLine($"GetSubscriptionsSync: {stopWatch.ElapsedMilliseconds} ms");

    stopWatch.Restart();
    await GetSubscriptionsAsync();
    stopWatch.Stop();
    sumGetSubscriptionsAsync += stopWatch.ElapsedMilliseconds;
    Console.WriteLine($"GetSubscriptionsAsync: {stopWatch.ElapsedMilliseconds} ms");

    stopWatch.Restart();
    await GetSubscriptionsTpl();
    stopWatch.Stop();
    sumGetSubscriptionsTpl += stopWatch.ElapsedMilliseconds;
    Console.WriteLine($"GetSubscriptionsTpl: {stopWatch.ElapsedMilliseconds} ms");

    stopWatch.Restart();
    GetSubscriptionsMultithreading();
    stopWatch.Stop();
    sumGetSubscriptionsMultithreading += stopWatch.ElapsedMilliseconds;
    Console.WriteLine($"GetSubscriptionsMultithreading: {stopWatch.ElapsedMilliseconds} ms");

    stopWatch.Restart();
    GetSubscriptionsMulticore();
    stopWatch.Stop();
    sumGetSubscriptionsMulticore += stopWatch.ElapsedMilliseconds;
    Console.WriteLine($"GetSubscriptionsMulticore: {stopWatch.ElapsedMilliseconds} ms");

    stopWatch.Restart();
    GetSubscriptionsParallel();
    stopWatch.Stop();
    sumGetSubscriptionsParallel += stopWatch.ElapsedMilliseconds;
    Console.WriteLine($"GetSubscriptionsParallel: {stopWatch.ElapsedMilliseconds} ms");
}

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"Average GetSubscriptionsSync: {sumGetSubscriptionsSync / testRuns} ms");
Console.WriteLine($"Average GetSubscriptionsAsync: {sumGetSubscriptionsAsync / testRuns} ms");
Console.WriteLine($"Average GetSubscriptionsTpl: {sumGetSubscriptionsTpl / testRuns} ms");
Console.WriteLine($"Average GetSubscriptionsMultithreading: {sumGetSubscriptionsMultithreading / testRuns} ms");
Console.WriteLine($"Average GetSubscriptionsMulticore: {sumGetSubscriptionsMulticore / testRuns} ms");
Console.WriteLine($"Average GetSubscriptionsParallel: {sumGetSubscriptionsParallel / testRuns} ms");
Console.ResetColor();

List<Subscription> GetSubscriptionsSync()
{
    using Db db = new(dbOptions);
    return db.ShopSubscriptions.ToList();
}

async Task<List<Subscription>> GetSubscriptionsAsync()
{
    using Db db = new(dbOptions);
    return await db.ShopSubscriptions.ToListAsync();
}

List<Subscription> GetSubscriptionsChunk(int i, int chunkSize, bool isLast)
{
    using Db db = new(dbOptions);

    if (isLast)
    {
        return db.ShopSubscriptions.Skip(i * chunkSize).ToList();
    }
    else
    {
        return db.ShopSubscriptions.Skip(i * chunkSize).Take(chunkSize).ToList();
    }
}

async Task<List<Subscription>> GetSubscriptionsTpl()
{
    List<Task<List<Subscription>>> tasks = new(chunks);

    for (int i = 0; i < chunks; i += 1)
    {
        tasks.Add(Task.Run(() => GetSubscriptionsChunk(i, chunkSize, i == chunks - 1)));
    }

    var subscriptionsMatrix = await Task.WhenAll(tasks);
    return FlattenSubscriptions(subscriptionsMatrix);
}

List<Subscription> GetSubscriptionsMultithreading()
{
    List<Thread> threads = new(chunks);
    var subscriptionsMatrix = new List<Subscription>[chunks];

    for (int i = 0; i < chunks; i += 1)
    {
        var chunkIndex = i;
        var thread = new Thread(() =>
        {
            subscriptionsMatrix[chunkIndex] = GetSubscriptionsChunk(
                chunkIndex, chunkSize, chunkIndex == chunks - 1
            );
        });
        thread.Start();
        threads.Add(thread);
    }

    foreach (var thread in threads)
    {
        thread.Join();
    }

    return FlattenSubscriptions(subscriptionsMatrix);
}

List<Subscription> GetSubscriptionsMulticore()
{
    List<Thread> threads = new(chunks);
    var subscriptionsMatrix = new List<Subscription>[chunks];

    for (int i = 0; i < chunks; i += 1)
    {
        var chunkIndex = i;
        var thread = new Thread(() =>
        {
            subscriptionsMatrix[chunkIndex] = GetSubscriptionsChunk(
                chunkIndex, chunkSize, chunkIndex == chunks - 1
            );
        });

        // Ensure each thread runs on separate core
        var process = Process.GetCurrentProcess();
        int coreMask = 1 << (chunkIndex % Environment.ProcessorCount);
        foreach (ProcessThread processThread in process.Threads)
        {
            if (processThread.Id == thread.ManagedThreadId)
            {
                processThread.ProcessorAffinity = (IntPtr)coreMask;
                break;
            }
        }

        thread.Start();
        threads.Add(thread);
    }

    foreach (var thread in threads)
    {
        thread.Join();
    }

    return FlattenSubscriptions(subscriptionsMatrix);
}

List<Subscription> GetSubscriptionsParallel()
{
    var subscriptionsMatrix = new List<Subscription>[chunks];

    var res = Parallel.For(0, chunks, (i) =>
    {
        subscriptionsMatrix[i] = GetSubscriptionsChunk(
            i, chunkSize, i == chunks - 1
        );
    });

    return FlattenSubscriptions(subscriptionsMatrix);
}

List<Subscription> FlattenSubscriptions(IEnumerable<List<Subscription>> subscriptionsMatrix)
{
    int subscriptionsCount = 0;
    foreach (var subscriptionsRow in subscriptionsMatrix)
    {
        subscriptionsCount += subscriptionsRow.Count;
    }

    var subscriptions = new List<Subscription>(subscriptionsCount);
    foreach (var subscriptionsRow in subscriptionsMatrix)
    {
        foreach (var subscription in subscriptionsRow)
        {
            subscriptions.Add(subscription);
        }
    }
    return subscriptions;
}