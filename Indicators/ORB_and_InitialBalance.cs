#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Xml.Serialization;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

// ORB_and_InitialBalance.cs
// NT8 8.1.5.2-compatible: HH:mm inputs, optional Eastern-time conversion, solid-line styling
namespace NinjaTrader.NinjaScript.Indicators
{
    public class ORB_and_InitialBalance : Indicator
    {
        // ---- Runtime tracking ----
        private bool   orbActive;
        private double orbHigh = double.MinValue;
        private double orbLow  = double.MaxValue;

        private bool   ibActive;
        private double ibHigh  = double.MinValue;
        private double ibLow   = double.MaxValue;

        private DateTime currentSessionDate;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                     = "ORB and Initial Balance (HH:mm)";
                Description              = "Plots Opening Range and Initial Balance using HH:mm entries; optional Eastern-time conversion.";
                Calculate                = Calculate.OnPriceChange;
                IsOverlay                = true;
                DrawOnPricePanel         = true;
                DisplayInDataBox         = false;
                IsSuspendedWhileInactive = true;

                // Defaults: equities/RTH in Eastern. If your chart is Central, enable UseEasternTime=true and
                // keep 09:30/09:45; it will auto-convert to 08:30/08:45 local.
                UseEasternTime = true;
                ORBStartHHmm   = "09:30";
                ORBEndHHmm     = "09:45";
                IBStartHHmm    = "09:30";
                IBMinutes      = 60;

                PlotORB        = true;
                PlotIB         = true;

                // Styling (solid lines for max compatibility)
                ORBHighBrush   = Brushes.DodgerBlue;
                ORBLowBrush    = Brushes.DodgerBlue;
                IBHighBrush    = Brushes.OrangeRed;
                IBLowBrush     = Brushes.OrangeRed;

                ORBHighWidth   = 2;
                ORBLowWidth    = 2;
                IBHighWidth    = 2;
                IBLowWidth     = 2;

                AddPlot(ORBHighBrush, "ORB High"); // [0]
                AddPlot(ORBLowBrush,  "ORB Low");  // [1]
                AddPlot(IBHighBrush,  "IB High");  // [2]
                AddPlot(IBLowBrush,   "IB Low");   // [3]
            }
            else if (State == State.DataLoaded)
            {
                ResetRanges();
                ApplyPlotStyling();
            }
        }

        private void ResetRanges()
        {
            orbActive = ibActive = false;

            orbHigh = ibHigh = double.MinValue;
            orbLow  = ibLow  = double.MaxValue;

            if (Count > 0)
            {
                currentSessionDate = Times[0][0].Date;
                for (int i = 0; i < 4; i++)
                    Values[i][0] = double.NaN;
            }
        }

        private void ApplyPlotStyling()
        {
            Plots[0].Brush = ORBHighBrush; Plots[0].Width = Math.Max(1, ORBHighWidth);
            Plots[1].Brush = ORBLowBrush;  Plots[1].Width = Math.Max(1, ORBLowWidth);
            Plots[2].Brush = IBHighBrush;  Plots[2].Width = Math.Max(1, IBHighWidth);
            Plots[3].Brush = IBLowBrush;   Plots[3].Width = Math.Max(1, IBLowWidth);
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
                return;

            if (Bars.IsFirstBarOfSession)
                ResetRanges();

            var barTime = Times[0][0];
            var barDate = barTime.Date;
            if (barDate != currentSessionDate)
            {
                currentSessionDate = barDate;
                ResetRanges();
            }

            // Build today's schedule
            DateTime orbStart = BuildScheduledTime(barDate, ORBStartHHmm);
            DateTime orbEnd   = BuildScheduledTime(barDate, ORBEndHHmm);

            DateTime ibStart  = BuildScheduledTime(barDate, IBStartHHmm);
            DateTime ibEnd    = ibStart.AddMinutes(Math.Max(1, IBMinutes));

            // ORB window
            if (PlotORB)
            {
                if (barTime >= orbStart && barTime <= orbEnd)
                {
                    orbActive = true;
                    if (High[0] > orbHigh) orbHigh = High[0];
                    if (Low[0]  < orbLow ) orbLow  = Low[0];

                    Values[0][0] = orbHigh; // show evolving during ORB
                    Values[1][0] = orbLow;
                }
                else
                {
                    if (barTime > orbEnd) orbActive = false;

                    if (!orbActive && orbHigh != double.MinValue && orbLow != double.MaxValue && barTime >= orbEnd)
                    {
                        Values[0][0] = orbHigh;
                        Values[1][0] = orbLow;
                    }
                    else
                    {
                        Values[0][0] = double.NaN;
                        Values[1][0] = double.NaN;
                    }
                }
            }
            else
            {
                Values[0][0] = double.NaN;
                Values[1][0] = double.NaN;
            }

            // IB window
            if (PlotIB)
            {
                if (barTime >= ibStart && barTime <= ibEnd)
                {
                    ibActive = true;
                    if (High[0] > ibHigh) ibHigh = High[0];
                    if (Low[0]  < ibLow ) ibLow  = Low[0];

                    Values[2][0] = ibHigh;
                    Values[3][0] = ibLow;
                }
                else
                {
                    if (barTime > ibEnd) ibActive = false;

                    if (!ibActive && ibHigh != double.MinValue && ibLow != double.MaxValue && barTime >= ibEnd)
                    {
                        Values[2][0] = ibHigh;
                        Values[3][0] = ibLow;
                    }
                    else
                    {
                        Values[2][0] = double.NaN;
                        Values[3][0] = double.NaN;
                    }
                }
            }
            else
            {
                Values[2][0] = double.NaN;
                Values[3][0] = double.NaN;
            }
        }

        // ---- Helpers ----
        private DateTime BuildScheduledTime(DateTime sessionDate, string hhmm)
        {
            var ts = ParseHHmm(hhmm);                   // 00:00 if invalid
            var baseLocal = sessionDate + ts;           // interpret in local/chart time by default

            if (!UseEasternTime)
                return baseLocal;

            // Interpret the HH:mm as Eastern Time and convert to local machine time.
            // (Most users run charts in local time; if your Data Series is set to Exchange/other,
            // set UseEasternTime = false and enter HH:mm in that chart timezone directly.)
            try
            {
                var etZone  = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                var utcFromEt = TimeZoneInfo.ConvertTimeToUtc(
                    new DateTime(sessionDate.Year, sessionDate.Month, sessionDate.Day, ts.Hours, ts.Minutes, 0, DateTimeKind.Unspecified),
                    etZone
                );
                return TimeZoneInfo.ConvertTimeFromUtc(utcFromEt, TimeZoneInfo.Local);
            }
            catch
            {
                return baseLocal; // fail-safe if TZ lookup fails
            }
        }

        private static TimeSpan ParseHHmm(string hhmm)
        {
            if (string.IsNullOrWhiteSpace(hhmm))
                return TimeSpan.Zero;

            // Accept "H:mm" or "HH:mm"
            TimeSpan ts;
            if (TimeSpan.TryParseExact(hhmm.Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out ts))
                return ts;

            // Accept "HHmm"
            if (TimeSpan.TryParseExact(hhmm.Trim(), new[] { @"hhmm" }, CultureInfo.InvariantCulture, out ts))
                return ts;

            return TimeSpan.Zero;
        }

        // ---- Properties ----

        // Time handling
        [NinjaScriptProperty]
        [Display(Name = "Use Eastern Time", GroupName = "Schedule", Order = 0, Description = "If true, interpret HH:mm as US Eastern and convert to local/chart time.")]
        public bool UseEasternTime { get; set; }

        // ORB window
        [NinjaScriptProperty]
        [Display(Name = "ORB Start (HH:mm)", GroupName = "ORB Window", Order = 10)]
        public string ORBStartHHmm { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ORB End (HH:mm)", GroupName = "ORB Window", Order = 11)]
        public string ORBEndHHmm { get; set; }

        // IB window
        [NinjaScriptProperty]
        [Display(Name = "IB Start (HH:mm)", GroupName = "IB Window", Order = 20)]
        public string IBStartHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(1, 300)]
        [Display(Name = "IB Minutes", GroupName = "IB Window", Order = 21)]
        public int IBMinutes { get; set; }

        // Toggles
        [NinjaScriptProperty]
        [Display(Name = "Plot ORB", GroupName = "Plots", Order = 30)]
        public bool PlotORB { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Plot IB", GroupName = "Plots", Order = 31)]
        public bool PlotIB { get; set; }

        // Styling (solid lines)
        [XmlIgnore]
        [Display(Name = "ORB High Color", GroupName = "Style: ORB", Order = 40)]
        public Brush ORBHighBrush { get; set; }
        [XmlIgnore]
        [Display(Name = "ORB Low Color", GroupName = "Style: ORB", Order = 41)]
        public Brush ORBLowBrush { get; set; }
        [Range(1,10)]
        [Display(Name = "ORB High Width", GroupName = "Style: ORB", Order = 42)]
        public int ORBHighWidth { get; set; }
        [Range(1,10)]
        [Display(Name = "ORB Low Width", GroupName = "Style: ORB", Order = 43)]
        public int ORBLowWidth { get; set; }

        [XmlIgnore]
        [Display(Name = "IB High Color", GroupName = "Style: IB", Order = 50)]
        public Brush IBHighBrush { get; set; }
        [XmlIgnore]
        [Display(Name = "IB Low Color", GroupName = "Style: IB", Order = 51)]
        public Brush IBLowBrush { get; set; }
        [Range(1,10)]
        [Display(Name = "IB High Width", GroupName = "Style: IB", Order = 52)]
        public int IBHighWidth { get; set; }
        [Range(1,10)]
        [Display(Name = "IB Low Width", GroupName = "Style: IB", Order = 53)]
        public int IBLowWidth { get; set; }
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ORB_and_InitialBalance[] cacheORB_and_InitialBalance;
		public ORB_and_InitialBalance ORB_and_InitialBalance(bool useEasternTime, string oRBStartHHmm, string oRBEndHHmm, string iBStartHHmm, int iBMinutes, bool plotORB, bool plotIB)
		{
			return ORB_and_InitialBalance(Input, useEasternTime, oRBStartHHmm, oRBEndHHmm, iBStartHHmm, iBMinutes, plotORB, plotIB);
		}

		public ORB_and_InitialBalance ORB_and_InitialBalance(ISeries<double> input, bool useEasternTime, string oRBStartHHmm, string oRBEndHHmm, string iBStartHHmm, int iBMinutes, bool plotORB, bool plotIB)
		{
			if (cacheORB_and_InitialBalance != null)
				for (int idx = 0; idx < cacheORB_and_InitialBalance.Length; idx++)
					if (cacheORB_and_InitialBalance[idx] != null && cacheORB_and_InitialBalance[idx].UseEasternTime == useEasternTime && cacheORB_and_InitialBalance[idx].ORBStartHHmm == oRBStartHHmm && cacheORB_and_InitialBalance[idx].ORBEndHHmm == oRBEndHHmm && cacheORB_and_InitialBalance[idx].IBStartHHmm == iBStartHHmm && cacheORB_and_InitialBalance[idx].IBMinutes == iBMinutes && cacheORB_and_InitialBalance[idx].PlotORB == plotORB && cacheORB_and_InitialBalance[idx].PlotIB == plotIB && cacheORB_and_InitialBalance[idx].EqualsInput(input))
						return cacheORB_and_InitialBalance[idx];
			return CacheIndicator<ORB_and_InitialBalance>(new ORB_and_InitialBalance(){ UseEasternTime = useEasternTime, ORBStartHHmm = oRBStartHHmm, ORBEndHHmm = oRBEndHHmm, IBStartHHmm = iBStartHHmm, IBMinutes = iBMinutes, PlotORB = plotORB, PlotIB = plotIB }, input, ref cacheORB_and_InitialBalance);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ORB_and_InitialBalance ORB_and_InitialBalance(bool useEasternTime, string oRBStartHHmm, string oRBEndHHmm, string iBStartHHmm, int iBMinutes, bool plotORB, bool plotIB)
		{
			return indicator.ORB_and_InitialBalance(Input, useEasternTime, oRBStartHHmm, oRBEndHHmm, iBStartHHmm, iBMinutes, plotORB, plotIB);
		}

		public Indicators.ORB_and_InitialBalance ORB_and_InitialBalance(ISeries<double> input , bool useEasternTime, string oRBStartHHmm, string oRBEndHHmm, string iBStartHHmm, int iBMinutes, bool plotORB, bool plotIB)
		{
			return indicator.ORB_and_InitialBalance(input, useEasternTime, oRBStartHHmm, oRBEndHHmm, iBStartHHmm, iBMinutes, plotORB, plotIB);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ORB_and_InitialBalance ORB_and_InitialBalance(bool useEasternTime, string oRBStartHHmm, string oRBEndHHmm, string iBStartHHmm, int iBMinutes, bool plotORB, bool plotIB)
		{
			return indicator.ORB_and_InitialBalance(Input, useEasternTime, oRBStartHHmm, oRBEndHHmm, iBStartHHmm, iBMinutes, plotORB, plotIB);
		}

		public Indicators.ORB_and_InitialBalance ORB_and_InitialBalance(ISeries<double> input , bool useEasternTime, string oRBStartHHmm, string oRBEndHHmm, string iBStartHHmm, int iBMinutes, bool plotORB, bool plotIB)
		{
			return indicator.ORB_and_InitialBalance(input, useEasternTime, oRBStartHHmm, oRBEndHHmm, iBStartHHmm, iBMinutes, plotORB, plotIB);
		}
	}
}

#endregion
