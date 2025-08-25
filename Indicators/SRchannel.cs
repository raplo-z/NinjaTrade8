#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

/*
    SRchannel (Symmetrical Pivot Detection in NinjaTrader)

    This indicator detects support/resistance channels using symmetrical pivot detection (similar to TradingView).
    Pivot detection is performed on the first tick of a new bar while the SR channel shading updates on every tick.
    The fill color follows this logic:
      - If both channel values are above the current price, the channel is filled with the resistance color.
      - If both channel values are below the current price, the channel is filled with the support color.
      - If the price is between the two levels, the channel is filled with the in‑channel color (grey by default).

    In this modified version, the indicator outputs each channel’s high and low values to the Data Box.
    It adds 20 additional plots (10 channels × 2 values) and updates their values on every bar update.

    Usage:
      1) Save as SRchannelSymPivot.cs in:
            Documents\NinjaTrader 8\bin\Custom\Indicators
      2) Compile in NinjaTrader 8.
      3) Add “SRchannel (Symmetrical Pivot Detection)” to your chart.
*/

namespace NinjaTrader.NinjaScript.Indicators
{
    // Enums for pivot and MA type
    public enum PivotSource
    {
        HighLow,
        CloseOpen
    }

    public enum MAType
    {
        SMA,
        EMA
    }

    public class SRchannelSymPivot : Indicator
    {
        #region Fields

        private List<double> pivotVals;
        private List<int>    pivotBarIndices;

        // Up to 10 channels => (top, bottom) => 20 slots
        private double[] srLevels;
        private double[] srStrength;

        // Rectangle tags for drawing SR channel fills
        private string[] rectangleTags;

        // Optional moving averages
        private SMA sma1;
        private EMA ema1;
        private SMA sma2;
        private EMA ema2;

        // Track prior bar's close for breakout detection
        private double priorClose = double.NaN;

        // Ensure enough bars for symmetrical pivot detection
        private int requiredBarsLookback;

        #endregion

        #region Parameters

        [NinjaScriptProperty]
        [Description("Pivot Period (bars to left & right).")]
        [Category("Parameters")]
        public int Prd { get; set; }

        [NinjaScriptProperty]
        [Description("Use High/Low or Close/Open for pivot detection.")]
        [Category("Parameters")]
        public PivotSource Ppsrc { get; set; }

        [NinjaScriptProperty]
        [Description("Max channel width as % of 300-bar range (1..8 typical).")]
        [Category("Parameters")]
        public int ChannelW { get; set; }

        [NinjaScriptProperty]
        [Description("Minimum 'strength' needed (each pivot adds 20).")]
        [Category("Parameters")]
        public int MinStrength { get; set; }

        [NinjaScriptProperty]
        [Description("Maximum number of S/R channels to show (1..10).")]
        [Category("Parameters")]
        public int MaxNumSr { get; set; }

        [NinjaScriptProperty]
        [Description("Loopback period for pivot lookback (100..400 typical).")]
        [Category("Parameters")]
        public int Loopback { get; set; }

        // Colors
        [NinjaScriptProperty]
        [XmlIgnore]
        [Description("Color used for Resistance channels.")]
        [Category("Visual")]
        public Brush ResColor { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Description("Color used for Support channels.")]
        [Category("Visual")]
        public Brush SupColor { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Description("Color used when price is inside the channel.")]
        [Category("Visual")]
        public Brush InchColor { get; set; }

        // Extras
        [NinjaScriptProperty]
        [Description("Show pivot markers (H/L)?")]
        [Category("Extras")]
        public bool ShowPp { get; set; }

        [NinjaScriptProperty]
        [Description("Show breakout markers (triangles) when S/R is broken?")]
        [Category("Extras")]
        public bool ShowSrBroken { get; set; }

        // Moving averages
        [NinjaScriptProperty]
        [Description("Enable first moving average?")]
        [Category("Moving Averages")]
        public bool ShowMa1 { get; set; }

        [NinjaScriptProperty]
        [Description("Length of first MA.")]
        [Category("Moving Averages")]
        public int Ma1Len { get; set; }

        [NinjaScriptProperty]
        [Description("Type of first MA (SMA or EMA).")]
        [Category("Moving Averages")]
        public MAType Ma1Type { get; set; }

        [NinjaScriptProperty]
        [Description("Enable second moving average?")]
        [Category("Moving Averages")]
        public bool ShowMa2 { get; set; }

        [NinjaScriptProperty]
        [Description("Length of second MA.")]
        [Category("Moving Averages")]
        public int Ma2Len { get; set; }

        [NinjaScriptProperty]
        [Description("Type of second MA (SMA or EMA).")]
        [Category("Moving Averages")]
        public MAType Ma2Type { get; set; }

        #endregion

        #region OnStateChange

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "SRchannel (Symmetrical Pivot Detection)";
                Description = "Support/Resistance Channels using symmetrical pivot detection (similar to TradingView).";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DisplayInDataBox = true;   // Enable Data Box display for all plots
                PaintPriceMarkers = true;

                // Default parameters
                Prd = 10;
                Ppsrc = PivotSource.HighLow;
                ChannelW = 5;
                MinStrength = 1;
                MaxNumSr = 6;
                Loopback = 290;

                ResColor = Brushes.Red;
                SupColor = Brushes.Lime;
                InchColor = Brushes.Gray;

                ShowPp = false;
                ShowSrBroken = false;

                ShowMa1 = false;
                Ma1Len = 50;
                Ma1Type = MAType.SMA;
                ShowMa2 = false;
                Ma2Len = 200;
                Ma2Type = MAType.SMA;

                // Add plots for optional MAs (plots 0 and 1)
                AddPlot(Brushes.Blue, "PlotMA1");
                AddPlot(Brushes.Red, "PlotMA2");

                // Add plots for channel highs and lows (10 channels = 20 plots)
                // Use a nearly invisible brush so the values show in the Data Box without cluttering the chart.
                for (int i = 0; i < 10; i++)
                {
                    AddPlot(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), "Channel" + (i + 1) + "High");
                    AddPlot(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), "Channel" + (i + 1) + "Low");
                }
            }
            else if (State == State.Configure)
            {
                pivotVals = new List<double>();
                pivotBarIndices = new List<int>();

                srLevels = new double[20];
                srStrength = new double[10];

                rectangleTags = new string[10];
                for (int i = 0; i < 10; i++)
                    rectangleTags[i] = "SRchannelRect_" + i;

                // Ensure we have enough bars for pivot detection
                requiredBarsLookback = (2 * Prd) + 10;
            }
            else if (State == State.DataLoaded)
            {
                // Initialize moving averages if enabled
                if (ShowMa1)
                {
                    if (Ma1Type == MAType.SMA)
                        sma1 = SMA(Close, Ma1Len);
                    else
                        ema1 = EMA(Close, Ma1Len);
                }
                if (ShowMa2)
                {
                    if (Ma2Type == MAType.SMA)
                        sma2 = SMA(Close, Ma2Len);
                    else
                        ema2 = EMA(Close, Ma2Len);
                }
            }
        }

        #endregion

        #region OnBarUpdate

        protected override void OnBarUpdate()
        {
            if (CurrentBar < requiredBarsLookback)
                return;

            // Perform pivot detection and SR channel recalculation only on the first tick of a new bar
            if (IsFirstTickOfBar)
            {
                if (CurrentBar > 0)
                    priorClose = Close[1];

                int candidateIndex = CurrentBar - Prd;
                if (candidateIndex >= 0)
                {
                    bool ph = IsPivotHighSym(candidateIndex, Prd);
                    bool pl = IsPivotLowSym(candidateIndex, Prd);

                    if (ph || pl)
                    {
                        double candidateVal = 0.0;
                        if (ph)
                        {
                            candidateVal = (Ppsrc == PivotSource.HighLow)
                                ? High.GetValueAt(candidateIndex)
                                : Math.Max(Close.GetValueAt(candidateIndex), Open.GetValueAt(candidateIndex));
                        }
                        else
                        {
                            candidateVal = (Ppsrc == PivotSource.HighLow)
                                ? Low.GetValueAt(candidateIndex)
                                : Math.Min(Close.GetValueAt(candidateIndex), Open.GetValueAt(candidateIndex));
                        }

                        pivotVals.Insert(0, candidateVal);
                        pivotBarIndices.Insert(0, candidateIndex);

                        for (int i = pivotVals.Count - 1; i >= 0; i--)
                        {
                            if ((CurrentBar - pivotBarIndices[i]) > Loopback)
                            {
                                pivotVals.RemoveAt(i);
                                pivotBarIndices.RemoveAt(i);
                            }
                        }
                        RecalcSR();
                    }

                    if (ShowPp && (IsPivotHighSym(candidateIndex, Prd) || IsPivotLowSym(candidateIndex, Prd)))
                    {
                        string tag = (IsPivotHighSym(candidateIndex, Prd) ? "PivotH" : "PivotL") + candidateIndex;
                        if (IsPivotHighSym(candidateIndex, Prd))
                        {
                            Draw.Text(
                                this,
                                tag,
                                "H",
                                candidateIndex,
                                (Ppsrc == PivotSource.HighLow
                                    ? High.GetValueAt(candidateIndex) + 2 * TickSize
                                    : Math.Max(Close.GetValueAt(candidateIndex), Open.GetValueAt(candidateIndex)) + 2 * TickSize),
                                Brushes.Red);
                        }
                        else
                        {
                            Draw.Text(
                                this,
                                tag,
                                "L",
                                candidateIndex,
                                (Ppsrc == PivotSource.HighLow
                                    ? Low.GetValueAt(candidateIndex) - 2 * TickSize
                                    : Math.Min(Close.GetValueAt(candidateIndex), Open.GetValueAt(candidateIndex)) - 2 * TickSize),
                                Brushes.Lime);
                        }
                    }
                }
            }

            // Update the SR channel shading on every tick
            DrawSRChannels();

            // Update the channel high/low plots in the Data Box.
            // Only update as many channels as allowed (up to MaxNumSr, or 10 channels maximum).
            int limit = Math.Min(10, MaxNumSr);
            for (int i = 0; i < limit; i++)
            {
                int highPlotIndex = 2 + (i * 2);     // Offset: plots 0 and 1 are for MA1 and MA2
                int lowPlotIndex = 2 + (i * 2) + 1;
                double top = srLevels[i * 2];
                double bot = srLevels[i * 2 + 1];
                // If no valid value (remains 0), output NaN so nothing appears in the Data Box.
                Values[highPlotIndex][0] = (top != 0.0 ? top : double.NaN);
                Values[lowPlotIndex][0] = (bot != 0.0 ? bot : double.NaN);
            }

            // Breakout logic (updates on every tick)
            if (ShowSrBroken && CurrentBar > 0)
            {
                bool inChannelNow = IsPriceInAnyChannel(Close[0]);
                if (!inChannelNow)
                {
                    bool resistanceBroken = false;
                    bool supportBroken = false;

                    for (int x = 0; x < Math.Min(10, MaxNumSr); x++)
                    {
                        double top = srLevels[x * 2];
                        double bot = srLevels[x * 2 + 1];
                        if (top == 0.0 && bot == 0.0)
                            continue;

                        if (priorClose <= top && Close[0] > top)
                            resistanceBroken = true;
                        if (priorClose >= bot && Close[0] < bot)
                            supportBroken = true;
                    }

                    if (resistanceBroken)
                    {
                        Draw.TriangleUp(
                            this,
                            "resBroke" + CurrentBar,
                            false,
                            0,
                            Low[0] - (2 * TickSize),
                            Brushes.Lime);
                    }
                    if (supportBroken)
                    {
                        Draw.TriangleDown(
                            this,
                            "supBroke" + CurrentBar,
                            false,
                            0,
                            High[0] + (2 * TickSize),
                            Brushes.Red);
                    }
                }
            }

            // Update moving average plots on each tick
            if (ShowMa1)
            {
                if (sma1 != null)
                    Values[0][0] = sma1[0];
                else if (ema1 != null)
                    Values[0][0] = ema1[0];
                else
                    Values[0][0] = double.NaN;
            }
            else
            {
                Values[0][0] = double.NaN;
            }

            if (ShowMa2)
            {
                if (sma2 != null)
                    Values[1][0] = sma2[0];
                else if (ema2 != null)
                    Values[1][0] = ema2[0];
                else
                    Values[1][0] = double.NaN;
            }
            else
            {
                Values[1][0] = double.NaN;
            }
        }

        #endregion

        #region Symmetrical Pivot Detection

        private bool IsPivotHighSym(int candIdx, int period)
        {
            if (candIdx < period || candIdx + period > CurrentBar)
                return false;

            double candVal = (Ppsrc == PivotSource.HighLow)
                ? High.GetValueAt(candIdx)
                : Math.Max(Close.GetValueAt(candIdx), Open.GetValueAt(candIdx));

            for (int i = candIdx - period; i < candIdx; i++)
            {
                double leftVal = (Ppsrc == PivotSource.HighLow)
                    ? High.GetValueAt(i)
                    : Math.Max(Close.GetValueAt(i), Open.GetValueAt(i));
                if (leftVal >= candVal)
                    return false;
            }

            for (int i = candIdx + 1; i <= candIdx + period; i++)
            {
                double rightVal = (Ppsrc == PivotSource.HighLow)
                    ? High.GetValueAt(i)
                    : Math.Max(Close.GetValueAt(i), Open.GetValueAt(i));
                if (rightVal > candVal)
                    return false;
            }
            return true;
        }

        private bool IsPivotLowSym(int candIdx, int period)
        {
            if (candIdx < period || candIdx + period > CurrentBar)
                return false;

            double candVal = (Ppsrc == PivotSource.HighLow)
                ? Low.GetValueAt(candIdx)
                : Math.Min(Close.GetValueAt(candIdx), Open.GetValueAt(candIdx));

            for (int i = candIdx - period; i < candIdx; i++)
            {
                double leftVal = (Ppsrc == PivotSource.HighLow)
                    ? Low.GetValueAt(i)
                    : Math.Min(Close.GetValueAt(i), Open.GetValueAt(i));
                if (leftVal <= candVal)
                    return false;
            }

            for (int i = candIdx + 1; i <= candIdx + period; i++)
            {
                double rightVal = (Ppsrc == PivotSource.HighLow)
                    ? Low.GetValueAt(i)
                    : Math.Min(Close.GetValueAt(i), Open.GetValueAt(i));
                if (rightVal < candVal)
                    return false;
            }
            return true;
        }

        #endregion

        #region SR Logic

        private void RecalcSR()
        {
            double highest300 = double.MinValue;
            double lowest300 = double.MaxValue;
            int checkBars = Math.Min(CurrentBar + 1, 300);

            for (int i = 0; i < checkBars; i++)
            {
                if (High[i] > highest300)
                    highest300 = High[i];
                if (Low[i] < lowest300)
                    lowest300 = Low[i];
            }
            double cwidth = (highest300 - lowest300) * (ChannelW / 100.0);

            var supres = new List<double>();
            for (int x = 0; x < pivotVals.Count; x++)
            {
                double pivotVal = pivotVals[x];
                double hi = pivotVal;
                double lo = pivotVal;
                double strength = 0;

                for (int y = 0; y < pivotVals.Count; y++)
                {
                    double other = pivotVals[y];
                    double wdth = (other <= hi) ? (hi - other) : (other - lo);
                    if (wdth <= cwidth)
                    {
                        if (other < lo)
                            lo = other;
                        if (other > hi)
                            hi = other;
                        strength += 20;
                    }
                }

                double overlap = 0;
                int limit = Math.Min(Loopback, CurrentBar + 1);
                for (int b = 0; b < limit; b++)
                {
                    double barH = High[b];
                    double barL = Low[b];
                    bool within = ((barH <= hi && barH >= lo) ||
                                   (barL <= hi && barL >= lo));
                    if (within)
                        overlap += 1.0;
                }
                double totalStr = strength + overlap;
                supres.Add(totalStr);
                supres.Add(hi);
                supres.Add(lo);
            }

            for (int i = 0; i < 20; i++)
                srLevels[i] = 0.0;
            for (int i = 0; i < 10; i++)
                srStrength[i] = 0.0;

            int used = 0;
            for (int i = 0; i < pivotVals.Count; i++)
            {
                double bestVal = -1;
                int bestIndex = -1;
                for (int j = 0; j < pivotVals.Count; j++)
                {
                    double sVal = supres[j * 3];
                    if (sVal > bestVal && sVal >= (MinStrength * 20.0))
                    {
                        bestVal = sVal;
                        bestIndex = j;
                    }
                }
                if (bestIndex >= 0)
                {
                    double hh = supres[bestIndex * 3 + 1];
                    double ll = supres[bestIndex * 3 + 2];

                    srLevels[used * 2] = hh;
                    srLevels[used * 2 + 1] = ll;
                    srStrength[used] = bestVal;

                    for (int k = 0; k < pivotVals.Count; k++)
                    {
                        double tmpH = supres[k * 3 + 1];
                        double tmpL = supres[k * 3 + 2];
                        bool overlaps = (tmpH <= hh && tmpH >= ll) || (tmpL <= hh && tmpL >= ll);
                        if (overlaps)
                            supres[k * 3] = -1.0;
                    }
                    used++;
                    if (used >= 10)
                        break;
                }
            }

            for (int x = 0; x < 9; x++)
            {
                for (int y = x + 1; y < 10; y++)
                {
                    if (srStrength[y] > srStrength[x])
                    {
                        double tmpStr = srStrength[y];
                        srStrength[y] = srStrength[x];
                        srStrength[x] = tmpStr;

                        double tmpTop = srLevels[y * 2];
                        double tmpBottom = srLevels[y * 2 + 1];
                        srLevels[y * 2] = srLevels[x * 2];
                        srLevels[y * 2 + 1] = srLevels[x * 2 + 1];
                        srLevels[x * 2] = tmpTop;
                        srLevels[x * 2 + 1] = tmpBottom;
                    }
                }
            }
        }

        private void DrawSRChannels()
        {
            int limit = Math.Min(10, MaxNumSr);
            for (int i = 0; i < limit; i++)
            {
                string tag = rectangleTags[i];
                double top = srLevels[i * 2];
                double bot = srLevels[i * 2 + 1];

                RemoveDrawObject(tag);
                if (top == 0.0 && bot == 0.0)
                    continue;

                // Determine the line color based on the current price relative to the channel.
                Brush lineBrush = InchColor;
                if (top > Close[0] && bot > Close[0])
                    lineBrush = ResColor;
                else if (top < Close[0] && bot < Close[0])
                    lineBrush = SupColor;

                // Create a new solid fill brush from the line color with a semi-transparent alpha.
                SolidColorBrush fillBrush = null;
                if (lineBrush is SolidColorBrush scb)
                {
                    fillBrush = new SolidColorBrush(Color.FromArgb(200, scb.Color.R, scb.Color.G, scb.Color.B));
                    fillBrush.Freeze();
                }
                else
                {
                    fillBrush = new SolidColorBrush(Colors.Transparent);
                }

                Draw.Rectangle(
                    this,
                    tag,
                    false,
                    0,            // left anchor (first bar)
                    top,          // top price
                    CurrentBar,   // right anchor (current bar)
                    bot,          // bottom price
                    lineBrush,    // outline color
                    fillBrush,    // fill color
                    1);
            }
        }

        private bool IsPriceInAnyChannel(double price)
        {
            int limit = Math.Min(10, MaxNumSr);
            for (int i = 0; i < limit; i++)
            {
                double top = srLevels[i * 2];
                double bot = srLevels[i * 2 + 1];
                if (top == 0.0 && bot == 0.0)
                    continue;

                double hi = Math.Max(top, bot);
                double lo = Math.Min(top, bot);
                if (price <= hi && price >= lo)
                    return true;
            }
            return false;
        }

        #endregion

        #region Plot Access
        // Values[0] => PlotMA1 (blue)
        // Values[1] => PlotMA2 (red)
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private SRchannelSymPivot[] cacheSRchannelSymPivot;
		public SRchannelSymPivot SRchannelSymPivot(int prd, PivotSource ppsrc, int channelW, int minStrength, int maxNumSr, int loopback, Brush resColor, Brush supColor, Brush inchColor, bool showPp, bool showSrBroken, bool showMa1, int ma1Len, MAType ma1Type, bool showMa2, int ma2Len, MAType ma2Type)
		{
			return SRchannelSymPivot(Input, prd, ppsrc, channelW, minStrength, maxNumSr, loopback, resColor, supColor, inchColor, showPp, showSrBroken, showMa1, ma1Len, ma1Type, showMa2, ma2Len, ma2Type);
		}

		public SRchannelSymPivot SRchannelSymPivot(ISeries<double> input, int prd, PivotSource ppsrc, int channelW, int minStrength, int maxNumSr, int loopback, Brush resColor, Brush supColor, Brush inchColor, bool showPp, bool showSrBroken, bool showMa1, int ma1Len, MAType ma1Type, bool showMa2, int ma2Len, MAType ma2Type)
		{
			if (cacheSRchannelSymPivot != null)
				for (int idx = 0; idx < cacheSRchannelSymPivot.Length; idx++)
					if (cacheSRchannelSymPivot[idx] != null && cacheSRchannelSymPivot[idx].Prd == prd && cacheSRchannelSymPivot[idx].Ppsrc == ppsrc && cacheSRchannelSymPivot[idx].ChannelW == channelW && cacheSRchannelSymPivot[idx].MinStrength == minStrength && cacheSRchannelSymPivot[idx].MaxNumSr == maxNumSr && cacheSRchannelSymPivot[idx].Loopback == loopback && cacheSRchannelSymPivot[idx].ResColor == resColor && cacheSRchannelSymPivot[idx].SupColor == supColor && cacheSRchannelSymPivot[idx].InchColor == inchColor && cacheSRchannelSymPivot[idx].ShowPp == showPp && cacheSRchannelSymPivot[idx].ShowSrBroken == showSrBroken && cacheSRchannelSymPivot[idx].ShowMa1 == showMa1 && cacheSRchannelSymPivot[idx].Ma1Len == ma1Len && cacheSRchannelSymPivot[idx].Ma1Type == ma1Type && cacheSRchannelSymPivot[idx].ShowMa2 == showMa2 && cacheSRchannelSymPivot[idx].Ma2Len == ma2Len && cacheSRchannelSymPivot[idx].Ma2Type == ma2Type && cacheSRchannelSymPivot[idx].EqualsInput(input))
						return cacheSRchannelSymPivot[idx];
			return CacheIndicator<SRchannelSymPivot>(new SRchannelSymPivot(){ Prd = prd, Ppsrc = ppsrc, ChannelW = channelW, MinStrength = minStrength, MaxNumSr = maxNumSr, Loopback = loopback, ResColor = resColor, SupColor = supColor, InchColor = inchColor, ShowPp = showPp, ShowSrBroken = showSrBroken, ShowMa1 = showMa1, Ma1Len = ma1Len, Ma1Type = ma1Type, ShowMa2 = showMa2, Ma2Len = ma2Len, Ma2Type = ma2Type }, input, ref cacheSRchannelSymPivot);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.SRchannelSymPivot SRchannelSymPivot(int prd, PivotSource ppsrc, int channelW, int minStrength, int maxNumSr, int loopback, Brush resColor, Brush supColor, Brush inchColor, bool showPp, bool showSrBroken, bool showMa1, int ma1Len, MAType ma1Type, bool showMa2, int ma2Len, MAType ma2Type)
		{
			return indicator.SRchannelSymPivot(Input, prd, ppsrc, channelW, minStrength, maxNumSr, loopback, resColor, supColor, inchColor, showPp, showSrBroken, showMa1, ma1Len, ma1Type, showMa2, ma2Len, ma2Type);
		}

		public Indicators.SRchannelSymPivot SRchannelSymPivot(ISeries<double> input , int prd, PivotSource ppsrc, int channelW, int minStrength, int maxNumSr, int loopback, Brush resColor, Brush supColor, Brush inchColor, bool showPp, bool showSrBroken, bool showMa1, int ma1Len, MAType ma1Type, bool showMa2, int ma2Len, MAType ma2Type)
		{
			return indicator.SRchannelSymPivot(input, prd, ppsrc, channelW, minStrength, maxNumSr, loopback, resColor, supColor, inchColor, showPp, showSrBroken, showMa1, ma1Len, ma1Type, showMa2, ma2Len, ma2Type);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.SRchannelSymPivot SRchannelSymPivot(int prd, PivotSource ppsrc, int channelW, int minStrength, int maxNumSr, int loopback, Brush resColor, Brush supColor, Brush inchColor, bool showPp, bool showSrBroken, bool showMa1, int ma1Len, MAType ma1Type, bool showMa2, int ma2Len, MAType ma2Type)
		{
			return indicator.SRchannelSymPivot(Input, prd, ppsrc, channelW, minStrength, maxNumSr, loopback, resColor, supColor, inchColor, showPp, showSrBroken, showMa1, ma1Len, ma1Type, showMa2, ma2Len, ma2Type);
		}

		public Indicators.SRchannelSymPivot SRchannelSymPivot(ISeries<double> input , int prd, PivotSource ppsrc, int channelW, int minStrength, int maxNumSr, int loopback, Brush resColor, Brush supColor, Brush inchColor, bool showPp, bool showSrBroken, bool showMa1, int ma1Len, MAType ma1Type, bool showMa2, int ma2Len, MAType ma2Type)
		{
			return indicator.SRchannelSymPivot(input, prd, ppsrc, channelW, minStrength, maxNumSr, loopback, resColor, supColor, inchColor, showPp, showSrBroken, showMa1, ma1Len, ma1Type, showMa2, ma2Len, ma2Type);
		}
	}
}

#endregion
