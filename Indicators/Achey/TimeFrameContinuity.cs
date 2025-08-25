#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators.Achey
{
    // Version: 1.0.0
    // Author: Damon Achey
    // Email: Damon@Achey.Net
    public class TimeFrameContinuity : Indicator
    {
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Overview of multiple time frames";
                Name = "TimeFrameContinuity";
                Calculate = Calculate.OnPriceChange;
                IsAutoScale = true;
                IsOverlay = false;
                DisplayInDataBox = false;
                DrawOnPricePanel = false;
                DrawHorizontalGridLines = false;
                DrawVerticalGridLines = false;
                PaintPriceMarkers = false;
                ScaleJustification = ScaleJustification.Right;

                ShowLabels = true;
                LabelFontSize = 12;
                DotSize = 3;
                Show15Minute = true;
                Show30Minute = true;
                Show1Hour = true;
                Show4Hour = true;
                Show1Day = true;
                Show1Week = true;
                Show1Month = true;
                Show1Quarter = true;

                //Disable this property if your indicator requires custom values that cumulate with each new market data event. 
                //See Help Guide for additional information.
                IsSuspendedWhileInactive = true;
            }
            else if (State == State.Configure)
            {
                var dotStyleHelper = DashStyleHelper.Dot;
                var stroke = new Stroke(Brushes.Transparent, dotStyleHelper, DotSize);

                AddPlot(stroke, PlotStyle.Dot, "Current");
                Periods.Add(0, (BarsPeriod.Value + " " + BarsPeriod.BarsPeriodType).ToLower());

                var index = 1;

                if (Show15Minute &&
                    (BarsPeriod.BarsPeriodType < BarsPeriodType.Minute || (BarsPeriod.BarsPeriodType == BarsPeriodType.Minute && BarsPeriod.Value < 15)))
                {
                    AddDataSeries(BarsPeriodType.Minute, 15);
                    AddPlot(stroke, PlotStyle.Dot, "15 min");
                    Periods.Add(index++, "15 min");
                }

                if (Show30Minute &&
                    (BarsPeriod.BarsPeriodType < BarsPeriodType.Minute || (BarsPeriod.BarsPeriodType == BarsPeriodType.Minute && BarsPeriod.Value < 30)))
                {
                    AddDataSeries(BarsPeriodType.Minute, 30);
                    AddPlot(stroke, PlotStyle.Dot, "30 min");
                    Periods.Add(index++, "30 min");
                }

                if (Show1Hour &&
                    (BarsPeriod.BarsPeriodType < BarsPeriodType.Minute || (BarsPeriod.BarsPeriodType == BarsPeriodType.Minute && BarsPeriod.Value < 60)))
                {
                    AddDataSeries(BarsPeriodType.Minute, 60);
                    AddPlot(stroke, PlotStyle.Dot, "1 hour");
                    Periods.Add(index++, "1 hour");
                }

                if (Show4Hour &&
                    (BarsPeriod.BarsPeriodType < BarsPeriodType.Minute || (BarsPeriod.BarsPeriodType == BarsPeriodType.Minute && BarsPeriod.Value < 240)))
                {
                    AddDataSeries(BarsPeriodType.Minute, 240);
                    AddPlot(stroke, PlotStyle.Dot, "4 hour");
                    Periods.Add(index++, "4 hour");
                }

                if (Show1Day &&
                    (BarsPeriod.BarsPeriodType < BarsPeriodType.Day || (BarsPeriod.BarsPeriodType == BarsPeriodType.Day && BarsPeriod.Value < 1)))
                {
                    AddDataSeries(BarsPeriodType.Day, 1);
                    AddPlot(stroke, PlotStyle.Dot, "1 day");
                    Periods.Add(index++, "1 day");
                }

                if (Show1Week &&
                    (BarsPeriod.BarsPeriodType < BarsPeriodType.Week || (BarsPeriod.BarsPeriodType == BarsPeriodType.Week && BarsPeriod.Value < 1)))
                {
                    AddDataSeries(Instrument.FullName, new BarsPeriod { BarsPeriodType = BarsPeriodType.Week, Value = 1 }, 52, Bars.TradingHours.Name, false);
                    AddPlot(stroke, PlotStyle.Dot, "1 week");
                    Periods.Add(index++, "1 week");
                }

                if (Show1Month &&
                    (BarsPeriod.BarsPeriodType < BarsPeriodType.Month || (BarsPeriod.BarsPeriodType == BarsPeriodType.Month && BarsPeriod.Value < 1)))
                {
                    AddDataSeries(Instrument.FullName, new BarsPeriod { BarsPeriodType = BarsPeriodType.Month, Value = 1 }, 12, Bars.TradingHours.Name, false);
                    AddPlot(stroke, PlotStyle.Dot, "1 month");
                    Periods.Add(index++, "1 month");
                }

                if (Show1Quarter &&
                    (BarsPeriod.BarsPeriodType < BarsPeriodType.Month || (BarsPeriod.BarsPeriodType == BarsPeriodType.Month && BarsPeriod.Value < 3)))
                {
                    AddDataSeries(Instrument.FullName, new BarsPeriod { BarsPeriodType = BarsPeriodType.Month, Value = 1 }, 12, Bars.TradingHours.Name, false);
                    AddPlot(stroke, PlotStyle.Dot, "1 quarter");
                    Periods.Add(index++, "1 quarter");
                }

                AddPlot(new Stroke(Brushes.Transparent, dotStyleHelper, DotSize), PlotStyle.Block, "summary");
                Print("here");

                LabelFont = new SimpleFont() { Size = LabelFontSize };
            }
        }

        private readonly Dictionary<int, string> Periods = new Dictionary<int, string>();
        SimpleFont LabelFont;

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < 1)
                return;

            var allGreen = true;
            var allRed = true;

            for (var i = CurrentBars.Length - 1; i >= 0; i--)
            {
                if (CurrentBars[i] > 0)
                {
                    var row = CurrentBars.Length - i;
                    Values[i][0] = row;

                    var open = Opens[i][0];
                    var close = Closes[0][0];
                    var skip = false;

                    if (State == State.Historical)
                    {
                        open = Closes[i][0];
                        close = Closes[0][0];

                        if (Times[i][0] == Times[0][0])
                        {
                            open = Opens[i][0];
                        }
                    }

                    if (!skip)
                    {
                        if (open < close)
                        {
                            PlotBrushes[i][0] = ChartBars.Properties.ChartStyle.UpBrush;
                            allRed = false;
                        }
                        else if (open > close)
                        {
                            PlotBrushes[i][0] = ChartBars.Properties.ChartStyle.DownBrush;
                            allGreen = false;
                        }
                        else
                        {
                            PlotBrushes[i][0] = Brushes.Gray;
                            allGreen = false;
                            allRed = false;
                        }
                    }

                    if (ShowLabels)
                        Draw.Text(this, "period" + i, true, "  " + Periods[i], 0, row, 0, ChartControl.Properties.ChartText, LabelFont, TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
                }
            }

            var color = allGreen ? ChartBars.Properties.ChartStyle.UpBrush
                : allRed ? ChartBars.Properties.ChartStyle.DownBrush
                : Brushes.Gray;

            Values[CurrentBars.Length][0] = CurrentBars.Length + 1;
            PlotBrushes[CurrentBars.Length][0] = color;

            if (ShowLabels)
                Draw.Text(this, "summary", true, "  summary", 0, CurrentBars.Length + 1, 0, ChartControl.Properties.ChartText, LabelFont, TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "Show labels", Order = 1, GroupName = "NinjaScriptParameters")]
        public bool ShowLabels
        { get; set; }

        [NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "Label font size", Order = 2, GroupName = "NinjaScriptParameters")]
        public int LabelFontSize
        { get; set; }

        [NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "Dot size", Order = 3, GroupName = "NinjaScriptParameters")]
        public int DotSize
        { get; set; }

        [NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "Show 15 minute", Order = 4, GroupName = "NinjaScriptParameters")]
        public bool Show15Minute
        { get; set; }

        [NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "Show 30 minute", Order = 5, GroupName = "NinjaScriptParameters")]
        public bool Show30Minute
        { get; set; }

        [NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "Show 1 hour", Order = 6, GroupName = "NinjaScriptParameters")]
        public bool Show1Hour
        { get; set; }

        [NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "Show 4 hour", Order = 7, GroupName = "NinjaScriptParameters")]
        public bool Show4Hour
        { get; set; }

        [NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "Show 1 day", Order = 8, GroupName = "NinjaScriptParameters")]
        public bool Show1Day
        { get; set; }

        [NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "Show 1 week", Order = 9, GroupName = "NinjaScriptParameters")]
        public bool Show1Week
        { get; set; }

        [NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "Show 1 month", Order = 10, GroupName = "NinjaScriptParameters")]
        public bool Show1Month
        { get; set; }

        [NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "Show 1 quarter", Order = 11, GroupName = "NinjaScriptParameters")]
        public bool Show1Quarter
        { get; set; }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Achey.TimeFrameContinuity[] cacheTimeFrameContinuity;
		public Achey.TimeFrameContinuity TimeFrameContinuity(bool showLabels, int labelFontSize, int dotSize, bool show15Minute, bool show30Minute, bool show1Hour, bool show4Hour, bool show1Day, bool show1Week, bool show1Month, bool show1Quarter)
		{
			return TimeFrameContinuity(Input, showLabels, labelFontSize, dotSize, show15Minute, show30Minute, show1Hour, show4Hour, show1Day, show1Week, show1Month, show1Quarter);
		}

		public Achey.TimeFrameContinuity TimeFrameContinuity(ISeries<double> input, bool showLabels, int labelFontSize, int dotSize, bool show15Minute, bool show30Minute, bool show1Hour, bool show4Hour, bool show1Day, bool show1Week, bool show1Month, bool show1Quarter)
		{
			if (cacheTimeFrameContinuity != null)
				for (int idx = 0; idx < cacheTimeFrameContinuity.Length; idx++)
					if (cacheTimeFrameContinuity[idx] != null && cacheTimeFrameContinuity[idx].ShowLabels == showLabels && cacheTimeFrameContinuity[idx].LabelFontSize == labelFontSize && cacheTimeFrameContinuity[idx].DotSize == dotSize && cacheTimeFrameContinuity[idx].Show15Minute == show15Minute && cacheTimeFrameContinuity[idx].Show30Minute == show30Minute && cacheTimeFrameContinuity[idx].Show1Hour == show1Hour && cacheTimeFrameContinuity[idx].Show4Hour == show4Hour && cacheTimeFrameContinuity[idx].Show1Day == show1Day && cacheTimeFrameContinuity[idx].Show1Week == show1Week && cacheTimeFrameContinuity[idx].Show1Month == show1Month && cacheTimeFrameContinuity[idx].Show1Quarter == show1Quarter && cacheTimeFrameContinuity[idx].EqualsInput(input))
						return cacheTimeFrameContinuity[idx];
			return CacheIndicator<Achey.TimeFrameContinuity>(new Achey.TimeFrameContinuity(){ ShowLabels = showLabels, LabelFontSize = labelFontSize, DotSize = dotSize, Show15Minute = show15Minute, Show30Minute = show30Minute, Show1Hour = show1Hour, Show4Hour = show4Hour, Show1Day = show1Day, Show1Week = show1Week, Show1Month = show1Month, Show1Quarter = show1Quarter }, input, ref cacheTimeFrameContinuity);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Achey.TimeFrameContinuity TimeFrameContinuity(bool showLabels, int labelFontSize, int dotSize, bool show15Minute, bool show30Minute, bool show1Hour, bool show4Hour, bool show1Day, bool show1Week, bool show1Month, bool show1Quarter)
		{
			return indicator.TimeFrameContinuity(Input, showLabels, labelFontSize, dotSize, show15Minute, show30Minute, show1Hour, show4Hour, show1Day, show1Week, show1Month, show1Quarter);
		}

		public Indicators.Achey.TimeFrameContinuity TimeFrameContinuity(ISeries<double> input , bool showLabels, int labelFontSize, int dotSize, bool show15Minute, bool show30Minute, bool show1Hour, bool show4Hour, bool show1Day, bool show1Week, bool show1Month, bool show1Quarter)
		{
			return indicator.TimeFrameContinuity(input, showLabels, labelFontSize, dotSize, show15Minute, show30Minute, show1Hour, show4Hour, show1Day, show1Week, show1Month, show1Quarter);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Achey.TimeFrameContinuity TimeFrameContinuity(bool showLabels, int labelFontSize, int dotSize, bool show15Minute, bool show30Minute, bool show1Hour, bool show4Hour, bool show1Day, bool show1Week, bool show1Month, bool show1Quarter)
		{
			return indicator.TimeFrameContinuity(Input, showLabels, labelFontSize, dotSize, show15Minute, show30Minute, show1Hour, show4Hour, show1Day, show1Week, show1Month, show1Quarter);
		}

		public Indicators.Achey.TimeFrameContinuity TimeFrameContinuity(ISeries<double> input , bool showLabels, int labelFontSize, int dotSize, bool show15Minute, bool show30Minute, bool show1Hour, bool show4Hour, bool show1Day, bool show1Week, bool show1Month, bool show1Quarter)
		{
			return indicator.TimeFrameContinuity(input, showLabels, labelFontSize, dotSize, show15Minute, show30Minute, show1Hour, show4Hour, show1Day, show1Week, show1Month, show1Quarter);
		}
	}
}

#endregion
