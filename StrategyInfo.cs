using Core.Model;

namespace CryptoLp.Bot.ExchangeLiquidity.HedgeTrading.Strategies.MakerHedge.PriceTrends
{
    public class StrategyInfo
    {
        public StrategyInfo(ExchangeIdSymbol targetEis, ExchangeIdSymbol referenceEis, decimal referenceDeltaPriceThresholdRate)
        {
            TargetEis = targetEis;
            ReferenceEis = referenceEis;
            ReferenceDeltaPriceThresholdRate = referenceDeltaPriceThresholdRate;
        }

        public ExchangeIdSymbol TargetEis { get; }
        public ExchangeIdSymbol ReferenceEis { get; }
        public decimal ReferenceDeltaPriceThresholdRate { get; }
    }
}