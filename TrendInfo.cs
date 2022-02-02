using System;
using System.Collections.Generic;
using Core.Model;

#nullable enable

namespace CryptoLp.Bot.ExchangeLiquidity.HedgeTrading.Strategies.MakerHedge.PriceTrends
{
    public class TrendInfo
    {
        /// <summary>
        /// sync object for _buyMutable, _sellMutable, ExceededDeltaPriceStartTime
        /// </summary>
        public readonly object Sync = new object();
        
        private readonly MutableTrendInfoBySide _buyMutable = new MutableTrendInfoBySide();
        private readonly MutableTrendInfoBySide _sellMutable = new MutableTrendInfoBySide();
        private DateTime? _tryResetStartTime;

        public TrendInfo(
            ExchangeIdSymbol targetEis,
            ExchangeIdSymbol referenceEis)
        {
            ReferenceEis = referenceEis;
            TargetEis = targetEis;
        }

        public DateTime? ExceededDeltaPriceStartTime { get; set; }

        
        public ExchangeIdSymbol TargetEis { get; }
        public ExchangeIdSymbol ReferenceEis { get; }

        public MutableTrendInfoBySide GetMutable(TradeSide side)
        {
            return side switch
            {
                TradeSide.Buy => _buyMutable,
                TradeSide.Sell => _sellMutable,
                _ => throw new ArgumentOutOfRangeException(nameof(side), side, null)
            };
        }

        public void TryResetProhibitionPeriod(DateTime now, TimeSpan resetPeriod)
        {
            if (_tryResetStartTime == null)
            {
                _tryResetStartTime = now;
                return;
            }

            if (now - _tryResetStartTime.Value <= resetPeriod)
            {
                return;
            }

            ExceededDeltaPriceStartTime = now;

            _buyMutable.Clear();
            _sellMutable.Clear();
            
            _tryResetStartTime = null;
        }

        public void AbortReset()
        {
            _tryResetStartTime = null;
        }
    }

    public class MutableTrendInfoBySide
    {
        public readonly Queue<(DateTime Time, decimal DeltaPrice)> DeltaPricesQueue =
            new Queue<(DateTime Time, decimal DeltaPrice)>();
        
        public decimal DeltaPriceSum { get; set; }

        public decimal Equilibrium { get; set; }

        internal decimal CalcMeanDeltaPrice()
        {
            var deltaPricesQueue = DeltaPricesQueue;
            var deltaPriceSum = DeltaPriceSum;
            
            return deltaPriceSum / deltaPricesQueue.Count;
        }

        internal void UpdateStateIncrementally(decimal referenceDeltaPrice, DateTime now)
        {
            var deltaPriceSum = DeltaPriceSum;

            var deltaPricesQueue = DeltaPricesQueue;
            var boundTime = now - TrendService.WindowPeriod;
            while (deltaPricesQueue.Count > 0)
            {
                var (time, deltaPrice) = deltaPricesQueue.Peek();
                if (time < boundTime)
                {
                    deltaPriceSum -= deltaPrice;
                    deltaPricesQueue.Dequeue();
                }
                else
                {
                    break;
                }
            }

            deltaPricesQueue.Enqueue((now, referenceDeltaPrice));
            deltaPriceSum += referenceDeltaPrice;
            DeltaPriceSum = deltaPriceSum;
        }

        internal void Clear()
        {
            DeltaPriceSum = 0;
            DeltaPricesQueue.Clear();
        }
    }
}