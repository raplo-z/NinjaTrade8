// NinjaTrader 8 strategy
// Name: EMAtrendNQ
// Primary bars: 300-tick (apply to a 300-tick NQ chart)
// Secondary bars: 5-minute (trend confirmation)
// Entries: 2 contracts, one position at a time
// Session: 9:30–16:00 (based on your chart's Trading Hours time zone)

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations; // For [Display]
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.NinjaScript;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class EMAtrendNQ : Strategy
    {
        #region Parameters
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Fast EMA", GroupName = "Parameters", Order = 1)]
        public int FastEma { get; set; } = 21;

        [NinjaScriptProperty]
        [Range(2, int.MaxValue)]
        [Display(Name = "Slow EMA", GroupName = "Parameters", Order = 2)]
        public int SlowEma { get; set; } = 55;

        [NinjaScriptProperty]
        [Range(10, int.MaxValue)]
        [Display(Name = "HigherTF EMA (5m)", GroupName = "Parameters", Order = 3)]
        public int HigherEma { get; set; } = 100;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop Loss (ticks)", GroupName = "Risk", Order = 1)]
        public int StopLossTicks { get; set; } = 40;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Profit Target (ticks)", GroupName = "Risk", Order = 2)]
        public int ProfitTargetTicks { get; set; } = 80;

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Start Time (HHmmss)", GroupName = "Session", Order = 1)]
        public int StartTime { get; set; } = 93000;  // 09:30:00

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "End Time (HHmmss)", GroupName = "Session", Order = 2)]
        public int EndTime { get; set; } = 160000;   // 16:00:00
        #endregion

        #region Private fields
        private EMA emaFast;
        private EMA emaSlow;
        private EMA emaHigherTF;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                    = "EMAtrendNQ";
                Calculate               = Calculate.OnBarClose;   // safer for backtests; you can switch to OnEachTick later
                EntriesPerDirection     = 1;                       // single position only
                EntryHandling           = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;               // flatten at session close
                ExitOnSessionCloseSeconds   = 5;
                IsInstantiatedOnEachOptimizationIteration = false;
                // Default order quantity = 2 contracts
                // You can also set this per strategy instance in the UI
                SetOrderQuantity = SetOrderQuantity.DefaultQuantity;
                DefaultQuantity   = 2;
            }
            else if (State == State.Configure)
            {
                // Add 5-minute secondary bars for higher timeframe confirmation
                AddDataSeries(BarsPeriodType.Minute, 5);
            }
            else if (State == State.DataLoaded)
            {
                // Cache indicator references for efficiency
                emaFast     = EMA(Close, FastEma);
                emaSlow     = EMA(Close, SlowEma);
                emaHigherTF = EMA(Closes[1], HigherEma); // Closes[1] => 5-minute series

                // Optional: plot on chart for visual debug
                AddChartIndicator(emaFast);
                AddChartIndicator(emaSlow);
                // AddChartIndicator(emaHigherTF); // plots on the 5m panel if you enable multi-timeframe plots
            }
        }

        protected override void OnBarUpdate()
        {
            // We only place/manage trades from the primary series (300-tick). Attach this strategy to a 300-tick chart.
            if (BarsInProgress != 0)
                return;

            // Ensure we have enough bars on both series
            if (CurrentBars[0] < Math.Max(FastEma, SlowEma) || CurrentBars[1] < HigherEma)
                return;

            // Time filter (based on the Trading Hours time zone of your chart)
            int now = ToTime(Time[0]);
            if (now < StartTime || now > EndTime)
                return;

            // Read indicator values
            double f  = emaFast[0];
            double s  = emaSlow[0];
            double h0 = emaHigherTF[0];
            double h1 = emaHigherTF[1];

            bool higherUp   = h0 > h1;
            bool higherDown = h0 < h1;

            // Entry conditions (one position at a time enforced by EntriesPerDirection=1)
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                // Bullish: fast crosses above slow AND higher timeframe EMA is rising
                if (CrossAbove(emaFast, emaSlow, 1) && higherUp)
                {
                    EnterLong(DefaultQuantity, "EMA_Long");
                }
                // Bearish: fast crosses below slow AND higher timeframe EMA is falling
                else if (CrossBelow(emaFast, emaSlow, 1) && higherDown)
                {
                    EnterShort(DefaultQuantity, "EMA_Short");
                }
            }

            // Manage exits dynamically after entry using stop/target orders
            if (Position.MarketPosition == MarketPosition.Long)
            {
                double stopPrice   = Position.AveragePrice - StopLossTicks   * TickSize;
                double targetPrice = Position.AveragePrice + ProfitTargetTicks * TickSize;

                // Use consistent signal names so NinjaTrader modifies existing orders instead of stacking new ones
                ExitLongStopMarket(0, true, Position.Quantity, stopPrice,  "EMA_Long_Stop",   "EMA_Long");
                ExitLongLimit     (0, true, Position.Quantity, targetPrice, "EMA_Long_Target", "EMA_Long");
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                double stopPrice   = Position.AveragePrice + StopLossTicks   * TickSize;
                double targetPrice = Position.AveragePrice - ProfitTargetTicks * TickSize;

                ExitShortStopMarket(0, true, Position.Quantity, stopPrice,  "EMA_Short_Stop",   "EMA_Short");
                ExitShortLimit     (0, true, Position.Quantity, targetPrice, "EMA_Short_Target", "EMA_Short");
            }
        }
    }
}
