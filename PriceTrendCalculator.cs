using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Core.Bot.LocalSnapshots;
using Core.Exchanges.Core;
using Core.Model;
using Core.Model.OrderBook.Snapshots;
using CryptoLp.Bot.ExchangeLiquidity.TargetTrading.DesiredPrice;
using CryptoLp.Bot.Settings;
using Shared.Common;
using Serilog;

#nullable enable

namespace CryptoLp.Bot.Common
{
    public static class PriceTrendCalculator
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static decimal CalcReferenceDeltaPrice(
            decimal targetPriceBySide,
            decimal referencePriceBySide,
            decimal equilibrium) => CalcPriceOffset(referencePriceBySide, targetPriceBySide, equilibrium);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static decimal CalcReferenceThreshold(decimal referencePrice, decimal referenceDeltaPriceThresholdRate) =>
            referenceDeltaPriceThresholdRate * referencePrice;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static decimal CalcPriceOffset(
            decimal referencePriceBySide,
            decimal targetPriceBySide,
            decimal equilibrium) => referencePriceBySide - targetPriceBySide - equilibrium;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PriceTrend CalculatePriceTrend(
            decimal referencePriceBySide,
            decimal targetPriceBySide,
            decimal equilibrium,
            TradeSide side,
            decimal referenceDeltaPriceThresholdRate,
            Explanation? explanation)
        {
            //var refThreshold = CalcReferenceThreshold(referencePriceBySide, referenceDeltaPriceThresholdRate);
            var refThreshold = 0m;
            var priceOffset = CalcPriceOffset(referencePriceBySide, targetPriceBySide, equilibrium);

            return side switch
            {
                TradeSide.Sell when priceOffset < -refThreshold => NegativeTrend(),
                TradeSide.Buy  when priceOffset >  refThreshold => NegativeTrend(),
                TradeSide.Sell when priceOffset >  refThreshold => PositiveTrend(),
                TradeSide.Buy  when priceOffset < -refThreshold => PositiveTrend(),
                _ => NoneTrend()
            };

            PriceTrend PositiveTrend()
            {
                explanation?.AddReason($"Positive trend |priceoffset {priceOffset}| > {refThreshold} with equilibrium {equilibrium} (referencePrice {referencePriceBySide} targetPrice {targetPriceBySide})");
                return PriceTrend.Positive;
            }

            PriceTrend NegativeTrend()
            {
                explanation?.AddReason($"Negative trend |priceoffset {priceOffset}| > {refThreshold} with equilibrium {equilibrium} (referencePrice {referencePriceBySide} targetPrice {targetPriceBySide})");
                return PriceTrend.Negative;
            }
            
            PriceTrend NoneTrend()
            {
                explanation?.AddReason($"None trend |priceoffset {priceOffset}| < {refThreshold} with equilibrium {equilibrium} (referencePrice {referencePriceBySide} targetPrice {targetPriceBySide})");
                return PriceTrend.None;
            }
        }

        public static decimal? CalculateReferencePrice(
            ExchangeIdSymbol eis,
            TradeSide side,
            ILocalSnapshotService localSnapshotService,
            ILogger log,
            out ILocalOrderBookSnapshot? snapshot)
        {
            var ens = eis.ExchangeNameSymbol;
            if (!localSnapshotService.TryGetSnapshot(ens, out snapshot))
            {
                log.Warning("Can't get snapshot for {ens}", ens);
                return null;
            }
            
            var priceLevelBySide = snapshot!.GetTop(side.ToOrderSide());
            if (priceLevelBySide == null)
            {
                log.Warning("There is no price levels in snapshot {ens} {side}", eis, side);
                return null;
            }

            return priceLevelBySide.GetPrice();
        }
        
        public static NaiveMakerLiquiditySettings GetNaiveMakerConfig(
            LiquiditySettings liquiditySettings,
            ExchangeSymbol target)
        {
            // NOTE: expect that hedge == target for NaiveMakerStrategy
            var symbolLiquiditySettings = liquiditySettings.Exchanges[target.Exchange.Id][target.Symbol.CurrencyCodePair];
            var naiveMakerConfig = symbolLiquiditySettings.NaiveMaker;
            if (naiveMakerConfig == null)
            {
                throw new Exception($"NaiveMaker should be specified for {target.Eis}");
            }

            return naiveMakerConfig;
        }
        
        public static PriceByOrderSide CalculateFilteredTopPrices(
            ExchangeIdSymbol eis,
            ConcurrentDictionary<string, Exchange> exchangesById,
            ILocalOrderBookSnapshot snapshot)
        {
            var exchange = exchangesById[eis.ExchangeId];
            var orders = exchange.NotFinishedOrdersByClientOrderId.Values;
            return FilteredTopPriceCalculator.GetTopPrices(orders, snapshot);
        }
        
        public static decimal? CalculateDesiredPrice(PriceByOrderSide filteredTopPrices)
        {
            if (filteredTopPrices.TopAsk == null || filteredTopPrices.TopBid == null)
            {
                return null;
            }

            return (filteredTopPrices.TopAsk.Value + filteredTopPrices.TopBid.Value) / 2m;
        }
    }
}