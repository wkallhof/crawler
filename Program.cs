using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Extensions.DependencyInjection;

namespace crawler
{
    class Program
    {
        public async static Task Main(string[] args)
        {
            await Run();
        }

        public static async Task Run()
        {
            var services = Configure();

            var state = services.GetRequiredService<CrawlState>();
            state.EnqueueUrl("https://stage.wmk.io");

            
            var tokenSource = new CancellationTokenSource();

            Enumerable.Range(0, 2).Select(x => services.GetRequiredService<LinkCrawler>()).ToList().ForEach(x => {
                Task.Run(async () => await x.Crawl(tokenSource.Token));
            });

            Console.ReadLine();
            tokenSource.Cancel();
            Console.WriteLine("Stopping");
            Console.ReadLine();
        }

        public static ServiceProvider Configure()
        {
            var services = new ServiceCollection();
            services.AddHttpClient();
            services.AddTransient<LinkCrawler, LinkCrawler>();
            services.AddSingleton<CrawlState>(new CrawlState());

            return services.BuildServiceProvider();
        }
    }

    public class LinkCrawler
    {
        private readonly IHttpClientFactory _clientFactory;
        private readonly IServiceProvider _serviceProvider;
        private readonly CrawlState _crawlState;

        public LinkCrawler(IHttpClientFactory clientFactory, IServiceProvider serviceProvider, CrawlState state)
        {
            this._clientFactory = clientFactory;
            this._serviceProvider = serviceProvider;
            this._crawlState = state;
        }

        public async Task Crawl(CancellationToken cancellationToken)
        {
            
            while(!cancellationToken.IsCancellationRequested){
                try{
                    var url = await this._crawlState.DequeueUrl(cancellationToken);
                    if (url == null){
                        await Task.Delay(100);
                        continue;
                    }
                        
                    var client = this._clientFactory.CreateClient();
                    var response = await client.GetAsync(url);
                    var content = await response.Content.ReadAsStringAsync();

                    Console.WriteLine($"URL:{url.PadRight(100)} QueueLength: {this._crawlState.QueueCount} \t CrawledLinks: {this._crawlState.CrawledCount}");
                    var links = GetLinks(content);

                    if (!links.Any())
                        return;

                    links.ToList().ForEach(x => this._crawlState.EnqueueUrl(EnsureAbsoluteUrl(x, "https://stage.wmk.io")));
                }
                catch(Exception e){
                    Console.WriteLine(e.Message);
                    continue;
                }

            }
        }

        private IEnumerable<string> GetLinks(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return doc.DocumentNode.SelectNodes("//a[@href]").Select(x => x.Attributes["href"].Value);
        }

        private string EnsureAbsoluteUrl(string url, string baseUrl)
        {
            if(url.StartsWith("http") && Uri.TryCreate(url, UriKind.Absolute, out _))
                return url;

            return baseUrl + (url.StartsWith("/") ? url : "/" + url);
        }
    }

    public class CrawlState
    {
        private ConcurrentDictionary<string, string> _crawledLinks = new ConcurrentDictionary<string, string>();
        private ConcurrentQueue<string> _linksToCrawl = new ConcurrentQueue<string>();
        private SemaphoreSlim _signal = new SemaphoreSlim(0);

        public int CrawledCount => _crawledLinks.Count;
        public int QueueCount => _linksToCrawl.Count;

        public void EnqueueUrl(string url){
            if(_crawledLinks.ContainsKey(url))
                return;

            _linksToCrawl.Enqueue(url);
            _signal.Release();
        }

        public async Task<string> DequeueUrl(CancellationToken cancellationToken){
            await _signal.WaitAsync(cancellationToken);
            if(!_linksToCrawl.TryDequeue(out var url)){
                return null;
            }

            if(!_crawledLinks.TryAdd(url, "")){
                //Console.WriteLine("ERROR ADDING TO CRAWLED LINKS");
                return null;
            }

            return url;
        }
        
    }
}

// class ThreadPoolSample  
// {  

//     static async Task Main(string[] args)  
//     {
//         var queue = new ConcurrentQueue<string>();
//         queue.Enqueue("http://www.google.com");
//         queue.Enqueue("http://www.microsoft.com");

//         while(!queue.IsEmpty){
//             if(!queue.TryDequeue(out string url))
//                 continue;

//             //await Process(url, queue);
//             await Task.Run(() => ProcessAsync(url, queue));
//         }

//     }

//     static async Task ProcessAsync(string url, ConcurrentQueue<string> queue){
//         var rand = new Random();
//         Console.WriteLine($"Processing: {url}");
//         if(rand.Next(100) > 50){
//             var newUrls = Enumerable.Range(0, rand.Next(3)).Select(x => "rand" + rand.Next(100)).ToList();
//             newUrls.ForEach(x => queue.Enqueue(x));
//         }
//         await Task.Delay(rand.Next(5000));
//         //Thread.Sleep(rand.Next(5000));
//         Console.WriteLine($"Processing: {url} Complete");
//     }
// }

//     public class Program{
//         static async Task Main(string[] args)  
//         {
//             var queue = new ConcurrentQueue<string>();
//             queue.Enqueue("http://www.google.com");
//             queue.Enqueue("http://www.microsoft.com");

//             var tokenSource = new CancellationTokenSource();


//             Enumerable.Range(0, 5).Select(x => new Processor(queue)).ToList().ForEach(x => {
//                 Task.Run(async () => await x.Watch(tokenSource.Token));
//             });

//             Console.ReadLine();
//             tokenSource.Cancel();
//             Console.WriteLine("Stopping");
//             Console.ReadLine();
//         }

//         static async Task ProcessAsync(string url, ConcurrentQueue<string> queue){
//             var rand = new Random();
//             Console.WriteLine($"Processing: {url}");

//             var newUrls = Enumerable.Range(0, rand.Next(1, 3)).Select(x => "rand" + rand.Next(100)).ToList();
//             newUrls.ForEach(x => queue.Enqueue(x));

//             await Task.Delay(rand.Next(5000));
//             //Thread.Sleep(rand.Next(5000));
//             Console.WriteLine($"Processing: {url} Complete");
//         }
//     }

//     public class Processor{
//         private ConcurrentQueue<string> _queue;
//         public Processor(ConcurrentQueue<string> queue){
//             this._queue = queue;
//         }

//         public async Task Watch(CancellationToken token){
//             while (!token.IsCancellationRequested)
//             {
//                 if (!this._queue.TryDequeue(out var url))
//                 {
//                     await Task.Delay(2);
//                     continue;
//                 }

//                 var rand = new Random();
//                 Console.WriteLine($"Processing: {url}");

//                 var newUrls = Enumerable.Range(0, rand.Next(1,3)).Select(x => "rand" + rand.Next(100)).ToList();
//                 newUrls.ForEach(x => this._queue.Enqueue(x));

//                 await Task.Delay(rand.Next(500, 5000));
//                 //Thread.Sleep(rand.Next(5000));
//                 Console.WriteLine($"Processing: {url} Complete");

//             }
//         }
//     }
// }
