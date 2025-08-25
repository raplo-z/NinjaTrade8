//#define USE_BANNER   // ← Uncomment to enable chart banner (requires NinjaTrader.Gui.Tools + TextPosition)

#region Using declarations
using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;                      // [Display] (metadata)
using System.ComponentModel.DataAnnotations;      // [Range], [Display]
using NinjaTrader.Cbi;                            // Account, Order, Position, enums
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
#endregion

// Strategy: OpenMajorityNine_CashOpen v1.7.4
// Change: ALL debug/diagnostic messages now use Log(..., LogLevel.Information) with tags:
//         [OPEN DEBUG], [IB DEBUG], [FILL], [EXIT].
//         Includes v1.7.2 fix: IB confirmation bars count real closed bars on tick charts.
namespace NinjaTrader.NinjaScript.Strategies
{
    public class OpenMajorityNine_CashOpen : Strategy
    {
        // --- Core parameters (Cash-Open) ---
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "LookbackBars", Order = 1, GroupName = "Open")]
        public int LookbackBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProfitTargetPoints", Order = 2, GroupName = "Open")]
        public double ProfitTargetPoints { get; set; }

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Order = 3, GroupName = "Open")]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "CashOpenHour (CT)", Order = 4, GroupName = "Open")]
        public int CashOpenHour { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "CashOpenMinute (CT)", Order = 5, GroupName = "Open")]
        public int CashOpenMinute { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "EnableAlerts", Order = 6, GroupName = "Visuals")]
        public bool EnableAlerts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "OpenEnableDebug", Order = 7, GroupName = "Open")]
        public bool OpenEnableDebug { get; set; }

        // --- Risk (shared, used by cash-open and IB Mode 2) ---
        [NinjaScriptProperty]
        [Display(Name = "EnableStopLoss", Order = 10, GroupName = "Risk")]
        public bool EnableStopLoss { get; set; }

        [NinjaScriptProperty, Range(0.0, double.MaxValue)]
        [Display(Name = "StopRiskDollars (total)", Order = 11, GroupName = "Risk")]
        public double StopRiskDollars { get; set; }

        // --- Open filters (do NOT affect IB) ---
        [NinjaScriptProperty]
        [Display(Name = "SkipFridays (Open only)", Order = 20, GroupName = "Open Filters")]
        public bool SkipFridays { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SkipDatesCsv (Open only)", Order = 21, GroupName = "Open Filters")]
        public string SkipDatesCsv { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "UsePreOpenRangeFilter (Open only)", Order = 22, GroupName = "Open Filters")]
        public bool UseOvernightRangeFilter { get; set; }

        [NinjaScriptProperty, Range(0.0, double.MaxValue)]
        [Display(Name = "MinPreOpenRangePoints (Open only)", Order = 23, GroupName = "Open Filters")]
        public double MinOvernightRangePoints { get; set; }

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "PreOpenRangeMinutes (Open only)", Order = 24, GroupName = "Open Filters")]
        public int PreOpenRangeMinutes { get; set; }

        // --- Initial Balance (IB) parameters ---
        [NinjaScriptProperty]
        [Display(Name = "EnableIB", Order = 30, GroupName = "IB")]
        public bool EnableIB { get; set; }

        [NinjaScriptProperty, Range(1, 240)]
        [Display(Name = "IBMinutes", Order = 31, GroupName = "IB")]
        public int IBMinutes { get; set; }

        [NinjaScriptProperty, Range(1, 2)]
        [Display(Name = "IBMode (1=IBRange, 2=OpenLike)", Order = 32, GroupName = "IB")]
        public int IBMode { get; set; }

        [NinjaScriptProperty, Range(0.0, double.MaxValue)]
        [Display(Name = "IBTargetFrac (of IB range)", Order = 33, GroupName = "IB")]
        public double IBTargetFrac { get; set; }

        [NinjaScriptProperty, Range(0.0, double.MaxValue)]
        [Display(Name = "IBStopFrac (of IB range)", Order = 34, GroupName = "IB")]
        public double IBStopFrac { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "IBRequireFlatFromOpen", Order = 35, GroupName = "IB")]
        public bool IBRequireFlatFromOpen { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "IBRequireCloseThrough (true=close, false=touch)", Order = 36, GroupName = "IB")]
        public bool IBRequireCloseThrough { get; set; }

        [NinjaScriptProperty, Range(0,23)]
        [Display(Name = "IBLatestEntryHour (CT)", Order = 37, GroupName = "IB")]
        public int IBLatestEntryHour { get; set; }

        [NinjaScriptProperty, Range(0,59)]
        [Display(Name = "IBLatestEntryMinute (CT)", Order = 38, GroupName = "IB")]
        public int IBLatestEntryMinute { get; set; }

        // --- IB Entry Style controls ---
        [NinjaScriptProperty, Range(0,3)]
        [Display(Name = "IBEntryStyle (0=Immediate,1=Retest,2=Confirm,3=ConfirmThenRetest)", Order = 39, GroupName = "IB Entry")]
        public int IBEntryStyle { get; set; }

        [NinjaScriptProperty, Range(1, 20)]
        [Display(Name = "IBConfirmBars (for styles 2/3)", Order = 40, GroupName = "IB Entry")]
        public int IBConfirmBars { get; set; }

        [NinjaScriptProperty, Range(0, 50)]
        [Display(Name = "IBRetestToleranceTicks (for styles 1/3)", Order = 41, GroupName = "IB Entry")]
        public int IBRetestToleranceTicks { get; set; }

        [NinjaScriptProperty, Range(1, 200)]
        [Display(Name = "IBRetestMaxBars (timeout)", Order = 42, GroupName = "IB Entry")]
        public int IBRetestMaxBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "IBEnableDebug", Order = 50, GroupName = "IB Debug")]
        public bool IBEnableDebug { get; set; }

        // --- Internals ---
        private bool openTradedToday;
        private bool ibTradedToday;
        private DateTime tradingDay;
        private TimeSpan openTime;
        private HashSet<DateTime> skipDates = new HashSet<DateTime>();

        private double preHi, preLo; // open-filter

        // IB internals
        private bool ibWindowComplete;
        private double ibHigh, ibLow;
        private DateTime ibHighTime, ibLowTime;
        private TimeSpan ibEndTime;

        // IB entry state machine
        private int ibBreakDir; // +1 long, -1 short, 0 none
        private int ibBreakBarIndex;
        private int ibConfirmCount;
        private bool ibWaitingForRetest;

        // Fills/outcomes
        private DateTime? entryFillTime = null;
        private double entryAvgPrice = 0.0;
        private int entryQty = 0;
        private string lastEntryTag = string.Empty;
        private DateTime? exitFillTime = null;
        private string lastExitReason = "MANUAL/OTHER";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                = "OpenMajorityNine_CashOpen";
                Calculate           = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling       = EntryHandling.AllEntries;
                IsUnmanaged         = false;

                // Open defaults
                LookbackBars        = 9;
                ProfitTargetPoints  = 2.0;
                Contracts           = 3;
                CashOpenHour        = 8;
                CashOpenMinute      = 30;
                EnableAlerts        = false;
                OpenEnableDebug     = true;

                // Risk
                EnableStopLoss      = true;
                StopRiskDollars     = 1250.0;

                // Open filters
                SkipFridays             = false;
                SkipDatesCsv            = "2025-07-18,2025-08-01,2025-08-15";
                UseOvernightRangeFilter = true;
                MinOvernightRangePoints = 1.5;
                PreOpenRangeMinutes     = 60;

                // IB
                EnableIB            = true;
                IBMinutes           = 60;
                IBMode              = 1;
                IBTargetFrac        = 1.0;
                IBStopFrac          = 0.5;
                IBRequireFlatFromOpen = true;
                IBRequireCloseThrough = true;
                IBLatestEntryHour   = 12;
                IBLatestEntryMinute = 0;

                // Entry styles
                IBEntryStyle            = 2;
                IBConfirmBars           = 2;
                IBRetestToleranceTicks  = 4;
                IBRetestMaxBars         = 40;

                IBEnableDebug       = true;

                DefaultQuantity     = 3;
                TraceOrders         = false;
                BarsRequiredToTrade = 10;
            }
            else if (State == State.Configure)
            {
                SetProfitTarget(CalculationMode.Ticks, GetPtTicks());
                if (EnableStopLoss)
                    SetStopLoss(CalculationMode.Ticks, GetStopTicksPerContract());
            }
            else if (State == State.DataLoaded)
            {
                openTime           = new TimeSpan(CashOpenHour, CashOpenMinute, 0);
                ibEndTime          = openTime.Add(TimeSpan.FromMinutes(IBMinutes));
                tradingDay         = DateTime.MinValue;

                // Parse skip dates for OPEN only
                skipDates.Clear();
                if (!string.IsNullOrWhiteSpace(SkipDatesCsv))
                {
                    foreach (var p in SkipDatesCsv.Split(new char[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        DateTime dt;
                        if (DateTime.TryParse(p.Trim(), out dt))
                            skipDates.Add(dt.Date);
                    }
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(LookbackBars + 1, 2))
                return;

            DateTime curDay = Time[0].Date;
            if (tradingDay.Date != curDay)
            {
                tradingDay       = curDay;
                openTradedToday  = false;
                ibTradedToday    = false;
                ibWindowComplete = false;
                preHi = double.MinValue; preLo = double.MaxValue;
                ibHigh = double.MinValue; ibLow = double.MaxValue;
                ibHighTime = DateTime.MinValue; ibLowTime = DateTime.MinValue;
                ibEndTime = new TimeSpan(CashOpenHour, CashOpenMinute, 0).Add(TimeSpan.FromMinutes(IBMinutes));

                ibBreakDir = 0; ibBreakBarIndex = -1; ibConfirmCount = 0; ibWaitingForRetest = false;

                entryFillTime = null; exitFillTime = null; lastExitReason = "MANUAL/OTHER"; lastEntryTag = string.Empty;
                entryAvgPrice = 0.0; entryQty = 0;

                if (IBEnableDebug)
                {
                    DateTime start = curDay.Add(openTime);
                    DateTime end   = curDay.Add(ibEndTime);
                    Log($"[IB DEBUG] New day {curDay:yyyy-MM-dd}. IB window: {start:HH:mm:ss} → {end:HH:mm:ss} CT. BarsPeriod={BarsPeriod.BarsPeriodType}, Value={BarsPeriod.Value}", LogLevel.Information);
                }
                if (OpenEnableDebug)
                {
                    Log($"[OPEN DEBUG] New day {curDay:yyyy-MM-dd}. Open @ {openTime:hh\\:mm\\:ss} CT, Filters: Fridays={SkipFridays}, OvernightRangeFilter={UseOvernightRangeFilter}", LogLevel.Information);
                }
            }

            HandleCashOpenModule();
            if (EnableIB)
                HandleIBModule();
        }

        private void HandleCashOpenModule()
        {
            int openHMS = ToHmsInt(openTime);

            if (UseOvernightRangeFilter && ToTime(Time[0]) < openHMS)
            {
                DateTime today = Time[0].Date;
                DateTime startWindow = today.Add(openTime).AddMinutes(-PreOpenRangeMinutes);
                if (Time[0] >= startWindow)
                {
                    if (High[0] > preHi) preHi = High[0];
                    if (Low[0]  < preLo) preLo = Low[0];
                    if (OpenEnableDebug && IsFirstTickOfBar)
                        Log($"[OPEN DEBUG] Pre-open window progress @{Time[0]:HH:mm:ss}: preHi={preHi:0.####}, preLo={preLo:0.####}", LogLevel.Information);
                }
            }

            bool crossedOpen = ToTime(Time[1]) < openHMS && ToTime(Time[0]) >= openHMS;
            if (!openTradedToday && crossedOpen)
            {
                DateTime today = Time[0].Date;

                if (SkipFridays && Time[0].DayOfWeek == DayOfWeek.Friday)
                {
                    Log($"[OPEN DEBUG] {GetAcct()} Skip (Open): Friday filter.", LogLevel.Information);
                    return;
                }

                if (skipDates.Contains(today))
                {
                    Log($"[OPEN DEBUG] {GetAcct()} Skip (Open): In SkipDatesCsv ({today:yyyy-MM-dd}).", LogLevel.Information);
                    return;
                }

                if (UseOvernightRangeFilter)
                {
                    if (preHi == double.MinValue || preLo == double.MaxValue)
                    {
                        Log($"[OPEN DEBUG] {GetAcct()} Skip (Open): Not enough bars for pre-open range window.", LogLevel.Information);
                        return;
                    }
                    double rangePts = (preHi - preLo);
                    if (rangePts < MinOvernightRangePoints)
                    {
                        Log($"[OPEN DEBUG] {GetAcct()} Skip (Open): Pre-open range {rangePts:0.####} < Min {MinOvernightRangePoints:0.####}.", LogLevel.Information);
                        return;
                    }
                }

                int greens = 0, reds = 0;
                for (int i = 1; i <= LookbackBars; i++)
                {
                    if (Close[i] > Open[i]) greens++;
                    else if (Close[i] < Open[i]) reds++;
                }

                string ts   = Time[0].ToString("yyyy-MM-dd HH:mm:ss");
                int conf    = Math.Max(greens, reds);
                string dir  = greens > reds ? "LONG" : reds > greens ? "SHORT" : "NONE (TIE)";
                int ptTicks = GetPtTicks();
                int slTicks = EnableStopLoss ? GetStopTicksPerContract() : 0;
                double slPts = EnableStopLoss ? slTicks * TickSize : 0.0;

                string msg = $"[{ts}] {GetAcct()} OPEN signal: {dir} | greens={greens}, reds={reds}, conf={conf}/{LookbackBars}, qty={Contracts}, TP={ProfitTargetPoints:0.####} pt ({ptTicks} ticks)"
                           + (EnableStopLoss ? $", SL≈{slPts:0.####} pt ({slTicks} ticks) ~ ${StopRiskDollars:0}" : ", SL=DISABLED");
                Log(msg, LogLevel.Information);

                if (greens > reds)
                {
                    SetProfitTarget("OpenLong", CalculationMode.Ticks, ptTicks);
                    if (EnableStopLoss) SetStopLoss("OpenLong", CalculationMode.Ticks, slTicks, false);
                    EnterLong(Contracts, "OpenLong");
                    openTradedToday = true;
                }
                else if (reds > greens)
                {
                    SetProfitTarget("OpenShort", CalculationMode.Ticks, ptTicks);
                    if (EnableStopLoss) SetStopLoss("OpenShort", CalculationMode.Ticks, slTicks, false);
                    EnterShort(Contracts, "OpenShort");
                    openTradedToday = true;
                }
            }
        }

        private void HandleIBModule()
        {
            DateTime barTime = Time[0];
            TimeSpan nowTs = barTime.TimeOfDay;
            TimeSpan ibStart = openTime;
            TimeSpan ibEnd   = ibEndTime;

            int ibStartHms = ToHmsInt(ibStart);
            int ibEndHms   = ToHmsInt(ibEnd);
            int nowHms     = ToHmsInt(nowTs);
            int latestHms  = IBLatestEntryHour * 10000 + IBLatestEntryMinute * 100;

            // Build IB range (include end bar)
            if (!ibWindowComplete && nowHms >= ibStartHms && nowHms <= ibEndHms)
            {
                bool changed = false;
                if (High[0] > ibHigh) { ibHigh = High[0]; ibHighTime = barTime; changed = true; }
                if (Low[0]  < ibLow)  { ibLow  = Low[0];  ibLowTime  = barTime; changed = true; }
                if (changed && IBEnableDebug && IsFirstTickOfBar)
                    Log($"[IB DEBUG] Update @{barTime:HH:mm:ss} → IBHigh={ibHigh:0.####} (at {ibHighTime:HH:mm:ss}), IBLow={ibLow:0.####} (at {ibLowTime:HH:mm:ss})", LogLevel.Information);
            }

            if (!ibWindowComplete && ToTime(Time[1]) <= ibEndHms && ToTime(Time[0]) > ibEndHms)
            {
                ibWindowComplete = true;
                ibBreakDir = 0; ibBreakBarIndex = -1; ibConfirmCount = 0; ibWaitingForRetest = false;

                if (IBEnableDebug)
                    Log($"[IB DEBUG] Window complete @{barTime:HH:mm:ss} — Final IBHigh={ibHigh:0.####} (at {ibHighTime:HH:mm:ss}), IBLow={ibLow:0.####} (at {ibLowTime:HH:mm:ss}), Range={(ibHigh - ibLow):0.####}", LogLevel.Information);
            }

            if (!ibWindowComplete || ibTradedToday) return;
            if (nowHms > latestHms) return;
            if (IBRequireFlatFromOpen && Position.MarketPosition != MarketPosition.Flat) return;

            double longThresh = ibHigh;
            double shortThresh = ibLow;

            // --- Initial break detection (bar-close aware) ---
            bool longBreak = false, shortBreak = false;

            if (IBRequireCloseThrough)
            {
                if (IsFirstTickOfBar)
                {
                    longBreak  = Close[1] > longThresh && Close[2] <= longThresh;
                    shortBreak = Close[1] < shortThresh && Close[2] >= shortThresh;
                }
            }
            else
            {
                if (IsFirstTickOfBar)
                {
                    longBreak  = High[1] >= longThresh && High[2] < longThresh;
                    shortBreak = Low[1]  <= shortThresh && Low[2]  > shortThresh;
                }
            }

            if (IsFirstTickOfBar && (longBreak || shortBreak) && ibBreakDir == 0)
            {
                ibBreakDir = longBreak ? +1 : -1;
                ibBreakBarIndex = CurrentBar - 1; // the bar that closed
                ibConfirmCount = 1; // first bar beyond
                ibWaitingForRetest = (IBEntryStyle == 1 || IBEntryStyle == 3);
                if (IBEnableDebug)
                    Log($"[IB DEBUG] Initial break (bar close) @{barTime:HH:mm:ss} dir={(ibBreakDir>0?"LONG":"SHORT")} — EntryStyle={IBEntryStyle}", LogLevel.Information);
            }

            if (ibBreakDir == 0)
                return;

            bool shouldEnterNow = false;
            string entryReason = "";

            // --- ENTRY STYLE LOGIC ---

            // 2/3) Confirmation counting happens ONLY on bar close -> evaluate at first tick of new bar using bar [1]
            if (IsFirstTickOfBar && (IBEntryStyle == 2 || IBEntryStyle == 3))
            {
                bool closedBeyondSameDir =
                    (ibBreakDir > 0 && Close[1] > longThresh) ||
                    (ibBreakDir < 0 && Close[1] < shortThresh);

                if (closedBeyondSameDir)
                    ibConfirmCount++;
                else if (Close[1] <= longThresh && Close[1] >= shortThresh)
                    ibConfirmCount = 0; // reset if back inside band

                if (IBEnableDebug)
                    Log($"[IB DEBUG] ConfirmCount={ibConfirmCount}/{IBConfirmBars} at {barTime:HH:mm:ss}", LogLevel.Information);

                if (IBEntryStyle == 2 && ibConfirmCount >= IBConfirmBars)
                {
                    shouldEnterNow = true;
                    entryReason = $"Confirmation({IBConfirmBars})";
                }
            }

            // 1/3) Retest logic remains tick-based, but can start only after confirmation for style 3
            if (!shouldEnterNow && (IBEntryStyle == 1 || IBEntryStyle == 3))
            {
                bool confirmationsOk = (IBEntryStyle == 1) || (ibConfirmCount >= IBConfirmBars);

                if (confirmationsOk)
                {
                    double tol = IBRetestToleranceTicks * TickSize;
                    if (ibBreakDir > 0) // long
                    {
                        if (ibWaitingForRetest && Low[0] <= longThresh + tol)
                        {
                            ibWaitingForRetest = false;
                            if (IBEnableDebug) Log($"[IB DEBUG] Retest satisfied (LONG) @{barTime:HH:mm:ss}", LogLevel.Information);
                        }
                        if (!ibWaitingForRetest && Close[0] > longThresh)
                        {
                            shouldEnterNow = true;
                            entryReason = (IBEntryStyle == 1) ? "Retest" : $"Confirm({IBConfirmBars})+Retest";
                        }
                    }
                    else // short
                    {
                        if (ibWaitingForRetest && High[0] >= shortThresh - tol)
                        {
                            ibWaitingForRetest = false;
                            if (IBEnableDebug) Log($"[IB DEBUG] Retest satisfied (SHORT) @{barTime:HH:mm:ss}", LogLevel.Information);
                        }
                        if (!ibWaitingForRetest && Close[0] < shortThresh)
                        {
                            shouldEnterNow = true;
                            entryReason = (IBEntryStyle == 1) ? "Retest" : $"Confirm({IBConfirmBars})+Retest";
                        }
                    }

                    // timeout (bars-based)
                    if (!shouldEnterNow && IBRetestMaxBars > 0 && ibBreakBarIndex >= 0 && CurrentBar - ibBreakBarIndex > IBRetestMaxBars)
                    {
                        if (IBEnableDebug) Log($"[IB DEBUG] Retest timeout after {IBRetestMaxBars} bars — cancelling entry.", LogLevel.Information);
                        ibBreakDir = 0; ibBreakBarIndex = -1; ibConfirmCount = 0; ibWaitingForRetest = false;
                        return;
                    }
                }
            }

            if (!shouldEnterNow)
                return;

            // --- Compute PT/SL based on IBMode
            double ibRangePts = Math.Max(0.0, ibHigh - ibLow);
            int ptTicks = 0, slTicks = 0;
            double ptPts = 0.0, slPts = 0.0;
            string modeLabel = IBMode == 1 ? "IBRange" : "OpenLike";

            if (IBMode == 1)
            {
                ptPts  = Math.Max(0.0, ibRangePts * IBTargetFrac);
                slPts  = Math.Max(0.0, ibRangePts * IBStopFrac);
                ptTicks = Math.Max(1, (int)Math.Round(ptPts / TickSize));
                slTicks = Math.Max(1, (int)Math.Round(slPts / TickSize));
            }
            else // IBMode == 2
            {
                ptPts  = ProfitTargetPoints;
                slPts  = EnableStopLoss ? GetStopPointsPerContract() : 0.0;
                ptTicks = GetPtTicks();
                slTicks = EnableStopLoss ? GetStopTicksPerContract() : 0;
            }

            string ts = barTime.ToString("yyyy-MM-dd HH:mm:ss");
            string info = $"[{ts}] {GetAcct()} IB entry ({modeLabel} | {entryReason}): "
                        + ((ibBreakDir>0) ? "LONG" : "SHORT")
                        + $" | IBHi={ibHigh:0.####}, IBLow={ibLow:0.####}, IBRange={ibRangePts:0.####}"
                        + $", TP={ptPts:0.####} pt ({ptTicks} ticks)"
                        + (EnableStopLoss ? $", SL≈{slPts:0.####} pt ({slTicks} ticks)" : ", SL=DISABLED");
            Log(info, LogLevel.Information);

            if (ibBreakDir > 0)
            {
                SetProfitTarget("IBLong", CalculationMode.Ticks, ptTicks);
                if (EnableStopLoss) SetStopLoss("IBLong", CalculationMode.Ticks, slTicks, false);
                EnterLong(Contracts, "IBLong");
            }
            else
            {
                SetProfitTarget("IBShort", CalculationMode.Ticks, ptTicks);
                if (EnableStopLoss) SetStopLoss("IBShort", CalculationMode.Ticks, slTicks, false);
                EnterShort(Contracts, "IBShort");
            }
            ibTradedToday = true;

            ibBreakDir = 0; ibBreakBarIndex = -1; ibConfirmCount = 0; ibWaitingForRetest = false;
        }

        // --- Order/Position callbacks (NT8 8.1 compatible) ---
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

                if (orderState == OrderState.Filled
                    && entryFillTime == null
                    && (tag == "OpenLong" || tag == "OpenShort" || tag == "IBLong" || tag == "IBShort"))
                {
                    entryFillTime = time;
                    entryAvgPrice = averageFillPrice > 0 ? averageFillPrice : Position.AveragePrice;
                    entryQty      = Math.Abs(quantity);
                    lastEntryTag  = tag;

                    Log($"[FILL] {GetAcct()} Entry filled: {tag}, qty={entryQty}, avg={entryAvgPrice:0.#####}", LogLevel.Information);
                }

                if (orderState == OrderState.Filled)
                {
                    string low = tag.ToLowerInvariant();
                    if (low.Contains("profit") || low.Contains("target") || low.Contains("tgt"))
                    {
                        lastExitReason = "TARGET";
                        exitFillTime = time;
                    }
                    else if (low.Contains("stop") || low.Contains("stp"))
                    {
                        lastExitReason = "STOP";
                        exitFillTime = time;
                    }
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
                if (position == null) return;

                if (position.MarketPosition == MarketPosition.Flat && entryFillTime != null)
                {
                    DateTime exitTime = exitFillTime ?? DateTime.Now;
                    double seconds = (exitTime - entryFillTime.Value).TotalSeconds;

                    double pnlCurrency = 0.0;
                    try
                    {
                        var trades = SystemPerformance.AllTrades;
                        if (trades != null && trades.Count > 0)
                        {
                            var lastTrade = trades[trades.Count - 1];
                            pnlCurrency = lastTrade.ProfitCurrency;
                        }
                    }
                    catch { }

                    Log($"[EXIT] {GetAcct()} Exit ({lastEntryTag}): {lastExitReason} | time_to_exit={seconds:0.00}s | pnl=${pnlCurrency:0.##}", LogLevel.Information);

                    entryFillTime = null;
                    exitFillTime  = null;
                    entryAvgPrice = 0.0;
                    entryQty      = 0;
                    lastEntryTag  = string.Empty;
                    lastExitReason = "MANUAL/OTHER";
                }
            }
            catch (Exception ex)
            {
                Log("[ERROR] OnPositionUpdate: " + ex.Message, LogLevel.Error);
            }
        }

        // --- Helpers ---
        private string GetAcct()
        {
            try
            {
                return (Account != null && !string.IsNullOrEmpty(Account.Name)) ? Account.Name : "Backtest/Unknown";
            }
            catch { return "Backtest/Unknown"; }
        }

        private int GetPtTicks() => Math.Max(1, (int)Math.Round(ProfitTargetPoints / TickSize));

        private double GetPointValue()
        {
            double pv = Instrument?.MasterInstrument?.PointValue ?? 0.0;
            return pv > 0 ? pv : 20.0;
        }

        private double GetStopPointsPerContract()
        {
            if (!EnableStopLoss || StopRiskDollars <= 0 || Contracts <= 0)
                return 0.0;
            double pv = GetPointValue();
            return StopRiskDollars / (Contracts * pv);
        }

        private int GetStopTicksPerContract()
        {
            double pts = GetStopPointsPerContract();
            if (pts <= 0.0) return 0;
            return Math.Max(1, (int)Math.Round(pts / TickSize));
        }

        private int ToHmsInt(TimeSpan ts) => ts.Hours * 10000 + ts.Minutes * 100 + ts.Seconds;
    }
}
