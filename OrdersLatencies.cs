using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Analysis.Storages;
using Core.Model;
using Shared.Database;
using JM.LinqFaster;
using Microsoft.EntityFrameworkCore;

#nullable enable

namespace Analysis
{
    public class OrdersLatencies
    {
        public static async Task Analyse()
        {
            var connectionString = "host=database.cryptolp.net;Database=cryptolpaax50;User Id=postgres;password=123!crypto";

            // await ShowOrderCreationDistribution(connectionString);
            // await ShowOrderCancellationDistribution(connectionString);
            // await ShowTradesLatenciesDistribution(connectionString);
            await ShowOrderBookLatenciesDistribution(connectionString);
            //ShowDelayBetweenOrders(orders, file);
        }

        private static void ShowDelayBetweenOrders(List<Order> orders, StreamWriter file)
        {
            var timer = Stopwatch.StartNew();
            var wraps = orders.SelectF(x => new OrderDelayWrap(x));

            var filtered = wraps.WhereF(x => x.IsFiltered);
            file.WriteLine($"Filtered when wrapping {filtered.Count} orders");

            var selected = wraps.WhereF(x => !x.IsFiltered);

            var sortedByCreated = selected.OrderByF(x => x.CreatedTime);
            var sortedByFinished = selected
                .WhereF(x => x.FinishedTime != null)
                .OrderByF(x => x.FinishedTime);

            var distribution = new Dictionary<int, int>
            {
                [0] = 0
            };
            
            var notFoundPreviousOrderCount = 0;
            var bigDelayOrders = new List<BigDelay>();
            foreach (var orderWrap in selected)
            {
                var createdTime = orderWrap.CreatedTime;
                var beforeCreated = createdTime.AddTicks(-1);

                var foundByCreatedTimeIndex = BinarySearchLess(sortedByCreated,
                    new OrderDelayWrap(selected[0].Order)
                    {
                        CreatedTime = beforeCreated
                    },
                    Comparer<OrderDelayWrap>.Create((x, y) => x.CreatedTime.CompareTo(y.CreatedTime)));

                if (foundByCreatedTimeIndex != null)
                {
                    var orderByCreatedTime = sortedByCreated[foundByCreatedTimeIndex.Value];
                    if (orderByCreatedTime.FinishedTime >= createdTime)
                    {
                        distribution[0] = distribution[0] + 1;

                        continue;
                    }
                }

                var foundByFinishedTimeIndex = BinarySearchLess(sortedByFinished,
                    new OrderDelayWrap(selected[0].Order)
                    {
                        FinishedTime = beforeCreated
                    },
                    Comparer<OrderDelayWrap>.Create((x, y) => x.FinishedTime.GetValueOrDefault().CompareTo(y.FinishedTime.GetValueOrDefault())));

                if (foundByFinishedTimeIndex != null)
                {
                    var orderByFinishedTime = sortedByFinished[foundByFinishedTimeIndex.Value];

                    var finishedTime = orderByFinishedTime.FinishedTime.GetValueOrDefault();
                    var delay = (int)(createdTime - finishedTime).TotalMilliseconds;

                    if (200 <= delay && delay <= 1_000)
                    {
                        bigDelayOrders.Add(new BigDelay(delay, orderByFinishedTime, orderWrap));
                    }
                    
                    IncrementDistributionCase(distribution, delay);
                }
                else
                {
                    notFoundPreviousOrderCount += 1;
                }
            }

            file.WriteLine($"Not found previous order for {notFoundPreviousOrderCount} orders");

            file.WriteLine("Distribution of delays between orders:");
            var sortedDistribution = distribution.Select(x => (x.Key, Count: x.Value)).OrderBy(x => x.Key).ToList();
            
            foreach (var pair in sortedDistribution)
            {
                file.WriteLine($"{pair.Key:D6} ms, count {pair.Count}");
            }
            
            file.WriteLine();
            file.WriteLine("Orders for big delays:");

            foreach (var bigDelay in bigDelayOrders)
            {
                file.WriteLine($"Delay: {bigDelay.Delay}");
                var prevOrder = bigDelay.PreviousOrder;
                file.WriteLine($"Previous order {prevOrder.Order.ClientOrderId} {prevOrder.FinishedTime.GetValueOrDefault():O}");
                var order = bigDelay.Order;
                file.WriteLine($"Current order  {order.Order.ClientOrderId} {order.CreatedTime:O}");
                file.WriteLine();
            }

            file.WriteLine($"Distributions counts sum {sortedDistribution.SumF(x => x.Count)}");
            file.WriteLine($"Big delays sum {bigDelayOrders.Count}");

            file.WriteLine($"Calculation time: {timer.ElapsedMilliseconds} ms");
        }

        private static void IncrementDistributionCase(Dictionary<int, int> distribution, int delay)
        {
            if (distribution.TryGetValue(delay, out var count))
            {
                distribution[delay] = count + 1;
            }
            else
            {
                distribution[delay] = 1;
            }
        }

        private static int? BinarySearchLess(List<OrderDelayWrap> list, OrderDelayWrap item,
            Comparer<OrderDelayWrap> comparer)
        {
            var resIndex = list.BinarySearch(
                item, comparer);

            if (resIndex > 0)
            {
                return resIndex;
            }

            resIndex = ~resIndex;

            for (int i = resIndex - 1; i >= 0; i--)
            {
                if (comparer.Compare(list[i], item) == -1)
                {
                    return i;
                }
            }

            return null;
        }

        private static async Task ShowOrderCreationDistribution(string connectionString)
        {
            using var file = File.CreateText("Analysis(creation).txt");
            var orders = await LoadOrders(connectionString, file);

            var wraps = orders.SelectF(x => new OrderCreationWrap(x));

            var filtered = wraps.WhereF(x => x.IsFiltered);
            file.WriteLine($"Filtered {filtered.Count} orders");

            var selectedOrders = wraps.WhereF(x => !x.IsFiltered);

            file.WriteLine("Distribution of orders latencies:");

            var latencyGroupsByExchangeName = selectedOrders
                .GroupBy(x => (x.Order.ExchangeName, x.Latency))
                .GroupBy(x => x.Key.ExchangeName);

            foreach (var latencyGroups in latencyGroupsByExchangeName)
            {
                file.WriteLine(latencyGroups.Key);
                file.WriteLine();
                
                foreach (var latencyGroup in latencyGroups.OrderBy(x => x.Key.Latency))
                {
                    file.WriteLine($"{latencyGroup.Key.Latency:D6} ms, count {latencyGroup.Count()}");
                }
                file.WriteLine();
            }
        }

        private static async Task ShowOrderCancellationDistribution(
            string connectionString)
        {
            using var file = File.CreateText("Analysis(cancellation).txt");
            var orders = await LoadOrders(connectionString, file);
            
            var wraps = orders.SelectF(x => new OrderCancellationWrap(x));

            var filtered = wraps.WhereF(x => x.IsFiltered);
            file.WriteLine($"Filtered {filtered.Count} orders");

            var selectedOrders = wraps.WhereF(x => !x.IsFiltered);

            file.WriteLine("Distribution of orders latencies:");

            var latencyGroupsByExchangeName = selectedOrders
                .GroupBy(x => (x.Order.ExchangeName, x.Latency))
                .GroupBy(x => x.Key.ExchangeName);

            foreach (var latencyGroups in latencyGroupsByExchangeName)
            {
                file.WriteLine(latencyGroups.Key);
                file.WriteLine();
                
                foreach (var latencyGroup in latencyGroups.OrderBy(x => x.Key.Latency))
                {
                    file.WriteLine($"{latencyGroup.Key.Latency:D6} ms, count {latencyGroup.Count()}");
                }
                file.WriteLine();
            }
        }

        private static async Task<List<Order>?> LoadOrders(string connectionString, StreamWriter? file)
        {
            var builder = new DbContextOptionsBuilder<BotDbContext>();
            builder.UseNpgsql(connectionString);

            var sqlCtx = new BotDbContext(builder.Options);

            var timer = Stopwatch.StartNew();
            //var ordersCount = 100;
            var ordersCount = 1_000_000;
            var orders = await sqlCtx.QueryOrders().OrderByDescending(x => x.DateTime).Take(ordersCount).ToListAsync();

            file.WriteLine($"Loaded {orders.Count} orders ({timer.ElapsedMilliseconds} ms)");

            var minTime = orders.MinF(x => x.DateTime);
            var maxTime = orders.MaxF(x => x.DateTime);

            file.WriteLine($"Orders time approximately {minTime} - {maxTime}");
            return orders;
        }
        
        private static async Task ShowTradesLatenciesDistribution(string connectionString)
        {
            using var file = File.CreateText("Analysis(trades).txt");
            var trades = await MartenTypes.LoadTrades(connectionString, file);
            
            file.WriteLine("Distribution of trades latencies:");

            var latencyGroupsByExchangeName = trades
                .SelectMany(x => x.TradeItems, (x, y) => (x.ExchangeName, Latency: (int)(x.DateTime - y.TransactionTime).TotalMilliseconds))
                .GroupBy(x => x.ExchangeName)
                .ToDictionary(x => x.Key,
                    x => x.GroupBy(y => y.Latency)
                        .OrderBy(y => y.Key));

            foreach (var latencyGroups in latencyGroupsByExchangeName)
            {
                file.WriteLine(latencyGroups.Key);
                file.WriteLine();
                
                foreach (var latencyGroup in latencyGroups.Value)
                {
                    file.WriteLine($"{latencyGroup.Key:D6} ms, count {latencyGroup.Count()}");
                }
                
                file.WriteLine();
            }
        }
        
        private static async Task ShowOrderBookLatenciesDistribution(string connectionString)
        {
            using var file = File.CreateText("Analysis(orderbook).txt");
            var orderBooks = await MartenTypes.LoadOrderBook(connectionString, file);

            var groups = orderBooks.GroupBy(x => x.ExchangeDateTime == null).ToDictionary(x => x.Key);

            file.WriteLine($"{groups[true].Count()} orderbooks with ExchangeDateTime == null");

            
            file.WriteLine("Distribution of latencies:");
            
            var latencyGroupsByExchangeName = groups[false]
                .Select(x => (x.ExchangeName, Latency: (int)(x.DateTime - x.ExchangeDateTime.Value).TotalMilliseconds))
                .GroupBy(x => x.ExchangeName)
                .ToDictionary(x => x.Key,
                    x => x.GroupBy(y => y.Latency)
                        .OrderBy(y => y.Key));

            foreach (var latencyGroups in latencyGroupsByExchangeName)
            {
                file.WriteLine(latencyGroups.Key);
                file.WriteLine();
                
                foreach (var latencyGroup in latencyGroups.Value)
                {
                    file.WriteLine($"{latencyGroup.Key:D6} ms, count {latencyGroup.Count()}");
                }
                
                file.WriteLine();
            }
        }
    }

    public class BigDelay
    {
        public BigDelay(int delay, OrderDelayWrap previousOrder, OrderDelayWrap order)
        {
            Delay = delay;
            PreviousOrder = previousOrder;
            Order = order;
        }

        public int Delay;
        public OrderDelayWrap PreviousOrder;
        public OrderDelayWrap Order;
    }

    public class OrderDelayWrap
    {
        public OrderDelayWrap(Order order)
        {
            Order = order;

            var timingsOpt = CalcTimings(order);
            if (timingsOpt == null)
            {
                IsFiltered = true;
                return;
            }

            var t = timingsOpt.GetValueOrDefault();
            CreatingTime = t.CreatingTime;
            CreatedTime = t.CreatedTime;
            FinishedTime = t.FinishedTime;
        }

        public bool IsFiltered;
        public DateTime CreatingTime;
        public DateTime CreatedTime;
        public DateTime? FinishedTime;
        public Order Order;

        private static (DateTime CreatingTime, DateTime CreatedTime, DateTime? FinishedTime)? CalcTimings(Order order)
        {
            if (order.StatusChanges.Count < 3)
            {
                return null;
            }

            var creatingTime = order.StatusChanges.First(x => x.Status == OrderStatus.Creating);
            if (creatingTime == null)
            {
                return null;
            }

            var createdTime = order.StatusChanges.First(x => x.Status == OrderStatus.Created);
            if (createdTime == null)
            {
                return null;
            }

            var finishedTime = order.FinishedDateTime;

            return (creatingTime.DateTime, createdTime.DateTime, finishedTime);
        }
    }

    public class OrderCreationWrap
    {
        public OrderCreationWrap(Order order)
        {
            Order = order;

            var latencyOpt = CalcLatency(order);
            if (latencyOpt == null)
            {
                IsFiltered = true;
                Latency = 0;

                return;
            }

            Latency = latencyOpt.Value;
        }

        public bool IsFiltered;
        public int Latency;
        public Order Order;

        private static int? CalcLatency(Order order)
        {
            if (order.StatusChanges.Count < 2)
            {
                return null;
            }

            var oscCreating = order.StatusChanges.FirstOrDefault(x => x.Status == OrderStatus.Creating);
            if (oscCreating == null)
            {
                return null;
            }

            var oscCreated = order.StatusChanges.FirstOrDefault(x => x.Status == OrderStatus.Created);
            if (oscCreated == null)
            {
                return null;
            }

            return (int) (oscCreated.DateTime - oscCreating.DateTime).TotalMilliseconds;
        }
    }
    
    public class OrderCancellationWrap
    {
        public OrderCancellationWrap(Order order)
        {
            Order = order;

            var latencyOpt = CalcLatency(order);
            if (latencyOpt == null)
            {
                IsFiltered = true;
                Latency = 0;

                return;
            }

            Latency = latencyOpt.Value;
        }

        public bool IsFiltered;
        public int Latency;
        public Order Order;

        private static int? CalcLatency(Order order)
        {
            if (order.StatusChanges.Count < 2)
            {
                return null;
            }

            var oscCanceling = order.StatusChanges.FirstOrDefault(x => x.Status == OrderStatus.Canceling);
            if (oscCanceling == null)
            {
                return null;
            }

            var oscCanceled = order.StatusChanges.FirstOrDefault(x => x.Status == OrderStatus.Canceled);
            if (oscCanceled == null)
            {
                return null;
            }

            return (int) (oscCanceled.DateTime - oscCanceling.DateTime).TotalMilliseconds;
        }
    }
}