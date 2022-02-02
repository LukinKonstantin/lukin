using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Core.Bot.LocalSnapshots;
using Core.Exchanges.Core;
using Core.Model;
using CryptoLp.Bot.Common;
using Serilog;

#nullable enable

namespace CryptoLp.Bot.ExchangeLiquidity.HedgeTrading.Strategies.MakerHedge.PriceTrends
{
    public class TrendService : ITrendService
    {
        const decimal TargetSpreadThreshold = 70;
        const decimal ReferenceDeltaPriceThreshold = 70;

        public static TimeSpan WindowPeriod = TimeSpan.FromSeconds(180);
        private static readonly TimeSpan ResetPeriod = TimeSpan.FromSeconds(0);

        private readonly IDateTimeService _dateTimeService;
        private readonly ConcurrentDictionary<string, Exchange> _exchangesById;

        /// <summary>
        /// Key - related trade place (currently: reference exchange & target exchange)
        /// </summary>
        private readonly Dictionary<TradePlace, List<TrendInfo>> _relatedTrends;

        /// <summary>
        /// Key - target trade place
        /// </summary>
        private readonly ConcurrentDictionary<TradePlace, TrendInfo> _trends;
        
        private readonly ILogger _log = Log.ForContext<TrendService>();

        public TrendService(
            IDateTimeService dateTimeService,
            List<StrategyInfo> strategies,
            ConcurrentDictionary<string, Exchange> exchangesById)
        {
            _dateTimeService = dateTimeService;
            _exchangesById = exchangesById;

            (_trends, _relatedTrends) = CreateTrends(strategies);
        }

        private static (ConcurrentDictionary<TradePlace, TrendInfo>, Dictionary<TradePlace, List<TrendInfo>>) CreateTrends(List<StrategyInfo> strategies)
        {
            var trends = new ConcurrentDictionary<TradePlace, TrendInfo>();
            var relatedTrends = new Dictionary<TradePlace, List<TrendInfo>>();
            
            foreach (var strategy in strategies)
            {
                var trendInfo = new TrendInfo(
                    strategy.TargetEis,
                    strategy.ReferenceEis);

                if (!trends.TryAdd(strategy.TargetEis.ToTradePlace(), trendInfo))
                {
                    throw new Exception($"Can't add new trend {strategy.TargetEis.ToTradePlace()} in {nameof(TrendService)}");
                }

                AddStrategy(relatedTrends, strategy.TargetEis.ToTradePlace(), trendInfo);
                AddStrategy(relatedTrends, strategy.ReferenceEis.ToTradePlace(), trendInfo);
            }

            return (trends, relatedTrends);
        }

        public void Start()
        {
            var now = _dateTimeService.UtcNow;
            foreach (var (_, trendInfo) in _trends)
            {
                lock (trendInfo.Sync)
                {
                    trendInfo.TryResetProhibitionPeriod(now, ResetPeriod);
                }
            }
        }

        private static void AddStrategy(
            Dictionary<TradePlace, List<TrendInfo>> dictionary,
            TradePlace tradePlace,
            TrendInfo trendInfo)
        {
            if (!dictionary.TryGetValue(tradePlace, out var list))
            {
                list = new List<TrendInfo>();
                dictionary[tradePlace] = list;
            }

            list.Add(trendInfo);
        }

        public void UpdateTrends(ExchangeIdSymbol eis, ILocalSnapshotService localSnapshotService)
        {
            UpdateTrends(eis, TradeSide.Buy, localSnapshotService);
            UpdateTrends(eis, TradeSide.Sell, localSnapshotService);
        }

        private void UpdateTrends(ExchangeIdSymbol eventEis, TradeSide side, ILocalSnapshotService localSnapshotService)
        {
            if (!_relatedTrends.TryGetValue(eventEis.ToTradePlace(), out var trendInfoList))
            {
                return;
            }

            foreach (var trendInfo in trendInfoList)
            {
                UpdateTrendState(trendInfo, side, localSnapshotService);
            }
        }

        private void UpdateTrendState(TrendInfo trendInfo, TradeSide side, ILocalSnapshotService localSnapshotService)
        {
            var pricesOpt = CalculatePrices(trendInfo.TargetEis, trendInfo.ReferenceEis, side, localSnapshotService);
            if (pricesOpt == null)
            {
                return;
            }
            var prices = pricesOpt.Value;
            
            var referenceDeltaPrice = PriceTrendCalculator.CalcPriceOffset(prices.ReferencePrice, prices.TargetPrice, 0);
            
            lock (trendInfo.Sync)
            {
                var mutTrendInfoBySide = trendInfo.GetMutable(side);

                var now = _dateTimeService.UtcNow;
                mutTrendInfoBySide.UpdateStateIncrementally(referenceDeltaPrice, now);

                var isReset = prices.TargetSpread > TargetSpreadThreshold ||
                              Math.Abs(referenceDeltaPrice) > ReferenceDeltaPriceThreshold;
                
                var exceededDeltaPriceStartTime = trendInfo.ExceededDeltaPriceStartTime;
                if (exceededDeltaPriceStartTime != null)
                {
                    if (isReset)
                    {
                        trendInfo.TryResetProhibitionPeriod(now, ResetPeriod);
                    }
                    else
                    {
                        trendInfo.AbortReset();
                        if (exceededDeltaPriceStartTime + WindowPeriod <= now)
                        {
                            // prohibition period is ended
                            trendInfo.ExceededDeltaPriceStartTime = null;

                            var buyTrendInfo = trendInfo.GetMutable(TradeSide.Buy);
                            buyTrendInfo.Equilibrium = buyTrendInfo.CalcMeanDeltaPrice();
                            
                            var sellTrendInfo = trendInfo.GetMutable(TradeSide.Sell);
                            sellTrendInfo.Equilibrium = sellTrendInfo.CalcMeanDeltaPrice();
                        }
                    }
                }
                else
                {
                    if (isReset)
                    {
                        trendInfo.TryResetProhibitionPeriod(now, ResetPeriod);
                    }
                    else
                    {
                        trendInfo.AbortReset();
                        mutTrendInfoBySide.Equilibrium = mutTrendInfoBySide.CalcMeanDeltaPrice();
                    }
                }
            }
        }

        private (decimal TargetPrice, decimal TargetSpread, decimal ReferencePrice)? CalculatePrices(
            ExchangeIdSymbol targetEis,
            ExchangeIdSymbol referenceEis,
            TradeSide side,
            ILocalSnapshotService localSnapshotService)
        {
            if (!localSnapshotService.TryGetSnapshot(targetEis.ExchangeNameSymbol, out var targetSnapshot))
            {
                _log.Warning($"Can't get target snapshot {{eis}} in {nameof(TrendService)}", targetEis);
                return null;
            }

            var filteredTargetTopPrices = PriceTrendCalculator.CalculateFilteredTopPrices(targetEis, _exchangesById, targetSnapshot);
            var targetPriceOpt = filteredTargetTopPrices.GetPrice(side);
            var targetPriceReverseOpt = filteredTargetTopPrices.GetPrice(side.ChangeSide());
            if (targetPriceOpt == null)
            {
                return null;
            }
            if (targetPriceReverseOpt == null)
            {
                return null;
            }

            var referencePriceBySide =
                PriceTrendCalculator.CalculateReferencePrice(
                    referenceEis,
                    side,
                    localSnapshotService,
                    _log,
                    out _);
                
            if (referencePriceBySide == null)
            {
                return null;
            }

            return (targetPriceOpt.Value, Math.Abs(targetPriceReverseOpt.Value - targetPriceOpt.Value), referencePriceBySide.Value);
        }

        public decimal? GetEquilibrium(TradePlace targetTradePlace, TradeSide side)
        {
            var trendInfo = _trends[targetTradePlace];
            lock (trendInfo.Sync)
            {
                if (trendInfo.ExceededDeltaPriceStartTime != null)
                {
                    return null;
                }
                
                return trendInfo.GetMutable(side).Equilibrium;
            }
        }
    }
}