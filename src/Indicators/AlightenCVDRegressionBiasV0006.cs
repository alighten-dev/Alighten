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
namespace NinjaTrader.NinjaScript.Indicators
{
	public class AlightenCVDRegressionBiasV0006 : Indicator
	{
		// --- User params ---
        [NinjaScriptProperty]
        [Display(Name = "Regression Length (N)", GroupName = "Parameters", Order = 0)]
        public int RegLength { get; set; } = 30;

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "Min R²", GroupName = "Parameters", Order = 1)]
        public double MinR2 { get; set; } = 0.3;

        [NinjaScriptProperty]
        [Display(Name = "Slope Epsilon (deadband)", GroupName = "Parameters", Order = 2)]
        public double SlopeEps { get; set; } = 0; // set small value if your CVD units are large/noisy

        [NinjaScriptProperty]
        [Display(Name = "Flip Confirm Bars (X)", GroupName = "Parameters", Order = 3)]
        public int FlipConfirmBars { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Swing Strength", GroupName = "Parameters", Order = 4)]
        public int SwingStrength { get; set; } = 3;
		
		[NinjaScriptProperty]
		[Display(Name="Bias Visual Scale", GroupName="Parameters", Order=5)]
		public double BiasVisualScale { get; set; } = 0.8;
		
//		[NinjaScriptProperty]
//		[Display(Name="Session Warmup Bars", GroupName="Parameters", Order=6)]
//		public int SessionWarmupBars { get; set; } = 15;
		
		[NinjaScriptProperty]
		[Display(Name="Force Test Bias (debug)", GroupName="Debug", Order=100)]
		public bool ForceTestBias { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name="Bypass R2 Gate", GroupName="Bypass", Order=101)]
		public bool BypassR2 { get; set; } = true;
		
		[NinjaScriptProperty]
		[Display(Name="Bypass Structure Gate", GroupName="Bypass", Order=102)]
		public bool BypassStructure { get; set; } = true;

        // --- Plots ---
        private Series<double> lrSlope;
        private Series<double> r2Series;
        private Series<double> biasSeries;

        // Swing tracking
        private Swing swing;
        private double lastHigh1 = double.NaN, lastHigh2 = double.NaN;
        private double lastLow1  = double.NaN, lastLow2  = double.NaN;

        // Bias state
        private int currentBias = 0; // -1 short, 0 neutral, +1 long
        private int oppStrongCount = 0;

        // Precomputed X-moments for regression on x = {0..N-1}
        private double Sx, Sxx, denomVarX;
		
		private Series<double> biasVisual;
		private Series<double> emaAbsSlope;
		
		private struct SwingPoint { public double Price; public int BarsAgo; }
		private SwingPoint hi1, hi2, lo1, lo2;
		
		private int barsInSession = 0;
		private bool isWarmup = false;


		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Regression Slopw of CVD";
				Name										= "AlightenCVDRegressionBiasV0006";
				Calculate									= Calculate.OnBarClose;
				IsOverlay									= false;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				//Disable this property if your indicator requires custom values that cumulate with each new market data event. 
				//See Help Guide for additional information.
				IsSuspendedWhileInactive					= true;
				ShowTransparentPlotsInDataBox 				= true;
				
				AddPlot(Brushes.DodgerBlue, "LR_Slope");
                AddPlot(Brushes.Gray, "R2");
                AddPlot(Brushes.Transparent, "Bias");
				AddPlot(Brushes.Gold, "BiasVisual"); 
				AddPlot(Brushes.Transparent, "LR_SlopeOfSlope"); 
			}
			else if (State == State.Configure)
			{
			}
			else if (State == State.DataLoaded)
            {
                lrSlope    = new Series<double>(this);
                r2Series   = new Series<double>(this);
                biasSeries = new Series<double>(this);
				biasVisual  = new Series<double>(this);
				emaAbsSlope = new Series<double>(this);

                swing = Swing(BarsArray[0], SwingStrength);

                // Precompute X sums: Sx = sum(i), Sxx = sum(i^2) for i=0..N-1
                RecomputeXMoments();
				
			    Plots[2].PlotStyle          = PlotStyle.Bar;      // Bias
			    Plots[2].Width              = 3;
				Plots[3].PlotStyle 			= PlotStyle.Bar;
				Plots[3].Width     			= 3;
            }
		}

		private void RecomputeXMoments()
        {
            int N = Math.Max(2, RegLength);
            Sx = (N - 1) * N / 2.0;                           // sum i
            Sxx = (N - 1) * N * (2 * N - 1) / 6.0;            // sum i^2
            denomVarX = N * Sxx - Sx * Sx;                    // N*Σx^2 - (Σx)^2
        }

        protected override void OnBarUpdate()
        {
			
			
            if (RegLength < 2)
                return;

            if (CurrentBar < RegLength)
            {
                Values[0][0] = 0;
                Values[1][0] = 0;
                Values[2][0] = 0;
                return;
            }
			
			
			// --- Smoke test: proves Bias plot renders. Toggle it on for 10 seconds, then turn off.
			if (ForceTestBias)
			{
			    int test = (CurrentBar % 20 < 10) ? +1 : -1; // alternate +1 / -1
			    Values[2][0] = test;          // Bias
			    if (Plots.Count() > 3)          // if you added BiasVisual
			        Values[3][0] = test * 100; // big number so you SEE it
			    return;
			}
			

            // Recompute X moments if user changed length
            if (Bars.IsFirstBarOfSession && denormChanged())
                RecomputeXMoments();

            // --- 1) Linear regression on INPUT (your CVD series) over last N bars ---
            int N = RegLength;

            double Sy = 0.0, Syy = 0.0, Sxy = 0.0;
            // x goes 0..N-1, y = Input[i]
            for (int i = 0; i < N; i++)
			{
			    double x = i;                       // 0 .. N-1 (oldest .. newest)
			    double y = Input[N - 1 - i];        // oldest at x=0, newest at x=N-1
			    Sy  += y;
			    Syy += y * y;
			    Sxy += x * y;
			}

            double denom = denomVarX; // N*Σx^2 - (Σx)^2
            double slope = 0.0;
            double r2    = 0.0;

            if (denom > 0.0)
            {
                double numSlope = N * Sxy - Sx * Sy;                // N*Σxy - Σx*Σy
                slope = numSlope / denom;

                double denomR = (N * Syy - Sy * Sy) * denom;        // (N*Σy^2 - (Σy)^2) * (N*Σx^2 - (Σx)^2)
                if (denomR > 0.0)
                {
                    double rNum = numSlope * numSlope;
                    r2 = rNum / denomR;
                }
            }

            lrSlope[0]  = slope;
            r2Series[0] = r2;

            Values[0][0] = lrSlope[0];
            Values[1][0] = r2Series[0];
			
			// --- SLOPE OF SLOPE (+1/-1) and color LR_Slope line ---
			double slopeOfSlope = 0.0;
			if (CurrentBar > 0)
			    slopeOfSlope = lrSlope[0] - lrSlope[1];
			
			// Write +1 / -1 to LR_SlopeOfSlope plot (Values[4])
			int sosSign = 0;
			if (slopeOfSlope > 0) sosSign = 1;
			else if (slopeOfSlope < 0) sosSign = -1;
			Values[4][0] = sosSign;
			
			// Color the LR_Slope plot per bar
			if (sosSign > 0)
			    PlotBrushes[0][0] = Brushes.LimeGreen;   // LR_Slope turns lime when its slope rises
			else if (sosSign < 0)
			    PlotBrushes[0][0] = Brushes.Red;         // LR_Slope turns red when its slope falls
			else
			    PlotBrushes[0][0] = Brushes.DodgerBlue;        // optional: flat = gray (or omit to keep default)


            // --- 2) Price structure via swings (HH/HL or LH/LL) ---
            UpdateSwings();

            bool upStructure   = IsUpStructure();
            bool downStructure = IsDownStructure();

            // --- 3) Bias logic with R² gate and flip confirmation ---
            int desired = 0;
			bool r2OK = r2 >= MinR2 || BypassR2;
			bool upOK = upStructure || BypassStructure;
			bool dnOK = downStructure || BypassStructure;
			
			if (r2OK)
			{
			    if (slope >  Math.Abs(SlopeEps) && upOK) desired = +1;
			    if (slope < -Math.Abs(SlopeEps) && dnOK) desired = -1;
			}

            if (desired == 0)
            {
                // no strong signal → keep current bias, decay opp counter
                oppStrongCount = 0;
            }
            else if (currentBias == desired)
            {
                // reinforce same-side strong signal
                oppStrongCount = 0;
            }
            else
            {
                // opposite strong signal; count until FlipConfirmBars
                oppStrongCount++;
                if (oppStrongCount >= FlipConfirmBars)
                {
                    currentBias = desired;
                    oppStrongCount = 0;
                }
            }
			
//			if (CurrentBar > RegLength + 5)
//			{
//			    Print(string.Format(
//			        "{0:HH:mm:ss} | slope={1:F3}  r2={2:F2}  up={3}  dn={4}  r2OK={5}  desired={6}  oppCnt={7}  bias={8}  sh=({9},{10})  sl=({11},{12})",
//			        Time[0], slope, r2, upStructure, downStructure, r2 >= MinR2, desired, oppStrongCount, currentBias,
//			        double.IsNaN(lastHigh1) ? 0 : lastHigh1, double.IsNaN(lastHigh2) ? 0 : lastHigh2,
//			        double.IsNaN(lastLow1)  ? 0 : lastLow1,  double.IsNaN(lastLow2)  ? 0 : lastLow2
//			    ));
//			}

            biasSeries[0] = currentBias;
            Values[2][0]  = currentBias;
			
			

            // Visual hint
			
			double k = 0.0;
			if (CurrentBar > RegLength)
			{
			    double absSlope = Math.Abs(lrSlope[0]);
			    // 2*RegLength for smoothing so it doesn’t jump
			    double alpha = 2.0 / (2 * RegLength + 1.0);
			    emaAbsSlope[0] = (CurrentBar == RegLength + 1) ? absSlope
			                     : alpha * absSlope + (1 - alpha) * emaAbsSlope[1];
			    k = emaAbsSlope[0] * BiasVisualScale;
			}
			biasVisual[0] = currentBias * k;
			Values[3][0]  = biasVisual[0];
        }

        private bool denormChanged()
        {
            // simple guard in case length changes intra-session (rare via UI)
            int N = Math.Max(2, RegLength);
            double newDenom = N * (N - 1) * (2 * N - 1) / 6.0 * N - Math.Pow((N - 1) * N / 2.0, 2.0);
            return Math.Abs(newDenom - denomVarX) > 1e-9;
        }
		
		private void MomentsForN(int N, out double SxN, out double SxxN, out double denomVarXN)
		{
		    SxN       = (N - 1) * N / 2.0;
		    SxxN      = (N - 1) * N * (2 * N - 1) / 6.0;
		    denomVarXN = N * SxxN - SxN * SxN;
		}

        // Replace your UpdateSwings() with this version:
		private void UpdateSwings()
		{
		    int lookback = Math.Min(CurrentBar, 600);  // how far back to search
		
		    int h1Bar = swing.SwingHighBar(0, 1, lookback); // most recent swing high (barsAgo)
		    int h2Bar = swing.SwingHighBar(0, 2, lookback); // prior swing high
		
		    int l1Bar = swing.SwingLowBar(0, 1, lookback);  // most recent swing low
		    int l2Bar = swing.SwingLowBar(0, 2, lookback);  // prior swing low
		
		    // guard against not-found (-1) or out of range
		    lastHigh1 = (h1Bar >= 0 && h1Bar < Count) ? High[h1Bar] : double.NaN;
		    lastHigh2 = (h2Bar >= 0 && h2Bar < Count) ? High[h2Bar] : double.NaN;
		    lastLow1  = (l1Bar >= 0 && l1Bar < Count) ? Low[l1Bar]   : double.NaN;
		    lastLow2  = (l2Bar >= 0 && l2Bar < Count) ? Low[l2Bar]   : double.NaN;
		
		    // optional debug
		    hi1 = new SwingPoint { Price = lastHigh1, BarsAgo = h1Bar };
		    hi2 = new SwingPoint { Price = lastHigh2, BarsAgo = h2Bar };
		    lo1 = new SwingPoint { Price = lastLow1,  BarsAgo = l1Bar };
		    lo2 = new SwingPoint { Price = lastLow2,  BarsAgo = l2Bar };
		}

		
		private bool IsUpStructure()
		{
		    return !double.IsNaN(lastHigh1) && !double.IsNaN(lastHigh2)
		        && !double.IsNaN(lastLow1)  && !double.IsNaN(lastLow2)
		        && lastHigh1 > lastHigh2 && lastLow1 > lastLow2;
		}
		
		private bool IsDownStructure()
		{
		    return !double.IsNaN(lastHigh1) && !double.IsNaN(lastHigh2)
		        && !double.IsNaN(lastLow1)  && !double.IsNaN(lastLow2)
		        && lastHigh1 < lastHigh2 && lastLow1 < lastLow2;
		}


        #region Plot accessors
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> LR_Slope => Values[0];

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> R2 => Values[1];

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Bias => Values[2];
        #endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AlightenCVDRegressionBiasV0006[] cacheAlightenCVDRegressionBiasV0006;
		public AlightenCVDRegressionBiasV0006 AlightenCVDRegressionBiasV0006(int regLength, double minR2, double slopeEps, int flipConfirmBars, int swingStrength, double biasVisualScale, bool forceTestBias, bool bypassR2, bool bypassStructure)
		{
			return AlightenCVDRegressionBiasV0006(Input, regLength, minR2, slopeEps, flipConfirmBars, swingStrength, biasVisualScale, forceTestBias, bypassR2, bypassStructure);
		}

		public AlightenCVDRegressionBiasV0006 AlightenCVDRegressionBiasV0006(ISeries<double> input, int regLength, double minR2, double slopeEps, int flipConfirmBars, int swingStrength, double biasVisualScale, bool forceTestBias, bool bypassR2, bool bypassStructure)
		{
			if (cacheAlightenCVDRegressionBiasV0006 != null)
				for (int idx = 0; idx < cacheAlightenCVDRegressionBiasV0006.Length; idx++)
					if (cacheAlightenCVDRegressionBiasV0006[idx] != null && cacheAlightenCVDRegressionBiasV0006[idx].RegLength == regLength && cacheAlightenCVDRegressionBiasV0006[idx].MinR2 == minR2 && cacheAlightenCVDRegressionBiasV0006[idx].SlopeEps == slopeEps && cacheAlightenCVDRegressionBiasV0006[idx].FlipConfirmBars == flipConfirmBars && cacheAlightenCVDRegressionBiasV0006[idx].SwingStrength == swingStrength && cacheAlightenCVDRegressionBiasV0006[idx].BiasVisualScale == biasVisualScale && cacheAlightenCVDRegressionBiasV0006[idx].ForceTestBias == forceTestBias && cacheAlightenCVDRegressionBiasV0006[idx].BypassR2 == bypassR2 && cacheAlightenCVDRegressionBiasV0006[idx].BypassStructure == bypassStructure && cacheAlightenCVDRegressionBiasV0006[idx].EqualsInput(input))
						return cacheAlightenCVDRegressionBiasV0006[idx];
			return CacheIndicator<AlightenCVDRegressionBiasV0006>(new AlightenCVDRegressionBiasV0006(){ RegLength = regLength, MinR2 = minR2, SlopeEps = slopeEps, FlipConfirmBars = flipConfirmBars, SwingStrength = swingStrength, BiasVisualScale = biasVisualScale, ForceTestBias = forceTestBias, BypassR2 = bypassR2, BypassStructure = bypassStructure }, input, ref cacheAlightenCVDRegressionBiasV0006);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenCVDRegressionBiasV0006 AlightenCVDRegressionBiasV0006(int regLength, double minR2, double slopeEps, int flipConfirmBars, int swingStrength, double biasVisualScale, bool forceTestBias, bool bypassR2, bool bypassStructure)
		{
			return indicator.AlightenCVDRegressionBiasV0006(Input, regLength, minR2, slopeEps, flipConfirmBars, swingStrength, biasVisualScale, forceTestBias, bypassR2, bypassStructure);
		}

		public Indicators.AlightenCVDRegressionBiasV0006 AlightenCVDRegressionBiasV0006(ISeries<double> input , int regLength, double minR2, double slopeEps, int flipConfirmBars, int swingStrength, double biasVisualScale, bool forceTestBias, bool bypassR2, bool bypassStructure)
		{
			return indicator.AlightenCVDRegressionBiasV0006(input, regLength, minR2, slopeEps, flipConfirmBars, swingStrength, biasVisualScale, forceTestBias, bypassR2, bypassStructure);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenCVDRegressionBiasV0006 AlightenCVDRegressionBiasV0006(int regLength, double minR2, double slopeEps, int flipConfirmBars, int swingStrength, double biasVisualScale, bool forceTestBias, bool bypassR2, bool bypassStructure)
		{
			return indicator.AlightenCVDRegressionBiasV0006(Input, regLength, minR2, slopeEps, flipConfirmBars, swingStrength, biasVisualScale, forceTestBias, bypassR2, bypassStructure);
		}

		public Indicators.AlightenCVDRegressionBiasV0006 AlightenCVDRegressionBiasV0006(ISeries<double> input , int regLength, double minR2, double slopeEps, int flipConfirmBars, int swingStrength, double biasVisualScale, bool forceTestBias, bool bypassR2, bool bypassStructure)
		{
			return indicator.AlightenCVDRegressionBiasV0006(input, regLength, minR2, slopeEps, flipConfirmBars, swingStrength, biasVisualScale, forceTestBias, bypassR2, bypassStructure);
		}
	}
}

#endregion
