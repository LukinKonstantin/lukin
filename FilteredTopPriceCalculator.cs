using System;
using System.Collections.Generic;
using Core.Model;
using Core.Model.Enums;
using Core.Model.OrderBook.Snapshots;
using Core.Model.OrderBook.Snapshots.L1;
using Core.Model.OrderBook.Snapshots.L2;
using JM.LinqFaster;

#nullable enable

namespace CryptoLp.Bot.ExchangeLiquidity.TargetTrading.DesiredPrice
{
    public static class FilteredTopPriceCalculator
    {
        private readonly struct OrderBookSideFlat
        {
            public readonly int Size;
            public readonly decimal[] Prices;
            public readonly decimal[] Amounts;
            public readonly IComparer<decimal> Comparer;

            public OrderBookSideFlat(SortedDictionary<decimal, decimal> side)
            {
                Size = side.Count;
                Prices = new decimal[Size];
                Amounts = new decimal[Size];
                Comparer = side.Comparer;

                var index = 0;
                foreach (var (price, amount) in side)
                {
                    Prices[index] = price;
                    Amounts[index] = amount;
                    index++;
                }
            }
        }
        
        public static decimal? GetFilteredTopMidPrice(
            IEnumerable<Order> myNotFinishedOrders,
            ILocalOrderBookSnapshot snapshot)
        {
            var (asks, bids) = FlattenSnapshot(snapshot);
            RemoveOrders(myNotFinishedOrders, asks, bids);
            var prices = FilterByPercentileTopPrices(asks, bids);
            if (prices.TopAsk == null || prices.TopBid == null)
            {
                return null;
            }
            
            return (prices.TopAsk.Value + prices.TopBid.Value) / 2;
        }

        public static PriceByOrderSide GetTopPrices(
            IEnumerable<Order> myNotFinishedOrders,
            ILocalOrderBookSnapshot snapshot)
        {
            var (asks, bids) = FlattenSnapshot(snapshot);
            RemoveOrders(myNotFinishedOrders, asks, bids);
            return FilterByPercentileTopPrices(asks, bids);
        }

        private static (OrderBookSideFlat asks, OrderBookSideFlat bids) FlattenSnapshot(ILocalOrderBookSnapshot snapshot)
        {
            SortedDictionary<decimal, decimal> asks, bids;
            switch (snapshot)
            {
                case L1LocalOrderBookSnapshot l1:
                    asks = l1.GetAsks();
                    bids = l1.GetBids();
                    break;
                case L2LocalOrderBookSnapshot l2:
                    asks = l2.Asks;
                    bids = l2.Bids;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(snapshot), snapshot, null);
            }
            
            return (new OrderBookSideFlat(asks), new OrderBookSideFlat(bids));
        }

        private static void RemoveOrders(
            IEnumerable<Order> notFinishedOrders,
            OrderBookSideFlat asks,
            OrderBookSideFlat bids)
        {
            foreach (var order in notFinishedOrders)
            {
                if (!order.Price.HasValue)
                {
                    continue;
                }

                var bookSide = order.Side == OrderSide.Buy ? bids : asks;
                var index = Array.BinarySearch(bookSide.Prices, order.Price.Value, bookSide.Comparer);
                if (index >= 0)
                {
                    bookSide.Amounts[index] -= order.Amount - order.FilledAmount;
                }
            }
        }

        private static PriceByOrderSide FilterByPercentileTopPrices(OrderBookSideFlat asks, OrderBookSideFlat bids)
        {
            const decimal percentileRate = 0.1m;
            var topAsk = FilterByAmountPercentileAndGetTop(asks, percentileRate);
            var topBid = FilterByAmountPercentileAndGetTop(bids, percentileRate);
            return new PriceByOrderSide(topBid, topAsk);
        }

        private static decimal? FilterByAmountPercentileAndGetTop(OrderBookSideFlat bookSide, decimal percentileRate)
        {
            if (bookSide.Size == 0)
            {
                return null;
            }
            
            var percentileAmount = bookSide.Size >= 5
                ? GetPercentileAmount(bookSide.Amounts, percentileRate)
                : 0m; // it is not necessary to filter side if there are not enough levels
            
            // prices are sorted, so we can go from top (because we want to remove top positions only)
            for (int i = 0; i < bookSide.Size; i++)
            {
                if (bookSide.Amounts[i] > percentileAmount)
                {
                    return bookSide.Prices[i];
                }
            }

            return null;
        }

        private static decimal GetPercentileAmount(decimal[] amounts, decimal percentileRate)
        {
            var sequence = amounts.WhereF(x => x > 0);
            var count = sequence.Length;
            if (count == 1)
            {
                return sequence[0];
            }

            Array.Sort(sequence);
            var n = (count - 1) * percentileRate + 1;
            var k = (int) n;
            if (k == count)
            {
                return sequence[count - 1];
            }

            var d = n - k;
            return sequence[k - 1] + d * (sequence[k] - sequence[k - 1]);
        }
    }
}
