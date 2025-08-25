#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;
#endregion

// Strategy: IBmicro v1.0.0
// Purpose: Standalone IB breakout strategy for MNQ with initial trailing logic (breakeven then ATR trail).
// Notes:
//  - Run this on an MNQ chart or Strategy Analyzer. Default quantity 3.
//  - Uses bar-close confirmation logic so "ConfirmBars" truly counts bars on tick charts.
//  - All diagnostics go to Strategy Log via Log(...).

namespace NinjaTrader.NinjaScript.Strategies
{
    public class IBmicro : Strategy
    {
        // --- Parameters ---
        [NinjaScriptProperty, Range(1, 240)]
        [Display(Name = "IBMinutes", Order = 1, GroupName = "IB")]
        public int IBMinutes { get; set; }

        [NinjaScriptProperty, Range(0, 10)]
        [Display(Name = "ConfirmBars (0=Immediate)", Order = 2, GroupName = "IB")]
        public int ConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "RequireCloseThrough (true=close, false=touch)", Order = 3, GroupName = "IB")]
        public bool RequireCloseThrough { get; set; }

        [NinjaScriptProperty, Range(0, 23)]
        [Display(Name = "LatestEntryHour (CT)", Order = 4, GroupName = "IB")]
        public int LatestEntryHour { get; set; }

        [NinjaScriptProperty, Range(0, 59)]
        [Display(Name = "LatestEntryMinute (CT)", Order = 5, GroupName = "IB")]
        public int LatestEntryMinute { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Contracts", Order = 10, GroupName = "Orders")]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProfitTargetTicks (0=disabled)", Order = 11, GroupName = "Orders")]
        public int ProfitTargetTicks { get; set; }

        [NinjaScriptProperty, Range(0.0, 10.0)]
        [Display(Name = "IBStopFrac (of IB range)", Order = 12, GroupName = "Orders")]
        public double IBStopFrac { get; set; }

        [NinjaScriptProperty, Range(1, 200)]
        [Display(Name = "ATRPeriod", Order = 20, GroupName = "Trailing")]
        public int ATRPeriod { get; set; }

        [NinjaScriptProperty, Range(0.1, 10.0)]
        [Display(Name = "ATRTrailMult", Order = 21, GroupName = "Trailing")]
        public double ATRTrailMult { get; set; }

        [NinjaScriptProperty, Range(0, 200)]
        [Display(Name = "MinTrailTicks", Order = 22, GroupName = "Trailing")]
        public int MinTrailTicks { get; set; }

        [NinjaScriptProperty, Range(0, 500)]
        [Display(Name = "BETriggerTicks", Order = 23, GroupName = "Trailing")]
        public int BETriggerTicks { get; set; }

        [NinjaScriptProperty, Range(0, 50)]
        [Display(Name = "BEOffsetTicks", Order = 24, GroupName = "Trailing")]
        public int BEOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "EnableDebug", Order = 30, GroupName = "Diagnostics")]
        public bool EnableDebug { get; set; }

        // --- Internals ---
        private TimeSpan openTime;          // 08:30 CT
        private TimeSpan ibEndTime;

        private bool ibWindowComplete;
        private double ibHigh, ibLow;
        private DateTime ibHighTime, ibLowTime;

        private int breakDir;               // +1 long, -1 short, 0 none
        private int confirmCount;
        private int breakBarIndex;

        private ATR atr;

        // Trailing state
        private bool beArmed;
        private double entryPrice;
        private double lastStop;
        private double bestPrice; // highest since entry (long) or lowest (short)

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                = "IBmicro";
                Calculate           = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling       = EntryHandling.AllEntries;
                IsUnmanaged         = false;
                DefaultQuantity     = 3;

                // Defaults sized for MNQ
                IBMinutes           = 60;
                ConfirmBars         = 2;
                RequireCloseThrough = true;
                LatestEntryHour     = 12;
                LatestEntryMinute   = 0;

                Contracts           = 3;
                ProfitTargetTicks   = 0;     // disabled by default, rely on trailing
                IBStopFrac          = 0.5;

                ATRPeriod           = 14;
                ATRTrailMult        = 2.0;
                MinTrailTicks       = 12;    // ~3 pts on MNQ if 4 ticks/pt
                BETriggerTicks      = 20;    // move to BE after +20 ticks
                BEOffsetTicks       = 2;     // lock +2 ticks

                EnableDebug         = true;

                BarsRequiredToTrade = 10;
            }
            else if (State == State.Configure)
            {
                if (ProfitTargetTicks > 0)
                    SetProfitTarget(CalculationMode.Ticks, ProfitTargetTicks);
            }
            else if (State == State.DataLoaded)
            {
                openTime     = new TimeSpan(8, 30, 0);
                ibEndTime    = openTime.Add(TimeSpan.FromMinutes(IBMinutes));
                atr          = ATR(ATRPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 5) return;

            // New day reset
            if (Bars.IsFirstBarOfSession)
            {
                ibWindowComplete = false;
                ibHigh = double.MinValue; ibLow = double.MaxValue;
                ibHighTime = DateTime.MinValue; ibLowTime = DateTime.MinValue;
                breakDir = 0; confirmCount = 0; breakBarIndex = -1;

                // trailing reset
                entryPrice = 0; lastStop = 0; bestPrice = 0; beArmed = false;
                ibEndTime = openTime.Add(TimeSpan.FromMinutes(IBMinutes));

                if (EnableDebug)
                {
                    DateTime start = Time[0].Date.Add(openTime);
                    DateTime end   = Time[0].Date.Add(ibEndTime);
                    Log($"[IBmicro DEBUG] New session. IB window {start:HH:mm:ss} → {end:HH:mm:ss}", LogLevel.Information);
                }
            }

            // Build IB range (include end bar)
            int nowHms = ToTime(Time[0]);
            int ibStartHms = ToTime(Time[0].Date.Add(openTime));
            int ibEndHms   = ToTime(Time[0].Date.Add(ibEndTime));

            if (!ibWindowComplete && nowHms >= ibStartHms && nowHms <= ibEndHms)
            {
                bool changed = false;
                if (High[0] > ibHigh) { ibHigh = High[0]; ibHighTime = Time[0]; changed = true; }
                if (Low[0]  < ibLow)  { ibLow  = Low[0];  ibLowTime  = Time[0]; changed = true; }
                if (changed && EnableDebug && IsFirstTickOfBar)
                    Log($"[IBmicro DEBUG] Update @{Time[0]:HH:mm:ss} → IBHigh={ibHigh:0.####} (at {ibHighTime:HH:mm:ss}), IBLow={ibLow:0.####} (at {ibLowTime:HH:mm:ss})", LogLevel.Information);
            }

            if (!ibWindowComplete && ToTime(Time[1]) <= ibEndHms && ToTime(Time[0]) > ibEndHms)
            {
                ibWindowComplete = true;
                breakDir = 0; confirmCount = 0; breakBarIndex = -1;
                if (EnableDebug)
                    Log($"[IBmicro DEBUG] Window complete @{Time[0]:HH:mm:ss} — IBHigh={ibHigh:0.####}, IBLow={ibLow:0.####}, Range={(ibHigh-ibLow):0.####}", LogLevel.Information);
            }

            // Entry eligibility
            if (!ibWindowComplete) return;
            int latestHms = LatestEntryHour * 10000 + LatestEntryMinute * 100;
            if (nowHms > latestHms) return;

            // If already in position, manage trailing
            if (Position.MarketPosition != MarketPosition.Flat) { ManageTrailing(); return; }

            // Initial break detection on bar close (evaluate prior bar at first tick of new bar)
            bool longBreak=false, shortBreak=false;
            if (IsFirstTickOfBar)
            {
                if (RequireCloseThrough)
                {
                    longBreak  = Close[1] > ibHigh && Close[2] <= ibHigh;
                    shortBreak = Close[1] < ibLow  && Close[2] >= ibLow;
                }
                else
                {
                    longBreak  = High[1] >= ibHigh && High[2] < ibHigh;
                    shortBreak = Low[1]  <= ibLow  && Low[2]  > ibLow;
                }
            }

            if (IsFirstTickOfBar && (longBreak || shortBreak) && breakDir == 0)
            {
                breakDir = longBreak ? +1 : -1;
                confirmCount = 1;
                breakBarIndex = CurrentBar - 1;
                if (EnableDebug)
                    Log($"[IBmicro DEBUG] Initial break (bar close) @{Time[0]:HH:mm:ss} dir={(breakDir>0? "LONG":"SHORT")}", LogLevel.Information);
            }

            // Confirmation bars logic
            if (breakDir != 0 && IsFirstTickOfBar)
            {
                bool closedBeyond =
                    (breakDir > 0 && Close[1] > ibHigh) ||
                    (breakDir < 0 && Close[1] < ibLow);

                if (closedBeyond) confirmCount++;
                else if (Close[1] <= ibHigh && Close[1] >= ibLow) confirmCount = 0;

                if (EnableDebug)
                    Log($"[IBmicro DEBUG] ConfirmCount={confirmCount}/{Math.Max(1, ConfirmBars)} @{Time[0]:HH:mm:ss}", LogLevel.Information);

                bool enterNow = (ConfirmBars <= 0 && (longBreak || shortBreak)) || (ConfirmBars > 0 && confirmCount >= ConfirmBars);

                if (enterNow)
                {
                    double range = Math.Max(0.0, ibHigh - ibLow);
                    int initStopTicks = Math.Max(1, (int)Math.Round((range * IBStopFrac) / TickSize));

                    if (breakDir > 0)
                    {
                        EnterLong(Contracts, "IBmicroLong");
                        if (EnableDebug) Log($"[IBmicro] LONG signal — initStop≈{initStopTicks} ticks", LogLevel.Information);
                    }
                    else
                    {
                        EnterShort(Contracts, "IBmicroShort");
                        if (EnableDebug) Log($"[IBmicro] SHORT signal — initStop≈{initStopTicks} ticks", LogLevel.Information);
                    }

                    breakDir = 0; confirmCount = 0; breakBarIndex = -1;
                }
            }

            if (Position.MarketPosition != MarketPosition.Flat)
                ManageTrailing();
        }

        private void ManageTrailing()
        {
            if (entryPrice <= 0) return;

            double atrTicks = Math.Max(1.0, (atr[0] * ATRTrailMult) / TickSize);
            int trailTicks = Math.Max(MinTrailTicks, (int)Math.Round(atrTicks));

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (bestPrice < GetCurrentBid()) bestPrice = GetCurrentBid();

                double upTicks = (GetCurrentBid() - entryPrice) / TickSize;
                if (!beArmed && upTicks >= BETriggerTicks) { beArmed = true; }

                double beStop = entryPrice + (BEOffsetTicks * TickSize);
                double trailStop = bestPrice - trailTicks * TickSize;
                double desired = beArmed ? Math.Max(beStop, trailStop) : (entryPrice - (MinTrailTicks * TickSize));

                if (lastStop == 0 || desired > lastStop + 0.5 * TickSize)
                {
                    SetStopLoss("IBmicroLong", CalculationMode.Price, desired, false);
                    lastStop = desired;
                    if (EnableDebug) Log($"[IBmicro TRAIL] LONG stop -> {desired:0.####} (best={bestPrice:0.####}, trailTicks={trailTicks}, BEarmed={beArmed})", LogLevel.Information);
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (bestPrice == 0 || bestPrice > GetCurrentAsk()) bestPrice = GetCurrentAsk();

                double downTicks = (entryPrice - GetCurrentAsk()) / TickSize;
                if (!beArmed && downTicks >= BETriggerTicks) { beArmed = true; }

                double beStop = entryPrice - (BEOffsetTicks * TickSize);
                double trailStop = bestPrice + trailTicks * TickSize;
                double desired = beArmed ? Math.Min(beStop, trailStop) : (entryPrice + (MinTrailTicks * TickSize));

                if (lastStop == 0 || desired < lastStop - 0.5 * TickSize)
                {
                    SetStopLoss("IBmicroShort", CalculationMode.Price, desired, false);
                    lastStop = desired;
                    if (EnableDebug) Log($"[IBmicro TRAIL] SHORT stop -> {desired:0.####} (best={bestPrice:0.####}, trailTicks={trailTicks}, BEarmed={beArmed})", LogLevel.Information);
                }
            }
        }

        // --- Order/Position callbacks (8.1-compatible) ---
        protected override void OnOrderUpdate(
            NinjaTrader.Cbi.Order order,
            double limitPrice,
            double stopPrice,
            int quantity,
            int filled,
            double averageFillPrice,
            NinjaTrader.Cbi.OrderState orderState,
            DateTime time,
            NinjaTrader.Cbi.ErrorCode error,
            string nativeError)
        {
            try
            {
                if (order == null) return;
                string tag = order.Name ?? string.Empty;

                if (orderState == OrderState.Filled && (tag == "IBmicroLong" || tag == "IBmicroShort"))
                {
                    entryPrice = averageFillPrice > 0 ? averageFillPrice : Position.AveragePrice;
                    bestPrice  = entryPrice;
                    lastStop   = 0; beArmed = false;

                    double initTicks = MinTrailTicks; // fallback
                    if (ibHigh > ibLow && IBStopFrac > 0)
                    {
                        double range = ibHigh - ibLow;
                        initTicks = Math.Max(1, Math.Round(range * IBStopFrac / TickSize));
                    }
                    double initStopPrice = (tag == "IBmicroLong")
                        ? entryPrice - initTicks * TickSize
                        : entryPrice + initTicks * TickSize;

                    SetStopLoss(tag, CalculationMode.Price, initStopPrice, false);
                    lastStop = initStopPrice;

                    Log($"[FILL] Entry {tag} qty={Math.Abs(quantity)} avg={entryPrice:0.####} initStop≈{initStopPrice:0.####}", LogLevel.Information);

                    if (ProfitTargetTicks > 0)
                        SetProfitTarget(tag, CalculationMode.Ticks, ProfitTargetTicks);
                }
            }
            catch (Exception ex)
            {
                Log("[ERROR] OnOrderUpdate: " + ex.Message, LogLevel.Error);
            }
        }

        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition)
        {
            try
            {
                if (position != null && position.MarketPosition == MarketPosition.Flat)
                {
                    entryPrice = 0; lastStop = 0; bestPrice = 0; beArmed = false;
                }
            }
            catch (Exception ex)
            {
                Log("[ERROR] OnPositionUpdate: " + ex.Message, LogLevel.Error);
            }
        }
    }
}
