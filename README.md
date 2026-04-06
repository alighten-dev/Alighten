# Alighten Custom NinjaTrader 8 Tools

This repository contains a collection of custom Indicators and Market Analyzer Columns for NinjaTrader 8, developed under the "Alighten" namespace.

## The Mirror Ecosystem
The core of the market structure analysis relies on the **Mirror** indicator.

### AlightenMirror
*   **AlightenMirror** (`AlightenMirrorV0014.cs`)
    *   **Description:** Acts as the master orchestrator, layering up to 7 distinct timeframes into a holistic order flow map on the active chart. It coordinates signal gating and visibility across the sub-indicators, managing performance explicitly by limiting background calculations and ensuring clean, high-performance re-rendering loops.

### Required Sub-Indicators
The Mirror indicator relies on an array of distinct algorithms working computationally in the background:
*   **AlightenMirrorPtA** (`AlightenMirrorPtAV0003.cs`): Base detector mapped to "Pattern A" (Initial Impulse and trend shifts). Identifies primary market structures.
*   **AlightenMirrorPtB** (`AlightenMirrorPtBV0003.cs`): Detector mapped to "Pattern B". Uses specific gates and trends to validate secondary pivots.
*   **AlightenMirrorPtC** (`AlightenMirrorPtCV0004.cs`): Detector mapped to "Pattern C". Filters specific pivot conditions for continuation or rejection structures.
*   **AlightenMirrorPtE** (`AlightenMirrorPtEV0005.cs`): Detector mapped to "Pattern E" (S/R Flips). Detects precise support/resistance break-and-retest geometries with dynamic confirmation logic.
*   **AlightenLevelMultiTimeframeRetest** (`AlightenLevelMultiTimeframeRetestV0001.cs`): Handles tracking for aggressive momentum testing and structural "Gain/Loss" levels over time.
*   **AlightenHTFVP** (`AlightenHTFVPV0004.cs`): Higher Timeframe Volume Profile. A modular VP engine that computes volume delta, value area highs/lows, and Point of Control (POC) dynamically, enabling the Mirror to bias-filter trend signals automatically based on dominant value zones.

## Order Flow & Footprint
*   **AlightenFootprintOrderFlow** (`AlightenFootprintOrderFlowV00015.cs`)
    *   **Description:** A comprehensive Footprint chart implementation with extensive configuration for aggregation, signal detection, and visualization.
    *   **Key Features:**
        *   **Aggregation:** Custom interval, value area settings, session alignment.
        *   **Signals:** Detects Volume Sequencing, Stacked Imbalances, Divergences, Delta Flips, Sweeps, and more.
        *   **Visuals:** Highly customizable colors for bars, outlines, POC, and signal markers.
        *   **Live Updates:** Supports intrabar updates for real-time signal generation.

*   **AlightenOrderFlowTools** (`AlightenOrderFlowToolsV0005.cs`)
    *   **Description:** A clean-room implementation of order-flow trade detection tools.
    *   **Key Features:**
        *   **Large Trades:** Visualizes large trade bubbles based on volume/size filters.
        *   **Trapped Traders:** Identification of traders trapped by adverse price moves.
        *   **Stacked Imbalance Trap:** Signals for stacked imbalances followed by reversals.
        *   **Speed of Tape:** Real-time monitoring of tape speed (contracts/sec).

*   **AlightenSpeedOfTape** (`AlightenSpeedOfTapeV0001.cs`)
    *   **Description:** Visualizes the speed of the tape as a histogram with dynamic thresholds.
    *   **Key Features:**
        *   **Metrics:** Displays total Speed and Net Speed (Ask - Bid).
        *   **Threshold:** Dynamic threshold line based on statistical outliers (Mean + StdDev).
        *   **Display Modes:** Speed or Net Speed histogram styles.

*   **AlightenCVDRegressionBias** (`AlightenCVDRegressionBiasV0006.cs`)
    *   **Description:** Analyzes Cumulative Volume Delta (CVD) using regression to determine market bias.

## Installation

1.  Place the `.cs` files in your NinjaTrader 8 custom directory:
    *   Indicators: `Documents\NinjaTrader 8\bin\Custom\Indicators`
    *   Market Analyzer Columns: `Documents\NinjaTrader 8\bin\Custom\MarketAnalyzerColumns`
2.  Compile the code in the NinjaTrader Editor (F5).
